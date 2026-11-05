using CentralGit.Application.Features.Branches;
using CentralGit.Application.Operations;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Health;

namespace CentralGit.Application.Tests;

public class BranchServiceTests
{
    private readonly FakeBranches _branches = new();
    private readonly RecordingOperations _operations = new();
    private BranchService Service => new(_branches, _operations);

    private static readonly RemoteBranch Feature = new("origin/feature", "origin", "feature", null);

    [Fact]
    public async Task Checking_out_a_remote_branch_reuses_the_local_branch_tracking_it()
    {
        _branches.Local.Add(FakeBranches.Branch("my-feature", upstream: "origin/feature"));
        Assert.Equal(
            new CheckoutPlan(CheckoutAction.SwitchToLocal, "my-feature"),
            await Service.PlanCheckoutAsync(Feature)
        );
    }

    [Fact]
    public async Task Checking_out_never_guesses_when_a_same_named_branch_tracks_something_else()
    {
        _branches.Local.Add(FakeBranches.Branch("feature", upstream: "fork/feature"));
        var plan = await Service.PlanCheckoutAsync(Feature);
        Assert.Equal(CheckoutAction.Blocked, plan.Action);
        Assert.Contains("doesn't track origin/feature", plan.Problem);
    }

    [Fact]
    public async Task Checking_out_an_unknown_remote_branch_creates_a_tracking_branch()
    {
        Assert.Equal(
            new CheckoutPlan(CheckoutAction.CreateTracking, "feature"),
            await Service.PlanCheckoutAsync(Feature)
        );
        Service.Track(Feature);
        Assert.Equal(new TrackRemoteBranch("origin/feature"), Assert.Single(_operations.Prepared));
    }

    [Fact]
    public async Task New_names_are_checked_by_rules_then_git_then_against_existing_branches()
    {
        _branches.Local.Add(FakeBranches.Branch("main"));
        _branches.GitAcceptsName = name => !name.Contains("..");

        Assert.Equal("Type a name for the branch.", await Service.ValidateNewNameAsync(" "));
        Assert.Equal(BranchNames.Invalid("a..b"), await Service.ValidateNewNameAsync("a..b"));
        Assert.Equal("A branch called main already exists.", await Service.ValidateNewNameAsync("main"));
        Assert.Null(await Service.ValidateNewNameAsync("topic/new"));
    }

    [Fact]
    public void Cleanup_deletes_local_branches_together_and_remote_ones_per_remote_but_never_the_current_branch()
    {
        static BranchAssessment Assessed(string name, bool remote, BranchVerdict verdict) =>
            new(
                new BranchFacts(name, remote, null, null),
                verdict,
                verdict == BranchVerdict.Merged ? "origin/main" : null,
                null,
                false
            );

        var plan = Service.PlanCleanup(
            [
                Assessed("done", false, BranchVerdict.Merged),
                Assessed("current", false, BranchVerdict.Merged),
                Assessed("old", false, BranchVerdict.Abandoned),
                Assessed("origin/feature/a", true, BranchVerdict.Merged),
                Assessed("fork/b", true, BranchVerdict.Gone),
                Assessed("origin/c", true, BranchVerdict.Merged),
            ],
            currentBranch: "current"
        );

        Assert.Equal(["done", "old"], plan.Local);
        Assert.Equal(2, plan.Remote.Count);
        Assert.Equal(["feature/a", "c"], plan.Remote.Single(r => r.Remote == "origin").ShortNames);
        Assert.Equal(["b"], plan.Remote.Single(r => r.Remote == "fork").ShortNames);
        Assert.Equal(["old", "fork/b"], plan.Unmerged.Select(a => a.Name));
    }
}
