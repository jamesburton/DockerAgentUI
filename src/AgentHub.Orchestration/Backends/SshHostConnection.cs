using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AgentHub.Orchestration.HostDaemon;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace AgentHub.Orchestration.Backends;

/// <summary>
/// Abstraction for SSH host connections to enable unit testing without real SSH.
/// </summary>
public interface ISshHostConnection : IAsyncDisposable
{
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken ct);
    Task<string> ExecuteCommandAsync(string commandJson, CancellationToken ct);
    Task<(StreamReader stdout, StreamReader stderr)> StartDaemonSessionAsync(string commandJson, CancellationToken ct);
    Task StartStreamingCommandAsync(string command, Func<string, Task> onLine, Func<Task>? onHeartbeat, CancellationToken ct);
}

/// <summary>
/// Factory for creating SSH host connections. Enables DI and testing.
/// </summary>
public interface ISshHostConnectionFactory
{
    ISshHostConnection Create(string host, string username, string privateKeyPath);
    string? DaemonPath { get; set; }
}

/// <summary>
/// Real SSH connection wrapper using SSH.NET with heartbeat and reconnection detection.
/// Routes JSON commands through the host daemon script when configured.
/// </summary>
public sealed class SshHostConnection : ISshHostConnection
{
    private readonly SshClient _client;
    private readonly Timer _heartbeat;
    private readonly ILogger? _logger;
    private readonly string? _daemonPath;
    private volatile bool _connected;

    public bool IsConnected => _connected && _client.IsConnected;

