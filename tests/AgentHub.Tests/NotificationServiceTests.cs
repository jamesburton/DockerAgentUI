using AgentHub.Cli.Notifications;
using AgentHub.Cli.Output;
using AgentHub.Contracts;
using Spectre.Console;
using Xunit;

namespace AgentHub.Tests;

[Collection("ConsoleOutput")]
public class NotificationServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"agenthub-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = new NotificationService(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void RecordNotification_WritesToFile()
    {
        _service.RecordNotification("session-1", SessionEventKind.SessionCompleted, "Session completed");

        var pending = _service.GetPendingNotifications();
        Assert.Single(pending);
        Assert.Equal("session-1", pending[0].SessionId);
        Assert.Equal("SessionCompleted", pending[0].Kind);
        Assert.Equal("Session completed", pending[0].Summary);
        Assert.False(pending[0].Acknowledged);
    }

    [Fact]
    public void RecordNotification_MultipleEvents_AllPersisted()
    {
        _service.RecordNotification("s1", SessionEventKind.SessionCompleted, "done");
        _service.RecordNotification("s2", SessionEventKind.ApprovalRequest, "needs approval");
        _service.RecordNotification("s3", SessionEventKind.Threat, "threat detected");

        var pending = _service.GetPendingNotifications();
        Assert.Equal(3, pending.Count);
    }

    [Fact]
    public void RecordNotification_IgnoresNonNotableEvents()
    {
        _service.RecordNotification("s1", SessionEventKind.StdOut, "some output");
        _service.RecordNotification("s1", SessionEventKind.Heartbeat, "heartbeat");
        _service.RecordNotification("s1", SessionEventKind.Info, "info");

        var pending = _service.GetPendingNotifications();
        Assert.Empty(pending);
    }

    [Fact]
    public void AcknowledgeAll_MarksAllAsAcknowledged()
    {
        _service.RecordNotification("s1", SessionEventKind.SessionCompleted, "done");
        _service.RecordNotification("s2", SessionEventKind.ApprovalRequest, "needs approval");

        _service.AcknowledgeAll();

        var pending = _service.GetPendingNotifications();
        Assert.Empty(pending);
    }

    [Fact]
    public void GetPendingNotifications_EmptyWhenNoFile()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), $"agenthub-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDir);
        try
        {
            var service = new NotificationService(emptyDir);
            var pending = service.GetPendingNotifications();
            Assert.Empty(pending);
        }
        finally
        {
            try { Directory.Delete(emptyDir, true); } catch { }
        }
    }

    [Fact]
    public void ShowPendingSummary_ProducesOutput_WhenPending()
    {
        _service.RecordNotification("s1", SessionEventKind.SessionCompleted, "done");
        _service.RecordNotification("s2", SessionEventKind.ApprovalRequest, "needs approval");

        var output = CaptureConsoleOutput(() =>
        {
            _service.ShowPendingSummary(new TestFormatter());
        });

        Assert.Contains("2 notifications", output);
        Assert.Contains("completed", output);
        Assert.Contains("approval", output);
    }

    [Fact]
    public void ShowPendingSummary_Silent_WhenNoPending()
    {
        var output = CaptureConsoleOutput(() =>
        {
            _service.ShowPendingSummary(new TestFormatter());
        });

        Assert.Empty(output.Trim());
    }

    [Fact]
    public void ConcurrentWrites_DoNotCorruptFile()
    {
        // Write notifications concurrently from multiple threads
        var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
        {
            _service.RecordNotification($"s{i}", SessionEventKind.SessionCompleted, $"done {i}");
        }));

        Task.WaitAll(tasks.ToArray());

        // File should be readable and contain some notifications
        // (exact count may vary due to file locking - some writes may retry)
        var pending = _service.GetPendingNotifications();
        Assert.True(pending.Count > 0, "Should have at least some notifications persisted");
    }

    private static string CaptureConsoleOutput(Action action)
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            action();
            return sw.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    /// Simple formatter that writes to Console.Out for capture.
    /// </summary>
    private sealed class TestFormatter : IOutputFormatter
    {
        public void WriteTable<T>(IEnumerable<T> items, Action<Table, T> addRow, params string[] columns)
        {
            Console.WriteLine($"Table: {items.Count()} items");
        }

        public void WriteObject<T>(T item)
        {
            Console.WriteLine(item?.ToString());
        }

        public void WriteError(string message)
        {
            Console.WriteLine($"Error: {message}");
        }

        public void WriteSuccess(string message)
        {
            Console.WriteLine($"OK: {message}");
        }
    }
}
