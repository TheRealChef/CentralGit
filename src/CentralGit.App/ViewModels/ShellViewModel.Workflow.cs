using System.Collections.ObjectModel;
using CentralGit.Application.Features.Workflow;
using CentralGit.Application.Git;
using CentralGit.Application.Operations;
using CentralGit.Application.Ports;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Workflow;

namespace CentralGit.App.ViewModels;

/// <summary>
/// Commit actions (cherry-pick, revert, reset, tag), merge and rebase, the banner for an operation
/// that stopped on conflicts (continue, abort, take a side), and the sidebar's tags.
/// </summary>
public sealed partial class ShellViewModel
{
    private InProgress _inProgress;
    private int _conflicts;
    private int _inProgressGeneration;

    /// <summary>Asks to pick one of several choices: (title, message, choices) → index or -1.</summary>
    public Func<string, string, IReadOnlyList<string>, Task<int>>? Choose { get; set; }

    /// <summary>Asks for a tag name and optional message: (title, message, validate) → (name, message) or null.</summary>
    public Func<
        string,
        string,
        Func<string, Task<string?>>,
        Task<(string Name, string Message)?>
    >? PromptTag { get; set; }

    // ─── Stopped operations ───────────────────────────────────────────────

    public InProgress InProgress => _inProgress;
    public bool HasInProgress => _inProgress != InProgress.None;
    public bool CanContinue => HasInProgress && _conflicts == 0;
    public string InProgressTitle =>
        _conflicts > 0
            ? $"{InProgressText.Verb(_inProgress)} stopped on {Plural(_conflicts, "conflict")}"
            : $"{InProgressText.Verb(_inProgress)}: all conflicts resolved";
    public string InProgressDetail =>
        _conflicts > 0
            ? "Fix the conflicted files (marked !) in your editor and stage them, or take one side whole with Keep ours / Take theirs. Abort goes back to how things were."
            : $"Stage anything else you want in it, then continue to finish the {InProgressText.Noun(_inProgress)}.";
    public string ContinueLabel => $"Continue {InProgressText.Noun(_inProgress)}";
    public string AbortLabel => $"Abort {InProgressText.Noun(_inProgress)}";

