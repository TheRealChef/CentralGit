using CentralGit.Domain.Changes;

namespace CentralGit.Domain.Commits;

/// <summary>
/// One commit in a file's history. <see cref="Path"/> is the file's name in that commit, which differs
/// from today's name when the file was renamed later (<see cref="OldPath"/> is set on the rename itself).
/// </summary>
public sealed record FileRevision(
    string Hash,
    string? Parent,
    string Path,
    string? OldPath,
    ChangeKind Kind,
    string Author,
    DateTimeOffset When,
    string Subject
)
{
    public string ShortHash => Hash.Length > 9 ? Hash[..9] : Hash;

    /// <summary>What the diff compares against: the first parent, or nothing for the first commit.</summary>
    public string DiffBase => Parent ?? CommitDetails.EmptyTree;

    public ChangedFile AsChangedFile() => new(Path, OldPath, Kind, 0, 0, false);
}
