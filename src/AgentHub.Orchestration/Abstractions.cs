using AgentHub.Contracts;
using AgentHub.Orchestration.Config;
using AgentHub.Orchestration.Security;

namespace AgentHub.Orchestration;

public sealed record NodeCapability(
    string NodeId,
    string Backend,
    string Os,
    int CpuTotal,
    int MemTotalMb,
    bool HasGpu,
    bool AllowRiskyDirectExec,
    Dictionary<string, string> Labels
);

public sealed record PlacementDecision(
    string Backend,
    string NodeId,
    Dictionary<string, string>? Extra = null
);

public interface IPlacementEngine
{
    PlacementDecision ChooseNode(
        string ownerUserId,
        SessionRequirements requirements,
        IReadOnlyList<NodeCapability> inventory);
}

public interface ISessionBackend
{
    string Name { get; }
    bool CanHandle(SessionRequirements requirements, NodeCapability node);
    Task<IReadOnlyList<NodeCapability>> GetInventoryAsync(CancellationToken ct);
    Task<string> StartAsync(string ownerUserId, StartSessionRequest request, PlacementDecision placement, Func<SessionEvent, Task> emit, CancellationToken ct);
    Task<bool> SendInputAsync(string sessionId, SendInputRequest request, CancellationToken ct);
    Task StopAsync(string sessionId, bool forceKill, CancellationToken ct);
    Task<SessionSummary?> GetAsync(string sessionId, CancellationToken ct);
    Task<IReadOnlyList<SessionSummary>> ListAsync(string ownerUserId, CancellationToken ct);
}

public interface IHostRegistry
{
    Task<IReadOnlyList<HostRecord>> ListAsync(CancellationToken ct);
    Task<HostRecord?> GetAsync(string hostId, CancellationToken ct);
}

public interface ISanitizationService
{
    SanitizationDecision Evaluate(SendInputRequest request, SessionSummary session, SkillManifest? skill);
    TrustTierDecision EvaluateWithTrustTier(string action, ScopedPolicyConfig? policy);
}

public interface ISkillRegistry
{
    IReadOnlyList<SkillManifest> GetAll();
    SkillManifest? TryGet(string skillId);
}

public interface ISkillPolicyService
{
    PolicySnapshot GetPolicySnapshot();
    bool IsAllowed(string? skillId, SessionSummary session, bool elevated);
}

public interface ISharedStorageProvider
{
    string Name { get; }
    Task<string> MaterializeAsync(string worktreeId, string destinationRoot, CancellationToken ct);
    Task UploadTextAsync(string worktreeId, string relativePath, string content, CancellationToken ct);
}

public interface IWorktreeProvider
{
    Task<string> EnsureMaterializedAsync(WorktreeDescriptor descriptor, string destinationRoot, CancellationToken ct);
}

public interface ISessionCoordinator
{
    Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(string userId, CancellationToken ct);
    Task<SessionSummary?> GetSessionAsync(string sessionId, string userId, CancellationToken ct);
    Task<string> StartSessionAsync(string userId, StartSessionRequest request, Func<SessionEvent, Task> emit, CancellationToken ct);
    Task<bool> SendInputAsync(string userId, string sessionId, SendInputRequest request, Func<SessionEvent, Task> emit, CancellationToken ct);
    Task StopSessionAsync(string userId, string sessionId, CancellationToken ct);
    Task StopSessionAsync(string userId, string sessionId, bool forceKill, CancellationToken ct);
    Task<(IReadOnlyList<SessionSummary> Items, int TotalCount)> GetSessionHistoryAsync(string userId, int skip, int take, string? stateFilter, CancellationToken ct);
}
