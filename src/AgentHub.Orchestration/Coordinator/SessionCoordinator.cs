using AgentHub.Contracts;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentHub.Orchestration.Coordinator;

public sealed class SessionCoordinator(
    IEnumerable<ISessionBackend> backends,
    IPlacementEngine placement,
    ISanitizationService sanitizer,
    ISkillRegistry skills,
    ISkillPolicyService policy,
    ApprovalService approval,
    IDbContextFactory<AgentHubDbContext> dbFactory) : ISessionCoordinator
{
    private readonly IReadOnlyList<ISessionBackend> _backends = backends.ToList();

    public async Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(string userId, CancellationToken ct)
    {
        var items = new List<SessionSummary>();
        foreach (var backend in _backends)
            items.AddRange(await backend.ListAsync(userId, ct));
        return items.OrderByDescending(x => x.CreatedUtc).ToArray();
    }

    public async Task<SessionSummary?> GetSessionAsync(string sessionId, string userId, CancellationToken ct)
    {
        foreach (var backend in _backends)
        {
            var result = await backend.GetAsync(sessionId, ct);
            if (result is not null && result.OwnerUserId == userId)
                return result;
        }

        return null;
    }

    public async Task<string> StartSessionAsync(string userId, StartSessionRequest request, Func<SessionEvent, Task> emit, CancellationToken ct)
    {
        var inventory = new List<NodeCapability>();
        foreach (var backend in _backends)
            inventory.AddRange(await backend.GetInventoryAsync(ct));

        var placementDecision = placement.ChooseNode(userId, request.Requirements, inventory);
        var node = inventory.First(x => x.NodeId == placementDecision.NodeId && x.Backend == placementDecision.Backend);
        var selectedBackend = _backends.First(x => string.Equals(x.Name, node.Backend, StringComparison.OrdinalIgnoreCase));

        if (!selectedBackend.CanHandle(request.Requirements, node))
            throw new InvalidOperationException($"Backend {selectedBackend.Name} cannot handle the selected request.");

        // Resolve prompt: Prompt field first, fall back to Reason, then empty string
        var resolvedPrompt = request.Prompt ?? request.Reason ?? string.Empty;
        var resolvedRequest = request with { Prompt = resolvedPrompt };

        return await selectedBackend.StartAsync(userId, resolvedRequest, placementDecision, emit, ct);
    }

    public async Task SendInputAsync(string userId, string sessionId, SendInputRequest request, Func<SessionEvent, Task> emit, CancellationToken ct)
    {
        var session = await GetSessionAsync(sessionId, userId, ct) ?? throw new InvalidOperationException("Session not found.");
        var skill = string.IsNullOrWhiteSpace(request.SkillId) ? null : skills.TryGet(request.SkillId!);

        if (!policy.IsAllowed(request.SkillId, session, request.RequiresElevation))
        {
            await emit(new SessionEvent(sessionId, SessionEventKind.Policy, DateTimeOffset.UtcNow,
                $"Denied skill {request.SkillId ?? "<raw-input>"} by policy.",
                new Dictionary<string, string> { ["decision"] = "deny" }));
            throw new InvalidOperationException("Skill is not enabled by policy.");
        }

        var decision = sanitizer.Evaluate(request, session, skill);
        if (!decision.Allowed)
        {
            await emit(new SessionEvent(sessionId, SessionEventKind.Threat, DateTimeOffset.UtcNow,
                string.Join("; ", decision.Reasons),
                new Dictionary<string, string> { ["risk"] = decision.Risk.ToString() }));
            throw new InvalidOperationException("Input blocked by sanitizer.");
        }

        var backend = _backends.First(x => string.Equals(x.Name, session.Backend, StringComparison.OrdinalIgnoreCase));
        await emit(new SessionEvent(sessionId, SessionEventKind.Audit, DateTimeOffset.UtcNow,
            $"Input accepted for backend {backend.Name}.",
            new Dictionary<string, string>
            {
                ["skillId"] = request.SkillId ?? string.Empty,
                ["risk"] = decision.Risk.ToString()
            }));

        await backend.SendInputAsync(sessionId, request with { Input = decision.NormalizedInput }, ct);
    }

    public async Task StopSessionAsync(string userId, string sessionId, CancellationToken ct)
    {
        await StopSessionAsync(userId, sessionId, forceKill: false, ct);
    }

    public async Task StopSessionAsync(string userId, string sessionId, bool forceKill, CancellationToken ct)
    {
        var session = await GetSessionAsync(sessionId, userId, ct) ?? throw new InvalidOperationException("Session not found.");
        var backend = _backends.First(x => string.Equals(x.Name, session.Backend, StringComparison.OrdinalIgnoreCase));
        await backend.StopAsync(sessionId, forceKill, ct);
    }

    public async Task<(IReadOnlyList<SessionSummary> Items, int TotalCount)> GetSessionHistoryAsync(
        string userId, int skip, int take, string? stateFilter, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = db.Sessions
            .Where(s => s.OwnerUserId == userId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(stateFilter) && Enum.TryParse<SessionState>(stateFilter, ignoreCase: true, out var state))
        {
            query = query.Where(s => s.State == state);
        }

        var totalCount = await query.CountAsync(ct);

        var entities = await query
            .OrderByDescending(s => s.CreatedUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var items = entities.Select(e => new SessionSummary(
            e.SessionId,
            e.OwnerUserId,
            e.State,
            e.CreatedUtc,
            e.Backend,
            e.Node,
            System.Text.Json.JsonSerializer.Deserialize<SessionRequirements>(e.RequirementsJson) ?? new SessionRequirements(),
            e.WorktreePath,
            e.RiskAcceptedBy
        )).ToList();

        return (items, totalCount);
    }
}
