using AgentHub.Orchestration.Worktree;
using Xunit;

namespace AgentHub.Tests;

public class BranchNameGeneratorTests
{
    [Fact]
    public void Generate_WithPrompt_ReturnsSlugifiedBranch()
    {
        var result = BranchNameGenerator.Generate("abc12345-long-id", "Fix the login bug");

        Assert.Equal("agenthub/abc12345-fix-the-login-bug", result);
    }

    [Fact]
    public void Generate_WithNullPrompt_ReturnsTimestampFallback()
    {
        var result = BranchNameGenerator.Generate("abc12345-long-id", null);

        Assert.StartsWith("agenthub/abc12345-", result);
        // Should match timestamp pattern YYYYMMDD-HHmm
        var suffix = result["agenthub/abc12345-".Length..];
        Assert.Matches(@"^\d{8}-\d{4}$", suffix);
    }

    [Fact]
    public void Generate_WithEmptyPrompt_ReturnsTimestampFallback()
    {
        var result = BranchNameGenerator.Generate("abc12345-long-id", "");

        Assert.StartsWith("agenthub/abc12345-", result);
        var suffix = result["agenthub/abc12345-".Length..];
        Assert.Matches(@"^\d{8}-\d{4}$", suffix);
    }

    [Fact]
    public void Generate_TruncatesLongPrompt_ToMaxWordsAndLength()
    {
        var result = BranchNameGenerator.Generate(
            "abc12345-long-id",
            "A very long prompt with many words that exceeds the limit");

        // Max 5 words, max 40 chars for slug portion
        Assert.StartsWith("agenthub/abc12345-", result);
        var slug = result["agenthub/abc12345-".Length..];
        Assert.True(slug.Length <= 40, $"Slug '{slug}' exceeds 40 chars (was {slug.Length})");
        // Should have at most 5 words (hyphens = word count - 1, max 4 hyphens)
        var wordCount = slug.Split('-', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(wordCount <= 5, $"Slug has {wordCount} words, expected <= 5");
    }

    [Fact]
    public void Generate_SanitizesSpecialCharacters()
    {
        var result = BranchNameGenerator.Generate("abc12345-long-id", "Special chars!@#$%^&*()");

        Assert.StartsWith("agenthub/abc12345-", result);
        var slug = result["agenthub/abc12345-".Length..];
        // Should only contain lowercase alphanumeric and hyphens
        Assert.Matches(@"^[a-z0-9-]+$", slug);
    }

    [Fact]
    public void Generate_UsesFirst8CharsOfSessionId()
    {
        var result = BranchNameGenerator.Generate("abcdefgh-ijklmnop", "test");

        Assert.StartsWith("agenthub/abcdefgh-", result);
    }

    [Fact]
    public void Generate_WhitespaceOnlyPrompt_ReturnsTimestampFallback()
    {
        var result = BranchNameGenerator.Generate("abc12345-long-id", "   ");

        Assert.StartsWith("agenthub/abc12345-", result);
        var suffix = result["agenthub/abc12345-".Length..];
        Assert.Matches(@"^\d{8}-\d{4}$", suffix);
    }
}
