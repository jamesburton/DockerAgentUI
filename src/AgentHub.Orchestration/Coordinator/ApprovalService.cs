using System.Collections.Concurrent;
using System.Text.Json;
using AgentHub.Contracts;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentHub.Orchestration.Coordinator;

/// <summary>
/// Decision result from the approval flow.
/// </summary>
public enum ApprovalDecision
{
    Approved,
    Denied,
    TimedOut,
    AutoApproved
}

/// <summary>
/// Context describing the action that requires approval.
/// </summary>
public sealed record ApprovalContext(
    string Action,
    string? Command,
    string? FilePath,
    string? DiffPreview,
    int? TimeoutSeconds,
    string? TimeoutAction,
    bool SkipPermissionPrompts);

/// <summary>
/// Manages the approval/elevation gating flow for destructive agent actions.
/// Registered as singleton; uses ConcurrentDictionary for pending approvals
/// and IDbContextFactory for scoped DB access.
/// </summary>
public sealed class ApprovalService
{
    private readonly IDbContextFactory<AgentHubDbContext> _dbFactory;
    private readonly ILogger<ApprovalService> _logger;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ApprovalDecision>> _pending = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ApprovalService(IDbContextFactory<AgentHubDbContext> dbFactory, ILogger<ApprovalService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Requests approval for a destructive action. Blocks until resolved, timed out, or auto-approved.
    /// </summary>
    public async Task<ApprovalDecision> RequestApprovalAsync(
        string sessionId,
        ApprovalContext context,
        Func<SessionEvent, Task> emit,
        CancellationToken ct)
    {
        // Fast path: skip permissions auto-approves without SSE event or DB record
        if (context.SkipPermissionPrompts)
        {
            _logger.LogDebug("Auto-approving action {Action} for session {SessionId} (SkipPermissionPrompts)", context.Action, sessionId);
            return ApprovalDecision.AutoApproved;
        }

        var approvalId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<ApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[approvalId] = tcs;

        try
        {
            // Persist approval entity to DB
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var entity = new ApprovalEntity
            {
                ApprovalId = approvalId,
                SessionId = sessionId,
                Action = context.Action,
                Context = JsonSerializer.Serialize(context, JsonOpts),
                Status = "pending",
                RequestedUtc = DateTimeOffset.UtcNow,
                TimeoutSeconds = context.TimeoutSeconds,
                TimeoutAction = context.TimeoutAction
            };
            db.Approvals.Add(entity);
            await db.SaveChangesAsync(ct);

            // Emit SSE event with approval context
            var meta = new Dictionary<string, string> { ["approvalId"] = approvalId };
            var eventData = JsonSerializer.Serialize(context, JsonOpts);
            await emit(new SessionEvent(
                sessionId,
                SessionEventKind.ApprovalRequest,
                DateTimeOffset.UtcNow,
                eventData,
                meta));

            // Wait for resolution or timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (context.TimeoutSeconds is > 0)
                cts.CancelAfter(TimeSpan.FromSeconds(context.TimeoutSeconds.Value));

            try
            {
                return await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout fired (not external cancellation)
                var decision = ResolveTimeoutAction(context.TimeoutAction);
                var status = decision == ApprovalDecision.Approved ? "timed-out" : "timed-out";

                await UpdateEntityStatusAsync(approvalId, "timed-out", null, ct);
                _logger.LogInformation("Approval {ApprovalId} timed out, action: {TimeoutAction} -> {Decision}",
                    approvalId, context.TimeoutAction, decision);

                return decision;
            }
        }
        finally
        {
            _pending.TryRemove(approvalId, out _);
        }
    }

    /// <summary>
    /// Resolves a pending approval request. Uses TrySetResult for race-condition safety.
    /// </summary>
    public void ResolveApproval(string approvalId, bool approved, string? resolvedBy)
    {
        if (_pending.TryGetValue(approvalId, out var tcs))
        {
            var decision = approved ? ApprovalDecision.Approved : ApprovalDecision.Denied;
            if (tcs.TrySetResult(decision))
            {
                var status = approved ? "approved" : "denied";
                // Fire-and-forget DB update (best effort)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await UpdateEntityStatusAsync(approvalId, status, resolvedBy, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to update approval entity {ApprovalId} status", approvalId);
                    }
                });
            }
            else
            {
                _logger.LogDebug("Approval {ApprovalId} already resolved (race condition handled)", approvalId);
            }
        }
        else
        {
            _logger.LogWarning("Approval {ApprovalId} not found in pending (already resolved or timed out)", approvalId);
        }
    }

    /// <summary>
    /// Returns the IDs of all currently pending approvals. Useful for testing.
    /// </summary>
    public IReadOnlyList<string> GetPendingApprovalIds() => _pending.Keys.ToList();

    private async Task UpdateEntityStatusAsync(string approvalId, string status, string? resolvedBy, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Approvals.FindAsync([approvalId], ct);
        if (entity is not null)
        {
            entity.Status = status;
            entity.ResolvedUtc = DateTimeOffset.UtcNow;
            entity.ResolvedBy = resolvedBy;
            await db.SaveChangesAsync(ct);
        }
    }

    private static ApprovalDecision ResolveTimeoutAction(string? timeoutAction) =>
        timeoutAction?.ToLowerInvariant() switch
        {
            "stop" => ApprovalDecision.Denied,
            "auto-approve" => ApprovalDecision.Approved,
            "continue" => ApprovalDecision.Approved,
            _ => ApprovalDecision.Approved // default: continue
        };
}