    public SshHostConnection(string host, string username, string privateKeyPath, string? daemonPath = null, ILogger? logger = null)
    {
        _logger = logger;
        _daemonPath = daemonPath;
        var keyFile = new PrivateKeyFile(privateKeyPath);
        var authMethod = new PrivateKeyAuthenticationMethod(username, keyFile);
        var connectionInfo = new ConnectionInfo(host, username, authMethod);

        _client = new SshClient(connectionInfo);
        _client.KeepAliveInterval = TimeSpan.FromSeconds(15);
        _client.ErrorOccurred += (_, e) =>
        {
            _logger?.LogWarning(e.Exception, "SSH connection error on {Host}", host);
            _connected = false;
        };

        _heartbeat = new Timer(SendHeartbeat, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        await Task.Run(() => _client.Connect(), ct);
        _connected = true;
        _heartbeat.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public async Task<string> ExecuteCommandAsync(string commandJson, CancellationToken ct)
    {
        var remoteCommand = WrapCommand(commandJson);
        var cmd = _client.CreateCommand(remoteCommand);
        return await Task.Run(() => cmd.Execute(), ct);
    }

    public async Task<(StreamReader stdout, StreamReader stderr)> StartDaemonSessionAsync(string commandJson, CancellationToken ct)
    {
        var remoteCommand = WrapCommand(commandJson);
        var cmd = _client.CreateCommand(remoteCommand);

        // Begin execution asynchronously - the command will keep running
        await Task.Run(() => cmd.BeginExecute(), ct);

        var stdout = new StreamReader(cmd.OutputStream);
        var stderr = new StreamReader(cmd.ExtendedOutputStream);
        return (stdout, stderr);
    }

    /// <summary>
    /// Executes a command via ShellStream (PTY-allocated) for real-time line-by-line output.
    /// SSH.NET's CreateCommand buffers output until completion; ShellStream provides data immediately.
    /// </summary>
    public async Task StartStreamingCommandAsync(string command, Func<string, Task> onLine, Func<Task>? onHeartbeat, CancellationToken ct)
    {
        var exitMarker = $"__AGENTHUB_DONE_{Guid.NewGuid():N}__";

        // Use dumb terminal to avoid TUI escape sequences from tools like Claude --print
        using var shell = _client.CreateShellStream("dumb", 250, 50, 800, 600, 8192);

        // Wait for shell to be ready, drain any banner/MOTD
        await Task.Delay(2000, ct);
        var bannerLines = 0;
        while (shell.DataAvailable)
        {
            var bannerLine = shell.ReadLine(TimeSpan.FromMilliseconds(200));
            bannerLines++;
            _logger?.LogDebug("ShellStream banner drain [{Count}]: [{Line}]", bannerLines, bannerLine);
        }
        _logger?.LogInformation("ShellStream drained {Count} banner lines, sending command: {Command}", bannerLines, command);

        // Send command followed by exit marker so we know when it's done
        shell.WriteLine(command);
        shell.WriteLine($"echo {exitMarker}");
        shell.WriteLine("exit");

        // Track lines seen for command echo detection.
        // cmd.exe echoes the full prompt+command, e.g. "C:\Users\james>claude --print ..."
        // We skip lines until we see the first line that is NOT a prompt echo or known control line.
        var linesRead = 0;
        var commandEchoesRemaining = 1; // Skip the first non-empty line (the command echo)
        var noDataCount = 0;

        while (!ct.IsCancellationRequested)
        {
            var line = shell.ReadLine(TimeSpan.FromSeconds(5));

            if (line is null)
            {
                noDataCount++;
                _logger?.LogTrace("ShellStream ReadLine returned null (count: {Count})", noDataCount);
                // 5s * 360 = 30 minute max idle (extended for long-running Claude inference)
                if (noDataCount > 360)
                {
                    _logger?.LogWarning("ShellStream idle timeout after 30 minutes");
                    break;
                }
                // Emit heartbeat every ~30s (6 * 5s) to keep session alive
                if (onHeartbeat is not null && noDataCount % 6 == 0)
                    await onHeartbeat();
                continue;
            }

            linesRead++;
            noDataCount = 0;

            // Log raw line BEFORE any processing
            _logger?.LogDebug("ShellStream raw line [{LineNum}]: [{Line}]", linesRead, line);

            var clean = StripAnsi(line);
            _logger?.LogDebug("ShellStream clean line [{LineNum}]: [{Clean}]", linesRead, clean);

            // Detect exit marker
            if (clean.Contains(exitMarker))
            {
                _logger?.LogInformation("ShellStream exit marker detected at line {LineNum}", linesRead);
                break;
            }

            // Skip empty lines after stripping
            if (string.IsNullOrWhiteSpace(clean))
                continue;

            // Skip shell prompt lines: match "X:\path>" or "user@host:path$ " patterns
            // Use regex to be precise — avoid false positives on content lines ending with >
            if (IsShellPrompt(clean))
            {
                _logger?.LogDebug("ShellStream skipping prompt line [{LineNum}]: [{Clean}]", linesRead, clean);
                continue;
            }

            // Skip the command echo (first non-empty, non-prompt line from the shell)
            if (commandEchoesRemaining > 0)
            {
                commandEchoesRemaining--;
                _logger?.LogDebug("ShellStream skipping command echo [{LineNum}]: [{Clean}]", linesRead, clean);
                continue;
            }

            // Skip the echo command itself and exit command
            if (clean.StartsWith("echo ") || clean.Trim() == "exit")
            {
                _logger?.LogDebug("ShellStream skipping control line [{LineNum}]: [{Clean}]", linesRead, clean);
                continue;
            }

            _logger?.LogDebug("ShellStream EMITTING line [{LineNum}]: [{Clean}]", linesRead, clean);
            await onLine(clean);
        }

        _logger?.LogInformation("ShellStream finished after {LinesRead} lines", linesRead);
    }

    /// <summary>
    /// Detects shell prompt lines precisely to avoid false positives.
    /// Matches Windows cmd prompts (e.g., "C:\Users\james>") and Unix prompts (e.g., "user@host:~$").
    /// </summary>
    private static bool IsShellPrompt(string line)
    {
        var trimmed = line.TrimEnd();
        // Windows cmd.exe prompt: drive letter + colon + path + ">"
        // e.g., "C:\Users\james>" or "D:\Projects\foo>"
        if (Regex.IsMatch(trimmed, @"^[A-Za-z]:\\[^>]*>$"))
            return true;
        // Unix prompt ending with "$ " or "#" (typically user@host patterns)
        if (Regex.IsMatch(trimmed, @"^[\w.+-]+@[\w.+-]+[:\s].*[\$#]$"))
            return true;
        // PowerShell prompt: "PS C:\path>"
        if (Regex.IsMatch(trimmed, @"^PS [A-Za-z]:\\[^>]*>$"))
            return true;
        return false;
    }

    /// <summary>
    /// Strips ANSI/VT escape sequences and OSC codes from PTY output.
    /// </summary>
    private static string StripAnsi(string input)
    {
        // CSI sequences: ESC[ ... final byte
        // OSC sequences: ESC] ... ST (BEL or ESC\)
        // Also handle raw BEL-terminated OSC: ]digits;...BEL
        var result = Regex.Replace(input, @"\x1b\[[0-9;?]*[a-zA-Z]", "");
        result = Regex.Replace(result, @"\x1b\][^\x07\x1b]*[\x07]", "");
        result = Regex.Replace(result, @"\x1b\][^\x1b]*\x1b\\", "");
        result = Regex.Replace(result, @"\][0-9]*;[^\x07]*\x07?", "");
        result = Regex.Replace(result, @"\x1b[()][0-9A-B]", "");
        result = Regex.Replace(result, @"\x1b[=>]", "");
        // Clean any remaining control chars except newline/tab
        result = Regex.Replace(result, @"[\x00-\x08\x0b\x0c\x0e-\x1f]", "");
        return result.Trim();
    }

    /// <summary>
    /// Wraps a JSON protocol command through the daemon script if configured.
    /// Raw shell commands (git, metric collection) are sent directly.
    /// Uses echo piping through stdin to avoid cmd.exe quoting issues on Windows SSH hosts.
    /// </summary>
    private string WrapCommand(string command)
    {
        if (string.IsNullOrEmpty(_daemonPath))
            return command;

        // Only wrap JSON protocol commands (start with '{'), pass raw shell commands through directly
        if (!command.TrimStart().StartsWith('{'))
            return command;

        return $"echo {command} | powershell -NoProfile -ExecutionPolicy Bypass -File \"{_daemonPath}\"";
    }

    private void SendHeartbeat(object? state)
    {
        if (!_connected || !_client.IsConnected) return;
        try
        {
            var ping = HostCommandProtocol.Serialize(HostCommandProtocol.CreatePing());
            var remoteCommand = WrapCommand(ping);
            var cmd = _client.CreateCommand(remoteCommand);
            cmd.Execute();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SSH heartbeat failed");
            _connected = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _heartbeat.DisposeAsync();
        if (_client.IsConnected)
        {
            _client.Disconnect();
        }
        _client.Dispose();
    }
}

/// <summary>
/// Default factory that creates real SshHostConnection instances.
/// </summary>
public sealed class SshHostConnectionFactory(ILoggerFactory? loggerFactory = null) : ISshHostConnectionFactory
{
    public string? DaemonPath { get; set; }

    public ISshHostConnection Create(string host, string username, string privateKeyPath)
    {
        // Strip protocol prefix (ssh://) so SSH.NET gets a plain hostname for DNS resolution
        var cleanHost = host.Replace("ssh://", "");
        var logger = loggerFactory?.CreateLogger<SshHostConnection>();
        return new SshHostConnection(cleanHost, username, privateKeyPath, DaemonPath, logger);
    }
}
