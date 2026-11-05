namespace CentralGit.Domain.Commits;

public sealed record Signature(string Name, string Email, DateTimeOffset When);

public sealed record CommitInfo(
    string Hash,
    IReadOnlyList<string> Parents,
    Signature Author,
    Signature Committer,
    IReadOnlyList<string> Refs,
    string Subject
)
{
    public string ShortHash => Hash.Length > 7 ? Hash[..7] : Hash;
    public bool IsMerge => Parents.Count > 1;
}
