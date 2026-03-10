# Phase 9: Git Worktree Isolation - Research

**Researched:** 2026-03-10
**Domain:** Git worktree lifecycle management via SSH, C# orchestration
**Confidence:** HIGH

## Summary

This phase replaces the stub `GitWorktreeProvider` (which only creates a local directory with a text marker) with real `git worktree` operations executed on remote hosts via SSH. The core workflow is: before agent launch, SSH into the remote host and run `git worktree add -b agenthub/{sessionId}-{slug} <path> HEAD`; on session end, stash uncommitted work, remove the worktree, and optionally delete the branch. A diff stats endpoint lets operators assess merge-readiness.

The existing codebase provides strong scaffolding: `WorktreeDescriptor`, `SessionEntity.WorktreePath`, `SessionEntity.CleanupPolicy`, `StartSessionRequest.WorktreeId`, and the `ISshHostConnection.ExecuteCommandAsync` method for running commands on remote hosts. The main implementation work is (1) a `WorktreeService` that constructs and executes git commands via SSH, (2) hooking worktree create/cleanup into `SshBackend.StartAsync`/`StopAsync`, (3) branch naming with slug generation, (4) diff stats retrieval via `git diff --stat`, and (5) CLI + Web UI surfaces.

**Primary recommendation:** Implement a `WorktreeService` class in `AgentHub.Orchestration` that encapsulates all git worktree SSH commands, then integrate it into `SshBackend` lifecycle methods. Keep git command construction simple -- use `ISshHostConnection.ExecuteCommandAsync` with shell commands, matching the existing pattern used by `HostInventoryPollingService`.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Explicit `--worktree` flag opts in per session -- default is no worktree (agent runs in main checkout)
- Worktree location configurable per host in host config, defaulting to a `.worktrees/` directory inside the repo root (gitignored)
- Worktree based on current HEAD of the default branch (main/master) -- agent always starts from latest stable code
- Git worktree created on remote host via SSH before agent process starts -- part of session launch pipeline in SshBackend
- Branch format: `agenthub/{sessionId}-{slug}` -- namespaced prefix, session ID for uniqueness, slugified prompt summary for readability
- When no prompt is provided (file reference or fire-and-forget), fall back to timestamp suffix: `agenthub/{sessionId}-{YYYYMMDD-HHmm}`
- Cleanup respects the existing per-session CleanupPolicy flag -- `--keep` or `--cleanup` override the default
- Default: clean up worktree on success, keep on failure/kill (carried from Phase 2)
- Branch cleanup is configurable: default delete both worktree and branch, `--keep-branch` flag preserves branch for cherry-picking
- Before cleanup, system runs `git stash` in the worktree to preserve any uncommitted work -- stash accessible from main repo
- Manual cleanup command (`ah worktree cleanup`) for orphaned worktrees from crashed sessions -- API endpoint + CLI command, operator-triggered
- Dedicated `ah session diff {id}` CLI command AND diff summary shown in session detail views (CLI watch + web detail page)
- CLI: default git diffstat format, `--detailed` flag for full Spectre.Console table (file path, status, lines changed)
- Web UI: presentation at Claude's discretion (collapsible panel or tab -- consistent with existing MudBlazor patterns)

