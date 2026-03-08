using AgentHub.Contracts;

namespace AgentHub.Orchestration.Placement;

public sealed class SimplePlacementEngine : IPlacementEngine
{
    public PlacementDecision ChooseNode(string ownerUserId, SessionRequirements req, IReadOnlyList<NodeCapability> inventory)
    {
        IEnumerable<NodeCapability> q = inventory.Where(x => true);

        if (req.ExecutionMode == ExecutionMode.Nomad)
            q = q.Where(n => string.Equals(n.Backend, "nomad", StringComparison.OrdinalIgnoreCase));
        else if (req.ExecutionMode == ExecutionMode.Ssh)
            q = q.Where(n => string.Equals(n.Backend, "ssh", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(req.TargetHostId))
            q = q.Where(n => string.Equals(n.NodeId, req.TargetHostId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(req.Os))
            q = q.Where(n => string.Equals(n.Os, req.Os, StringComparison.OrdinalIgnoreCase));

        if (req.NeedsGpu)
            q = q.Where(n => n.HasGpu);

        if (req.CpuMin is not null)
            q = q.Where(n => n.CpuTotal >= req.CpuMin.Value);

        if (req.MemMinMb is not null)
            q = q.Where(n => n.MemTotalMb >= req.MemMinMb.Value);

        if (req.ExecutionMode == ExecutionMode.Ssh && !req.AcceptRisk)
            throw new InvalidOperationException("SSH execution requires AcceptRisk=true.");

        if (req.Labels is not null && req.Labels.Count > 0)
            q = q.Where(n => req.Labels.All(kv => n.Labels.TryGetValue(kv.Key, out var v) && v == kv.Value));

        var chosen = q.FirstOrDefault() ?? throw new InvalidOperationException("No eligible node found for requirements.");
        return new PlacementDecision(chosen.Backend, chosen.NodeId);
    }
}
