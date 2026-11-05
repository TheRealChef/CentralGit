namespace CentralGit.Domain.Operations;

public enum PullMode
{
    FastForwardOnly,
    Rebase,
    Merge,
}

/// <summary>What the UI can offer as the next step after a failure.</summary>
public enum SuggestedFix
{
    None,
    PullFirst, // push rejected: remote has commits we don't
    ChooseReconcile, // pull: branches diverged, pick rebase or merge
    PublishBranch, // push: no upstream yet
    SignIn, // authentication failed
    CommitOrStash, // local changes would be overwritten
    ResolveConflicts, // merge/rebase stopped on conflicts
    CheckConnection, // host unreachable
    ForceDelete, // branch -d refused: not fully merged
    PickAnotherName, // a branch with that name exists
}

/// <summary>A failure in words a person can act on, and the fix to offer.</summary>
public sealed record OperationExplanation(string Title, string Detail, SuggestedFix Fix);

/// <summary>One progress report from a running operation: "Receiving objects" at 45 %.</summary>
public readonly record struct GitProgress(string Phase, int? Percent);