### Claude's Discretion
- Git version validation approach (pre-check vs try-and-handle)
- Prompt slugification algorithm (word count, char limit, sanitization rules)
- Fallback naming when no prompt available
- Diff base strategy (worktree creation point vs current HEAD)
- Web UI diff panel design (collapsible vs tab)
- SSH command construction for worktree operations (create, remove, prune)
- Error handling for worktree creation failures

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| WKTREE-01 | System creates a git worktree on the remote host before launching an agent session | WorktreeService.CreateAsync called from SshBackend.StartAsync; uses `git worktree add -b` via SSH |
| WKTREE-02 | System cleans up worktree and branch when session ends (stop or force-kill) | WorktreeService.CleanupAsync called from SshBackend.StopAsync; stash + remove + branch delete |
| WKTREE-03 | Worktree branches are auto-named based on session ID and prompt summary | BranchNameGenerator with slugification; format `agenthub/{sessionId}-{slug}` |
| WKTREE-04 | User can view git diff stats for a completed worktree session (merge-readiness) | WorktreeService.GetDiffStatsAsync; API endpoint + CLI command + Web UI panel |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| SSH.NET (Renci.SshNet) | already in project | Execute git commands on remote hosts | Already used by SshBackend and polling services |
| Spectre.Console | already in project | CLI diff stats table rendering | Already used by SessionWatchCommand |
| MudBlazor | already in project | Web UI diff panel | Already used throughout web app |
| xUnit | already in project | Unit tests | Already used by test project |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Text.RegularExpressions | built-in | Prompt slug sanitization | Branch name generation |
| System.Text.Json | built-in | Parse git diff JSON output | Diff stats parsing |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Raw SSH git commands | LibGit2Sharp | LibGit2Sharp runs locally, not on remote -- SSH is the right approach for this architecture |
| Custom slug algorithm | Slugify NuGet package | Unnecessary dependency for a 10-line function |

**Installation:**
No new packages needed. Everything required is already in the project.

## Architecture Patterns

### Recommended Project Structure
```
src/AgentHub.Orchestration/
  Worktree/
    WorktreeService.cs          # Core git worktree operations via SSH
    BranchNameGenerator.cs      # Slug generation + branch naming
    DiffStatsParser.cs          # Parse git diff --stat output
  Backends/
    SshBackend.cs               # Modified: integrate worktree create/cleanup
  HostDaemon/
    HostCommandProtocol.cs      # No changes needed (use raw SSH commands instead)
src/AgentHub.Contracts/
  Models.cs                     # Add DiffStats record, extend SessionSummary
src/AgentHub.Cli/
  Commands/Session/
    SessionDiffCommand.cs       # New: `ah session diff {id}`
  Commands/Worktree/
    WorktreeCleanupCommand.cs   # New: `ah worktree cleanup`
src/AgentHub.Web/
  Components/Pages/
    SessionDetail.razor          # Modified: add diff panel
  Components/Shared/
    LaunchDialog.razor           # Modified: add worktree toggle
```

### Pattern 1: Git Commands via SSH (matching existing inventory probe pattern)
**What:** Execute git commands on remote hosts using `ISshHostConnection.ExecuteCommandAsync`
**When to use:** All worktree operations (create, remove, stash, diff, prune)
**Example:**
```csharp
// Follows same pattern as HostInventoryPollingService SSH probes
// Commands are constructed as shell strings, not HostCommandProtocol JSON
public async Task<string> CreateWorktreeAsync(
    ISshHostConnection connection,
    string repoRoot,
    string worktreePath,
    string branchName,
    CancellationToken ct)
{
    // Create worktree with new branch from HEAD
    var cmd = $"cd {ShellEscape(repoRoot)} && git worktree add -b {ShellEscape(branchName)} {ShellEscape(worktreePath)} HEAD 2>&1";
    var result = await connection.ExecuteCommandAsync(cmd, ct);
    return worktreePath;
}
```

### Pattern 2: Lifecycle Hook in SshBackend
**What:** Insert worktree creation before agent start, cleanup after agent stop
**When to use:** When `StartSessionRequest.WorktreeId` is non-null (opt-in via `--worktree` flag)
**Example:**
```csharp
// In SshBackend.StartAsync, before building StartSessionPayload:
if (!string.IsNullOrEmpty(request.WorktreeId))
{
    var branchName = BranchNameGenerator.Generate(sessionId, request.Prompt);
    var worktreePath = await _worktreeService.CreateWorktreeAsync(
        connection, repoRoot, worktreeDir, branchName, ct);
    // Use worktreePath as WorkingDirectory in payload instead of /sessions/{id}
}
```

