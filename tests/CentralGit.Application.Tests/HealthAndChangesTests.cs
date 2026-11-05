using CentralGit.Application.Features.Changes;
using CentralGit.Application.Features.Health;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Health;

namespace CentralGit.Application.Tests;

public class HealthServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Branches_are_judged_against_every_integration_branch_with_the_trunk_named_first()
    {
        var health = new FakeHealth
        {
            Refs = new BranchRefs(
                [
                    new BranchFacts("feature/x", false, Now.AddDays(-20), "Ana"),
                    new BranchFacts("old", false, Now.AddDays(-200), "Ana"),
                ],
                [new BranchFacts("origin/main", true, Now, "Ana"), new BranchFacts("origin/develop", true, Now, "Ana")],
                OriginHead: "origin/main"
            ),
        };
        // feature/x is in both; the trunk must win.
        health.Merged["origin/develop"] = ["refs/heads/feature/x"];
        health.Merged["origin/main"] = ["refs/heads/feature/x", "refs/remotes/origin/main"];

        var review = await new HealthService(health, new FixedClock(Now)).ReviewBranchesAsync();

        Assert.Equal("origin/main", review.Trunk);
        Assert.Equal(["origin/main", "origin/develop"], review.Targets);
        Assert.Equal(["origin/main", "origin/develop"], health.Asked.Order().Reverse());
        var local = review.Local.ToDictionary(a => a.Name);
        Assert.Equal("origin/main", local["feature/x"].MergedInto);
        Assert.Equal(BranchVerdict.Abandoned, local["old"].Verdict);
    }

    [Fact]
    public async Task The_report_is_taken_at_one_moment_and_diagnosed()
    {
        var snapshot = await new HealthService(new FakeHealth(), new FixedClock(Now)).LoadReportAsync();
        Assert.Equal(Now, snapshot.At);
        Assert.Equal("Ana", snapshot.Report.UserName);
        Assert.Contains(snapshot.Report.Findings, f => f.Title == "Remote data is fresh");
    }
}

public class ChangesServiceTests
{
    [Fact]
    public async Task Committing_everything_stages_first_and_stops_if_that_fails()
    {
        var tree = new FakeWorkingTree { StageAllResult = FakeWorkingTree.Failed("fatal: index.lock exists") };
        var outcome = await new ChangesService(tree).CommitAsync("Fix", "", amend: false, stageEverything: true);

        Assert.True(outcome.StagingFailed);
        Assert.Equal(["stage all"], tree.Calls);
    }

    [Fact]
    public async Task Commit_messages_are_composed_the_way_git_expects()
    {
        var tree = new FakeWorkingTree();
        var outcome = await new ChangesService(tree).CommitAsync(
            "  Fix totals ",
            "# Why\rQuantity was ignored.",
            amend: true,
            stageEverything: false
        );

        Assert.False(outcome.StagingFailed);
        Assert.Equal(["commit --amend: Fix totals\n\n# Why\nQuantity was ignored."], tree.Calls);
    }

    [Fact]
    public async Task Amending_starts_from_the_last_message_split_for_the_composer()
    {
        var tree = new FakeWorkingTree { LastMessage = "Fix it\n\nBecause." };
        Assert.Equal(("Fix it", "Because."), await new ChangesService(tree).LastMessageAsync());
        Assert.Null(await new ChangesService(new FakeWorkingTree()).LastMessageAsync());
    }

    [Fact]
    public async Task Counts_come_from_both_sides_of_the_index_plus_untracked_files_read_from_disk()
    {
        var tree = new FakeWorkingTree();
        tree.FileLines["new.txt"] = new LineCounts(3, 0);
        var counts = await new ChangesService(tree).ReadCountsAsync([
            new FileChange("new.txt", ChangeKind.Untracked, ChangeKind.Untracked),
            new FileChange("edited.cs", ChangeKind.Unmodified, ChangeKind.Modified),
        ]);

        Assert.Equal(new LineCounts(1, 2), counts.Unstaged["Unstaged.cs"]);
        Assert.Equal(new LineCounts(1, 2), counts.Staged["Staged.cs"]);
        Assert.Equal(new LineCounts(3, 0), Assert.Single(counts.Untracked).Value);
        Assert.DoesNotContain("count edited.cs", tree.Calls);
    }
}
