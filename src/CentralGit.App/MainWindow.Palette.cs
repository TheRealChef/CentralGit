using CentralGit.App.ViewModels;
using CentralGit.Application.Ports;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CentralGit.App;

/// <summary>
/// The command palette (Ctrl+K). Its entries are rebuilt every time it opens, so it only offers
/// what makes sense right now: actions on the selected commit, Continue while a merge is stopped…
/// </summary>
public sealed partial class MainWindow
{
    public PaletteViewModel CommandPalette { get; } = new();

    private void OpenPalette()
    {
        SwitcherOverlay.Visibility = Visibility.Collapsed;
        CommandPalette.Reset(BuildPaletteItems());
        PaletteOverlay.Visibility = Visibility.Visible;
        PaletteBox.Focus(FocusState.Programmatic);
    }

    private bool ClosePalette()
    {
        if (PaletteOverlay.Visibility != Visibility.Visible)
            return false;
        PaletteOverlay.Visibility = Visibility.Collapsed;
        return true;
    }

    private async Task RunPaletteItemAsync(PaletteItemViewModel? item)
    {
        if (item is null)
            return;
        ClosePalette();
        FocusSelectedCommit();
        await item.Run();
    }

    private void OnOpenPalette(object sender, RoutedEventArgs e) => OpenPalette();

    private void OnPaletteBackdropTapped(object sender, TappedRoutedEventArgs e)
    {
        ClosePalette();
        FocusSelectedCommit();
    }

    private async void OnPaletteItemClick(object sender, ItemClickEventArgs e) =>
        await RunPaletteItemAsync(e.ClickedItem as PaletteItemViewModel);

