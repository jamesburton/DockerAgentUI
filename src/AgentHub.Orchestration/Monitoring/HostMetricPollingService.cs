using System.Globalization;
using AgentHub.Contracts;
using AgentHub.Orchestration.Backends;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using AgentHub.Orchestration.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentHub.Orchestration.Monitoring;

/// <summary>
/// BackgroundService that SSHs into registered hosts every 30 seconds to collect
/// CPU and memory metrics. Updates HostEntity fields in DB and emits HostMetrics
/// SSE events via DurableEventService for real-time dashboard delivery.
/// </summary>
public sealed class HostMetricPollingService : BackgroundService
{
    private readonly IDbContextFactory<AgentHubDbContext> _dbFactory;
    private readonly ISshHostConnectionFactory _connectionFactory;
    private readonly DurableEventService _events;
    private readonly ILogger<HostMetricPollingService> _logger;
    private readonly string _sshKeyPath;
    private readonly string _sshUsername;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30);

    public HostMetricPollingService(
        IDbContextFactory<AgentHubDbContext> dbFactory,
        ISshHostConnectionFactory connectionFactory,
        DurableEventService events,
        ILogger<HostMetricPollingService> logger,
        IConfiguration configuration)
    {
        _dbFactory = dbFactory;
        _connectionFactory = connectionFactory;
        _events = events;
        _logger = logger;
        _sshKeyPath = configuration["Ssh:PrivateKeyPath"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_rsa");
        _sshUsername = configuration["Ssh:Username"] ?? "agent-user";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "HostMetricPollingService started (interval: {Interval}s)",
            _pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during host metric polling cycle");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Run a single polling cycle. Exposed for testing.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var hosts = await db.Hosts
            .Where(h => h.Enabled && h.AllowSsh && h.Address != null)
            .ToListAsync(ct);

        foreach (var host in hosts)
        {
            try
            {
                await PollHostAsync(db, host, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to poll metrics for host {HostId}", host.HostId);
            }
        }
    }

    private async Task PollHostAsync(AgentHubDbContext db, HostEntity host, CancellationToken ct)
    {
        var command = GetMetricCommand(host.Os);
        if (command is null)
        {
            _logger.LogDebug("Unsupported OS '{Os}' for host {HostId}, skipping metrics", host.Os, host.HostId);
            return;
        }

        var conn = _connectionFactory.Create(host.Address!, _sshUsername, _sshKeyPath);
        await using (conn)
        {
            await conn.ConnectAsync(ct);

            // Execute metric command via SSH - wrap in a HostCommand JSON for the daemon protocol
            var output = await conn.ExecuteCommandAsync(command, ct);

            var parsed = ParseMetricOutput(output?.Trim() ?? "");
            if (parsed is null)
            {
                _logger.LogWarning("Failed to parse metric output from host {HostId}: '{Output}'",
                    host.HostId, output);
                return;
            }

            var (cpu, memTotal, memUsed) = parsed.Value;

            host.CpuPercent = cpu;
            host.MemTotalMb = memTotal;
            host.MemUsedMb = memUsed;
            host.MetricsUpdatedUtc = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);

            await _events.EmitAsync(new SessionEvent(
                SessionId: "",
                Kind: SessionEventKind.HostMetrics,
                TsUtc: DateTimeOffset.UtcNow,
                Data: $"Host {host.HostId} metrics updated",
                Meta: new Dictionary<string, string>
                {
                    ["hostId"] = host.HostId,
                    ["cpu"] = host.CpuPercent?.ToString("F1", CultureInfo.InvariantCulture) ?? "",
                    ["memUsedMb"] = host.MemUsedMb?.ToString(CultureInfo.InvariantCulture) ?? "",
                    ["memTotalMb"] = host.MemTotalMb?.ToString(CultureInfo.InvariantCulture) ?? ""
                }));

            _logger.LogDebug(
                "Host {HostId} metrics: CPU={Cpu:F1}%, Mem={MemUsed}/{MemTotal}MB",
                host.HostId, cpu, memUsed, memTotal);
        }
    }

    /// <summary>
    /// Returns the OS-appropriate shell command to collect CPU and memory metrics.
    /// Output format: "cpu|memTotal|memUsed" (pipe-delimited).
    /// </summary>
    public static string? GetMetricCommand(string os)
    {
        if (os.Contains("windows", StringComparison.OrdinalIgnoreCase))
        {
            return "powershell -Command \"$cpu=(Get-Counter '\\Processor(_Total)\\% Processor Time').CounterSamples[0].CookedValue; $os=Get-CimInstance Win32_OperatingSystem; \\\"$cpu|$([math]::Round($os.TotalVisibleMemorySize/1024))|$([math]::Round(($os.TotalVisibleMemorySize-$os.FreePhysicalMemory)/1024))\\\"\"";
        }

        if (os.Contains("linux", StringComparison.OrdinalIgnoreCase))
        {
            return "bash -c \"cpu=$(grep 'cpu ' /proc/stat | awk '{u=$2+$4; t=$2+$4+$5} END {printf \\\"%.1f\\\", u*100/t}'); mem=$(free -m | awk '/Mem:/ {print $2\\\"|\\\"$3}'); echo \\\"$cpu|$mem\\\"\"";
        }

        if (os.Contains("macos", StringComparison.OrdinalIgnoreCase) ||
            os.Contains("darwin", StringComparison.OrdinalIgnoreCase))
        {
            return "bash -c \"cpu=$(top -l 1 -n 0 | grep 'CPU usage' | awk '{print $3}' | tr -d '%'); mem_total=$(sysctl -n hw.memsize | awk '{print int($1/1048576)}'); mem_used=$(vm_stat | awk '/Pages active|Pages wired/ {sum+=$NF} END {print int(sum*4096/1048576)}'); echo \\\"$cpu|$mem_total|$mem_used\\\"\"";
        }

        return null;
    }

    /// <summary>
    /// Parses pipe-delimited metric output: "cpu|memTotal|memUsed".
    /// Returns null if parsing fails.
    /// </summary>
    public static (double Cpu, long MemTotal, long MemUsed)? ParseMetricOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var parts = output.Split('|');
        if (parts.Length != 3)
            return null;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var cpu))
            return null;

        if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var memTotal))
            return null;

        if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var memUsed))
            return null;

        return (cpu, memTotal, memUsed);
    }
}
