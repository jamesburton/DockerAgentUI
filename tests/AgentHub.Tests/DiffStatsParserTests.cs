using AgentHub.Orchestration.Worktree;
using Xunit;

namespace AgentHub.Tests;

public class DiffStatsParserTests
{
    [Fact]
    public void Parse_MultipleFiles_ReturnCorrectTotals()
    {
        var numstat = "10\t5\tsrc/file.cs\n3\t0\tsrc/new.cs";
        var stat = " src/file.cs | 15 +++++-----\n src/new.cs  |  3 +++\n 2 files changed, 13 insertions(+), 5 deletions(-)";

        var result = DiffStatsParser.Parse(numstat, stat);

        Assert.Equal(2, result.Files.Count);
        Assert.Equal(13, result.TotalInsertions);
        Assert.Equal(5, result.TotalDeletions);
    }

    [Fact]
    public void Parse_EmptyOutput_ReturnsEmptyDiffStats()
    {
        var result = DiffStatsParser.Parse("", "");

        Assert.Empty(result.Files);
        Assert.Equal(0, result.TotalInsertions);
        Assert.Equal(0, result.TotalDeletions);
    }

    [Fact]
    public void Parse_BinaryFile_HandledWithZeroStats()
    {
        var numstat = "-\t-\tbinary-file.bin";
        var stat = " binary-file.bin | Bin 0 -> 1234 bytes\n 1 file changed";

        var result = DiffStatsParser.Parse(numstat, stat);

        Assert.Single(result.Files);
        var file = result.Files[0];
        Assert.Equal("binary-file.bin", file.Path);
        Assert.Equal("binary", file.Status);
        Assert.Equal(0, file.Insertions);
        Assert.Equal(0, file.Deletions);
    }

    [Fact]
    public void Parse_ModifiedFile_StatusIsModified()
    {
        var numstat = "10\t5\tsrc/file.cs";
        var stat = " src/file.cs | 15 +++++-----\n 1 file changed";

        var result = DiffStatsParser.Parse(numstat, stat);

        Assert.Single(result.Files);
        Assert.Equal("modified", result.Files[0].Status);
        Assert.Equal(10, result.Files[0].Insertions);
        Assert.Equal(5, result.Files[0].Deletions);
    }

    [Fact]
    public void Parse_AddedFile_StatusIsAdded()
    {
        var numstat = "10\t0\tsrc/new.cs";
        var stat = " src/new.cs | 10 ++++++++++\n 1 file changed";

        var result = DiffStatsParser.Parse(numstat, stat);

        Assert.Single(result.Files);
        Assert.Equal("added", result.Files[0].Status);
    }

    [Fact]
    public void Parse_DeletedFile_StatusIsDeleted()
    {
        var numstat = "0\t15\tsrc/old.cs";
        var stat = " src/old.cs | 15 ---------------\n 1 file changed";

        var result = DiffStatsParser.Parse(numstat, stat);

        Assert.Single(result.Files);
        Assert.Equal("deleted", result.Files[0].Status);
    }

    [Fact]
    public void Parse_SummaryFromStatOutput()
    {
        var numstat = "10\t5\tsrc/file.cs";
        var stat = " src/file.cs | 15 +++++-----\n 1 file changed, 10 insertions(+), 5 deletions(-)";

        var result = DiffStatsParser.Parse(numstat, stat);

        Assert.Contains("1 file changed", result.Summary);
    }
}