### Pattern 3: Cleanup with Stash-Before-Remove
**What:** Stash uncommitted work before removing worktree to prevent data loss
**When to use:** Always during worktree cleanup
**Example:**
```csharp
// 1. Stash any uncommitted work (stash is visible from main repo)
await connection.ExecuteCommandAsync(
    $"cd {ShellEscape(worktreePath)} && git stash push -m 'agenthub-auto-stash-{sessionId}' 2>&1 || true", ct);

// 2. Remove the worktree
await connection.ExecuteCommandAsync(
    $"cd {ShellEscape(repoRoot)} && git worktree remove {ShellEscape(worktreePath)} --force 2>&1", ct);

// 3. Optionally delete the branch
if (!keepBranch)
    await connection.ExecuteCommandAsync(
        $"cd {ShellEscape(repoRoot)} && git branch -D {ShellEscape(branchName)} 2>&1", ct);
```

### Anti-Patterns to Avoid
- **Using HostCommandProtocol for git commands:** The protocol is for daemon-to-agent communication. Git commands should be raw SSH commands, same as inventory probes.
- **Running git worktree add without -b:** Always create a named branch. Detached HEAD worktrees are confusing for operators.
- **Deleting worktree without stashing first:** Agent may have uncommitted work. Always stash before remove.
- **Hardcoding repo root path:** Must be configurable per host. Different hosts may have repos in different locations.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| SSH command execution | Custom SSH client | `ISshHostConnection.ExecuteCommandAsync` | Already abstracted, mockable for tests |
| Shell argument escaping | Naive string concat | Proper escaping with single-quote wrapping | Prevents command injection from prompt text |
| Diff stat parsing | Custom regex parser | Parse `git diff --numstat` output (tab-separated) | `--numstat` is machine-readable, unlike `--stat` which is for humans |
| Orphan detection | Manual scan | `git worktree list --porcelain` + cross-reference with DB sessions | Git already tracks worktree state |

**Key insight:** Git's built-in worktree management handles most complexity. The system just needs to issue the right commands at the right lifecycle points and parse the output.

## Common Pitfalls

### Pitfall 1: Worktree Lock Files
**What goes wrong:** If a process crashes mid-operation, git leaves `.lock` files that prevent worktree operations.
**Why it happens:** `git worktree add` creates lock files during creation.
**How to avoid:** The `--force` flag on `git worktree remove` bypasses locks. For cleanup of truly orphaned worktrees, `git worktree prune` removes stale administrative files.
**Warning signs:** "fatal: is locked" errors from git commands.

### Pitfall 2: Branch Name Collisions
**What goes wrong:** Two sessions with similar prompts could generate the same branch name.
**Why it happens:** Slug algorithm produces identical output for similar inputs.
**How to avoid:** Session ID is always part of the branch name, guaranteeing uniqueness. The slug is purely for readability.
**Warning signs:** "fatal: a branch named 'X' already exists" from git.

### Pitfall 3: Worktree Path Conflicts
**What goes wrong:** If a previous session's worktree was not properly cleaned up, the path already exists.
**Why it happens:** Session crash, SSH disconnect, or force-kill without cleanup.
**How to avoid:** Check if path exists before creating; if it does, run `git worktree remove --force` first, then recreate. The manual cleanup command (`ah worktree cleanup`) handles bulk cleanup.
**Warning signs:** "fatal: X already exists" from `git worktree add`.

### Pitfall 4: Git Version Incompatibility
**What goes wrong:** `git worktree` was introduced in Git 2.5 (July 2015). Older Git versions fail.
**Why it happens:** Remote hosts may have very old Git installations.
**How to avoid:** Phase 7 already captures `HostInventory.GitVersion`. Use try-and-handle: attempt worktree creation, if it fails with "unknown command", report a clear error. Pre-check is also fine but adds latency.
**Warning signs:** "git: 'worktree' is not a git command" error.

### Pitfall 5: Stash in Wrong Directory
**What goes wrong:** Running `git stash` from the repo root instead of the worktree directory stashes main branch changes.
**Why it happens:** Forgot to `cd` into worktree path before stashing.
**How to avoid:** Always `cd` into worktree path before running stash. Use `&&` chaining to ensure correct directory.
**Warning signs:** Unexpected stash entries on main branch; worktree changes lost.