    /// <summary>Re-reads whether a merge/rebase/… is waiting; <paramref name="status"/> gives the conflict count.</summary>
    private async Task RefreshInProgressAsync(WorkingTreeStatus status)
    {
        if (_session is not { } session)
            return;
        var generation = ++_inProgressGeneration;
        InProgress state;
        try
        {
            state = await Task.Run(() => session.Actions.GetInProgressAsync());
        }
        catch (GitException)
        {
            state = InProgress.None;
        }
        if (generation != _inProgressGeneration || _session != session)
            return;
        _inProgress = state;
        _conflicts = status.Changes.Count(c => c.IsConflicted);
        OnPropertyChanged(nameof(InProgress));
        OnPropertyChanged(nameof(HasInProgress));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(InProgressTitle));
        OnPropertyChanged(nameof(InProgressDetail));
        OnPropertyChanged(nameof(ContinueLabel));
        OnPropertyChanged(nameof(AbortLabel));
    }

    public Task ContinueInProgressAsync() =>
        _inProgress == InProgress.None
            ? Task.CompletedTask
            : RunActionAsync(
                "merge",
                ContinueLabel,
                a => a.Continue(_inProgress),
                null,
                () => $"Finished the {InProgressText.Noun(_inProgress)}"
            );

    public async Task AbortInProgressAsync()
    {
        if (_inProgress == InProgress.None)
            return;
        var noun = InProgressText.Noun(_inProgress);
        if (
            ConfirmDestructive is not null
            && !await ConfirmDestructive(
                $"Abort the {noun}?",
                $"Everything goes back to how it was before the {noun} started. Conflict resolutions you made so far are thrown away.",
                AbortLabel
            )
        )
            return;
        await RunActionAsync("merge", AbortLabel, a => a.Abort(_inProgress), null, () => $"Aborted the {noun}");
    }

    /// <summary>Resolves a conflicted file by taking one side whole, then refreshes the Changes page.</summary>
    public async Task ResolveConflictAsync(string path, ConflictSide side)
    {
        if (_session is not { } session)
            return;
        var result = await Task.Run(() => session.Actions.ResolveConflictAsync(path, side));
        SetStatus(
            result.Succeeded
                ? $"Resolved {path} with {(side == ConflictSide.Ours ? "our" : "their")} version"
                : $"Couldn't resolve {path}: {FirstLine(result.StandardError)}",
            error: !result.Succeeded
        );
        await Changes.RefreshAsync();
    }

    // ─── Commit actions ───────────────────────────────────────────────────

    public Task CherryPickAsync(CommitRowViewModel row) =>
        RunActionAsync(
            "merge",
            $"Cherry-pick {row.ShortHash}",
            a => a.CherryPick(row.Commit),
            null,
            () => $"Copied “{row.Subject}” onto {CurrentBranch ?? "HEAD"}"
        );

    public async Task RevertAsync(CommitRowViewModel row)
    {
        if (
            ConfirmDestructive is not null
            && !await ConfirmDestructive(
                "Revert this commit?",
                $"Adds a new commit to {CurrentBranch ?? "HEAD"} that undoes “{row.Subject}”. History stays as it is, so this is safe on shared branches.",
                "Revert"
            )
        )
            return;
        await RunActionAsync(
            "merge",
            $"Revert {row.ShortHash}",
            a => a.Revert(row.Commit),
            null,
            () => $"Reverted “{row.Subject}”"
        );
    }

    public async Task ResetToAsync(CommitRowViewModel row)
    {
        if (Choose is null || _branch?.Head is not { } branch)
        {
            SetStatus("Reset needs a branch: switch to one first.", error: true);
            return;
        }
        // Commits only HEAD has (not on the upstream) are safe to drop; pushed ones rewrite shared history.
        var pushed = _branch.Upstream is not null && row.Sync != CommitSync.Outgoing && !row.IsHead;
        var warning = pushed
            ? "\n\nCommits after this one are already on the remote: pushing afterwards needs a force push and affects others."
            : "";
        var choice = await Choose(
            $"Reset {branch} to {row.ShortHash}?",
            $"Moves {branch} back to “{row.Subject}”. What should happen to the changes from the commits after it?{warning}",
            [
                "Keep them staged (soft)",
                "Keep them in the working tree (mixed)",
                "Throw everything away (hard), including uncommitted changes",
            ]
        );
        if (choice < 0)
            return;
        var mode = (ResetMode)choice;
        if (
            mode == ResetMode.Hard
            && ConfirmDestructive is not null
            && !await ConfirmDestructive(
                "Throw the changes away?",
                "A hard reset deletes uncommitted changes for good. The commits themselves can still be found in Recovery for a while.",
                "Reset hard"
            )
        )
            return;
        await RunActionAsync(
            "reset",
            $"Reset {branch} to {row.ShortHash} ({mode.ToString().ToLowerInvariant()})",
            a => a.Reset(row.Commit.Hash, mode),
            null,
            () => $"{branch} is now at {row.ShortHash}"
        );
    }

    /// <summary>Undo the last commit but keep its changes staged, ready to fix and commit again.</summary>
    public async Task UndoLastCommitAsync()
    {
        if (_branch?.Head is not { } branch || _all.FirstOrDefault(r => r.IsHead) is not { } head)
            return;
        if (head.Commit.Parents.Count == 0)
        {
            SetStatus("That's the first commit: there's nothing before it to go back to.", error: true);
            return;
        }
        var pushed = _branch.Upstream is not null && head.Sync != CommitSync.Outgoing;
        if (
            ConfirmDestructive is not null
            && !await ConfirmDestructive(
                "Undo the last commit?",
                $"“{head.Subject}” comes off {branch}; its changes stay staged so you can fix them and commit again."
                    + (pushed ? "\n\nIt's already on the remote, so pushing afterwards needs a force push." : ""),
                "Undo commit"
            )
        )
            return;
        await RunActionAsync(
            "reset",
            $"Undo “{head.Subject}”",
            a => a.Reset(head.Commit.Parents[0], ResetMode.Soft),
            null,
            () => $"Undid “{head.Subject}”. Its changes are staged."
        );
    }

    public Task MergeIntoCurrentAsync(string branch) =>
        CurrentBranch is not { } current || branch == current
            ? Task.CompletedTask
            : RunActionAsync(
                "merge",
                $"Merge {branch} into {current}",
                a => a.Merge(branch),
                null,
                () => $"Merged {branch} into {current}"
            );

    public async Task RebaseCurrentOntoAsync(string onto)
    {
        if (CurrentBranch is not { } current || onto == current)
            return;
        if (
            ConfirmDestructive is not null
            && !await ConfirmDestructive(
                $"Rebase {current} onto {onto}?",
                $"Your commits on {current} are replayed on top of {onto}, so they get new hashes. If {current} is already pushed, you'll need a force push afterwards.",
                "Rebase"
            )
        )
            return;
        await RunActionAsync(
            "merge",
            $"Rebase {current} onto {onto}",
            a => a.Rebase(onto),
            null,
            () => $"{current} now sits on top of {onto}"
        );
    }

    private async Task RunActionAsync(
        string kind,
        string title,
        Func<ICommitActionService, IPreparedOperation> operation,
        string? target,
        Func<string> success
    )
    {
        if (_session is not { } session || Operations.IsBusy)
            return;
        var (result, op) = await Operations.RunAsync(kind, title, operation(session.Actions));
        op.Target = target;
        await FinishAsync(result, op, success);
        OnPropertyChanged(nameof(CurrentBranch));
        // Stopped on conflicts: the place to deal with them is the Changes page.
        if (result.Failure?.Fix == Domain.Operations.SuggestedFix.ResolveConflicts)
            Page = AppPage.Changes;
    }

    // ─── Tags ─────────────────────────────────────────────────────────────

    public ObservableCollection<TagItemViewModel> Tags { get; } = [];
    public bool HasTags => Tags.Count > 0;
    public string TagsHeader => Tags.Count > 0 ? $"TAGS · {Tags.Count}" : "TAGS";

    private async Task LoadTagsAsync(Application.Features.Repositories.RepositorySession session)
    {
        try
        {
            var tags = await Task.Run(() => session.Tags.GetTagsAsync());
            if (_session != session)
                return;
            Tags.Clear();
            var now = DateTimeOffset.Now;
            foreach (var tag in tags)
                Tags.Add(new TagItemViewModel(tag, now));
        }
        catch (GitException)
        {
            Tags.Clear();
        }
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(TagsHeader));
    }

    public async Task CreateTagAsync(CommitRowViewModel row)
    {
        if (_session is not { } session || PromptTag is null)
            return;
        var answer = await PromptTag(
            $"Tag {row.ShortHash}",
            $"“{row.Subject}”. With a message the tag is annotated (records who tagged it and when), which is what releases usually want.",
            name => session.Tags.ValidateNewNameAsync(name)
        );
        if (answer is not { } tag)
            return;
        await RunTagOperationAsync(
            $"Tag {row.ShortHash} as {tag.Name}",
            session.Tags.Create(tag.Name, row.Commit.Hash, tag.Message),
            () => $"Tagged {row.ShortHash} as {tag.Name}. Push it to share it."
        );
    }

    public bool RevealTag(TagItemViewModel tag)
    {
        Page = AppPage.History;
        if (SelectCommit(tag.Tag.Commit))
            return true;
        ReportNotLoaded(tag.Tag.Commit[..7]);
        return false;
    }

    public async Task PushTagAsync(TagItemViewModel tag)
    {
        if (_session is not { } session)
            return;
        var remote = await DefaultRemoteAsync(session);
        if (remote is null)
        {
            SetStatus("This repository has no remote to push the tag to.", error: true);
            return;
        }
        await RunTagOperationAsync(
            $"Push tag {tag.Name} to {remote}",
            session.Tags.Push(remote, tag.Name),
            () => $"Pushed {tag.Name} to {remote}",
            kind: "push"
        );
    }

    public async Task DeleteTagAsync(TagItemViewModel tag)
    {
        if (_session is not { } session || Choose is null)
            return;
        var remote = await DefaultRemoteAsync(session);
        IReadOnlyList<string> choices = remote is null
            ? ["Delete it here"]
            : ["Delete it here only", $"Delete it here and on {remote}"];
        var choice = await Choose(
            $"Delete tag {tag.Name}?",
            remote is null
                ? "The commit it points at stays where it is."
                : $"The commit stays. Deleting it on {remote} affects everyone who uses that remote.",
            choices
        );
        if (choice < 0)
            return;
        await RunTagOperationAsync(
            $"Delete tag {tag.Name}",
            session.Tags.Delete(tag.Name),
            () => $"Deleted tag {tag.Name}"
        );
        if (choice == 1 && remote is not null)
            await RunTagOperationAsync(
                $"Delete tag {tag.Name} on {remote}",
                session.Tags.DeleteOnRemote(remote, tag.Name),
                () => $"Deleted tag {tag.Name} here and on {remote}",
                kind: "push"
            );
    }

    private async Task<string?> DefaultRemoteAsync(Application.Features.Repositories.RepositorySession session)
    {
        if (_branch?.Upstream is { } upstream && upstream.IndexOf('/') > 0)
            return upstream[..upstream.IndexOf('/')];
        var remotes = await session.Sync.GetRemotesAsync();
        return remotes.Contains("origin") ? "origin" : remotes.FirstOrDefault();
    }

    private async Task RunTagOperationAsync(
        string title,
        IPreparedOperation operation,
        Func<string> success,
        string kind = "branch"
    )
    {
        if (_session is not { } session || Operations.IsBusy)
            return;
        var (result, op) = await Operations.RunAsync(kind, title, operation);
        await FinishAsync(result, op, success);
        await LoadTagsAsync(session);
    }

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";
}

