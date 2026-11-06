using CentralGit.Application.Git;
using CentralGit.Domain.Operations;
using CentralGit.Domain.Workflow;

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

// ─── Commits ─────────────────────────────────────────────────────────────

/// <summary>Apply a commit's change on top of HEAD as a new commit. Merge commits replay against their first parent.</summary>
public sealed record CherryPick(string Commit, bool IsMerge) : GitOperation;

/// <summary>A new commit that undoes <see cref="Commit"/>. Merge commits are undone against their first parent.</summary>
public sealed record Revert(string Commit, bool IsMerge) : GitOperation;

/// <summary>Move the current branch to <see cref="Commit"/>; <see cref="ResetMode.Hard"/> throws work away.</summary>
public sealed record Reset(string Commit, ResetMode Mode) : GitOperation;

/// <summary>Merge <see cref="Branch"/> into the current branch (a merge commit unless it fast-forwards).</summary>
public sealed record Merge(string Branch) : GitOperation;

/// <summary>Replay the current branch's commits on top of <see cref="Onto"/>.</summary>
public sealed record Rebase(string Onto) : GitOperation;

/// <summary>Rewrites the commits after <paramref name="Base"/> (all of them when null) following the plan.</summary>
public sealed record InteractiveRebase(string? Base, IReadOnlyList<RebaseStep> Steps) : GitOperation;

/// <summary>Finish a stopped merge, rebase, cherry-pick or revert once conflicts are resolved and staged.</summary>
public sealed record ContinueInProgress(InProgress State) : GitOperation;

/// <summary>Give up a stopped merge, rebase, cherry-pick or revert and go back to where it started.</summary>
public sealed record AbortInProgress(InProgress State) : GitOperation;

// ─── Tags ────────────────────────────────────────────────────────────────

/// <summary>A tag at <see cref="Commit"/>; with a message it is annotated.</summary>
public sealed record CreateTag(string Name, string Commit, string? Message) : GitOperation;

public sealed record DeleteTag(string Name) : GitOperation;

public sealed record PushTag(string Remote, string Name) : GitOperation;

/// <summary>Removes a tag from a remote server — affects everyone using it.</summary>
public sealed record DeleteRemoteTag(string Remote, string Name) : GitOperation;

// ─── Stash ───────────────────────────────────────────────────────────────

/// <summary>Park uncommitted changes; <see cref="Paths"/> limits it to some files (empty = everything).</summary>
public sealed record StashSave(string Message, bool IncludeUntracked, IReadOnlyList<string> Paths) : GitOperation;

/// <summary>Bring a stash back; <see cref="Pop"/> also removes it when it applied cleanly.</summary>
public sealed record StashApply(string Name, bool Pop) : GitOperation;

public sealed record StashDrop(string Name) : GitOperation;

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

/// <summary>Starts a bisect with no commits marked yet.</summary>
public sealed record BisectStart : GitOperation;

/// <summary>Marks <paramref name="Commit"/> (HEAD when null) good, bad or untestable.</summary>
public sealed record BisectMarkCommit(string? Commit, BisectMark Mark) : GitOperation;

/// <summary>Ends the bisect and goes back to the branch it started from.</summary>
public sealed record BisectReset : GitOperation;