### Pitfall 6: SSH Connection Reuse
**What goes wrong:** Worktree operations need the same SSH connection used for the session, but the connection may drop between create and cleanup.
**Why it happens:** Long-running sessions may outlast SSH connections.
**How to avoid:** Create a new SSH connection for cleanup operations if the original is gone. The `_connections` dictionary in SshBackend tracks active connections, but cleanup needs a fallback path to create a fresh connection.
**Warning signs:** "No active SSH connection" when trying to clean up.

## Code Examples

### Branch Name Generation
```csharp
// Recommended: simple, testable, deterministic
public static class BranchNameGenerator
{
    private const int MaxSlugLength = 40;
    private const int MaxWordCount = 5;

    public static string Generate(string sessionId, string? prompt)
    {
        var slug = string.IsNullOrWhiteSpace(prompt)
            ? DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmm")
            : Slugify(prompt);

        // Trim session ID to first 8 chars for readability
        var shortId = sessionId.Length > 8 ? sessionId[..8] : sessionId;
        return $"agenthub/{shortId}-{slug}";
    }

    private static string Slugify(string text)
    {
        // Lowercase, replace non-alphanumeric with hyphens, collapse multiple hyphens
        var slug = Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');

        // Limit word count
        var words = slug.Split('-', StringSplitOptions.RemoveEmptyEntries);
        slug = string.Join("-", words.Take(MaxWordCount));

        // Limit total length
        if (slug.Length > MaxSlugLength)
            slug = slug[..MaxSlugLength].TrimEnd('-');

        return slug;
    }
}
```

### Diff Stats Retrieval
```csharp
// Use --numstat for machine-readable output, --stat for human display
public async Task<DiffStats> GetDiffStatsAsync(
    ISshHostConnection connection,
    string repoRoot,
    string branchName,
    CancellationToken ct)
{
    // git diff --numstat main...branchName gives per-file stats
    var numstatOutput = await connection.ExecuteCommandAsync(
        $"cd {ShellEscape(repoRoot)} && git diff --numstat main...{ShellEscape(branchName)} 2>&1", ct);

    // git diff --stat main...branchName gives human-readable summary
    var statOutput = await connection.ExecuteCommandAsync(
        $"cd {ShellEscape(repoRoot)} && git diff --stat main...{ShellEscape(branchName)} 2>&1", ct);

    return DiffStatsParser.Parse(numstatOutput, statOutput);
}
```

### DiffStats Record (for Contracts)
```csharp
public sealed record DiffStats(
    List<FileDiffStat> Files,
    int TotalInsertions,
    int TotalDeletions,
    string Summary);  // Human-readable summary line from --stat

public sealed record FileDiffStat(
    string Path,
    string Status,     // "modified", "added", "deleted", "renamed"
    int Insertions,
    int Deletions);
```

### Worktree Cleanup for Orphans
```csharp
// List all worktrees, cross-reference with active DB sessions
public async Task<List<string>> FindOrphanedWorktreesAsync(
    ISshHostConnection connection,
    string repoRoot,
    IReadOnlyList<string> activeSessionIds,
    CancellationToken ct)
{
    var output = await connection.ExecuteCommandAsync(
        $"cd {ShellEscape(repoRoot)} && git worktree list --porcelain 2>&1", ct);

    var worktrees = ParseWorktreeList(output);
    return worktrees
        .Where(w => w.Branch?.StartsWith("agenthub/") == true)
        .Where(w => !activeSessionIds.Any(id => w.Branch!.Contains(id)))
        .Select(w => w.Path)
        .ToList();
}
```

