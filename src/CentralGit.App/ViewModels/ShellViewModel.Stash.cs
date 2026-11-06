using CentralGit.Application.Operations;

namespace CentralGit.App.ViewModels;

/// <summary>The Stash page and stashing from the Changes page.</summary>
public sealed partial class ShellViewModel
{
    public StashViewModel Stashes { get; } = new();

    public Task LoadStashesAsync() => Stashes.LoadAsync();

    /// <summary>Parks uncommitted changes (all of them, or only <paramref name="paths"/>) under a message.</summary>
    public async Task StashChangesAsync(IReadOnlyList<string>? paths)
    {
        if (_session is not { } session || PromptText is null)
            return;
        var what = paths is { Count: > 0 } ? (paths.Count == 1 ? paths[0] : $"{paths.Count} files") : "all changes";
        var message = await PromptText(
            "Stash changes",
            $"Parks {what} (new files too) and leaves the working tree clean. Bring them back from the Stash page. A short note helps you find them later:",
            "",
            "Stash",
            _ => Task.FromResult<string?>(null)
        );
        if (message is null)
            return;
        await RunStashOperationAsync(
            "stash",
            $"Stash {what}",
            session.Stashes.Save(message, includeUntracked: true, paths ?? []),
            () => $"Stashed {what}. They're on the Stash page."
        );
    }

    public Task ApplyStashAsync(StashRowViewModel row, bool pop) =>
        _session is not { } session
            ? Task.CompletedTask
            : RunStashOperationAsync(
                "stash",
                $"{(pop ? "Pop" : "Apply")} “{row.Title}”",
                pop ? session.Stashes.Pop(row.Stash) : session.Stashes.Apply(row.Stash),
                () =>
                    pop
                        ? $"Brought back “{row.Title}” and removed it from the stash"
                        : $"Applied “{row.Title}”; it's still in the stash"
            );

    public async Task DropStashAsync(StashRowViewModel row)
    {
        if (_session is not { } session)
            return;
        if (
            ConfirmDestructive is not null
            && !await ConfirmDestructive(
                "Delete this stash?",
                $"“{row.Title}” is thrown away. It can still be found by its hash for a short while, but not from here.",
                "Delete stash"
            )
        )
            return;
        await RunStashOperationAsync(
            "stash",
            $"Drop “{row.Title}”",
            session.Stashes.Drop(row.Stash),
            () => $"Deleted “{row.Title}” from the stash"
        );
    }

    private async Task RunStashOperationAsync(
        string kind,
        string title,
        IPreparedOperation operation,
        Func<string> success
    )
    {
        if (Operations.IsBusy)
            return;
        var (result, op) = await Operations.RunAsync(kind, title, operation);
        await FinishAsync(result, op, success);
        await Changes.RefreshAsync();
        await Stashes.LoadAsync();
        if (result.Failure?.Fix == Domain.Operations.SuggestedFix.ResolveConflicts)
            Page = AppPage.Changes;
    }
}