/// <summary>A tag in the sidebar.</summary>
public sealed class TagItemViewModel(Tag tag, DateTimeOffset now)
{
    public Tag Tag { get; } = tag;
    public string Name { get; } = tag.Name;
    public string AgeText { get; } = tag.When is { } when ? RelativeTime.Format(when, now) : "";
    public string Tooltip { get; } =
        (tag.IsAnnotated ? (tag.Message.Length > 0 ? tag.Message : "Annotated tag") : "Lightweight tag")
        + $" · {tag.Commit[..Math.Min(9, tag.Commit.Length)]}";
}

public sealed partial class ShellViewModel
{
    /// <summary>Hands the repository (or one file in it) to Explorer, a terminal or the editor.</summary>
    public void OpenExternal(ExternalTool tool, string? file = null)
    {
        if (_session is not { } session)
            return;
        if (_external.Open(tool, session.Path, file) is { } problem)
            SetStatus(problem, error: true);
    }
}

public sealed partial class ShellViewModel
{
    /// <summary>Long diff lines wrap to the pane, or stay whole and scroll sideways. Remembered in settings.</summary>
    public bool WrapDiffLines
    {
        get => Diff.DiffRowViewModel.WrapLines;
        set
        {
            if (value == Diff.DiffRowViewModel.WrapLines)
                return;
            Diff.DiffRowViewModel.WrapLines = value;
            _settings.Update(s => s with { WrapDiffLines = value });
            OnPropertyChanged();
            Inspector.Relayout();
            Changes.Relayout();
            Compare.Relayout();
            FileHistory.Relayout();
            Stashes.Relayout();
        }
    }
}

