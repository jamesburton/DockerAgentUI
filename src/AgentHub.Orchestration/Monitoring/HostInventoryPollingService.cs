using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
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
/// Configuration record for an agent CLI definition loaded from agents.json.
/// </summary>
public sealed record AgentConfig(
    string Name,
    string VersionFlag,
    string VersionPattern,
    Dictionary<string, List<string>> Capabilities);

/// <summary>
/// BackgroundService that SSHs into registered hosts every 60 minutes (configurable)
/// to discover installed agent CLIs, versions, disk space, and git version.
/// Updates HostEntity.InventoryJson in DB and HostMetricCache for placement engine.
/// </summary>
public sealed class HostInventoryPollingService : BackgroundService
{
    private readonly IDbContextFactory<AgentHubDbContext> _dbFactory;
    private readonly ISshHostConnectionFactory _connectionFactory;
    private readonly HostMetricCache _cache;
    private readonly DurableEventService _events;
    private readonly ILogger<HostInventoryPollingService> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly string _sshKeyPath;
    private readonly string _sshUsername;
    private readonly string _agentsJsonPath;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public HostInventoryPollingService(
        IDbContextFactory<AgentHubDbContext> dbFactory,
        ISshHostConnectionFactory connectionFactory,
        HostMetricCache cache,
        DurableEventService events,
        ILogger<HostInventoryPollingService> logger,
        IConfiguration configuration)
    {
        _dbFactory = dbFactory;
        _connectionFactory = connectionFactory;
        _cache = cache;
        _events = events;
        _logger = logger;

        var intervalMinutes = configuration.GetValue<int?>("Inventory:PollIntervalMinutes") ?? 60;
        _pollInterval = TimeSpan.FromMinutes(intervalMinutes);

        _sshKeyPath = configuration["Ssh:PrivateKeyPath"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_rsa");
        _sshUsername = configuration["Ssh:Username"] ?? "agent-user";
        _agentsJsonPath = configuration["Inventory:AgentsJsonPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config", "agents.json");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "HostInventoryPollingService started (interval: {Interval}min)",
            _pollInterval.TotalMinutes);

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
                _logger.LogError(ex, "Error during host inventory polling cycle");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Run a single inventory polling cycle across all enabled SSH hosts.
    /// Exposed as public for testing and on-demand refresh-all API.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        var agents = LoadAgentConfigs();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var hosts = await db.Hosts
            .Where(h => h.Enabled && h.AllowSsh && h.Address != null)
            .ToListAsync(ct);

        foreach (var host in hosts)
        {
            try
            {
                await PollHostAsync(db, host, agents, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to poll inventory for host {HostId}", host.HostId);
            }
        }
    }

    /// <summary>
    /// Refresh inventory for a single host. Used by on-demand API endpoint.
    /// </summary>
    public async Task RefreshHostAsync(string hostId, CancellationToken ct)
    {
        var agents = LoadAgentConfigs();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var host = await db.Hosts.FindAsync(new object[] { hostId }, ct);
        if (host is null)
        {
            _logger.LogWarning("Host {HostId} not found for inventory refresh", hostId);
            return;
        }

        await PollHostAsync(db, host, agents, ct);
    }

    private async Task PollHostAsync(AgentHubDbContext db, HostEntity host, List<AgentConfig> agents, CancellationToken ct)
    {
        var command = GetInventoryCommand(host.Os, agents);
        if (command is null)
        {
            _logger.LogDebug("Unsupported OS '{Os}' for host {HostId}, skipping inventory", host.Os, host.HostId);
            return;
        }

        var conn = _connectionFactory.Create(host.Address!, _sshUsername, _sshKeyPath);
        await using (conn)
        {
            await conn.ConnectAsync(ct);

            var output = await conn.ExecuteCommandAsync(command, ct);

            var rawInventory = ParseInventoryOutput(output?.Trim());
            if (rawInventory is null)
            {
                _logger.LogWarning("Failed to parse inventory output from host {HostId}: '{Output}'",
                    host.HostId, output);
                return;
            }

            // Resolve capabilities and extract clean versions from raw output
            var resolvedAgents = rawInventory.Agents.Select(a =>
            {
                var agentConfig = agents.FirstOrDefault(ac =>
                    ac.Name.Equals(a.Name, StringComparison.OrdinalIgnoreCase));
                var cleanVersion = agentConfig is not null
                    ? ExtractVersion(a.Version, agentConfig.VersionPattern)
                    : a.Version;
                var capabilities = agentConfig is not null
                    ? ResolveCapabilities(cleanVersion, agentConfig.Capabilities)
                    : new List<string>();

                return new AgentInfo(a.Name, cleanVersion, a.Path, capabilities);
            }).ToList();

            var inventory = new HostInventory(resolvedAgents, rawInventory.DiskFreeGb, rawInventory.GitVersion);

            host.InventoryJson = JsonSerializer.Serialize(inventory, JsonOpts);
            await db.SaveChangesAsync(ct);

            _cache.UpdateInventory(host.HostId, inventory);

            await _events.EmitAsync(new SessionEvent(
                SessionId: "",
                Kind: SessionEventKind.HostMetrics,
                TsUtc: DateTimeOffset.UtcNow,
                Data: $"Host {host.HostId} inventory updated",
                Meta: new Dictionary<string, string>
                {
                    ["hostId"] = host.HostId,
                    ["agentCount"] = inventory.Agents.Count.ToString(CultureInfo.InvariantCulture),
                    ["diskFreeGb"] = inventory.DiskFreeGb?.ToString("F1", CultureInfo.InvariantCulture) ?? ""
                }));

            _logger.LogDebug(
                "Host {HostId} inventory: {AgentCount} agents, disk={DiskGb}GB, git={GitVer}",
                host.HostId, inventory.Agents.Count, inventory.DiskFreeGb, inventory.GitVersion);
        }
    }

    /// <summary>
    /// Returns the OS-appropriate composite SSH command to discover agents, disk, and git.
    /// Output format: JSON object with agents array, diskFreeGb, gitVersion.
    /// Uses printf/echo for JSON building (no jq dependency on remote hosts).
    /// </summary>
    public static string? GetInventoryCommand(string os, List<AgentConfig> agents)
    {
        var agentNames = agents.Select(a => a.Name).ToList();
        var versionFlags = agents.ToDictionary(a => a.Name, a => a.VersionFlag);

        if (os.Contains("windows", StringComparison.OrdinalIgnoreCase))
        {
            return BuildWindowsCommand(agentNames, versionFlags);
        }

        if (os.Contains("linux", StringComparison.OrdinalIgnoreCase))
        {
            return BuildLinuxCommand(agentNames, versionFlags);
        }

        if (os.Contains("macos", StringComparison.OrdinalIgnoreCase) ||
            os.Contains("darwin", StringComparison.OrdinalIgnoreCase))
        {
            return BuildMacOsCommand(agentNames, versionFlags);
        }

        return null;
    }

    private static string BuildWindowsCommand(List<string> agentNames, Dictionary<string, string> versionFlags)
    {
        // Build agent probe loop entries
        var agentProbes = string.Join("; ",
            agentNames.Select(name =>
            {
                var flag = versionFlags[name];
                return $"try {{ $p = (where.exe {name} 2>$null | Select-Object -First 1); if ($p) {{ $v = (& $p {flag} 2>$null); $agents += @{{ name='{name}'; version=$v; path=$p }} }} }} catch {{ }}";
            }));

        return $"powershell -Command \"$agents = @(); {agentProbes}; $disk = [math]::Round((Get-PSDrive C).Free / 1GB, 1); $gitVer = $null; try {{ $gitVer = (git --version 2>$null) }} catch {{ }}; @{{ agents=$agents; diskFreeGb=$disk; gitVersion=$gitVer }} | ConvertTo-Json -Compress\"";
    }

    private static string BuildLinuxCommand(List<string> agentNames, Dictionary<string, string> versionFlags)
    {
        // Build agent probes using printf - each independent with || true
        var agentProbes = string.Join(" ",
            agentNames.Select(name =>
            {
                var flag = versionFlags[name];
                return $"p=$(which {name} 2>/dev/null); if [ -n \\\"$p\\\" ]; then v=$($p {flag} 2>/dev/null | head -1); agents=\\\"$agents,{{\\\\\\\"name\\\\\\\":\\\\\\\"\\\\\\\"${{name}}\\\\\\\"\\\\\\\",\\\\\\\"version\\\\\\\":\\\\\\\"$v\\\\\\\",\\\\\\\"path\\\\\\\":\\\\\\\"$p\\\\\\\"}}\\\"; fi;";
            }));

        // Actually, let's use a simpler approach with printf JSON building
        var probeLines = new List<string>();
        probeLines.Add("agents=\\\"\\\"");
        probeLines.Add("sep=\\\"\\\"");

        foreach (var name in agentNames)
        {
            var flag = versionFlags[name];
            probeLines.Add($"p=$(which {name} 2>/dev/null) || true");
            probeLines.Add($"if [ -n \\\"$p\\\" ]; then v=$($p {flag} 2>/dev/null | head -1) || true; agents=\\\"${{agents}}${{sep}}{{\\\\\\\"name\\\\\\\":\\\\\\\"\\\\\\\"${name}\\\\\\\"\\\\\\\",\\\\\\\"version\\\\\\\":\\\\\\\"$v\\\\\\\",\\\\\\\"path\\\\\\\":\\\\\\\"$p\\\\\\\"}}\\\"; sep=\\\",\\\"; fi");
        }

        probeLines.Add("disk=$(df -BG / 2>/dev/null | awk 'NR==2 {gsub(/G/,\\\"\\\",$4); print $4}') || true");
        probeLines.Add("gitver=$(git --version 2>/dev/null) || true");
        probeLines.Add("printf '{\\\"agents\\\":[%s],\\\"diskFreeGb\\\":%s,\\\"gitVersion\\\":\\\"%s\\\"}' \\\"$agents\\\" \\\"${disk:-null}\\\" \\\"$gitver\\\"");

        return $"bash -c \"{string.Join("; ", probeLines)}\"";
    }

    private static string BuildMacOsCommand(List<string> agentNames, Dictionary<string, string> versionFlags)
    {
        var probeLines = new List<string>();
        probeLines.Add("agents=\\\"\\\"");
        probeLines.Add("sep=\\\"\\\"");

        foreach (var name in agentNames)
        {
            var flag = versionFlags[name];
            probeLines.Add($"p=$(which {name} 2>/dev/null) || true");
            probeLines.Add($"if [ -n \\\"$p\\\" ]; then v=$($p {flag} 2>/dev/null | head -1) || true; agents=\\\"${{agents}}${{sep}}{{\\\\\\\"name\\\\\\\":\\\\\\\"\\\\\\\"${name}\\\\\\\"\\\\\\\",\\\\\\\"version\\\\\\\":\\\\\\\"$v\\\\\\\",\\\\\\\"path\\\\\\\":\\\\\\\"$p\\\\\\\"}}\\\"; sep=\\\",\\\"; fi");
        }

        probeLines.Add("disk=$(df -g / 2>/dev/null | awk 'NR==2 {print $4}') || true");
        probeLines.Add("gitver=$(git --version 2>/dev/null) || true");
        probeLines.Add("printf '{\\\"agents\\\":[%s],\\\"diskFreeGb\\\":%s,\\\"gitVersion\\\":\\\"%s\\\"}' \\\"$agents\\\" \\\"${disk:-null}\\\" \\\"$gitver\\\"");

        return $"bash -c \"{string.Join("; ", probeLines)}\"";
    }

    /// <summary>
    /// Parses the JSON output from a composite SSH inventory probe.
    /// Returns null on failure. Handles partial results gracefully.
    /// </summary>
    public static HostInventory? ParseInventoryOutput(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var agents = new List<AgentInfo>();
            if (root.TryGetProperty("agents", out var agentsEl) && agentsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var agentEl in agentsEl.EnumerateArray())
                {
                    var name = agentEl.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.IsNullOrEmpty(name)) continue;

                    var version = agentEl.TryGetProperty("version", out var v) && v.ValueKind != JsonValueKind.Null
                        ? v.GetString() : null;
                    var path = agentEl.TryGetProperty("path", out var p) && p.ValueKind != JsonValueKind.Null
                        ? p.GetString() : null;

                    agents.Add(new AgentInfo(name, version, path, null));
                }
            }

