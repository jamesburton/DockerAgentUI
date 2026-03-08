using AgentHub.Contracts;

namespace AgentHub.Orchestration.Storage;

public sealed class BlobSharedStorageProvider : ISharedStorageProvider
{
    public string Name => "blob";

    public Task<string> MaterializeAsync(string worktreeId, string destinationRoot, CancellationToken ct)
    {
        var path = Path.Combine(destinationRoot, worktreeId);
        Directory.CreateDirectory(path);
        return Task.FromResult(path);
    }

    public async Task UploadTextAsync(string worktreeId, string relativePath, string content, CancellationToken ct)
    {
        var root = Path.Combine(Path.GetTempPath(), "AgentSafeEnv.BlobShadow", worktreeId);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, ct);
    }
}
