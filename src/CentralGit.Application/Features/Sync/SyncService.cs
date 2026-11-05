using CentralGit.Application.Operations;
using CentralGit.Application.Ports;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Operations;

namespace CentralGit.Application.Features.Sync;

/// <summary>Where a push goes. <see cref="Problem"/> is set when there's nothing sensible to push.</summary>
public sealed record PushPlan(string Branch, string Remote, string Target, bool HasUpstream, string? Problem = null)
{
    public static PushPlan Refused(string problem) => new("", "", "", false, problem);
}

public interface ISyncService
{
    IPreparedOperation Fetch();

    /// <summary>
    /// An explicit <paramref name="mode"/>, else the user's own pull configuration, else fast-forward only:
    /// it can never create a merge or rewrite anything. If the branches diverged, the failure says so.
    /// </summary>
    Task<IPreparedOperation> PreparePullAsync(PullMode? mode, CancellationToken ct = default);

    /// <summary>Publishes to origin (or the only remote) when the branch has no upstream yet.</summary>
    Task<PushPlan> PlanPushAsync(BranchStatus? branch, CancellationToken ct = default);

    /// <summary>Force is always --force-with-lease: it refuses to overwrite commits we haven't seen.</summary>
    IPreparedOperation Push(PushPlan plan, bool force);
}

public sealed class SyncService(IBranchSource repository, IOperationSource operations) : ISyncService
{
    public IPreparedOperation Fetch() => operations.Prepare(new FetchAll());

    public async Task<IPreparedOperation> PreparePullAsync(PullMode? mode, CancellationToken ct = default)
    {
        if (mode is { } explicitMode)
            return operations.Prepare(new Pull(explicitMode));
        var configured =
            await repository.GetUserConfigAsync("pull.rebase", ct).ConfigureAwait(false) is not null
            || await repository.GetUserConfigAsync("pull.ff", ct).ConfigureAwait(false) is not null;
        return operations.Prepare(new Pull(configured ? null : PullMode.FastForwardOnly));
    }

    public async Task<PushPlan> PlanPushAsync(BranchStatus? branch, CancellationToken ct = default)
    {
        if (branch is not { Head: { } name })
            return PushPlan.Refused("Can't push from a detached HEAD — check out a branch first.");

        var remotes = await repository.GetRemotesAsync(ct).ConfigureAwait(false);
        if (branch.Upstream is null && remotes.Count == 0)
            return PushPlan.Refused("This repository has no remote to push to.");
        var remote = remotes.Contains("origin") ? "origin" : remotes.FirstOrDefault() ?? "origin";
        return new PushPlan(name, remote, branch.Upstream ?? $"{remote}/{name}", branch.Upstream is not null);
    }

    public IPreparedOperation Push(PushPlan plan, bool force) =>
        plan.Problem is null
            ? operations.Prepare(new Push(plan.Branch, plan.HasUpstream, plan.Remote, force))
            : throw new InvalidOperationException(plan.Problem);
}
