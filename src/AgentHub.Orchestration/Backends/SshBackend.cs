using System.Collections.Concurrent;
using System.Text.Json;
using AgentHub.Contracts;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using AgentHub.Orchestration.HostDaemon;
using AgentHub.Orchestration.Worktree;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentHub.Orchestration.Backends;

/// <summary>
/// Real SSH execution backend using SSH.NET. Connects to remote hosts via SSH,
/// sends HostCommandProtocol commands, streams events back, and persists all
/// session state to the database.
/// </summary>
public sealed class SshBackend : ISessionBackend
{
    private readonly IDbContextFactory<AgentHubDbContext> _dbFactory;
    private readonly IHostRegistry _hostRegistry;
    private readonly ISshHostConnectionFactory _connectionFactory;
    private readonly WorktreeService _worktreeService;
    private readonly ILogger<SshBackend> _logger;
    private readonly string _sshKeyPath;
    private readonly string _sshUsername;
    private readonly TimeSpan _gracePeriod;

    /// <summary>
    /// Active SSH connections keyed by session ID.
    /// </summary>
    private readonly ConcurrentDictionary<string, ISshHostConnection> _connections = new();

    public string Name => "ssh";

    public SshBackend(
        IDbContextFactory<AgentHubDbContext> dbFactory,
        IHostRegistry hostRegistry,
        ISshHostConnectionFactory connectionFactory,
        WorktreeService worktreeService,
        ILogger<SshBackend> logger,
        IConfiguration configuration)
    {
        _dbFactory = dbFactory;
        _hostRegistry = hostRegistry;
        _connectionFactory = connectionFactory;
        _worktreeService = worktreeService;
        _logger = logger;
        _sshKeyPath = configuration["Ssh:PrivateKeyPath"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_rsa");
        _sshUsername = configuration["Ssh:Username"] ?? "agent-user";
        var graceSecondsStr = configuration["Ssh:GracePeriodSeconds"];
        var graceSeconds = int.TryParse(graceSecondsStr, out var gs) ? gs : 10;
        _gracePeriod = TimeSpan.FromSeconds(graceSeconds);
    }

    public bool CanHandle(SessionRequirements requirements, NodeCapability node)
        => (requirements.ExecutionMode == ExecutionMode.Ssh ||
            (requirements.ExecutionMode == ExecutionMode.Auto && !string.IsNullOrEmpty(requirements.TargetHostId)))
           && requirements.AcceptRisk
           && string.Equals(node.Backend, Name, StringComparison.OrdinalIgnoreCase)
           && node.AllowRiskyDirectExec;

    public async Task<IReadOnlyList<NodeCapability>> GetInventoryAsync(CancellationToken ct)
    {
        var hosts = await _hostRegistry.ListAsync(ct);
        return hosts
            .Where(h => h.Enabled && string.Equals(h.Backend, Name, StringComparison.OrdinalIgnoreCase))
            .Select(h => new NodeCapability(
                NodeId: h.HostId,
                Backend: Name,
                Os: h.Os,
                CpuTotal: 0, // Host reporting will populate real values
                MemTotalMb: 0,
                HasGpu: false,
                AllowRiskyDirectExec: h.AllowSsh,
                Labels: h.Labels ?? new Dictionary<string, string>()))
            .ToList();
    }

    public async Task<string> StartAsync(
        string ownerUserId,
        StartSessionRequest request,
        PlacementDecision placement,
        Func<SessionEvent, Task> emit,
        CancellationToken ct)
    {
        var host = await _hostRegistry.GetAsync(placement.NodeId, ct)
            ?? throw new InvalidOperationException($"Host {placement.NodeId} not found in registry.");

        if (string.IsNullOrEmpty(host.Address))
            throw new InvalidOperationException($"Host {placement.NodeId} has no SSH address configured.");

        // Create SSH connection
        var connection = _connectionFactory.Create(host.Address!, _sshUsername, _sshKeyPath);
        try
        {
            await connection.ConnectAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException(
                $"Cannot connect to host {placement.NodeId} at {host.Address}: {ex.Message}", ex);
        }

        var sessionId = $"ssh_{Guid.NewGuid():N}";

        // Worktree creation (before agent start)
        string? worktreePath = null;
        string? worktreeBranch = null;

        // Resolve repo path: explicit request > host default > git rev-parse
        string? repoRoot = request.RepoPath;
        if (string.IsNullOrEmpty(repoRoot))
            repoRoot = host.DefaultRepoPath;

        if (!string.IsNullOrEmpty(request.WorktreeId))
        {
            if (string.IsNullOrEmpty(repoRoot))
            {
                var repoRootCmd = "git rev-parse --show-toplevel 2>/dev/null";
                repoRoot = (await connection.ExecuteCommandAsync(repoRootCmd, ct)).Trim();
            }
            if (string.IsNullOrEmpty(repoRoot))
                throw new InvalidOperationException(
                    "Could not determine git repo root. Set a default repo path on the host or provide one in the request.");

            worktreeBranch = BranchNameGenerator.Generate(sessionId, request.Prompt);
            var wtPath = $"{repoRoot}/.worktrees/{sessionId}";
            worktreePath = await _worktreeService.CreateWorktreeAsync(
                connection, repoRoot, wtPath, worktreeBranch, ct);
        }

        // Build agent CLI command to execute directly via SSH (not through daemon)
        var isWindows = host.Os.Contains("windows", StringComparison.OrdinalIgnoreCase);
        var agentCliCommand = BuildAgentCommand(request, worktreePath, isWindows);
        _logger.LogInformation("SSH agent command for {SessionId}: {Command}", sessionId, agentCliCommand);

        // Persist session to database before starting (so SSE subscribers can find it)
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = new SessionEntity
        {
            SessionId = sessionId,
            OwnerUserId = ownerUserId,
            State = SessionState.Running,
            CreatedUtc = DateTimeOffset.UtcNow,
            Backend = Name,
            Node = placement.NodeId,
            AgentType = request.ImageOrProfile,
            RequirementsJson = JsonSerializer.Serialize(request.Requirements),
            WorktreePath = worktreePath ?? $"/sessions/{sessionId}",
            WorktreeBranch = worktreeBranch,
            KeepBranch = request.KeepBranch,
            RepoPath = repoRoot,
            RiskAcceptedBy = ownerUserId,
            IsFireAndForget = request.IsFireAndForget,
            Prompt = request.Prompt ?? request.Reason,
            CleanupPolicy = "auto"
        };
        db.Sessions.Add(entity);
        await db.SaveChangesAsync(ct);

        // Track connection
        _connections[sessionId] = connection;

        // Emit state changed event
        await emit(new SessionEvent(sessionId, SessionEventKind.StateChanged, DateTimeOffset.UtcNow, "Running"));

        // Stream agent output in background via PTY-allocated ShellStream for real-time delivery
        _ = Task.Run(async () => await ReadAgentOutputAsync(sessionId, agentCliCommand, connection, emit), CancellationToken.None);

        _logger.LogInformation(
            "Started session {SessionId} on {Host} (fire-and-forget: {IsFireAndForget})",
            sessionId, placement.NodeId, request.IsFireAndForget);

        return sessionId;
    }

    public async Task<bool> SendInputAsync(string sessionId, SendInputRequest request, CancellationToken ct)
    {
        if (!_connections.TryGetValue(sessionId, out var connection))
            throw new InvalidOperationException($"No active SSH connection for session {sessionId}.");

        if (!connection.IsConnected)
            throw new InvalidOperationException($"SSH connection for session {sessionId} is disconnected.");

        try
        {
            var command = HostCommandProtocol.CreateSendInput(sessionId, request.Input, request.IsFollowUp);
            var commandJson = HostCommandProtocol.Serialize(command);
            var responseJson = await connection.ExecuteCommandAsync(commandJson, ct);
            var response = HostCommandProtocol.DeserializeResponse(responseJson);
            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deliver input to session {SessionId}", sessionId);
            return false;
        }
    }

    public async Task StopAsync(string sessionId, bool forceKill, CancellationToken ct)
    {
        if (forceKill)
        {
            // Force-kill: send force-kill command immediately
            await SendCommandToSession(sessionId, HostCommandProtocol.CreateForceKill(sessionId), ct);
            await UpdateSessionStateAsync(sessionId, SessionState.Stopped, ct);
            // Force-kill keeps worktree by default; only cleanup if CleanupPolicy is explicitly "cleanup"
            await CleanupWorktreeIfNeeded(sessionId, forceKill: true, ct);
            await CleanupConnection(sessionId);
            _logger.LogInformation("Force-killed session {SessionId}", sessionId);
            return;
        }

        // Phase 1: Graceful stop (SIGINT)
        await SendCommandToSession(sessionId, HostCommandProtocol.CreateStopSession(sessionId), ct);
        _logger.LogInformation("Sent graceful stop to session {SessionId}, waiting {GracePeriod}s", sessionId, _gracePeriod.TotalSeconds);

        // Wait grace period
        try
        {
            await Task.Delay(_gracePeriod, ct);
        }
        catch (OperationCanceledException)
        {
            // Cancellation during grace period -- force kill
            await SendCommandToSession(sessionId, HostCommandProtocol.CreateForceKill(sessionId), CancellationToken.None);
            await UpdateSessionStateAsync(sessionId, SessionState.Stopped, CancellationToken.None);
            await CleanupConnection(sessionId);
            return;
        }

        // Phase 2: Check if still running, force-kill if needed
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var session = await db.Sessions.FindAsync(new object[] { sessionId }, ct);
        if (session is not null && session.State == SessionState.Running)
        {
            _logger.LogWarning("Session {SessionId} still running after grace period, sending force-kill", sessionId);
            await SendCommandToSession(sessionId, HostCommandProtocol.CreateForceKill(sessionId), ct);
        }

        await UpdateSessionStateAsync(sessionId, SessionState.Stopped, ct);
        await CleanupWorktreeIfNeeded(sessionId, forceKill: false, ct);
        await CleanupConnection(sessionId);
    }

    public async Task<SessionSummary?> GetAsync(string sessionId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Sessions.FindAsync(new object[] { sessionId }, ct);
        return entity?.ToDto();
    }

    public async Task<IReadOnlyList<SessionSummary>> ListAsync(string ownerUserId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entities = await db.Sessions
            .Where(s => s.OwnerUserId == ownerUserId && s.Backend == Name)
            .OrderByDescending(s => s.CreatedUtc)
            .ToListAsync(ct);
        return entities.Select(e => e.ToDto()).ToList();
    }

    // --- Private helpers ---

    private async Task CleanupWorktreeIfNeeded(string sessionId, bool forceKill, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var session = await db.Sessions.FindAsync(new object[] { sessionId }, ct);
        if (session?.WorktreeBranch is null) return; // Not a worktree session

        // Force-kill keeps worktree by default; only cleanup if CleanupPolicy is explicitly "cleanup"
        if (forceKill && session.CleanupPolicy != "cleanup") return;

        // Determine cleanup policy for graceful stop
        var shouldCleanup = session.CleanupPolicy switch
        {
            "keep" => false,
            "cleanup" => true,
            _ => true // "auto": clean on graceful stop
        };

        if (!shouldCleanup) return;

        var keepBranch = session.KeepBranch;

        // Get or create SSH connection for cleanup
        ISshHostConnection? connection = null;
        var needsDispose = false;
        if (_connections.TryGetValue(sessionId, out connection))
        {
            // Use existing connection
        }
        else
        {
            // Create fresh connection for cleanup
            var host = await _hostRegistry.GetAsync(session.Node!, ct);
            if (host?.Address is null) return;
            connection = _connectionFactory.Create(host.Address, _sshUsername, _sshKeyPath);
            await connection.ConnectAsync(ct);
            needsDispose = true;
        }

        try
        {
            var repoRoot = session.RepoPath;
            if (string.IsNullOrEmpty(repoRoot))
                repoRoot = (await connection.ExecuteCommandAsync("git rev-parse --show-toplevel 2>/dev/null", ct)).Trim();
            if (!string.IsNullOrEmpty(repoRoot))
            {
                await _worktreeService.CleanupWorktreeAsync(
                    connection, repoRoot, session.WorktreePath!, session.WorktreeBranch,
                    sessionId, keepBranch: keepBranch, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Worktree cleanup failed for session {SessionId}", sessionId);
        }
        finally
        {
            if (needsDispose && connection is not null)
                await connection.DisposeAsync();
        }
    }


    private async Task SendCommandToSession(string sessionId, HostCommand command, CancellationToken ct)
    {
        if (!_connections.TryGetValue(sessionId, out var connection))
        {
            _logger.LogWarning("No active connection for session {SessionId}, cannot send command", sessionId);
            return;
        }

        try
        {
            var json = HostCommandProtocol.Serialize(command);
            await connection.ExecuteCommandAsync(json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send command to session {SessionId}", sessionId);
        }
    }

    private async Task UpdateSessionStateAsync(string sessionId, SessionState state, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Sessions.FindAsync(new object[] { sessionId }, ct);
        if (entity is not null)
        {
            entity.State = state;
            if (state is SessionState.Stopped or SessionState.Failed)
            {
                entity.CompletedUtc = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task CleanupConnection(string sessionId)
    {
        if (_connections.TryRemove(sessionId, out var connection))
        {
            await connection.DisposeAsync();
        }
    }

    /// <summary>
    /// Builds the shell command to invoke the agent CLI directly via SSH.
    /// Uses cmd.exe syntax for Windows hosts, bash syntax for Linux/macOS.
    /// </summary>
    private static string BuildAgentCommand(StartSessionRequest request, string? worktreePath, bool isWindows)
    {
        var agentCmd = request.ImageOrProfile switch
        {
            "ClaudeCode" or "claude-code" => "claude",
            "Codex" => "codex",
            "Aider" => "aider",
            _ => request.ImageOrProfile.ToLowerInvariant()
        };

        var args = new List<string>();

        if (agentCmd == "claude")
        {
            args.Add("--print");
            if (request.Requirements.AcceptRisk)
                args.Add("--dangerously-skip-permissions");
        }

        var prompt = request.Prompt ?? request.Reason ?? "";
        if (!string.IsNullOrEmpty(prompt))
        {
            if (isWindows)
            {
                // cmd.exe: double-quote the prompt, escape inner double quotes
                var escaped = prompt.Replace("\"", "\\\"");
                args.Add($"\"{escaped}\"");
            }
            else
            {
                // bash: single-quote the prompt, escape inner single quotes
                var escaped = prompt.Replace("'", "'\\''");
                args.Add($"'{escaped}'");
            }
        }

        var fullCommand = $"{agentCmd} {string.Join(' ', args)}";

        // cd to working directory if specified
        if (!string.IsNullOrEmpty(worktreePath))
        {
            if (isWindows)
                fullCommand = $"cd /d \"{worktreePath}\" && {fullCommand}";
            else
                fullCommand = $"cd '{worktreePath}' && {fullCommand}";
        }

        return fullCommand;
    }

    /// <summary>
    /// Streams agent output via PTY-allocated ShellStream for real-time line delivery.
    /// </summary>
    private async Task ReadAgentOutputAsync(
        string sessionId,
        string agentCommand,
        ISshHostConnection connection,
        Func<SessionEvent, Task> emit)
    {
        try
        {
            await connection.StartStreamingCommandAsync(agentCommand, async line =>
            {
                await emit(new SessionEvent(sessionId, SessionEventKind.StdOut, DateTimeOffset.UtcNow, line));
            }, async () =>
            {
                await emit(new SessionEvent(sessionId, SessionEventKind.Heartbeat, DateTimeOffset.UtcNow, "waiting"));
            }, CancellationToken.None);

            // Session completed
            await emit(new SessionEvent(sessionId, SessionEventKind.SessionCompleted, DateTimeOffset.UtcNow, "Session completed"));
            await emit(new SessionEvent(sessionId, SessionEventKind.StateChanged, DateTimeOffset.UtcNow, "Stopped"));
            await UpdateSessionStateAsync(sessionId, SessionState.Stopped, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error reading agent output for session {SessionId}", sessionId);
            await emit(new SessionEvent(sessionId, SessionEventKind.StateChanged, DateTimeOffset.UtcNow,
                $"Failed ({ex.Message})"));
            await UpdateSessionStateAsync(sessionId, SessionState.Failed, CancellationToken.None);
        }
        finally
        {
            await CleanupWorktreeIfNeeded(sessionId, forceKill: false, CancellationToken.None);
            await CleanupConnection(sessionId);
        }
    }
}
