namespace CentralGit.Domain.Recovery;

/// <summary>What moved HEAD, from the reflog message (<c>commit (amend): …</c>, <c>checkout: moving from a to b</c>).</summary>
public enum ReflogAction
{
    Commit,
    Amend,
    Merge,
    Rebase,
    CherryPick,
    Revert,
    Reset,
    Checkout,
    Pull,
    Clone,
    Branch,
    Other,
}

/// <summary>One line of the HEAD reflog: where HEAD pointed and why it moved there.</summary>
public sealed record ReflogEntry(
    int Index,
    string Hash,
    DateTimeOffset When,
    string Message,
    string Subject,
    string Author
)
{
    public ReflogAction Action => Reflog.Classify(Message).Action;
    public string Description => Reflog.Classify(Message).Description;
    public string ShortHash => Hash.Length > 9 ? Hash[..9] : Hash;
}

/// <summary>A reflog entry and whether a branch, tag or stash still holds its commit.</summary>
public sealed record RecoveryPoint(ReflogEntry Entry, bool IsOnBranch);

public static class Reflog
{
    /// <summary>Splits <c>action: details</c> and words it for people.</summary>
    public static (ReflogAction Action, string Description) Classify(string message)
    {
        var colon = message.IndexOf(": ", StringComparison.Ordinal);
        var verb = colon < 0 ? message : message[..colon];
        var detail = colon < 0 ? "" : message[(colon + 2)..];

        if (verb.StartsWith("commit (amend)", StringComparison.Ordinal))
            return (ReflogAction.Amend, $"Amended: {detail}");
        if (verb.StartsWith("commit (merge)", StringComparison.Ordinal))
            return (ReflogAction.Merge, $"Merge commit: {detail}");
        if (verb.StartsWith("commit", StringComparison.Ordinal))
            return (ReflogAction.Commit, detail.Length > 0 ? detail : "Commit");
        if (verb.StartsWith("merge", StringComparison.Ordinal))
            return (ReflogAction.Merge, $"Merged {verb[5..].Trim()}{(detail.Length > 0 ? $" ({detail})" : "")}");
        if (verb.StartsWith("rebase", StringComparison.Ordinal))
            return (ReflogAction.Rebase, Rebase(verb, detail));
        if (verb.StartsWith("cherry-pick", StringComparison.Ordinal))
            return (ReflogAction.CherryPick, $"Cherry-picked: {detail}");
        if (verb.StartsWith("revert", StringComparison.Ordinal))
            return (ReflogAction.Revert, $"Reverted: {detail}");
        if (verb == "reset")
            return (
                ReflogAction.Reset,
                detail.StartsWith("moving to ", StringComparison.Ordinal) ? $"Reset to {detail[10..]}" : "Reset"
            );
        if (verb == "checkout")
            return (ReflogAction.Checkout, Checkout(detail));
        if (verb.StartsWith("pull", StringComparison.Ordinal))
            return (ReflogAction.Pull, detail.Length > 0 ? $"Pulled ({detail})" : "Pulled");
        if (verb == "clone")
            return (ReflogAction.Clone, "Cloned");
        if (verb == "branch")
            return (ReflogAction.Branch, detail);
        return (ReflogAction.Other, message);
    }

    /// <summary>
    /// The recoverable history: newest first, one row per commit (HEAD often returns to the same commit).
    /// <paramref name="lostOnly"/> keeps commits that no branch, tag or stash holds any more.
    /// </summary>
    public static IReadOnlyList<RecoveryPoint> Points(
        IEnumerable<ReflogEntry> entries,
        IReadOnlySet<string> unreachable,
        bool lostOnly
    )
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var points = new List<RecoveryPoint>();
        foreach (var entry in entries.OrderBy(e => e.Index))
        {
            if (!seen.Add(entry.Hash))
                continue;
            var onBranch = !unreachable.Contains(entry.Hash);
            if (lostOnly && onBranch)
                continue;
            points.Add(new RecoveryPoint(entry, onBranch));
        }
        return points;
    }

    private static string Checkout(string detail)
    {
        const string from = "moving from ";
        if (!detail.StartsWith(from, StringComparison.Ordinal))
            return "Checked out";
        var rest = detail[from.Length..];
        var to = rest.LastIndexOf(" to ", StringComparison.Ordinal);
        return to < 0 ? $"Left {rest}" : $"Switched from {rest[..to]} to {rest[(to + 4)..]}";
    }

    private static string Rebase(string verb, string detail)
    {
        var step =
            verb.Contains("(finish)", StringComparison.Ordinal) ? "finished"
            : verb.Contains("(start)", StringComparison.Ordinal) ? "started"
            : verb.Contains("(abort)", StringComparison.Ordinal) ? "aborted"
            : null;
        return step is not null ? $"Rebase {step}{(detail.Length > 0 ? $": {detail}" : "")}" : $"Rebase: {detail}";
    }
}
