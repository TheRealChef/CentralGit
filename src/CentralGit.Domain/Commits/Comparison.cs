namespace CentralGit.Domain.Commits;

/// <summary>
/// Two refs side by side. <see cref="Ahead"/> are commits only <see cref="Head"/> has, <see cref="Behind"/>
/// commits only <see cref="Base"/> has; <see cref="Files"/> is what merging Head would bring into Base
/// (the diff from their merge base, like a pull request).
/// </summary>
public sealed record Comparison(
    string Base,
    string Head,
    string BaseHash,
    string HeadHash,
    string? MergeBase,
    IReadOnlyList<CommitInfo> Ahead,
    IReadOnlyList<CommitInfo> Behind,
    int AheadCount,
    int BehindCount,
    IReadOnlyList<ChangedFile> Files
)
{
    /// <summary>What the file diffs start from: the merge base, or Base itself for unrelated histories.</summary>
    public string DiffBase => MergeBase ?? BaseHash;

    public bool IsSame => BaseHash == HeadHash;

    public int Added => Files.Sum(f => f.Added);
    public int Deleted => Files.Sum(f => f.Deleted);

    /// <summary>"3 ahead, 5 behind" style summary in words.</summary>
    public string Summary =>
        IsSame
            ? $"{Head} and {Base} point at the same commit."
            : (AheadCount, BehindCount) switch
            {
                (0, 0) => $"{Head} and {Base} have the same content.",
                (_, 0) => $"{Head} is {Commits(AheadCount)} ahead of {Base}.",
                (0, _) => $"{Head} is {Commits(BehindCount)} behind {Base}.",
                _ => $"{Head} is {Commits(AheadCount)} ahead and {Commits(BehindCount)} behind {Base}.",
            };

    private static string Commits(int n) => n == 1 ? "1 commit" : $"{n} commits";
}
