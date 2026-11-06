namespace CentralGit.Domain.Workflow;

/// <summary>A multi-step git command that stopped half-way and waits for the user (usually on conflicts).</summary>
public enum InProgress
{
    None,
    Merge,
    Rebase,
    CherryPick,
    Revert,
}

public static class InProgressText
{
    /// <summary>"Merging", "Rebasing"… for the banner.</summary>
    public static string Verb(InProgress state) =>
        state switch
        {
            InProgress.Merge => "Merging",
            InProgress.Rebase => "Rebasing",
            InProgress.CherryPick => "Cherry-picking",
            InProgress.Revert => "Reverting",
            _ => "",
        };

    /// <summary>"merge", "rebase"… for button labels ("Abort merge").</summary>
    public static string Noun(InProgress state) =>
        state switch
        {
            InProgress.Merge => "merge",
            InProgress.Rebase => "rebase",
            InProgress.CherryPick => "cherry-pick",
            InProgress.Revert => "revert",
            _ => "",
        };
}

/// <summary>How far <c>git reset</c> goes: the branch only, also the index, or also the working tree.</summary>
public enum ResetMode
{
    /// <summary>Move the branch; the undone commits' changes stay staged.</summary>
    Soft,

    /// <summary>Move the branch and the index; the changes stay in the working tree, unstaged.</summary>
    Mixed,

    /// <summary>Move everything: uncommitted changes and the undone commits' changes are thrown away.</summary>
    Hard,
}

/// <summary>A conflicted file resolved by taking one side whole.</summary>
public enum ConflictSide
{
    /// <summary>HEAD's version (during a rebase: the branch being rebased onto).</summary>
    Ours,

    /// <summary>The incoming version (during a rebase: your commit being replayed).</summary>
    Theirs,
}

/// <summary>A tag: lightweight (a name for a commit) or annotated (with its own message, tagger and date).</summary>
public sealed record Tag(string Name, string Commit, bool IsAnnotated, string Message, DateTimeOffset? When);

/// <summary>One entry of the stash: <c>stash@{Index}</c>.</summary>
public sealed record Stash(int Index, string Commit, string Message, string? Branch, DateTimeOffset When)
{
    public string Name => $"stash@{{{Index}}}";

    /// <summary>
    /// Git's subject is "WIP on main: abc123 subject" or "On main: my message"; the useful part is after the colon.
    /// </summary>
    public static (string? Branch, string Message) ParseSubject(string subject)
    {
        foreach (var prefix in new[] { "WIP on ", "On " })
        {
            if (!subject.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            var colon = subject.IndexOf(": ", prefix.Length, StringComparison.Ordinal);
            if (colon < 0)
                break;
            var branch = subject[prefix.Length..colon];
            var rest = subject[(colon + 2)..];
            // "WIP on main: abc1234 Last commit" — git's own label: say so instead of repeating the commit.
            if (prefix == "WIP on ")
            {
                var space = rest.IndexOf(' ');
                rest = space > 0 ? $"Work in progress (on top of “{rest[(space + 1)..]}”)" : "Work in progress";
            }
            return (branch == "(no branch)" ? null : branch, rest);
        }
        return (null, subject);
    }
}

/// <summary>One line of a blamed file and the commit that last changed it.</summary>
public sealed record BlameLine(
    int Number,
    string Commit,
    string Author,
    DateTimeOffset When,
    string Summary,
    string Text
)
{
    /// <summary>Lines not committed yet (the working tree) carry an all-zero hash.</summary>
    public bool IsUncommitted => Commit.All(c => c == '0');
}

public static class TagNames
{
    /// <summary>Quick checks before asking git: an error sentence, or null when it might be fine.</summary>
    public static string? Precheck(string name) =>
        name.Length == 0 ? "Give the tag a name."
        : name.Any(char.IsWhiteSpace) ? "Tag names can't contain spaces."
        : name.StartsWith('-') ? "Tag names can't start with a dash."
        : null;
}

/// <summary>
/// A file in a stash and the two revisions to diff it between: tracked changes against the commit
/// the stash was made on, untracked files (kept in a separate commit) against nothing.
/// </summary>
public sealed record StashFile(
    CentralGit.Domain.Commits.ChangedFile File,
    string Base,
    string Revision,
    bool IsUntracked
);

public enum BisectMark
{
    Good,
    Bad,
    Skip,
}

/// <summary>
/// Where a <c>git bisect</c> stands: the bad commit, the good ones, the commit checked out for
/// testing, and how many candidates are left. When only the bad commit remains, it is the culprit.
/// </summary>
public sealed record BisectState(
    bool IsActive,
    string? Bad,
    IReadOnlyList<string> Good,
    string? Current,
    int Candidates
)
{
    public static readonly BisectState Inactive = new(false, null, [], null, 0);

    public bool HasBothEnds => Bad is not null && Good.Count > 0;

    /// <summary>The first bad commit, once the search has narrowed down to it.</summary>
    public string? FirstBad => HasBothEnds && Candidates <= 1 ? Bad : null;

    /// <summary>Roughly how many more good/bad answers are needed.</summary>
    public int StepsLeft => Candidates <= 1 ? 0 : (int)Math.Ceiling(Math.Log2(Candidates));
}