            double? diskFreeGb = null;
            if (root.TryGetProperty("diskFreeGb", out var diskEl))
            {
                if (diskEl.ValueKind == JsonValueKind.Number)
                    diskFreeGb = diskEl.GetDouble();
                else if (diskEl.ValueKind == JsonValueKind.String &&
                         double.TryParse(diskEl.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    diskFreeGb = parsed;
            }

            string? gitVersion = null;
            if (root.TryGetProperty("gitVersion", out var gitEl) && gitEl.ValueKind == JsonValueKind.String)
                gitVersion = gitEl.GetString();

            return new HostInventory(agents, diskFreeGb, gitVersion);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Applies a regex pattern to raw CLI output to extract a version string.
    /// Returns null if input is null or pattern doesn't match.
    /// </summary>
    public static string? ExtractVersion(string? rawOutput, string pattern)
    {
        if (string.IsNullOrEmpty(rawOutput))
            return null;

        var match = Regex.Match(rawOutput, pattern);
        return match.Success && match.Groups.Count > 1
            ? match.Groups[1].Value
            : null;
    }

    /// <summary>
    /// Resolves capabilities for a given version using the version-to-capability mapping
    /// from agents.json. Capability keys use "X.Y.Z+" format meaning "version >= X.Y.Z".
    /// Returns empty list if version is null or below all thresholds.
    /// </summary>
    public static List<string> ResolveCapabilities(string? version, Dictionary<string, List<string>> capabilityMap)
    {
        if (string.IsNullOrEmpty(version) || capabilityMap is null || capabilityMap.Count == 0)
            return [];

        if (!Version.TryParse(version, out var parsedVersion))
            return [];

        var result = new List<string>();

        foreach (var (key, capabilities) in capabilityMap)
        {
            // Parse "X.Y.Z+" format
            var minVersionStr = key.TrimEnd('+');
            if (Version.TryParse(minVersionStr, out var minVersion) && parsedVersion >= minVersion)
            {
                result.AddRange(capabilities);
            }
        }

        return result;
    }

    private List<AgentConfig> LoadAgentConfigs()
    {
        try
        {
            var path = Path.GetFullPath(_agentsJsonPath);
            if (!File.Exists(path))
            {
                _logger.LogWarning("agents.json not found at {Path}", path);
                return [];
            }

            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);
            var agentsEl = doc.RootElement.GetProperty("agents");

            var configs = new List<AgentConfig>();
            foreach (var el in agentsEl.EnumerateArray())
            {
                var name = el.GetProperty("name").GetString()!;
                var versionFlag = el.GetProperty("versionFlag").GetString()!;
                var versionPattern = el.GetProperty("versionPattern").GetString()!;

                var capabilities = new Dictionary<string, List<string>>();
                if (el.TryGetProperty("capabilities", out var capsEl))
                {
                    foreach (var prop in capsEl.EnumerateObject())
                    {
                        var caps = new List<string>();
                        foreach (var cap in prop.Value.EnumerateArray())
                        {
                            caps.Add(cap.GetString()!);
                        }
                        capabilities[prop.Name] = caps;
                    }
                }

                configs.Add(new AgentConfig(name, versionFlag, versionPattern, capabilities));
            }

            return configs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load agents.json");
            return [];
        }
    }
}
