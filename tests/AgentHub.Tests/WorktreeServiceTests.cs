using AgentHub.Orchestration.Worktree;
using AgentHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentHub.Tests;

public class WorktreeServiceTests
{
    private static WorktreeService CreateService()
        => new(NullLogger<WorktreeService>.Instance);

    [Fact]
    public async Task CreateWorktreeAsync_SendsCorrectGitCommand()
    {
        var service = CreateService();
        var conn = new MockSshHostConnection();
        // Enqueue success response for git worktree add
        conn.EnqueueResponse("Preparing worktree (new branch 'agenthub/abc-test')");

        var result = await service.CreateWorktreeAsync(
            conn, "/repo", "/repo/.worktrees/session1", "agenthub/abc-test", CancellationToken.None);

        Assert.Equal("/repo/.worktrees/session1", result);
        Assert.Single(conn.CommandsSent);
        var cmd = conn.CommandsSent[0];
        Assert.Contains("git worktree add", cmd);
        Assert.Contains("agenthub/abc-test", cmd);
        Assert.Contains("/repo/.worktrees/session1", cmd);
    }

    [Fact]
    public async Task CleanupWorktreeAsync_SendsStashThenRemoveThenBranchDelete()
    {
        var service = CreateService();
        var conn = new MockSshHostConnection();
        // Enqueue responses for stash, remove, branch-delete
        conn.EnqueueResponse("No local changes to save");
        conn.EnqueueResponse("");
        conn.EnqueueResponse("Deleted branch agenthub/abc-test");

        await service.CleanupWorktreeAsync(
            conn, "/repo", "/repo/.worktrees/session1", "agenthub/abc-test",
            "session1", keepBranch: false, CancellationToken.None);

        Assert.Equal(3, conn.CommandsSent.Count);
        Assert.Contains("git stash", conn.CommandsSent[0]);
        Assert.Contains("git worktree remove", conn.CommandsSent[1]);
        Assert.Contains("git branch -D", conn.CommandsSent[2]);
    }

    [Fact]
    public async Task CleanupWorktreeAsync_WithKeepBranch_SkipsBranchDelete()
    {
        var service = CreateService();
        var conn = new MockSshHostConnection();
        conn.EnqueueResponse("No local changes to save");
        conn.EnqueueResponse("");

        await service.CleanupWorktreeAsync(
            conn, "/repo", "/repo/.worktrees/session1", "agenthub/abc-test",
            "session1", keepBranch: true, CancellationToken.None);

        Assert.Equal(2, conn.CommandsSent.Count);
        Assert.Contains("git stash", conn.CommandsSent[0]);
        Assert.Contains("git worktree remove", conn.CommandsSent[1]);
    }

    [Fact]
    public async Task GetDiffStatsAsync_ParsesOutput()
    {
        var service = CreateService();
        var conn = new MockSshHostConnection();
        // Response for default branch detection
        conn.EnqueueResponse("main");
        // Response for numstat
        conn.EnqueueResponse("10\t5\tsrc/file.cs");
        // Response for stat
        conn.EnqueueResponse(" src/file.cs | 15 +++++-----\n 1 file changed, 10 insertions(+), 5 deletions(-)");

        var result = await service.GetDiffStatsAsync(
            conn, "/repo", "agenthub/abc-test", CancellationToken.None);

        Assert.Single(result.Files);
        Assert.Equal(10, result.TotalInsertions);
        Assert.Equal(5, result.TotalDeletions);
    }

    [Fact]
    public async Task FindOrphanedWorktreesAsync_FiltersActiveSessionsOut()
    {
        var service = CreateService();
        var conn = new MockSshHostConnection();
        // Porcelain output with two worktrees - one active, one orphaned
        conn.EnqueueResponse(
            "worktree /repo\nHEAD abc123\nbranch refs/heads/main\n\n" +
            "worktree /repo/.worktrees/session-active\nHEAD def456\nbranch refs/heads/agenthub/active-test\n\n" +
            "worktree /repo/.worktrees/session-orphan\nHEAD ghi789\nbranch refs/heads/agenthub/orphan-test\n");

        var orphans = await service.FindOrphanedWorktreesAsync(
            conn, "/repo", new[] { "active" }, CancellationToken.None);

        Assert.Single(orphans);
        Assert.Equal("/repo/.worktrees/session-orphan", orphans[0]);
    }

    [Fact]
    public async Task CleanupOrphanedAsync_RemovesWorktreeAndBranch()
    {
        var service = CreateService();
        var conn = new MockSshHostConnection();
        // worktree list to get branch name
        conn.EnqueueResponse("worktree /repo/.worktrees/orphan1\nHEAD abc\nbranch refs/heads/agenthub/orphan-branch\n");
        // remove response
        conn.EnqueueResponse("");
        // branch delete response
        conn.EnqueueResponse("Deleted branch");

        await service.CleanupOrphanedAsync(
            conn, "/repo", new List<string> { "/repo/.worktrees/orphan1" }, CancellationToken.None);

        // Should have sent: worktree list (to find branch), worktree remove, branch -D
        Assert.True(conn.CommandsSent.Count >= 2);
    }
}
