using AgentHub.Contracts;

namespace AgentHub.Orchestration.Worktree;

/// <summary>
/// Parses git diff --numstat and --stat output into structured DiffStats records.
/// </summary>
public static class DiffStatsParser
{
    /// <summary>
    /// Parse git diff --numstat and --stat output into a DiffStats record.
    /// </summary>
    public static DiffStats Parse(string numstatOutput, string statOutput)
    {
        var files = new List<FileDiffStat>();
        var totalInsertions = 0;
        var totalDeletions = 0;

        if (!string.IsNullOrWhiteSpace(numstatOutput))
        {
            var lines = numstatOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('\t');
                if (parts.Length < 3) continue;

                var insertionsStr = parts[0].Trim();
                var deletionsStr = parts[1].Trim();
                var path = parts[2].Trim();

                if (insertionsStr == "-" || deletionsStr == "-")
                {
                    // Binary file
                    files.Add(new FileDiffStat(path, "binary", 0, 0));
                    continue;
                }

                var insertions = int.TryParse(insertionsStr, out var ins) ? ins : 0;
                var deletions = int.TryParse(deletionsStr, out var del) ? del : 0;

                var status = (insertions, deletions) switch
                {
                    ( > 0, > 0) => "modified",
                    ( > 0, 0) => "added",
                    (0, > 0) => "deleted",
                    _ => "unchanged"
                };

                files.Add(new FileDiffStat(path, status, insertions, deletions));
                totalInsertions += insertions;
                totalDeletions += deletions;
            }
        }

        // Extract summary from last line of --stat output
        var summary = "";
        if (!string.IsNullOrWhiteSpace(statOutput))
        {
            var statLines = statOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (statLines.Length > 0)
            {
                summary = statLines[^1].Trim();
            }
        }

        return new DiffStats(files, totalInsertions, totalDeletions, summary);
    }
}