/// <summary>The conflict editor: one conflicted file at a time, opened from the Changes page.</summary>
public sealed partial class ShellViewModel
{
    public ConflictEditorViewModel ConflictEditor { get; } = new();

    private string? _conflictPath;

    private void SyncConflictEditor()
    {
        var path = Changes.IsConflictSelected ? Changes.SelectedPath : null;
        if (path == _conflictPath)
            return;
        _conflictPath = path;
        if (path is null)
            ConflictEditor.Clear();
        else
            _ = ConflictEditor.LoadAsync(path);
    }

    /// <summary>Writes the decided file, stages it, and moves on (Continue becomes possible once none are left).</summary>
    public async Task SaveConflictResolutionAsync()
    {
        var path = ConflictEditor.Path;
        if (await ConflictEditor.SaveAsync() is not { Succeeded: true })
            return;
        _conflictPath = null;
        ConflictEditor.Clear();
        SetStatus($"Resolved {path} and staged it", error: false);
        await Changes.RefreshAsync();
    }
}

/// <summary>Interactive rebase: rewrite the commits from one in History up to HEAD.</summary>
public sealed partial class ShellViewModel
{
    /// <summary>Shows the planner; true when the user chose to run it.</summary>
    public Func<RebasePlannerViewModel, Task<bool>>? PlanRebase { get; set; }

    public async Task EditCommitsFromAsync(CommitRowViewModel row)
    {
        if (_session is not { } session || CurrentBranch is not { } branch || PlanRebase is null || Operations.IsBusy)
            return;
        if (HasInProgress)
        {
            SetStatus($"Finish or abort the {InProgressText.Noun(InProgress)} first.", error: true);
            return;
        }
        RebaseRange range;
        try
        {
            range = await Task.Run(() => session.Actions.GetRebaseRangeAsync(row.Commit.Hash));
        }
        catch (GitException e)
        {
            SetStatus($"{e.Result.CommandLine} failed: {e.Result.StandardError.Trim()}", error: true);
            return;
        }
        if (!range.Steps.Any(s => s.Hash == row.Commit.Hash))
        {
            SetStatus(
                $"{row.ShortHash} isn't on {branch}, so there's nothing of {branch} to rewrite from it.",
                error: true
            );
            return;
        }
        if (range.HasMerges)
        {
            SetStatus(
                "There are merge commits after this one. Rewriting across merges isn't supported here yet; start from a commit after the last merge.",
                error: true
            );
            return;
        }
        if (Changes.HasStaged || Changes.HasUnstaged)
        {
            SetStatus("Commit or stash your changes first: a rewrite needs a clean working tree.", error: true);
            return;
        }
        var planner = new RebasePlannerViewModel(range, branch);
        if (!await PlanRebase(planner))
            return;
        var count = planner.Steps.Count;
        await RunActionAsync(
            "merge",
            $"Rewrite {count} commit{(count == 1 ? "" : "s")} on {branch}",
            a => a.InteractiveRebase(range, planner.Plan),
            null,
            () => $"Rewrote {branch}. Push with force (with lease) if it was already pushed."
        );
    }
}