### Shell Escape Utility
```csharp
// Critical for security: prevents command injection from prompt text
private static string ShellEscape(string value)
{
    // Single-quote wrapping with internal single-quote escaping
    return "'" + value.Replace("'", "'\\''") + "'";
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `git worktree add` (basic) | `git worktree add` with `--orphan` option | Git 2.44 (Feb 2024) | `--orphan` creates empty worktree; not needed here since we branch from HEAD |
| Manual lock management | `git worktree lock/unlock` commands | Git 2.10 (Sep 2016) | Can lock worktrees to prevent pruning; useful for long-running sessions |
| `git worktree prune` required | Automatic cleanup on `git worktree remove` | Git 2.17 (Apr 2018) | `git worktree remove` was added; before that you had to manually delete + prune |

**Deprecated/outdated:**
- Manual worktree deletion (rm -rf + git worktree prune): Use `git worktree remove` instead (available since Git 2.17)
- `git worktree list` text format parsing: Use `--porcelain` flag for stable, machine-parseable output

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.x with Microsoft.NET.Test.Sdk 17.x |
| Config file | `tests/AgentHub.Tests/AgentHub.Tests.csproj` |
| Quick run command | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~Worktree" -v q` |
| Full suite command | `dotnet test tests/AgentHub.Tests -v q` |

### Phase Requirements to Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| WKTREE-01 | Worktree created via SSH before session launch | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~WorktreeServiceTests" -x` | No - Wave 0 |
| WKTREE-02 | Worktree + branch cleaned up on session end | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~WorktreeCleanup" -x` | No - Wave 0 |
| WKTREE-03 | Branch naming from session ID + prompt slug | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~BranchNameGenerator" -x` | No - Wave 0 |
| WKTREE-04 | Diff stats retrieval and parsing | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~DiffStats" -x` | No - Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~Worktree" -v q`
- **Per wave merge:** `dotnet test tests/AgentHub.Tests -v q`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/AgentHub.Tests/WorktreeServiceTests.cs` -- covers WKTREE-01, WKTREE-02 (create, cleanup, stash-before-remove, error handling)
- [ ] `tests/AgentHub.Tests/BranchNameGeneratorTests.cs` -- covers WKTREE-03 (slug generation, edge cases, collision avoidance)
- [ ] `tests/AgentHub.Tests/DiffStatsParserTests.cs` -- covers WKTREE-04 (numstat parsing, empty diff, binary files)
- [ ] `tests/AgentHub.Tests/SshBackendWorktreeTests.cs` -- covers WKTREE-01 + WKTREE-02 integration (SshBackend lifecycle with worktree flag)

## Open Questions

1. **Repo root path discovery on remote host**
   - What we know: Worktree location defaults to `.worktrees/` inside the repo root, configurable per host
   - What's unclear: How to discover the repo root when the host config does not explicitly set it
   - Recommendation: Require `WorkingDirectory` or `RepoRoot` in host config. Fall back to running `git rev-parse --show-toplevel` in the session's working directory.

2. **Default branch detection (main vs master)**
   - What we know: Decision says "current HEAD of the default branch (main/master)"
   - What's unclear: How to reliably detect whether the remote repo uses `main` or `master`
   - Recommendation: Run `git symbolic-ref refs/remotes/origin/HEAD 2>/dev/null || echo refs/heads/main` on the remote to detect. Cache result per host.

3. **Diff base for WKTREE-04**
   - What we know: User wants diff stats for "merge-readiness"
   - What's unclear: Diff against worktree creation point (specific commit) vs current default branch HEAD
   - Recommendation: Use three-dot diff (`main...branchName`) which shows changes since the branch diverged, regardless of subsequent main branch commits. This is the standard merge-readiness view.

## Sources

### Primary (HIGH confidence)
- [Git worktree official documentation](https://git-scm.com/docs/git-worktree) - command syntax, flags, behavior
- Project source code analysis (direct code reading) - existing patterns, stubs, interfaces

### Secondary (MEDIUM confidence)
- [GitKraken git worktree guide](https://www.gitkraken.com/learn/git/git-worktree) - practical worktree workflows
- [Git worktree gotchas](https://musteresel.github.io/posts/2018/01/git-worktree-gotcha-removed-directory.html) - cleanup edge cases

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - no new dependencies needed, all patterns established in codebase
- Architecture: HIGH - follows existing SshBackend/SSH command patterns exactly
- Pitfalls: HIGH - git worktree is stable, well-documented; edge cases are well-known
- Code examples: MEDIUM - examples are representative but untested; may need adjustment during implementation

**Research date:** 2026-03-10
**Valid until:** 2026-04-10 (stable domain, git worktree API is mature)
