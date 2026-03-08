using System.Threading.Channels;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AgentHub.Cli.Output;

/// <summary>
/// Thread-safe wrapper for Spectre.Console Live context using a channel-based update pattern.
/// Background SSE consumers post update actions to the channel; the main thread reads them
/// and applies updates inside the Live context.
/// </summary>
public static class LiveDisplayManager
{
    /// <summary>
    /// Run a live-updating table. The updateSource produces actions that mutate the table
    /// on each SSE event. The table is refreshed after each action.
    /// </summary>
    public static async Task RunLiveTableAsync(
        Func<Table> createTable,
        Func<CancellationToken, IAsyncEnumerable<Action<Table>>> updateSource,
        CancellationToken ct)
    {
        var table = createTable();

        await AnsiConsole.Live(table).StartAsync(async ctx =>
        {
            ctx.Refresh();

            await foreach (var update in updateSource(ct))
            {
                update(table);
                ctx.Refresh();
            }
        });
    }

    /// <summary>
    /// Run a live display with a channel-based update pattern for more complex scenarios.
    /// Returns the channel writer so callers can post updates from background tasks.
    /// </summary>
    public static async Task RunWithChannelAsync<T>(
        T renderable,
        Func<Channel<Action<LiveDisplayContext>>, CancellationToken, Task> backgroundWork,
        CancellationToken ct) where T : class, IRenderable
    {
        var channel = Channel.CreateUnbounded<Action<LiveDisplayContext>>(
            new UnboundedChannelOptions { SingleReader = true });

        await AnsiConsole.Live(renderable).StartAsync(async ctx =>
        {
            ctx.Refresh();

            // Start background work that posts to the channel
            var bgTask = backgroundWork(channel, ct);

            // Read updates from channel and apply them
            try
            {
                await foreach (var update in channel.Reader.ReadAllAsync(ct))
                {
                    update(ctx);
                    ctx.Refresh();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Clean exit
            }

            await bgTask;
        });
    }
}
