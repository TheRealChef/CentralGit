using CentralGit.Domain.Branches;

namespace CentralGit.Domain.Changes;

public enum ChangeKind
{
    Unmodified,
    Modified,
    TypeChanged,
    Added,
    Deleted,
    Renamed,
    Copied,
    Unmerged,
    Untracked,
    Ignored,
}

/// <summary>Which side of the index a working-tree diff shows.</summary>
public enum ChangeSide
{
    Unstaged,
    Staged,
}

public sealed record FileChange(string Path, ChangeKind Staged, ChangeKind Unstaged, string? OriginalPath = null)
{
    public bool IsStaged => Staged is not (ChangeKind.Unmodified or ChangeKind.Untracked or ChangeKind.Ignored);
    public bool IsUnstaged => Unstaged is not ChangeKind.Unmodified || Staged is ChangeKind.Untracked;
    public bool IsConflicted => Staged is ChangeKind.Unmerged;
}

public sealed record WorkingTreeStatus(BranchStatus Branch, IReadOnlyList<FileChange> Changes)
{
    public int ChangedFileCount => Changes.Count(c => c.Staged is not ChangeKind.Ignored);
}

/// <summary>Lines added and deleted in one file; null counts for binary files.</summary>
public sealed record LineCounts(int? Added, int? Deleted)
{
    public static readonly LineCounts Binary = new(null, null);
    public bool IsBinary => Added is null;
}
