using System.Text.Json;
using AgentHub.Cli.Config;
using AgentHub.Cli.Output;
using AgentHub.Contracts;

namespace AgentHub.Cli.Notifications;

/// <summary>
/// Manages notification persistence to ~/.agenthub/notifications.json.
/// Records notable events, rings terminal bell, and shows pending summaries.
/// </summary>
public sealed class NotificationService
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public NotificationService(string? notificationsDir = null)
    {
        var dir = notificationsDir ?? CliConfig.ConfigDir;
        _filePath = Path.Combine(dir, "notifications.json");
    }

    /// <summary>
    /// Record a notable notification. Rings terminal bell for ApprovalRequest and Failed events.
    /// </summary>
    public void RecordNotification(string sessionId, SessionEventKind kind, string summary)
    {
        // Only record notable events
        if (kind is not (SessionEventKind.SessionCompleted
            or SessionEventKind.ApprovalRequest
            or SessionEventKind.StateChanged
            or SessionEventKind.Threat))
            return;

        var notification = new StoredNotification(
            sessionId, kind.ToString(), summary, DateTimeOffset.UtcNow, false);

        var notifications = ReadNotifications();
        notifications.Add(notification);
        WriteNotifications(notifications);

        // Ring terminal bell for urgent events
        if (kind is SessionEventKind.ApprovalRequest or SessionEventKind.Threat)
        {
            Console.Write('\a');
        }
        else if (kind == SessionEventKind.StateChanged &&
                 summary.Contains("Failed", StringComparison.OrdinalIgnoreCase))
        {
            Console.Write('\a');
        }
    }

    /// <summary>
    /// Get all unacknowledged notifications.
    /// </summary>
    public List<StoredNotification> GetPendingNotifications()
    {
        return ReadNotifications().Where(n => !n.Acknowledged).ToList();
    }

    /// <summary>
    /// Mark all notifications as acknowledged.
    /// </summary>
    public void AcknowledgeAll()
    {
        var notifications = ReadNotifications();
        var updated = notifications
            .Select(n => n with { Acknowledged = true })
            .ToList();
        WriteNotifications(updated);
    }

    /// <summary>
    /// Show a compact summary of pending notifications if any exist.
    /// Called before each command execution.
    /// </summary>
    public void ShowPendingSummary(IOutputFormatter formatter)
    {
        var pending = GetPendingNotifications();
        if (pending.Count == 0)
            return;

        var completedCount = pending.Count(n => n.Kind == nameof(SessionEventKind.SessionCompleted));
        var approvalCount = pending.Count(n => n.Kind == nameof(SessionEventKind.ApprovalRequest));
        var failedCount = pending.Count(n =>
            n.Kind == nameof(SessionEventKind.StateChanged) &&
            n.Summary.Contains("Failed", StringComparison.OrdinalIgnoreCase));
        var threatCount = pending.Count(n => n.Kind == nameof(SessionEventKind.Threat));

        var parts = new List<string>();
        if (completedCount > 0) parts.Add($"{completedCount} completed");
        if (approvalCount > 0) parts.Add($"{approvalCount} approval pending");
        if (failedCount > 0) parts.Add($"{failedCount} failed");
        if (threatCount > 0) parts.Add($"{threatCount} threat");

        var detail = parts.Count > 0 ? string.Join(", ", parts) : "events";
        formatter.WriteSuccess($"{pending.Count} notifications: {detail}. Run `ah listen` for details.");
    }

    private List<StoredNotification> ReadNotifications()
    {
        if (!File.Exists(_filePath))
            return [];

        try
        {
            using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.None);
            return JsonSerializer.Deserialize<List<StoredNotification>>(fs, s_json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void WriteNotifications(List<StoredNotification> notifications)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var fs = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(fs, notifications, s_json);
    }
}

public sealed record StoredNotification(
    string SessionId,
    string Kind,
    string Summary,
    DateTimeOffset Timestamp,
    bool Acknowledged);
