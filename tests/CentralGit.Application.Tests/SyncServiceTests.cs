using CentralGit.Application.Features.Sync;
using CentralGit.Application.Operations;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Operations;

namespace CentralGit.Application.Tests;

public class SyncServiceTests
{
    private readonly FakeBranches _branches = new();
    private readonly RecordingOperations _operations = new();
    private SyncService Service => new(_branches, _operations);

    [Fact]
    public async Task Pull_starts_fast_forward_only_unless_the_user_configured_pulling()
    {
        await Service.PreparePullAsync(null);
        _branches.UserConfig["pull.rebase"] = "true";
        await Service.PreparePullAsync(null);
        await Service.PreparePullAsync(PullMode.Merge);

        Assert.Equal<GitOperation>(
            [new Pull(PullMode.FastForwardOnly), new Pull(null), new Pull(PullMode.Merge)],
            _operations.Prepared
        );
    }

    [Fact]
    public async Task Push_refuses_a_detached_head_and_a_repository_without_remotes()
    {
        Assert.Contains(
            "detached HEAD",
            (await Service.PlanPushAsync(new BranchStatus(null, "abc", null, 0, 0))).Problem
        );
        Assert.Contains(
            "no remote",
            (await Service.PlanPushAsync(new BranchStatus("main", "abc", null, 0, 0))).Problem
        );
        Assert.Throws<InvalidOperationException>(() => Service.Push(PushPlan.Refused("no"), force: false));
        Assert.Empty(_operations.Prepared);
    }

    [Fact]
    public async Task Unpublished_branches_go_to_origin_or_the_only_remote()
    {
        _branches.Remotes.AddRange(["fork", "origin"]);
        var toOrigin = await Service.PlanPushAsync(new BranchStatus("topic", "abc", null, 0, 0));
        Assert.Equal(new PushPlan("topic", "origin", "origin/topic", HasUpstream: false), toOrigin);

        _branches.Remotes.Remove("origin");
        Assert.Equal("fork/topic", (await Service.PlanPushAsync(new BranchStatus("topic", "abc", null, 0, 0))).Target);
    }

    [Fact]
    public async Task Tracked_branches_push_to_their_upstream_and_force_means_with_lease()
    {
        _branches.Remotes.Add("origin");
        var plan = await Service.PlanPushAsync(new BranchStatus("main", "abc", "origin/release", 2, 0));
        Assert.Equal("origin/release", plan.Target);

        Service.Push(plan, force: true);
        Assert.Equal(
            new Push("main", HasUpstream: true, "origin", ForceWithLease: true),
            Assert.Single(_operations.Prepared)
        );
    }
}
