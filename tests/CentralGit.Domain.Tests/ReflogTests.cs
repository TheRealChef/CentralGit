using CentralGit.Domain.Recovery;

namespace CentralGit.Domain.Tests;

public class ReflogTests
{
    [Theory]
    [InlineData("commit: Add login form", ReflogAction.Commit, "Add login form")]
    [InlineData("commit (initial): First", ReflogAction.Commit, "First")]
    [InlineData("commit (amend): Fix typo", ReflogAction.Amend, "Amended: Fix typo")]
    [InlineData("commit (merge): Merge branch 'x'", ReflogAction.Merge, "Merge commit: Merge branch 'x'")]
    [InlineData("merge origin/main: Fast-forward", ReflogAction.Merge, "Merged origin/main (Fast-forward)")]
    [InlineData(
        "checkout: moving from main to feature/to-do",
        ReflogAction.Checkout,
        "Switched from main to feature/to-do"
    )]
    [InlineData("reset: moving to HEAD~2", ReflogAction.Reset, "Reset to HEAD~2")]
    [InlineData(
        "rebase (finish): returning to refs/heads/topic",
        ReflogAction.Rebase,
        "Rebase finished: returning to refs/heads/topic"
    )]
    [InlineData("rebase -i (start): checkout main", ReflogAction.Rebase, "Rebase started: checkout main")]
    [InlineData("rebase (pick): Second", ReflogAction.Rebase, "Rebase: Second")]
    [InlineData("cherry-pick: Port fix", ReflogAction.CherryPick, "Cherry-picked: Port fix")]
    [InlineData("pull: Fast-forward", ReflogAction.Pull, "Pulled (Fast-forward)")]
    [InlineData("clone: from https://example.com/x.git", ReflogAction.Clone, "Cloned")]
    [InlineData("something new", ReflogAction.Other, "something new")]
    public void Messages_are_classified_and_worded(string message, ReflogAction action, string description)
    {
        Assert.Equal((action, description), Reflog.Classify(message));
    }

    private static ReflogEntry Entry(int index, string hash) =>
        new(index, hash, DateTimeOffset.UnixEpoch.AddDays(100 - index), "commit: x", "x", "Ana");

    [Fact]
    public void Each_commit_appears_once_at_its_newest_entry()
    {
        var points = Reflog.Points(
            [Entry(2, "a"), Entry(0, "a"), Entry(1, "b")],
            new HashSet<string>(),
            lostOnly: false
        );
        Assert.Equal([(0, "a"), (1, "b")], points.Select(p => (p.Entry.Index, p.Entry.Hash)));
    }

    [Fact]
    public void Lost_only_keeps_commits_no_ref_holds()
    {
        var points = Reflog.Points(
            [Entry(0, "a"), Entry(1, "b"), Entry(2, "c")],
            new HashSet<string> { "b" },
            lostOnly: true
        );
        var lost = Assert.Single(points);
        Assert.Equal("b", lost.Entry.Hash);
        Assert.False(lost.IsOnBranch);
    }
}
