using AgentHub.Orchestration.Backends;
using AgentHub.Orchestration.HostDaemon;

namespace AgentHub.Tests.Helpers;

/// <summary>
/// Mock SSH host connection for unit testing without real SSH.
/// Provides canned responses for start-session, stop-session, force-kill, and ping commands.
/// </summary>
public sealed class MockSshHostConnection : ISshHostConnection
{
    private bool _connected;
    private readonly Queue<string> _responses = new();
    private readonly List<string> _commandsSent = new();
    private bool _simulateConnectionFailure;

    public bool IsConnected => _connected && !_simulateConnectionFailure;

    /// <summary>All commands sent to this connection, in order.</summary>
    public IReadOnlyList<string> CommandsSent => _commandsSent;

    /// <summary>Enqueue a canned response for the next ExecuteCommandAsync call.</summary>
    public void EnqueueResponse(string responseJson) => _responses.Enqueue(responseJson);

    /// <summary>Enqueue a successful response for a given command type.</summary>
    public void EnqueueSuccessResponse(string command, string? sessionId = null)
    {
        var response = new HostCommandResponse
        {
            Success = true,
            Command = command,
            SessionId = sessionId
        };
        _responses.Enqueue(System.Text.Json.JsonSerializer.Serialize(response));
    }

    /// <summary>Simulate a connection failure.</summary>
    public void SimulateConnectionFailure() => _simulateConnectionFailure = true;

    /// <summary>Restore connection after simulated failure.</summary>
    public void RestoreConnection() => _simulateConnectionFailure = false;

    public Task ConnectAsync(CancellationToken ct)
    {
        if (_simulateConnectionFailure)
            throw new InvalidOperationException("Simulated connection failure");
        _connected = true;
        return Task.CompletedTask;
    }

    public Task<string> ExecuteCommandAsync(string commandJson, CancellationToken ct)
    {
        _commandsSent.Add(commandJson);

        if (_responses.Count > 0)
            return Task.FromResult(_responses.Dequeue());

        // Default success response
        return Task.FromResult(System.Text.Json.JsonSerializer.Serialize(new HostCommandResponse
        {
            Success = true,
            Command = "default"
        }));
    }

    public Task StartStreamingCommandAsync(string command, Func<string, Task> onLine, Func<Task>? onHeartbeat, CancellationToken ct)
    {
        _commandsSent.Add(command);
        return Task.CompletedTask;
    }

    public Task<(StreamReader stdout, StreamReader stderr)> StartDaemonSessionAsync(string commandJson, CancellationToken ct)
    {
        _commandsSent.Add(commandJson);
        // Return empty streams for testing
        var stdoutStream = new MemoryStream();
        var stderrStream = new MemoryStream();
        return Task.FromResult((new StreamReader(stdoutStream), new StreamReader(stderrStream)));
    }

    public ValueTask DisposeAsync()
    {
        _connected = false;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Mock factory that creates MockSshHostConnection instances for testing.
/// </summary>
public sealed class MockSshHostConnectionFactory : ISshHostConnectionFactory
{
    private readonly Queue<MockSshHostConnection> _connections = new();
    private MockSshHostConnection? _lastCreated;

    /// <summary>The last connection created by this factory.</summary>
    public MockSshHostConnection? LastCreated => _lastCreated;

    /// <summary>All connections created by this factory.</summary>
    public List<MockSshHostConnection> AllCreated { get; } = [];

    public string? DaemonPath { get; set; }

    /// <summary>Enqueue a pre-configured mock connection.</summary>
    public void EnqueueConnection(MockSshHostConnection connection) => _connections.Enqueue(connection);

    public ISshHostConnection Create(string host, string username, string privateKeyPath)
    {
        if (_connections.Count > 0)
        {
            _lastCreated = _connections.Dequeue();
        }
        else
        {
            _lastCreated = new MockSshHostConnection();
            // Default: enqueue a success start-session response
            _lastCreated.EnqueueSuccessResponse("start-session");
        }
        AllCreated.Add(_lastCreated);
        return _lastCreated;
    }
}
