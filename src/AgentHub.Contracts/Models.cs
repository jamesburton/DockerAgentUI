namespace AgentHub.Contracts;

public enum SessionState
{
    Pending = 0,
    Running = 1,
    Stopped = 2,
    Failed = 3
}

public enum ExecutionMode
{
    Auto = 0,
    Nomad = 1,
    Ssh = 2
}

public enum RiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2
}

public sealed record SessionRequirements(
    string? Os = null,
    int? CpuMin = null,
    int? MemMinMb = null,
    bool NeedsGpu = false,
    string? Isolation = null,
    Dictionary<string, string>? Labels = null,
    ExecutionMode ExecutionMode = ExecutionMode.Auto,
    bool AcceptRisk = false,
    string? TargetHostId = null,
    string? WorktreeId = null,
    string? SharedStorageProfile = null
);

public sealed record StartSessionRequest(
    string ImageOrProfile,
    SessionRequirements Requirements,
    Dictionary<string, string>? Env = null,
    string? WorktreeId = null,
    string? RepoPath = null,
    string? RequestedSkillProfile = null,
    string? Reason = null,
    string? Prompt = null,
    bool IsFireAndForget = false,
    bool KeepBranch = false,
    string? ParentSessionId = null
);

public sealed record SessionSummary(
    string SessionId,
    string OwnerUserId,
    SessionState State,
    DateTimeOffset CreatedUtc,
    string Backend,
    string? Node,
    SessionRequirements Requirements,
    string? WorktreePath = null,
    string? RiskAcceptedBy = null,
    string? WorktreeBranch = null,
    string? ParentSessionId = null
);

public sealed record SendInputRequest(
    string Input,
    bool IsBinary = false,
    string? SkillId = null,
    Dictionary<string, string>? Arguments = null,
    bool RequiresElevation = false,
    bool IsFollowUp = false);

public enum SessionEventKind
{
    Info,
    StdOut,
    StdErr,
    StateChanged,
    Metric,
    Custom,
    Policy,
    Threat,
    Audit,
    // Phase 2 additions:
    ApprovalRequest,
    ApprovalResponse,
    Heartbeat,
    SessionCompleted,
    CleanupStarted,
    CleanupCompleted,
    HostMetrics,
    SteeringInput,
    SteeringDelivered,
    // Phase 10 additions:
    ChildSpawned,
    ChildCompleted,
    ChildFailed
}

public sealed record SessionEvent(
    string SessionId,
    SessionEventKind Kind,
    DateTimeOffset TsUtc,
    string Data,
    Dictionary<string, string>? Meta = null
);

public sealed record HostRecord(
    string HostId,
    string DisplayName,
    string Backend,
    string Os,
    bool Enabled,
    bool AllowSsh,
    Dictionary<string, string>? Labels = null,
    string? Address = null,
    double? CpuPercent = null,
    long? MemUsedMb = null,
    long? MemTotalMb = null,
    HostInventory? Inventory = null,
    string? DefaultRepoPath = null);

public sealed record HostInventory(
    List<AgentInfo> Agents,
    double? DiskFreeGb,
    string? GitVersion);

public sealed record AgentInfo(
    string Name,
    string? Version,
    string? Path,
    List<string>? Capabilities);

public sealed record SkillManifest(
    string Id,
    string DisplayName,
    string Category,
    RiskLevel Risk,
    SkillExecDefinition Exec,
    string[]? Capabilities = null,
    SkillConstraints? Constraints = null);

public sealed record SkillExecDefinition(
    string Type,
    PlatformCommand? Windows = null,
    PlatformCommand? Linux = null);

public sealed record PlatformCommand(
    string File,
    string[] Args);

public sealed record SkillConstraints(
    int MaxSeconds = 900,
    bool AllowNetwork = false,
    bool AllowFilesystemWrite = true,
    bool RequiresElevation = false,
    string? WorkingDir = null);

public sealed record SanitizationDecision(
    bool Allowed,
    string NormalizedInput,
    RiskLevel Risk,
    string[] Reasons,
    string[]? SuggestedBlocks = null);

public sealed record PolicySnapshot(
    string Name,
    DateTimeOffset LoadedUtc,
    string[] EnabledSkills,
    string[] DisabledSkills,
    string[] Notes);

public sealed record WorktreeDescriptor(
    string WorktreeId,
    string RepoUrl,
    string Ref,
    bool Shallow,
    bool Sparse,
    string[]? SparsePaths = null);

public sealed record DiffStats(
    List<FileDiffStat> Files,
    int TotalInsertions,
    int TotalDeletions,
    string Summary);

public sealed record FileDiffStat(
    string Path,
    string Status,
    int Insertions,
    int Deletions);
