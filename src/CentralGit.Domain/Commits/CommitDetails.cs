using CentralGit.Domain.Changes;

namespace CentralGit.Domain.Commits;

public enum SignatureState
{
    Unsigned,
    Good,
    GoodUnknownValidity,
    Bad,
    ExpiredSignature,
    ExpiredKey,
    RevokedKey,
    CannotCheck,
}

public sealed record ChangedFile(string Path, string? OldPath, ChangeKind Kind, int Added, int Deleted, bool IsBinary)
{
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Directory => Path.Contains('/') ? Path[..Path.LastIndexOf('/')] : "";
}

public sealed record CommitDetails(
    string Hash,
    IReadOnlyList<string> Parents,
    Signature Author,
    Signature Committer,
    string Subject,
    string Body,
    SignatureState Signature,
    string? Signer,
    IReadOnlyList<ChangedFile> Files
)
{
    /// <summary>The well-known hash of the empty tree, used as the "parent" of root commits.</summary>
    public const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    /// <summary>What the diff is taken against: first parent, or the empty tree for a root commit.</summary>
    public string DiffBase => Parents.Count > 0 ? Parents[0] : EmptyTree;

    public int TotalAdded => Files.Sum(f => f.Added);
    public int TotalDeleted => Files.Sum(f => f.Deleted);
}
