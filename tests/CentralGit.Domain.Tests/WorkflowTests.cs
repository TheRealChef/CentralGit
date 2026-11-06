using CentralGit.Domain.Diff;
using CentralGit.Domain.Workflow;

namespace CentralGit.Domain.Tests;

public class WorkflowTests
{
    private static FileDiff Diff()
    {
        var lines = new List<DiffLine>
        {
            new(DiffLineKind.Context, 1, 1, "a"),
            new(DiffLineKind.Deleted, 2, null, "b"),
            new(DiffLineKind.Added, null, 2, "B"),
            new(DiffLineKind.Added, null, 3, "c"),
        };
        var hunk = new DiffHunk(1, 2, 1, 3, "", lines) { Raw = "@@ -1,2 +1,3 @@\n a\n-b\n+B\n+c\n" };
        return new FileDiff([hunk], false) { FileHeader = "--- a/f\n+++ b/f\n" };
    }

    [Fact]
    public void Staging_some_lines_drops_other_additions_and_keeps_other_deletions_as_context()
    {
        var patch = Diff().PatchForLines(0, new HashSet<int> { 3 }, reverse: false);
        Assert.Equal("--- a/f\n+++ b/f\n@@ -1,2 +1,3 @@\n a\n b\n+c\n", patch);
    }

    [Fact]
    public void Unstaging_some_lines_drops_other_deletions_and_keeps_other_additions_as_context()
    {
        var patch = Diff().PatchForLines(0, new HashSet<int> { 1 }, reverse: true);
        Assert.Equal("--- a/f\n+++ b/f\n@@ -1,2 +1,3 @@\n a\n-b\n B\n c\n", patch);
    }

    [Fact]
    public void No_newline_marker_follows_its_line()
    {
        var lines = new List<DiffLine>
        {
            new(DiffLineKind.Deleted, 1, null, "x"),
            new(DiffLineKind.Added, null, 1, "y"),
        };
        var hunk = new DiffHunk(1, 1, 1, 1, "", lines)
        {
            Raw = "@@ -1 +1 @@\n-x\n\\ No newline at end of file\n+y\n\\ No newline at end of file\n",
        };
        var diff = new FileDiff([hunk], false);
        Assert.Equal(
            "@@ -1 +1 @@\n x\n\\ No newline at end of file\n",
            diff.PatchForLines(0, new HashSet<int>(), reverse: false)
        );
    }

    [Theory]
    [InlineData("WIP on main: abc1234 Fix it", "main", "Work in progress (on top of “Fix it”)")]
    [InlineData("On feature/x: halfway there", "feature/x", "halfway there")]
    [InlineData("On (no branch): detached", null, "detached")]
    [InlineData("something else", null, "something else")]
    public void Stash_subjects_are_split_into_branch_and_message(string subject, string? branch, string message)
    {
        Assert.Equal((branch, message), Stash.ParseSubject(subject));
    }

    [Fact]
    public void Uncommitted_blame_lines_have_a_zero_hash()
    {
        Assert.True(new BlameLine(1, new string('0', 40), "", DateTimeOffset.MinValue, "", "").IsUncommitted);
    }
}
