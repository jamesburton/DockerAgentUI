using AgentHub.Contracts;
using AgentHub.Orchestration.Backends;
using Microsoft.Extensions.Logging;

namespace AgentHub.Orchestration.Worktree;

/// <summary>
/// Manages git worktree lifecycle via SSH connections: create, cleanup, diff, and orphan detection.
/// </summary>
public sealed class WorktreeService
{
    private readonly ILogger<WorktreeService> _logger;

    public WorktreeService(ILogger<WorktreeService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Create a git worktree on the remote host.
    /// </summary>
    public async Task<string> CreateWorktreeAsync(
        ISshHostConnection connection, string repoRoot, string worktreePath,
        string branchName, CancellationToken ct)
    {
        var cmd = $"cd {ShellEscape(repoRoot)} && git worktree add -b {ShellEscape(branchName)} {ShellEscape(worktreePath)} HEAD 2>&1";
        var output = await connection.ExecuteCommandAsync(cmd, ct);

        if (output.Contains("fatal:", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("error:", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Git worktree creation failed: {Output}", output);
            throw new InvalidOperationException($"Git worktree creation failed: {output.Trim()}");
        }

        _logger.LogInformation("Created worktree at {Path} on branch {Branch}", worktreePath, branchName);
        return worktreePath;
    }

    /// <summary>
    /// Cleanup a git worktree: stash uncommitted work, remove worktree, optionally delete branch.
    /// </summary>
    public async Task CleanupWorktreeAsync(
        ISshHostConnection connection, string repoRoot, string worktreePath,
        string branchName, string sessionId, bool keepBranch, CancellationToken ct)
    {
        // Step 1: Stash uncommitted work
        var stashCmd = $"cd {ShellEscape(worktreePath)} && git stash push -m {ShellEscape($"agenthub-auto-stash-{sessionId}")} 2>&1 || true";
        await connection.ExecuteCommandAsync(stashCmd, ct);
        _logger.LogInformation("Stashed work in worktree {Path}", worktreePath);

        // Step 2: Remove worktree
        var removeCmd = $"cd {ShellEscape(repoRoot)} && git worktree remove {ShellEscape(worktreePath)} --force 2>&1";
        await connection.ExecuteCommandAsync(removeCmd, ct);
        _logger.LogInformation("Removed worktree at {Path}", worktreePath);

        // Step 3: Delete branch (unless keepBranch is true)
        if (!keepBranch)
        {
            var branchCmd = $"cd {ShellEscape(repoRoot)} && git branch -D {ShellEscape(branchName)} 2>&1";
            await connection.ExecuteCommandAsync(branchCmd, ct);
            _logger.LogInformation("Deleted branch {Branch}", branchName);
        }
    }

    /// <summary>
    /// Get diff stats between the default branch and the worktree branch.
    /// </summary>
    public async Task<DiffStats> GetDiffStatsAsync(
        ISshHostConnection connection, string repoRoot, string branchName, CancellationToken ct)
    {
        // Detect default branch
        var defaultBranchCmd = $"cd {ShellEscape(repoRoot)} && git symbolic-ref refs/remotes/origin/HEAD 2>/dev/null | sed 's|refs/remotes/origin/||' || echo main";
        var defaultBranch = (await connection.ExecuteCommandAsync(defaultBranchCmd, ct)).Trim();
        if (string.IsNullOrEmpty(defaultBranch)) defaultBranch = "main";

        // Get numstat
        var numstatCmd = $"cd {ShellEscape(repoRoot)} && git diff --numstat {ShellEscape(defaultBranch)}...{ShellEscape(branchName)} 2>&1";
        var numstatOutput = await connection.ExecuteCommandAsync(numstatCmd, ct);

        // Get stat
        var statCmd = $"cd {ShellEscape(repoRoot)} && git diff --stat {ShellEscape(defaultBranch)}...{ShellEscape(branchName)} 2>&1";
        var statOutput = await connection.ExecuteCommandAsync(statCmd, ct);

        return DiffStatsParser.Parse(numstatOutput.Trim(), statOutput.Trim());
    }

    /// <summary>
    /// Find worktrees that belong to agenthub but have no active session.
    /// </summary>
    public async Task<List<string>> FindOrphanedWorktreesAsync(
        ISshHostConnection connection, string repoRoot, IReadOnlyList<string> activeSessionIds, CancellationToken ct)
    {
        var cmd = $"cd {ShellEscape(repoRoot)} && git worktree list --porcelain 2>&1";
        var output = await connection.ExecuteCommandAsync(cmd, ct);

        var orphanPaths = new List<string>();
        string? currentPath = null;
        string? currentBranch = null;

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("worktree "))
            {
                // Save previous entry if it was an orphan
                if (currentPath is not null && currentBranch is not null)
                {
                    if (IsOrphanedAgentHubWorktree(currentBranch, activeSessionIds))
                        orphanPaths.Add(currentPath);
                }
                currentPath = trimmed["worktree ".Length..];
                currentBranch = null;
            }
            else if (trimmed.StartsWith("branch refs/heads/"))
            {
                currentBranch = trimmed["branch refs/heads/".Length..];
            }
            else if (string.IsNullOrEmpty(trimmed))
            {
                // End of entry block
            }
        }

        // Check last entry
        if (currentPath is not null && currentBranch is not null)
        {
            if (IsOrphanedAgentHubWorktree(currentBranch, activeSessionIds))
                orphanPaths.Add(currentPath);
        }

        return orphanPaths;
    }

    /// <summary>
    /// Remove orphaned worktrees and their branches.
    /// </summary>
    public async Task CleanupOrphanedAsync(
        ISshHostConnection connection, string repoRoot, List<string> orphanPaths, CancellationToken ct)
    {
        // First, get the branch names for each orphan path
        var listCmd = $"cd {ShellEscape(repoRoot)} && git worktree list --porcelain 2>&1";
        var output = await connection.ExecuteCommandAsync(listCmd, ct);

        var pathToBranch = ParseWorktreeListForBranches(output);

        foreach (var path in orphanPaths)
        {
            // Remove worktree
            var removeCmd = $"cd {ShellEscape(repoRoot)} && git worktree remove {ShellEscape(path)} --force 2>&1";
            await connection.ExecuteCommandAsync(removeCmd, ct);

            // Delete branch if found
            if (pathToBranch.TryGetValue(path, out var branch))
            {
                var branchCmd = $"cd {ShellEscape(repoRoot)} && git branch -D {ShellEscape(branch)} 2>&1";
                await connection.ExecuteCommandAsync(branchCmd, ct);
            }

            _logger.LogInformation("Cleaned up orphaned worktree at {Path}", path);
        }
    }

    private static bool IsOrphanedAgentHubWorktree(string branchName, IReadOnlyList<string> activeSessionIds)
    {
        if (!branchName.StartsWith("agenthub/")) return false;
        return !activeSessionIds.Any(id => branchName.Contains(id, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string> ParseWorktreeListForBranches(string porcelainOutput)
    {
        var result = new Dictionary<string, string>();
        string? currentPath = null;

        foreach (var line in porcelainOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("worktree "))
            {
                currentPath = trimmed["worktree ".Length..];
            }
            else if (trimmed.StartsWith("branch refs/heads/") && currentPath is not null)
            {
                result[currentPath] = trimmed["branch refs/heads/".Length..];
            }
        }

        return result;
    }

    private static string ShellEscape(string value)
    {
        return "'" + value.Replace("'", "'\\''") + "'";
    }
}
