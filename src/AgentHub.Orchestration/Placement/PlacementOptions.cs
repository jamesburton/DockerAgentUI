namespace AgentHub.Orchestration.Placement;

public sealed class PlacementOptions
{
    public double CpuWeight { get; set; } = 0.4;
    public double MemoryWeight { get; set; } = 0.3;
    public double SessionWeight { get; set; } = 0.3;
    public double MinDiskFreeGb { get; set; } = 5.0;
    public double StaleMetricPenalty { get; set; } = 0.5;
    public int MaxSessionsPerHost { get; set; } = 5;
}
