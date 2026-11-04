using CentralGit.Core.Git;

namespace CentralGit.Core.Tests;

public class CommitLogTests
{
    private const char F = '\u001f';

    [Fact]
    public void Parses_merge_commit_with_refs()
    {
        var record = string.Join(F,
            "e81a5f0aaaa", "1b6c9e3bbbb 4d07aa2cccc",
            "Tomás Ferreira", "tomas@example.dev", "1793790000",
            "GitHub", "noreply@github.com", "1793790060",
            "HEAD -> refs/heads/main, refs/remotes/origin/main, tag: refs/tags/v2.8.1",
            "Merge pull request #412 from fix/vat-rounding");

        var commit = CommitLog.Parse("\n" + record)!;

        Assert.Equal("e81a5f0", commit.ShortHash);
        Assert.True(commit.IsMerge);
        Assert.Equal(["1b6c9e3bbbb", "4d07aa2cccc"], commit.Parents);
        Assert.Equal("Tomás Ferreira", commit.Author.Name);
        Assert.Equal("GitHub", commit.Committer.Name);
        Assert.Equal(["HEAD", "refs/heads/main", "refs/remotes/origin/main", "refs/tags/v2.8.1"], commit.Refs);
        Assert.Equal("Merge pull request #412 from fix/vat-rounding", commit.Subject);
    }

    [Fact]
    public void Root_commit_has_no_parents_and_blank_record_is_skipped()
    {
        var record = string.Join(F, "abc", "", "A", "a@x", "0", "A", "a@x", "0", "", "Initial commit");

        Assert.Empty(CommitLog.Parse(record)!.Parents);
        Assert.Null(CommitLog.Parse("\n"));
    }
}
