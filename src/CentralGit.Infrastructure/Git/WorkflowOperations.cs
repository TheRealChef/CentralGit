using CentralGit.Domain.Workflow;

namespace CentralGit.Infrastructure.Git;

/// <summary>
/// Command lines for commit actions, merge/rebase, tags and the stash. Nothing here may open an
/// editor: messages are given on the command line or git's default is kept (<c>--no-edit</c>,
/// <c>core.editor=true</c> for the --continue commands that would ask).
/// </summary>
public static class WorkflowOperations
{
    /// <summary>A merge commit is replayed against its first parent (the branch it was merged into).</summary>
    public static IReadOnlyList<string> CherryPick(string commit, bool isMerge) =>
        isMerge ? ["cherry-pick", "-m", "1", commit] : ["cherry-pick", commit];

    public static IReadOnlyList<string> Revert(string commit, bool isMerge) =>
        isMerge ? ["revert", "--no-edit", "-m", "1", commit] : ["revert", "--no-edit", commit];

    public static IReadOnlyList<string> Reset(string commit, ResetMode mode) =>
        [
            "reset",
            mode switch
            {
                ResetMode.Soft => "--soft",
                ResetMode.Hard => "--hard",
                _ => "--mixed",
            },
            commit,
        ];

    public static IReadOnlyList<string> Merge(string branch) => ["merge", "--no-edit", "--progress", branch];

    public static IReadOnlyList<string> Rebase(string onto) => ["rebase", onto];

    public static IReadOnlyList<string> BisectStart() => ["bisect", "start"];

    /// <summary><c>bisect good|bad|skip</c> on a commit, or on HEAD (the one git checked out to test).</summary>
    public static IReadOnlyList<string> BisectMark(string? commit, BisectMark mark)
    {
        var verb = mark switch
        {
            Domain.Workflow.BisectMark.Good => "good",
            Domain.Workflow.BisectMark.Bad => "bad",
            _ => "skip",
        };
        return commit is null ? ["bisect", verb] : ["bisect", verb, commit];
    }

    public static IReadOnlyList<string> BisectReset() => ["bisect", "reset"];

    /// <summary>
    /// <c>rebase -i</c> without an editor: the plan is written to a todo file that the sequence
    /// editor copies over git's own (<c>cp</c> runs in git's bundled shell), squash messages are kept as
    /// git combines them (<c>core.editor=true</c>), and rewording is an <c>exec</c> that amends with a
    /// message file right after the pick.
    /// </summary>
    public static IReadOnlyList<string> InteractiveRebase(string? @base, IReadOnlyList<RebaseStep> steps)
    {
        var folder = Path.Combine(Path.GetTempPath(), "CentralGit", "rebase-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(folder);
        var todo = new System.Text.StringBuilder();
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            todo.Append(RebasePlan.Verb(step.Action))
                .Append(' ')
                .Append(step.Hash)
                .Append(' ')
                .Append(step.Subject)
                .Append('\n');
            if (step.Action != RebaseAction.Reword)
                continue;
            var message = Path.Combine(folder, $"message-{i}.txt");
            File.WriteAllText(message, step.Message!.Trim() + "\n", new System.Text.UTF8Encoding(false));
            todo.Append("exec git commit --amend --only --no-verify --allow-empty -F ")
                .Append(ShellQuote(message))
                .Append('\n');
        }
        var todoFile = Path.Combine(folder, "todo.txt");
        File.WriteAllText(todoFile, todo.ToString(), new System.Text.UTF8Encoding(false));
        return
        [
            "-c",
            "sequence.editor=cp " + ShellQuote(todoFile),
            "-c",
            "core.editor=true",
            "rebase",
            "-i",
            @base ?? "--root",
        ];
    }

    /// <summary>A path for git's POSIX shell: forward slashes, single-quoted.</summary>
    private static string ShellQuote(string path) => "'" + path.Replace('\\', '/').Replace("'", "'\\''") + "'";

    public static IReadOnlyList<string> Continue(InProgress state) =>
        state switch
        {
            // A stopped merge is finished by committing; MERGE_MSG already holds the message.
            InProgress.Merge => ["commit", "--no-edit"],
            InProgress.Rebase => ["-c", "core.editor=true", "rebase", "--continue"],
            InProgress.CherryPick => ["-c", "core.editor=true", "cherry-pick", "--continue"],
            InProgress.Revert => ["-c", "core.editor=true", "revert", "--continue"],
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Nothing is in progress."),
        };

    public static IReadOnlyList<string> Abort(InProgress state) =>
        state switch
        {
            InProgress.Merge => ["merge", "--abort"],
            InProgress.Rebase => ["rebase", "--abort"],
            InProgress.CherryPick => ["cherry-pick", "--abort"],
            InProgress.Revert => ["revert", "--abort"],
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Nothing is in progress."),
        };

    public static IReadOnlyList<string> CreateTag(string name, string commit, string? message) =>
        message is null ? ["tag", name, commit] : ["tag", "-a", name, "-m", message, commit];

    public static IReadOnlyList<string> DeleteTag(string name) => ["tag", "-d", name];

    public static IReadOnlyList<string> PushTag(string remote, string name) =>
        ["push", "--progress", remote, $"refs/tags/{name}"];

    public static IReadOnlyList<string> DeleteRemoteTag(string remote, string name) =>
        ["push", "--progress", remote, "--delete", $"refs/tags/{name}"];

    public static IReadOnlyList<string> StashSave(string message, bool includeUntracked, IReadOnlyList<string> paths)
    {
        List<string> args = ["stash", "push"];
        if (includeUntracked)
            args.Add("--include-untracked");
        if (message.Length > 0)
            args.AddRange(["-m", message]);
        if (paths.Count > 0)
            args.AddRange(["--", .. paths.Select(p => ":(literal)" + p)]);
        return args;
    }

    public static IReadOnlyList<string> StashApply(string name, bool pop) => ["stash", pop ? "pop" : "apply", name];

    public static IReadOnlyList<string> StashDrop(string name) => ["stash", "drop", name];
}
