using System.Text.Json;

namespace AgentHub.Orchestration.HostDaemon;

/// <summary>
/// Protocol for host daemon communication.
///
/// Contract: Commands are sent as single-line JSON over SSH stdin.
/// Responses are single-line JSON over SSH stdout.
/// The host daemon reads stdin line by line, processes each command,
/// and writes the response to stdout.
/// </summary>
public static class HostCommandProtocol
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Create a start-session command.
    /// </summary>
    public static HostCommand CreateStartSession(string sessionId, StartSessionPayload payload)
    {
        var payloadElement = JsonSerializer.SerializeToElement(payload, s_options);
        return new HostCommand
        {
            Command = HostCommand.StartSession,
            SessionId = sessionId,
            Payload = payloadElement
        };
    }

    /// <summary>
    /// Create a send-input command for delivering follow-up instructions to a running session.
    /// </summary>
    public static HostCommand CreateSendInput(string sessionId, string input, bool isFollowUp = false)
    {
        var payload = new SendInputPayload { Input = input, IsFollowUp = isFollowUp };
        var payloadElement = JsonSerializer.SerializeToElement(payload, s_options);
        return new HostCommand
        {
            Command = HostCommand.SendInput,
            SessionId = sessionId,
            Payload = payloadElement
        };
    }

    /// <summary>
    /// Create a stop-session command.
    /// </summary>
    public static HostCommand CreateStopSession(string sessionId)
    {
        return new HostCommand
        {
            Command = HostCommand.StopSession,
            SessionId = sessionId
        };
    }

    /// <summary>
    /// Create a report-status command.
    /// </summary>
    public static HostCommand CreateReportStatus()
    {
        return new HostCommand
        {
            Command = HostCommand.ReportStatus
        };
    }

    /// <summary>
    /// Create a ping command.
    /// </summary>
    public static HostCommand CreatePing()
    {
        return new HostCommand
        {
            Command = HostCommand.Ping
        };
    }

    /// <summary>
    /// Create a force-kill command (bypasses graceful shutdown).
    /// </summary>
    public static HostCommand CreateForceKill(string sessionId, string? reason = null)
    {
        ForceKillPayload? payload = reason is not null ? new ForceKillPayload { Reason = reason } : null;
        return new HostCommand
        {
            Command = HostCommand.ForceKill,
            SessionId = sessionId,
            Payload = payload is not null ? JsonSerializer.SerializeToElement(payload, s_options) : null
        };
    }

    /// <summary>
    /// Create an approval-response command.
    /// </summary>
    public static HostCommand CreateApprovalResponse(string sessionId, string approvalId, bool approved, string? reason = null)
    {
        var payload = new ApprovalResponsePayload
        {
            ApprovalId = approvalId,
            Approved = approved,
            Reason = reason
        };
        var payloadElement = JsonSerializer.SerializeToElement(payload, s_options);
        return new HostCommand
        {
            Command = HostCommand.ApprovalResponse,
            SessionId = sessionId,
            Payload = payloadElement
        };
    }

    /// <summary>
    /// Serialize a command to a single-line JSON string (for SSH stdin piping).
    /// </summary>
    public static string Serialize(HostCommand command)
    {
        return JsonSerializer.Serialize(command, s_options);
    }

    /// <summary>
    /// Deserialize a response from a single-line JSON string (from SSH stdout).
    /// </summary>
    public static HostCommandResponse DeserializeResponse(string json)
    {
        return JsonSerializer.Deserialize<HostCommandResponse>(json, s_options)
            ?? throw new JsonException("Failed to deserialize HostCommandResponse");
    }
}
