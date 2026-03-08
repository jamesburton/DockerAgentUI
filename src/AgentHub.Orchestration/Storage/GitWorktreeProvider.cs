using AgentHub.Contracts;

namespace AgentHub.Orchestration.Storage;

public sealed class GitWorktreeProvider : IWorktreeProvider
{
    public async Task<string> EnsureMaterializedAsync(WorktreeDescriptor descriptor, string destinationRoot, CancellationToken ct)
    {
        var path = Path.Combine(destinationRoot, descriptor.WorktreeId);
        Directory.CreateDirectory(path);

        var readme = Path.Combine(path, ".agenthub-worktree.txt");
        var text = string.Join(Environment.NewLine,
        [
            $"RepoUrl={descriptor.RepoUrl}",
            $"Ref={descriptor.Ref}",
            $"Shallow={descriptor.Shallow}",
            $"Sparse={descriptor.Sparse}",
            $"SparsePaths={string.Join(',', descriptor.SparsePaths ?? [])}"
        ]);

        await File.WriteAllTextAsync(readme, text, ct);
        return path;
    }
}
