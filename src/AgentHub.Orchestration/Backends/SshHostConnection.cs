using System.Collections.Concurrent;
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
}

/// <summary>
/// Factory for creating SSH host connections. Enables DI and testing.
/// </summary>
public interface ISshHostConnectionFactory
{
    ISshHostConnection Create(string host, string username, string privateKeyPath);
}

/// <summary>
/// Real SSH connection wrapper using SSH.NET with heartbeat and reconnection detection.
/// </summary>
public sealed class SshHostConnection : ISshHostConnection
{
    private readonly SshClient _client;
    private readonly Timer _heartbeat;
    private readonly ILogger? _logger;
    private volatile bool _connected;

    public bool IsConnected => _connected && _client.IsConnected;

    public SshHostConnection(string host, string username, string privateKeyPath, ILogger? logger = null)
    {
        _logger = logger;
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
        var cmd = _client.CreateCommand(commandJson);
        return await Task.Run(() => cmd.Execute(), ct);
    }

    public async Task<(StreamReader stdout, StreamReader stderr)> StartDaemonSessionAsync(string commandJson, CancellationToken ct)
    {
        // Use CreateCommand for long-running daemon session.
        // The command is the daemon entry point that reads the start-session payload from its arguments.
        var cmd = _client.CreateCommand(commandJson);

        // Begin execution asynchronously - the command will keep running
        await Task.Run(() => cmd.BeginExecute(), ct);

        var stdout = new StreamReader(cmd.OutputStream);
        var stderr = new StreamReader(cmd.ExtendedOutputStream);
        return (stdout, stderr);
    }

    private void SendHeartbeat(object? state)
    {
        if (!_connected || !_client.IsConnected) return;
        try
        {
            var ping = HostCommandProtocol.Serialize(HostCommandProtocol.CreatePing());
            var cmd = _client.CreateCommand(ping);
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
    public ISshHostConnection Create(string host, string username, string privateKeyPath)
    {
        var logger = loggerFactory?.CreateLogger<SshHostConnection>();
        return new SshHostConnection(host, username, privateKeyPath, logger);
    }
}
