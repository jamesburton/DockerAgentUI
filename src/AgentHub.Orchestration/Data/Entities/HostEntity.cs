namespace AgentHub.Orchestration.Data.Entities;

public class HostEntity
{
    public string HostId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Backend { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool AllowSsh { get; set; }
    public string? LabelsJson { get; set; }
    public string? Address { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public string Status { get; set; } = "unknown";
    public double? CpuPercent { get; set; }
    public long? MemUsedMb { get; set; }
    public long? MemTotalMb { get; set; }
    public DateTimeOffset? MetricsUpdatedUtc { get; set; }
    public string? InventoryJson { get; set; }
    public string? DefaultRepoPath { get; set; }
}