    private async void OnPaletteKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Down:
                CommandPalette.Move(+1);
                PaletteList.ScrollIntoView(CommandPalette.Selected);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Up:
                CommandPalette.Move(-1);
                PaletteList.ScrollIntoView(CommandPalette.Selected);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Enter:
                e.Handled = true;
                await RunPaletteItemAsync(CommandPalette.Selected);
                break;
        }
    }

    private List<PaletteItemViewModel> BuildPaletteItems()
    {
        var items = new List<PaletteItemViewModel>();
        void Command(string title, string glyph, Func<Task> run, string shortcut = "", string detail = "") =>
            items.Add(new PaletteItemViewModel(PaletteGroups.Command, title, detail, glyph, run, shortcut));
        void Do(string title, string glyph, Action run, string shortcut = "", string detail = "") =>
            Command(
                title,
                glyph,
                () =>
                {
                    run();
                    return Task.CompletedTask;
                },
                shortcut,
                detail
            );

        var vm = ViewModel;
        var open = vm.RepositoryPath is not null;

        // Stopped operations come first: that's what you most likely opened the palette for.
        if (vm.HasInProgress)
        {
            if (vm.CanContinue)
                Command(vm.ContinueLabel, "", vm.ContinueInProgressAsync);
            Command(vm.AbortLabel, "", vm.AbortInProgressAsync);
        }

        if (vm.IsBisectTesting)
        {
            Command(
                "Bisect: this commit is good",
                "",
                () => vm.MarkBisectAsync(null, Domain.Workflow.BisectMark.Good)
            );
            Command("Bisect: this commit is bad", "", () => vm.MarkBisectAsync(null, Domain.Workflow.BisectMark.Bad));
            Command(
                "Bisect: can't test this commit",
                "",
                () => vm.MarkBisectAsync(null, Domain.Workflow.BisectMark.Skip)
            );
        }
        if (vm.HasBisect)
            Command("End bisect", "", vm.EndBisectAsync);

        if (open)
        {
            if (vm.HasPullRequest)
                Do($"Open pull request {vm.PullRequestText}", "", vm.OpenPullRequest);
            if (vm.CurrentBranch is { } branch && !vm.HasPullRequest)
                Command(
                    "Create pull request on GitHub",
                    "",
                    () => vm.CreatePullRequestAsync(branch, vm.Compare.Base is { } b ? BranchOnRemote(b) : "main"),
                    detail: branch
                );
            Do("Go to History", "", () => ShowPage(AppPage.History), "Ctrl+1");
            Do("Go to Changes", "", () => ShowPage(AppPage.Changes), "Ctrl+2");
            Do("Go to Health", "", () => ShowPage(AppPage.Health), "Ctrl+3");
            Do("Go to Compare", "", () => ShowPage(AppPage.Compare), "Ctrl+4");
            Do("Go to File History", "", () => ShowPage(AppPage.FileHistory), "Ctrl+5");
            Do("Go to Recovery", "", () => ShowPage(AppPage.Recovery), "Ctrl+6");
            Do("Go to Stash", "", () => ShowPage(AppPage.Stashes), "Ctrl+7");

            if (vm.FetchCommand.CanExecute(null))
                Command("Fetch", "", () => vm.FetchCommand.ExecuteAsync(null));
            if (vm.PullCommand.CanExecute(null))
                Command("Pull", "", () => vm.PullCommand.ExecuteAsync(null));
            if (vm.PushCommand.CanExecute(null))
                Command("Push", "", () => vm.PushCommand.ExecuteAsync(null));

            Do("Switch branch…", "", () => BranchFlyout.ShowAt(BranchButton), "Ctrl+B");
            Command("New branch…", "", () => vm.NewBranchAsync());
            if (Changes.HasStaged || Changes.HasUnstaged)
                Command("Stash all changes…", "", () => vm.StashChangesAsync(null));
            if (vm.CurrentBranch is not null && vm.LoadedCommits.FirstOrDefault(r => r.IsHead) is { } head)
                Command("Undo last commit", "", vm.UndoLastCommitAsync, detail: head.Subject);

            if (vm.SelectedCommit is { } commit)
            {
                var on = $"{commit.ShortHash} {commit.Subject}";
                Do("Copy commit hash", "", () => Copy(commit.Commit.Hash), detail: on);
                Command(
                    "New branch from selected commit…",
                    "",
                    () => vm.NewBranchAsync(commit.Commit.Hash, commit.ShortHash),
                    detail: on
                );
                Command("Tag selected commit…", "", () => vm.CreateTagAsync(commit), detail: on);
                if (vm.CurrentBranch is not null)
                {
                    if (!commit.IsHead)
                        Command("Cherry-pick selected commit", "", () => vm.CherryPickAsync(commit), detail: on);
                    Command("Revert selected commit…", "", () => vm.RevertAsync(commit), detail: on);
                    Command("Edit commits from selected…", "", () => vm.EditCommitsFromAsync(commit), detail: on);
                    if (!commit.IsHead)
                        Command(
                            "Reset current branch to selected commit…",
                            "",
                            () => vm.ResetToAsync(commit),
                            detail: on
                        );
                }
            }

            Do("Show in Explorer", "", () => vm.OpenExternal(ExternalTool.Explorer));
            Do("Open terminal here", "", () => vm.OpenExternal(ExternalTool.Terminal));
            Do("Open in editor", "", () => vm.OpenExternal(ExternalTool.Editor));
        }

        Do(
            vm.WrapDiffLines ? "Don't wrap long diff lines" : "Wrap long diff lines",
            "",
            () =>
            {
                vm.WrapDiffLines = !vm.WrapDiffLines;
                UpdateWrapButtons();
            }
        );
        Do("Switch repository…", "", OpenSwitcher, "Ctrl+P");
        Do("Settings", "", () => OpenSettings(SettingsSection.Repositories));
        Do("Appearance settings", "", () => OpenSettings(SettingsSection.Appearance));

        if (!open)
            return items;

        // Branches: switch, plus merge / rebase / compare against the current one.
        var current = vm.CurrentBranch;
        foreach (var branch in vm.LocalBranches)
        {
            var name = branch.Name;
            if (name == current)
                continue;
            items.Add(
                new PaletteItemViewModel(
                    PaletteGroups.Branch,
                    $"Switch to {name}",
                    "",
                    "",
                    () => vm.SwitchToAsync(name)
                )
            );
            items.Add(
                new PaletteItemViewModel(
                    PaletteGroups.Branch,
                    $"Compare {name}",
                    current is null ? "" : $"with {current}",
                    "",
                    async () =>
                    {
                        ShowPage(AppPage.Compare);
                        await vm.CompareBranchAsync(name);
                    }
                )
            );
            if (current is null)
                continue;
            items.Add(
                new PaletteItemViewModel(
                    PaletteGroups.Branch,
                    $"Merge {name} into {current}",
                    "",
                    "",
                    () => vm.MergeIntoCurrentAsync(name)
                )
            );
            items.Add(
                new PaletteItemViewModel(
                    PaletteGroups.Branch,
                    $"Rebase {current} onto {name}",
                    "",
                    "",
                    () => vm.RebaseCurrentOntoAsync(name)
                )
            );
        }

        foreach (var tag in vm.Tags)
            items.Add(
                new PaletteItemViewModel(
                    PaletteGroups.Tag,
                    tag.Name,
                    tag.AgeText,
                    "",
                    () =>
                    {
                        ShowPage(AppPage.History);
                        vm.RevealTag(tag);
                        return Task.CompletedTask;
                    }
                )
            );

        // Loaded commits, matched on subject and hash.
        foreach (var row in vm.LoadedCommits)
            items.Add(
                new PaletteItemViewModel(
                    PaletteGroups.Commit,
                    row.Subject,
                    $"{row.ShortHash} · {row.Author}",
                    "",
                    () =>
                    {
                        ShowPage(AppPage.History);
                        vm.SelectCommit(row.Commit.Hash);
                        return Task.CompletedTask;
                    },
                    searchText: $"{row.Subject} {row.Commit.Hash}"
                )
            );

        return items;
    }
}
