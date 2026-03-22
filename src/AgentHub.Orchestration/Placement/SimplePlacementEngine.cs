using AgentHub.Contracts;
using AgentHub.Orchestration.Coordinator;
using AgentHub.Orchestration.Monitoring;
using Microsoft.Extensions.Options;

namespace AgentHub.Orchestration.Placement;

public sealed class SimplePlacementEngine : IPlacementEngine
{
    private readonly PlacementOptions _options;
    private readonly HostMetricCache _metricCache;
    private readonly ActiveSessionTracker _sessionTracker;

    public SimplePlacementEngine(
        IOptions<PlacementOptions> options,
        HostMetricCache metricCache,
        ActiveSessionTracker sessionTracker)
    {
        _options = options.Value;
        _metricCache = metricCache;
        _sessionTracker = sessionTracker;
    }

    public PlacementDecision ChooseNode(string ownerUserId, SessionRequirements req, IReadOnlyList<NodeCapability> inventory)
    {
        IEnumerable<NodeCapability> q = inventory.Where(x => true);

        // --- Existing filter logic (preserved) ---

        if (req.ExecutionMode == ExecutionMode.Nomad)
            q = q.Where(n => string.Equals(n.Backend, "nomad", StringComparison.OrdinalIgnoreCase));
        else if (req.ExecutionMode == ExecutionMode.Ssh)
            q = q.Where(n => string.Equals(n.Backend, "ssh", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(req.TargetHostId))
        {
            // TargetHostId bypass: skip scoring, return matching node directly
            var targeted = q.FirstOrDefault(n => string.Equals(n.NodeId, req.TargetHostId, StringComparison.OrdinalIgnoreCase));
            if (targeted is not null)
                return new PlacementDecision(targeted.Backend, targeted.NodeId);
            throw new InvalidOperationException($"Targeted host '{req.TargetHostId}' not found or does not match requirements.");
        }

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

        // --- Weighted scoring (Phase 10 upgrade) ---

        var candidates = q.ToList();
        var scored = candidates
            .Select(n => new { Node = n, Score = ScoreNode(n.NodeId) })
            .Where(x => x.Score >= 0)
            .OrderByDescending(x => x.Score)
            .ToList();

        if (scored.Count == 0)
            throw new InvalidOperationException("No eligible node found for requirements.");

        var chosen = scored[0].Node;
        return new PlacementDecision(chosen.Backend, chosen.NodeId);
    }

    /// <summary>
    /// Scores a node based on CPU availability, memory availability, and active session count.
    /// Returns -1 to exclude a node (no metrics, stale metrics, low disk, at session cap).
    /// </summary>
    internal double ScoreNode(string nodeId)
    {
        var snapshot = _metricCache.Get(nodeId);
        if (snapshot is null)
            return -1;

        // Stale check: exclude if metrics older than 60 seconds
        if (snapshot.MetricsUpdatedUtc is null)
            return -1;

        var metricsAge = DateTimeOffset.UtcNow - snapshot.MetricsUpdatedUtc.Value;
        if (metricsAge.TotalSeconds > 60)
            return -1;

        // Disk hard filter
        var diskFreeGb = snapshot.Inventory?.DiskFreeGb;
        if (diskFreeGb.HasValue && diskFreeGb.Value < _options.MinDiskFreeGb)
            return -1;

        // Session limit check
        var activeSessions = _sessionTracker.GetCount(nodeId);
        if (activeSessions >= _options.MaxSessionsPerHost)
            return -1;

        // CPU score: higher available CPU = higher score
        double cpuScore = snapshot.CpuPercent.HasValue
            ? (100.0 - snapshot.CpuPercent.Value) / 100.0
            : 0.5;

        // Memory score: higher available memory = higher score
        double memScore = (snapshot.MemTotalMb.HasValue && snapshot.MemTotalMb.Value > 0 && snapshot.MemUsedMb.HasValue)
            ? (double)(snapshot.MemTotalMb.Value - snapshot.MemUsedMb.Value) / snapshot.MemTotalMb.Value
            : 0.5;

        // Session score: fewer sessions = higher score
        double sessionScore = 1.0 - ((double)activeSessions / _options.MaxSessionsPerHost);

        // Composite weighted score
        double composite = cpuScore * _options.CpuWeight
                         + memScore * _options.MemoryWeight
                         + sessionScore * _options.SessionWeight;

        // Stale penalty: if metrics age > 30s (but <= 60s), apply penalty multiplier
        if (metricsAge.TotalSeconds > 30)
            composite *= _options.StaleMetricPenalty;

        return composite;
    }
}
