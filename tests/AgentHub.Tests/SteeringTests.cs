using System.Text.Json;
using AgentHub.Contracts;
using AgentHub.Orchestration.HostDaemon;
using Xunit;

namespace AgentHub.Tests;

/// <summary>
/// Tests for steering contracts: IsFollowUp on SendInputRequest,
/// SteeringInput/SteeringDelivered event kinds, and HostCommandProtocol.CreateSendInput.
/// </summary>
public class SteeringContractTests
{
    [Fact]
    public void SendInputRequest_DefaultIsFollowUp_IsFalse()
    {
        // Backward compat: existing callers use positional "new SendInputRequest(text)"
        var request = new SendInputRequest("hello");
        Assert.False(request.IsFollowUp);
    }

    [Fact]
    public void SendInputRequest_WithIsFollowUp_True()
    {
        var request = new SendInputRequest("do this", IsFollowUp: true);
        Assert.True(request.IsFollowUp);
        Assert.Equal("do this", request.Input);
    }

    [Fact]
    public void SendInputRequest_IsFollowUp_SerializesCorrectly()
    {
        var request = new SendInputRequest("test input", IsFollowUp: true);
        var json = JsonSerializer.Serialize(request);
        Assert.Contains("\"IsFollowUp\":true", json);
    }

    [Fact]
    public void SessionEventKind_SteeringInput_ExistsAfterHostMetrics()
    {
        var steeringInput = SessionEventKind.SteeringInput;
        var hostMetrics = SessionEventKind.HostMetrics;
        Assert.True((int)steeringInput > (int)hostMetrics,
            "SteeringInput must be appended after HostMetrics");
    }

    [Fact]
    public void SessionEventKind_SteeringDelivered_ExistsAfterSteeringInput()
    {
        var steeringDelivered = SessionEventKind.SteeringDelivered;
        var steeringInput = SessionEventKind.SteeringInput;
        Assert.True((int)steeringDelivered > (int)steeringInput,
            "SteeringDelivered must come after SteeringInput");
    }

    [Fact]
    public void HostCommandProtocol_CreateSendInput_ProducesCorrectCommand()
    {
        var command = HostCommandProtocol.CreateSendInput("sess1", "do this", true);

        Assert.Equal(HostCommand.SendInput, command.Command);
        Assert.Equal("sess1", command.SessionId);
        Assert.NotNull(command.Payload);

        // Verify payload structure
        var payload = command.Payload!.Value;
        Assert.Equal("do this", payload.GetProperty("input").GetString());
        Assert.True(payload.GetProperty("isFollowUp").GetBoolean());
    }

    [Fact]
    public void HostCommandProtocol_CreateSendInput_DefaultIsFollowUpFalse()
    {
        var command = HostCommandProtocol.CreateSendInput("sess1", "hello");

        var payload = command.Payload!.Value;
        Assert.Equal("hello", payload.GetProperty("input").GetString());
        Assert.False(payload.GetProperty("isFollowUp").GetBoolean());
    }

    [Fact]
    public void HostCommandProtocol_CreateSendInput_SerializesAsJson()
    {
        var command = HostCommandProtocol.CreateSendInput("sess1", "test", true);
        var json = HostCommandProtocol.Serialize(command);

        Assert.Contains("\"command\":\"send-input\"", json);
        Assert.Contains("\"sessionId\":\"sess1\"", json);
    }

    [Fact]
    public void HostCommandProtocol_DeserializeResponse_ParsesSuccess()
    {
        var json = """{"success":true,"command":"send-input","sessionId":"sess1"}""";
        var response = HostCommandProtocol.DeserializeResponse(json);

        Assert.True(response.Success);
        Assert.Equal("send-input", response.Command);
        Assert.Equal("sess1", response.SessionId);
    }

    [Fact]
    public void HostCommandProtocol_DeserializeResponse_ParsesFailure()
    {
        var json = """{"success":false,"command":"send-input","sessionId":"sess1","error":"not found"}""";
        var response = HostCommandProtocol.DeserializeResponse(json);

        Assert.False(response.Success);
        Assert.Equal("not found", response.Error);
    }

    [Fact]
    public void HostCommand_SendInput_ConstantExists()
    {
        Assert.Equal("send-input", HostCommand.SendInput);
    }

    [Fact]
    public void SendInputPayload_HasExpectedProperties()
    {
        var payload = new SendInputPayload { Input = "test", IsFollowUp = true };
        Assert.Equal("test", payload.Input);
        Assert.True(payload.IsFollowUp);
    }
}
