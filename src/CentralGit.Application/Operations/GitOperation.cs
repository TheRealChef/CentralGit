using CentralGit.Application.Git;
using CentralGit.Domain.Operations;

namespace CentralGit.Application.Operations;

/// <summary>
/// A change to the repository or its remote, described by intent. Infrastructure turns it into a
/// command line; the Operations Center runs it and shows progress, output and an explanation.
/// </summary>
public abstract record GitOperation;

public sealed record FetchAll : GitOperation;

/// <summary>Pull with an explicit strategy, or with <c>null</c> exactly as the user configured git.</summary>
public sealed record Pull(PullMode? Mode) : GitOperation;

/// <summary>Push the current branch; without an upstream it is published to <see cref="Remote"/> and tracked.</summary>
public sealed record Push(string Branch, bool HasUpstream, string Remote, bool ForceWithLease) : GitOperation;

public sealed record SwitchBranch(string Branch) : GitOperation;

/// <summary>Create a local branch tracking a remote one (e.g. origin/feature) and switch to it.</summary>
public sealed record TrackRemoteBranch(string RemoteBranch) : GitOperation;

/// <summary>Create a branch at <see cref="StartPoint"/> (null = HEAD) and switch to it.</summary>
public sealed record CreateBranch(string Name, string? StartPoint) : GitOperation;

/// <summary>Create a branch at a commit without switching to it (Recovery).</summary>
public sealed record CreateBranchAt(string Name, string Commit) : GitOperation;

public sealed record RenameBranch(string From, string To) : GitOperation;

/// <summary>Without <see cref="Force"/> git refuses to delete unmerged work.</summary>
public sealed record DeleteBranch(string Name, bool Force) : GitOperation;

/// <summary>Several local branches at once (cleanup); the caller has checked or confirmed each.</summary>
public sealed record DeleteBranches(IReadOnlyList<string> Names) : GitOperation;

/// <summary>Branches on a remote server — affects everyone using it.</summary>
public sealed record DeleteRemoteBranches(string Remote, IReadOnlyList<string> ShortNames) : GitOperation;

/// <summary>Put uncommitted changes (untracked files too) in the stash, labelled with why.</summary>
public sealed record StashBeforeSwitch(string Target) : GitOperation;

/// <summary>An operation ready to run: the command line is known before anything happens.</summary>
public interface IPreparedOperation
{
    string CommandLine { get; }

    /// <summary>Runs it, reporting progress as it goes. Cancellation ends in an outcome, not an exception.</summary>
    Task<OperationOutcome> RunAsync(Action<GitProgress> onProgress, CancellationToken cancellationToken);
}

/// <summary>How an operation ended, with git's output and — for a failure — what it means.</summary>
public sealed record OperationOutcome(GitResult Result, bool Cancelled, OperationExplanation? Failure)
{
    public bool Succeeded => !Cancelled && Result.Succeeded;
}
