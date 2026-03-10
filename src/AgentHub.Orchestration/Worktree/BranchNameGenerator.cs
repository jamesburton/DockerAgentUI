using System.Text.RegularExpressions;

namespace AgentHub.Orchestration.Worktree;

/// <summary>
/// Generates git branch names for worktree sessions using a slugified prompt.
/// Format: agenthub/{shortSessionId}-{slug}
/// </summary>
public static partial class BranchNameGenerator
{
    private const int MaxSlugLength = 40;
    private const int MaxWordCount = 5;
    private const int ShortIdLength = 8;
    private const string Prefix = "agenthub/";

    /// <summary>
    /// Generate a branch name from a session ID and optional prompt text.
    /// Falls back to timestamp when prompt is null/empty/whitespace.
    /// </summary>
    public static string Generate(string sessionId, string? prompt)
    {
        var shortId = sessionId.Length > ShortIdLength
            ? sessionId[..ShortIdLength]
            : sessionId;

        var slug = string.IsNullOrWhiteSpace(prompt)
            ? DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmm")
            : Slugify(prompt);

        return $"{Prefix}{shortId}-{slug}";
    }

    private static string Slugify(string text)
    {
        // Lowercase
        var slug = text.ToLowerInvariant();

        // Replace non-alphanumeric with hyphens
        slug = NonAlphanumericRegex().Replace(slug, "-");

        // Collapse multiple hyphens
        slug = MultiHyphenRegex().Replace(slug, "-");

        // Trim leading/trailing hyphens
        slug = slug.Trim('-');

        // Limit word count
        var words = slug.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > MaxWordCount)
        {
            slug = string.Join('-', words.Take(MaxWordCount));
        }

        // Limit total length
        if (slug.Length > MaxSlugLength)
        {
            slug = slug[..MaxSlugLength].TrimEnd('-');
        }

        return slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRegex();

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultiHyphenRegex();
}
