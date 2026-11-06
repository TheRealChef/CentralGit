namespace CentralGit.Domain.Workflow;

/// <summary>What happens to one commit in an interactive rebase.</summary>
public enum RebaseAction
{
    Pick,
    Reword,
    Squash,
    Fixup,
    Drop,
}

/// <summary>One line of the plan: a commit and what to do with it (and its new message, for Reword).</summary>
public sealed record RebaseStep(
    string Hash,
    string Subject,
    RebaseAction Action = RebaseAction.Pick,
    string? Message = null
);

/// <summary>
/// The commits from a chosen one up to HEAD (oldest first), and the commit they sit on
/// (<see cref="Base"/>; null when the range starts at the root).
/// </summary>
public sealed record RebaseRange(string? Base, IReadOnlyList<RebaseStep> Steps, bool HasMerges);

public static class RebasePlan
{
    /// <summary>Why this plan can't run, or null when it can.</summary>
    public static string? Check(IReadOnlyList<RebaseStep> steps, IReadOnlyList<string> originalOrder)
    {
        var kept = steps.Where(s => s.Action != RebaseAction.Drop).ToList();
        if (kept.Count == 0)
            return "Every commit is dropped. To throw them all away, reset the branch instead.";
        if (kept[0].Action is RebaseAction.Squash or RebaseAction.Fixup)
            return "The first kept commit can't be squashed: there's nothing above it to fold into.";
        if (steps.FirstOrDefault(s => s.Action == RebaseAction.Reword && string.IsNullOrWhiteSpace(s.Message)) is { } r)
            return $"Write the new message for “{r.Subject}”.";
        if (IsUnchanged(steps, originalOrder))
            return "Nothing to change yet: reorder commits or pick a different action.";
        return null;
    }

    /// <summary>Same commits, same order, all picked: running it would do nothing.</summary>
    public static bool IsUnchanged(IReadOnlyList<RebaseStep> steps, IReadOnlyList<string> originalOrder) =>
        steps.All(s => s.Action == RebaseAction.Pick) && steps.Select(s => s.Hash).SequenceEqual(originalOrder);

    public static string Verb(RebaseAction action) =>
        action switch
        {
            RebaseAction.Reword => "pick", // the new message is applied by an exec line right after
            RebaseAction.Squash => "squash",
            RebaseAction.Fixup => "fixup",
            RebaseAction.Drop => "drop",
            _ => "pick",
        };
}
