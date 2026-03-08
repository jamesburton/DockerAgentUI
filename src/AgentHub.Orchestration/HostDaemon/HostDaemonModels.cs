using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentHub.Orchestration.HostDaemon;

/// <summary>
/// A command sent from the coordinator to a host daemon.
/// Commands are serialized as single-line JSON for SSH stdin piping.
/// </summary>
public sealed record HostCommand
{
    [JsonPropertyName("command")]
    public string Command { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0";

    [JsonPropertyName("sessionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SessionId { get; init; }

    [JsonPropertyName("payload")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Payload { get; init; }

    /// <summary>Well-known command: start a new agent session on this host.</summary>
    public const string StartSession = "start-session";

    /// <summary>Well-known command: stop a running session.</summary>
    public const string StopSession = "stop-session";

    /// <summary>Well-known command: report host status (resources, active sessions).</summary>
    public const string ReportStatus = "report-status";

    /// <summary>Well-known command: connectivity check.</summary>
    public const string Ping = "ping";

    /// <summary>Well-known command: force-kill a session (bypasses graceful shutdown).</summary>
    public const string ForceKill = "force-kill";

    /// <summary>Well-known command: respond to an approval request.</summary>
    public const string ApprovalResponse = "approval-response";
}

/// <summary>
/// Payload for the start-session command.
/// </summary>
public sealed record StartSessionPayload
{
    [JsonPropertyName("agentType")]
    public string AgentType { get; init; } = "";

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = "";

    [JsonPropertyName("workingDirectory")]
    public string WorkingDirectory { get; init; } = "";

    [JsonPropertyName("environment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Environment { get; init; }

    [JsonPropertyName("permissions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PermissionPayload? Permissions { get; init; }
}

/// <summary>
/// Permission configuration in host daemon protocol payloads.
/// </summary>
public sealed record PermissionPayload
{
    [JsonPropertyName("skipPermissionPrompts")]
    public bool SkipPermissionPrompts { get; init; }

    [JsonPropertyName("allowedTools")]
    public string[]? AllowedTools { get; init; }

    [JsonPropertyName("disallowedTools")]
    public string[]? DisallowedTools { get; init; }
}

/// <summary>
/// Response from a host daemon to a command.
/// Sent as single-line JSON over SSH stdout.
/// </summary>
public sealed record HostCommandResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("command")]
    public string Command { get; init; } = "";

    [JsonPropertyName("sessionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SessionId { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Data { get; init; }
}

/// <summary>
/// Payload for the force-kill command.
/// </summary>
public sealed record ForceKillPayload
{
    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }
}

/// <summary>
/// Payload for the approval-response command.
/// </summary>
public sealed record ApprovalResponsePayload
{
    [JsonPropertyName("approvalId")]
    public string ApprovalId { get; init; } = "";

    [JsonPropertyName("approved")]
    public bool Approved { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }
}

/// <summary>
/// Status report from a host daemon, returned as data in a report-status response.
/// </summary>
public sealed record HostStatusReport
{
    [JsonPropertyName("hostId")]
    public string HostId { get; init; } = "";

    [JsonPropertyName("uptimeSeconds")]
    public double UptimeSeconds { get; init; }

    [JsonPropertyName("activeSessions")]
    public int ActiveSessions { get; init; }

    [JsonPropertyName("cpuPercent")]
    public double CpuPercent { get; init; }

    [JsonPropertyName("memoryUsedMb")]
    public long MemoryUsedMb { get; init; }

    [JsonPropertyName("agentsAvailable")]
    public string[] AgentsAvailable { get; init; } = Array.Empty<string>();
}
