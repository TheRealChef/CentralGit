using CentralGit.Domain.Health;

namespace CentralGit.Domain.Tests;

public class SubmoduleLfsFindingsTests
{
    [Fact]
    public void Submodules_that_are_missing_or_moved_need_attention()
    {
        var findings = RepositoryHealth
            .DiagnoseSubmodules([new("a", "1", null), new("b", "1", "2"), new("c", "1", "1")])
            .ToList();
        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(FindingLevel.Attention, f.Level));
        Assert.Contains("1 submodule not checked out", findings.Select(f => f.Title));
    }

    [Fact]
    public void Submodules_in_sync_are_good_and_none_says_nothing()
    {
        Assert.Equal(FindingLevel.Good, Assert.Single(RepositoryHealth.DiagnoseSubmodules([new("a", "1", "1")])).Level);
        Assert.Empty(RepositoryHealth.DiagnoseSubmodules([]));
    }

    [Theory]
    [InlineData(3, false, 3, "Uses Git LFS, but git-lfs isn't installed")]
    [InlineData(3, true, 2, "2 LFS files not downloaded")]
    [InlineData(3, true, 0, "3 files in Git LFS")]
    public void Lfs_findings(int files, bool installed, int pointers, string title) =>
        Assert.Equal(title, Assert.Single(RepositoryHealth.DiagnoseLfs(new LfsInfo(files, installed, pointers))).Title);

    [Fact]
    public void No_lfs_says_nothing() => Assert.Empty(RepositoryHealth.DiagnoseLfs(new LfsInfo(0, true, 0)));
}
