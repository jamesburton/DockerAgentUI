using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using CliWrap;
using CliWrap.EventStream;
using Microsoft.Extensions.Logging;

namespace AgentHub.Orchestration.Agents;

/// <summary>
/// Agent adapter for Claude Code CLI.
/// Translates AgentStartRequest into claude CLI invocations using CliWrap,
/// parses --output-format stream-json output defensively.
/// </summary>
public sealed class ClaudeCodeAdapter : IAgentAdapter
{
    private readonly ILogger<ClaudeCodeAdapter> _logger;

    public ClaudeCodeAdapter(ILogger<ClaudeCodeAdapter> logger)
    {
        _logger = logger;
    }

    public string AgentType => "claude-code";

    public IReadOnlyList<string> BuildCommandArgs(AgentStartRequest request)
    {
        var args = new List<string>();

        // Prompt
        args.Add("-p");
        args.Add(request.Prompt);

        // Output format
        args.Add("--output-format");
        args.Add("stream-json");

        // No session persistence (stateless invocations)
        args.Add("--no-session-persistence");

        // Merge permissions: session overrides win over agent-type defaults
        var permissions = PermissionMerger.Merge(
            request.Permissions,
            request.AgentConfig?.DefaultPermissions);

        // Permission flags
        if (permissions.SkipPermissionPrompts)
        {
            args.Add("--dangerously-skip-permissions");
        }

        if (permissions.AllowedTools is { Length: > 0 })
        {
            args.Add("--allowedTools");
            foreach (var tool in permissions.AllowedTools)
                args.Add(tool);
        }

        if (permissions.DisallowedTools is { Length: > 0 })
        {
            args.Add("--disallowedTools");
            foreach (var tool in permissions.DisallowedTools)
                args.Add(tool);
        }

        return args;
    }

    public async Task<AgentProcess> StartAsync(AgentStartRequest request, CancellationToken ct)
    {
        var cliCommand = request.AgentConfig?.CliCommand ?? "claude";
        var args = BuildCommandArgs(request);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var channel = Channel.CreateUnbounded<AgentOutputLine>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        var cmd = Cli.Wrap(cliCommand)
            .WithArguments(args)
            .WithWorkingDirectory(request.WorkingDirectory)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(line =>
            {
                var outputLine = ParseOutputLine(line);
                channel.Writer.TryWrite(outputLine);
            }))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(line =>
            {
                channel.Writer.TryWrite(new AgentOutputLine(
                    AgentOutputKind.StdErr, line, DateTimeOffset.UtcNow));
            }))
            .WithValidation(CommandResultValidation.None);

        // Apply environment variables
        if (request.Environment is { Count: > 0 })
        {
            cmd = cmd.WithEnvironmentVariables(env =>
            {
                foreach (var kvp in request.Environment)
                    env.Set(kvp.Key, kvp.Value);
            });
        }

        var completion = Task.Run(async () =>
        {
            try
            {
                var result = await cmd.ExecuteAsync(cts.Token);
                _logger.LogInformation(
                    "Claude Code process exited with code {ExitCode} for session {SessionId}",
                    result.ExitCode, request.SessionId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Claude Code process cancelled for session {SessionId}", request.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Claude Code process failed for session {SessionId}", request.SessionId);
                channel.Writer.TryWrite(new AgentOutputLine(
                    AgentOutputKind.Error, ex.Message, DateTimeOffset.UtcNow));
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, CancellationToken.None);

        return new AgentProcess(
            Output: ReadChannel(channel.Reader, cts.Token),
            SendInput: _ => Task.CompletedTask, // Claude Code CLI doesn't support stdin in -p mode
            Stop: async () =>
            {
                await cts.CancelAsync();
                cts.Dispose();
            },
            Completion: completion);
    }

    private AgentOutputLine ParseOutputLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var kind = AgentOutputKind.Json;
            if (root.TryGetProperty("type", out var typeProp))
            {
                var typeStr = typeProp.GetString();
                kind = typeStr switch
                {
                    "tool_use" => AgentOutputKind.ToolUse,
                    "result" => AgentOutputKind.Result,
                    "error" => AgentOutputKind.Error,
                    "system" => AgentOutputKind.System,
                    _ => AgentOutputKind.Json
                };
            }

            return new AgentOutputLine(kind, line, DateTimeOffset.UtcNow);
        }
        catch (JsonException)
        {
            // Defensive: if not valid JSON, treat as plain stdout
            _logger.LogDebug("Non-JSON stdout line from Claude Code: {Line}", line);
            return new AgentOutputLine(AgentOutputKind.StdOut, line, DateTimeOffset.UtcNow);
        }
    }

    private static async IAsyncEnumerable<AgentOutputLine> ReadChannel(
        ChannelReader<AgentOutputLine> reader,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var item in reader.ReadAllAsync(ct))
        {
            yield return item;
        }
    }
}
