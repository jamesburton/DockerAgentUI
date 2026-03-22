using AgentHub.Contracts;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using AgentHub.Orchestration.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentHub.Orchestration.Coordinator;

public sealed class SessionCoordinator(
    IEnumerable<ISessionBackend> backends,
    IPlacementEngine placement,
    ISanitizationService sanitizer,
    ISkillRegistry skills,
    ISkillPolicyService policy,
    ApprovalService approval,
    IDbContextFactory<AgentHubDbContext> dbFactory,
    IOptions<CoordinationOptions> coordinationOptions,
    ActiveSessionTracker sessionTracker) : ISessionCoordinator
{
    private readonly CoordinationOptions _coordinationOptions = coordinationOptions.Value;
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
        // Validate cascade limits if spawning as a child
        if (!string.IsNullOrEmpty(request.ParentSessionId))
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await ValidateCascadeLimitsAsync(db, request.ParentSessionId, _coordinationOptions);
        }

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

        var sessionId = await selectedBackend.StartAsync(userId, resolvedRequest, placementDecision, emit, ct);

        // Track active session count
        sessionTracker.Increment(placementDecision.NodeId);

        // Set parent-child FK in DB if spawning as a child
        if (!string.IsNullOrEmpty(request.ParentSessionId))
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var entity = await db.Sessions.FindAsync(new object[] { sessionId }, ct);
            if (entity is not null)
            {
                entity.ParentSessionId = request.ParentSessionId;
                await db.SaveChangesAsync(ct);
            }

            // Emit ChildSpawned event on parent's stream
            await emit(new SessionEvent(
                request.ParentSessionId,
                SessionEventKind.ChildSpawned,
                DateTimeOffset.UtcNow,
                $"Child session {sessionId} spawned",
                new Dictionary<string, string>
                {
                    ["childSessionId"] = sessionId,
                    ["node"] = placementDecision.NodeId
                }));
        }

        return sessionId;
    }

    public async Task<bool> SendInputAsync(string userId, string sessionId, SendInputRequest request, Func<SessionEvent, Task> emit, CancellationToken ct)
    {
        var session = await GetSessionAsync(sessionId, userId, ct) ?? throw new InvalidOperationException("Session not found.");

        // If session is stopped and input is a follow-up, try to resume with --resume
        if (session.State == SessionState.Stopped && !string.IsNullOrWhiteSpace(request.Input))
        {
            var resumeBackend = _backends.First(x => string.Equals(x.Name, session.Backend, StringComparison.OrdinalIgnoreCase));
            var resumed = await resumeBackend.ResumeSessionAsync(sessionId, request.Input, emit, ct);
            if (resumed)
                return true;
            // If resume not supported, fall through to normal flow (which will likely fail on delivery)
        }

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

        // Trust tier evaluation: check if action requires approval gating
        var trustTierDecision = sanitizer.EvaluateWithTrustTier(request.SkillId ?? request.Input ?? "", policy: null);

        if (trustTierDecision.Denied)
        {
            await emit(new SessionEvent(sessionId, SessionEventKind.Policy, DateTimeOffset.UtcNow,
                $"Action denied by trust tier policy (tier: {trustTierDecision.Tier}).",
                new Dictionary<string, string> { ["decision"] = "deny", ["tier"] = trustTierDecision.Tier.ToString() }));
            throw new InvalidOperationException("Action denied by trust tier policy.");
        }

        if (trustTierDecision.RequiresApproval)
        {
            var approvalContext = new ApprovalContext(
                Action: request.SkillId ?? "raw-input",
                Command: request.Input,
                FilePath: null,
                DiffPreview: null,
                TimeoutSeconds: null,
                TimeoutAction: null,
                SkipPermissionPrompts: session.Requirements.AcceptRisk);

            var approvalResult = await approval.RequestApprovalAsync(sessionId, approvalContext, emit, ct);

            if (approvalResult is ApprovalDecision.Denied or ApprovalDecision.TimedOut)
            {
                await emit(new SessionEvent(sessionId, SessionEventKind.Policy, DateTimeOffset.UtcNow,
                    $"Action denied by approval ({approvalResult}).",
                    new Dictionary<string, string> { ["decision"] = approvalResult.ToString() }));
                throw new InvalidOperationException($"Action denied by approval ({approvalResult}).");
            }
        }

        var backend = _backends.First(x => string.Equals(x.Name, session.Backend, StringComparison.OrdinalIgnoreCase));
        await emit(new SessionEvent(sessionId, SessionEventKind.Audit, DateTimeOffset.UtcNow,
            $"Input accepted for backend {backend.Name}.",
            new Dictionary<string, string>
            {
                ["skillId"] = request.SkillId ?? string.Empty,
                ["risk"] = decision.Risk.ToString()
            }));

        // Emit steering event before backend call when this is a follow-up instruction
        if (request.IsFollowUp)
        {
            await emit(new SessionEvent(sessionId, SessionEventKind.SteeringInput, DateTimeOffset.UtcNow,
                request.Input,
                new Dictionary<string, string> { ["isFollowUp"] = "true" }));
        }

        var delivered = await backend.SendInputAsync(sessionId, request with { Input = decision.NormalizedInput }, ct);

        // Emit delivery confirmation or warning for follow-up instructions
        if (request.IsFollowUp)
        {
            if (delivered)
            {
                await emit(new SessionEvent(sessionId, SessionEventKind.SteeringDelivered, DateTimeOffset.UtcNow,
                    "Steering command delivered"));
            }
            else
            {
                await emit(new SessionEvent(sessionId, SessionEventKind.Info, DateTimeOffset.UtcNow,
                    "Warning: Delivery unconfirmed -- host daemon did not acknowledge",
                    new Dictionary<string, string> { ["warning"] = "delivery-unconfirmed" }));
            }
        }

        return delivered;
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

        // Decrement active session tracker
        if (session.Node is not null)
            sessionTracker.Decrement(session.Node);

        // Check for orphaned children
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var activeChildCount = await db.Sessions
            .CountAsync(s => s.ParentSessionId == sessionId && s.State == SessionState.Running, ct);

        if (activeChildCount > 0)
        {
            // Emit via a no-op since we don't have emit callback in stop -- use a direct event approach
            // We need to store a warning; since we don't have the emit callback here,
            // we persist the event directly to DB
            var warningEvent = new SessionEvent(
                sessionId,
                SessionEventKind.Info,
                DateTimeOffset.UtcNow,
                $"Warning: {activeChildCount} child session(s) still active",
                new Dictionary<string, string> { ["warning"] = "orphaned-children" });

            var eventEntity = warningEvent.ToEntity();
            db.Events.Add(eventEntity);
            await db.SaveChangesAsync(ct);
        }
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

        // SQLite does not support DateTimeOffset in ORDER BY; fetch user's sessions
        // and sort/paginate in memory (acceptable for user-scoped queries).
        var allEntities = await query.ToListAsync(ct);
        var totalCount = allEntities.Count;

        var entities = allEntities
            .OrderByDescending(s => s.CreatedUtc)
            .Skip(skip)
            .Take(take)
            .ToList();

        var items = entities.Select(e => new SessionSummary(
            e.SessionId,
            e.OwnerUserId,
            e.State,
            e.CreatedUtc,
            e.Backend,
            e.Node,
            System.Text.Json.JsonSerializer.Deserialize<SessionRequirements>(e.RequirementsJson) ?? new SessionRequirements(),
            e.WorktreePath,
            e.RiskAcceptedBy,
            e.WorktreeBranch,
            e.ParentSessionId
        )).ToList();

        return (items, totalCount);
    }

    /// <summary>
    /// Validates that spawning a child from the given parent session does not exceed
    /// cascade depth or child count limits. Throws InvalidOperationException if limits exceeded.
    /// Static for testability -- Plan 02 will wire this into StartSessionAsync.
    /// </summary>
    public static async Task ValidateCascadeLimitsAsync(AgentHubDbContext db, string parentSessionId, CoordinationOptions options)
    {
        // 1. Compute depth by walking ParentSessionId chain
        var depth = 0;
        var currentId = parentSessionId;
        var maxIterations = options.MaxDepth + 1; // safety bound

        for (var i = 0; i < maxIterations && currentId is not null; i++)
        {
            var session = await db.Sessions
                .AsNoTracking()
                .Where(s => s.SessionId == currentId)
                .Select(s => new { s.ParentSessionId })
                .FirstOrDefaultAsync();

            if (session?.ParentSessionId is null)
                break;

            depth++;
            currentId = session.ParentSessionId;
        }

        if (depth >= options.MaxDepth)
            throw new InvalidOperationException(
                $"Cascade depth limit exceeded: current depth is {depth}, maximum allowed depth is {options.MaxDepth}.");

        // 2. Count active children of this parent
        var activeChildCount = await db.Sessions
            .CountAsync(s => s.ParentSessionId == parentSessionId && s.State == SessionState.Running);

        if (activeChildCount >= options.MaxChildrenPerParent)
            throw new InvalidOperationException(
                $"Maximum children per parent exceeded: {activeChildCount} active children, limit is {options.MaxChildrenPerParent}.");
    }
}
