using CentralGit.Application.Operations;
using CentralGit.Application.Ports;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Health;

namespace CentralGit.Application.Features.Branches;

public sealed record BranchLists(IReadOnlyList<LocalBranch> Local, IReadOnlyList<RemoteBranch> Remote);

public enum CheckoutAction
{
    SwitchToLocal,
    CreateTracking,
    Blocked,
}

/// <summary>What checking out a remote branch means here: an existing local branch, a new one, or a name clash.</summary>
public sealed record CheckoutPlan(CheckoutAction Action, string LocalName, string? Problem = null);

public sealed record RemoteCleanup(string Remote, IReadOnlyList<string> ShortNames);

/// <summary>Selected cleanup branches, grouped the way they are deleted.</summary>
public sealed record CleanupPlan(
    IReadOnlyList<string> Local,
    IReadOnlyList<RemoteCleanup> Remote,
    IReadOnlyList<BranchAssessment> Unmerged
);

public interface IBranchService
{
    Task<BranchLists> GetBranchesAsync(CancellationToken ct = default);

    /// <summary>Local branches only — the sidebar, refreshed on every reload.</summary>
    Task<IReadOnlyList<LocalBranch>> GetLocalBranchesAsync(CancellationToken ct = default);

    /// <summary>An error sentence, or null when <paramref name="name"/> can be used for a new branch.</summary>
    Task<string?> ValidateNewNameAsync(string name, CancellationToken ct = default);

    Task<CheckoutPlan> PlanCheckoutAsync(RemoteBranch remote, CancellationToken ct = default);

    IPreparedOperation Switch(string branch);
    IPreparedOperation Track(RemoteBranch remote);
    IPreparedOperation Create(string name, string? startPoint);
    IPreparedOperation Rename(string from, string to);
    IPreparedOperation Delete(string name, bool force);
    IPreparedOperation StashBeforeSwitch(string target);
    IPreparedOperation DeleteLocal(IReadOnlyList<string> names);
    IPreparedOperation DeleteOnRemote(RemoteCleanup branches);

    /// <summary>
    /// Groups selected cleanup branches: local ones (never the current branch) in one command,
    /// remote ones per remote. <see cref="CleanupPlan.Unmerged"/> lists what deserves a warning.
    /// </summary>
    CleanupPlan PlanCleanup(IEnumerable<BranchAssessment> selected, string? currentBranch);
}

public sealed class BranchService(IBranchSource repository, IOperationSource operations) : IBranchService
{
    public async Task<BranchLists> GetBranchesAsync(CancellationToken ct = default)
    {
        var local = repository.GetLocalBranchesAsync(ct);
        var remote = repository.GetRemoteBranchesAsync(ct);
        return new BranchLists(await local.ConfigureAwait(false), await remote.ConfigureAwait(false));
    }

    public Task<IReadOnlyList<LocalBranch>> GetLocalBranchesAsync(CancellationToken ct = default) =>
        repository.GetLocalBranchesAsync(ct);

    public async Task<string?> ValidateNewNameAsync(string name, CancellationToken ct = default)
    {
        if (BranchNames.Precheck(name) is { } problem)
            return problem;
        if (!await repository.IsValidBranchNameAsync(name, ct).ConfigureAwait(false))
            return BranchNames.Invalid(name);
        var local = await repository.GetLocalBranchesAsync(ct).ConfigureAwait(false);
        return local.Any(b => b.Name == name) ? $"A branch called {name} already exists." : null;
    }

    public async Task<CheckoutPlan> PlanCheckoutAsync(RemoteBranch remote, CancellationToken ct = default)
    {
        var local = await repository.GetLocalBranchesAsync(ct).ConfigureAwait(false);
        if (local.FirstOrDefault(b => b.Upstream == remote.Name) is { } tracking)
            return new CheckoutPlan(CheckoutAction.SwitchToLocal, tracking.Name);
        // A local branch with that name exists but tracks something else: don't guess.
        return local.Any(b => b.Name == remote.ShortName)
            ? new CheckoutPlan(
                CheckoutAction.Blocked,
                remote.ShortName,
                $"A local branch {remote.ShortName} already exists and doesn't track {remote.Name}."
            )
            : new CheckoutPlan(CheckoutAction.CreateTracking, remote.ShortName);
    }

    public CleanupPlan PlanCleanup(IEnumerable<BranchAssessment> selected, string? currentBranch)
    {
        var list = selected.ToList();
        var local = list.Where(a => !a.Branch.IsRemote && a.Name != currentBranch).Select(a => a.Name).ToList();
        var remote = list.Where(a => a.Branch.IsRemote && a.Name.IndexOf('/') > 0)
            .GroupBy(a => a.Name[..a.Name.IndexOf('/')])
            .Select(g => new RemoteCleanup(g.Key, g.Select(a => a.Name[(a.Name.IndexOf('/') + 1)..]).ToList()))
            .ToList();
        return new CleanupPlan(local, remote, list.Where(a => !a.IsSafeToDelete).ToList());
    }

    public IPreparedOperation Switch(string branch) => operations.Prepare(new SwitchBranch(branch));

    public IPreparedOperation Track(RemoteBranch remote) => operations.Prepare(new TrackRemoteBranch(remote.Name));

    public IPreparedOperation Create(string name, string? startPoint) =>
        operations.Prepare(new CreateBranch(name, startPoint));

    public IPreparedOperation Rename(string from, string to) => operations.Prepare(new RenameBranch(from, to));

    public IPreparedOperation Delete(string name, bool force) => operations.Prepare(new DeleteBranch(name, force));

    public IPreparedOperation StashBeforeSwitch(string target) =>
        operations.Prepare(new Operations.StashBeforeSwitch(target));

    public IPreparedOperation DeleteLocal(IReadOnlyList<string> names) => operations.Prepare(new DeleteBranches(names));

    public IPreparedOperation DeleteOnRemote(RemoteCleanup branches) =>
        operations.Prepare(new DeleteRemoteBranches(branches.Remote, branches.ShortNames));
}
