using CentralGit.App.Theming;
using CentralGit.App.ViewModels;
using CentralGit.Application.Ports;
using CentralGit.Domain.Workflow;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace CentralGit.App;

/// <summary>
/// Commit actions from History, merge/rebase from the sidebar, the conflict banner, line staging,
/// tags, the Stash page and blame, plus the dialogs they need.
/// </summary>
public sealed partial class MainWindow
{
    private void InitializeWorkflow()
    {
        ViewModel.Choose = ChooseAsync;
        ViewModel.PromptTag = PromptTagAsync;
        ViewModel.PlanRebase = PlanRebaseAsync;
        Root.KeyboardAccelerators.Add(
            Accelerator(
                Windows.System.VirtualKey.Number7,
                (_, a) =>
                {
                    ShowPage(AppPage.Stashes);
                    a.Handled = true;
                }
            )
        );
        ViewModel.FileHistory.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FileHistoryViewModel.ShowBlame) or "")
                UpdateFileHistoryTabs();
        };
        UpdateFileHistoryTabs();
        UpdateWrapButtons();
    }

    // ─── History: one menu for every commit ───────────────────────────────

    private CommitRowViewModel? MenuCommit => ViewModel.SelectedCommit;

    /// <summary>Right-click acts on the row under the pointer: select it before the menu opens.</summary>
    private void OnHistoryContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        for (
            var element = e.OriginalSource as DependencyObject;
            element is not null;
            element = VisualTreeHelper.GetParent(element)
        )
        {
            if (element is FrameworkElement { DataContext: CommitRowViewModel row })
            {
                ViewModel.SelectedCommit = row;
                return;
            }
            if (element == HistoryList)
                return;
        }
    }

    private void OnCommitMenuOpening(object? sender, object e)
    {
        var row = MenuCommit;
        var onBranch = ViewModel.CurrentBranch is not null;
        CommitCherryPickItem.IsEnabled = row is { IsHead: false } && onBranch;
        CommitRevertItem.IsEnabled = row is not null && onBranch;
        CommitResetItem.IsEnabled = row is { IsHead: false } && onBranch;
        CommitEditItem.IsEnabled = row is not null && onBranch;
        CommitUndoItem.Visibility = row is { IsHead: true } && onBranch ? Visibility.Visible : Visibility.Collapsed;
        CommitRevertItem.Text = row?.IsMerge == true ? "Revert merge (against first parent)…" : "Revert…";
    }

    private static void Copy(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void OnCommitCopyHash(object sender, RoutedEventArgs e)
    {
        if (MenuCommit is { } row)
            Copy(row.Commit.Hash);
    }

    private void OnCommitCopyMessage(object sender, RoutedEventArgs e)
    {
        if (MenuCommit is not { } row)
            return;
        var details = ViewModel.Inspector.Details;
        Copy(details?.Hash == row.Commit.Hash && details.HasBody ? $"{row.Subject}\n\n{details.Body}" : row.Subject);
    }

    private async void OnCommitNewBranch(object sender, RoutedEventArgs e)
    {
        if (MenuCommit is { } row)
            await ViewModel.NewBranchAsync(row.Commit.Hash, row.ShortHash);
    }

    private async void OnCommitTag(object sender, RoutedEventArgs e)
    {
        if (MenuCommit is { } row)
            await ViewModel.CreateTagAsync(row);
    }

    private async void OnCommitCherryPick(object sender, RoutedEventArgs e)
    {
        if (MenuCommit is { } row)
            await ViewModel.CherryPickAsync(row);
    }

    private async void OnCommitRevert(object sender, RoutedEventArgs e)
    {
        if (MenuCommit is { } row)
            await ViewModel.RevertAsync(row);
    }

    private async void OnCommitReset(object sender, RoutedEventArgs e)
    {
        if (MenuCommit is { } row)
            await ViewModel.ResetToAsync(row);
    }

    private async void OnUndoLastCommit(object sender, RoutedEventArgs e) => await ViewModel.UndoLastCommitAsync();

    // ─── Sidebar: merge, rebase, tags ─────────────────────────────────────

    private async void OnBranchMerge(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name })
            await ViewModel.MergeIntoCurrentAsync(name);
    }

    private async void OnBranchRebase(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name })
            await ViewModel.RebaseCurrentOntoAsync(name);
    }

    private void OnToggleTags(object sender, RoutedEventArgs e)
    {
        var open = TagList.Visibility != Visibility.Visible;
        TagList.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        TagsChevron.Glyph = open ? "" : "";
    }

    private TagItemViewModel? TagNamed(object sender) =>
        sender is FrameworkElement { Tag: string name } ? ViewModel.Tags.FirstOrDefault(t => t.Name == name) : null;

    private void OnTagClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TagItemViewModel tag)
            ViewModel.RevealTag(tag);
    }

    private void OnTagReveal(object sender, RoutedEventArgs e)
    {
        if (TagNamed(sender) is { } tag)
            ViewModel.RevealTag(tag);
    }

    private async void OnTagPush(object sender, RoutedEventArgs e)
    {
        if (TagNamed(sender) is { } tag)
            await ViewModel.PushTagAsync(tag);
    }

    private async void OnTagDelete(object sender, RoutedEventArgs e)
    {
        if (TagNamed(sender) is { } tag)
            await ViewModel.DeleteTagAsync(tag);
    }

    // ─── Changes: stopped operations, conflicts, single lines ─────────────

    private async void OnContinueInProgress(object sender, RoutedEventArgs e) =>
        await ViewModel.ContinueInProgressAsync();

    private async void OnAbortInProgress(object sender, RoutedEventArgs e) => await ViewModel.AbortInProgressAsync();

    private async void OnTakeOurs(object sender, RoutedEventArgs e)
    {
        if (Changes.SelectedPath is { } path)
            await ViewModel.ResolveConflictAsync(path, ConflictSide.Ours);
    }

    private async void OnTakeTheirs(object sender, RoutedEventArgs e)
    {
        if (Changes.SelectedPath is { } path)
            await ViewModel.ResolveConflictAsync(path, ConflictSide.Theirs);
    }

    private void OnChangesDiffSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Changes.SelectDiffRows(ChangesDiffList.SelectedItems.OfType<Diff.DiffRowViewModel>());

    private async void OnApplyLines(object sender, RoutedEventArgs e) => await Changes.ApplySelectedLinesAsync();

    private async void OnDiscardLines(object sender, RoutedEventArgs e) => await Changes.DiscardSelectedLinesAsync();

    private async void OnStashAll(object sender, RoutedEventArgs e) => await ViewModel.StashChangesAsync(null);

    // ─── Stash page ───────────────────────────────────────────────────────

    private void OnShowStashes(object sender, RoutedEventArgs e) => ShowPage(AppPage.Stashes);

    private async void OnStashPop(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Stashes.Selected is { } row)
            await ViewModel.ApplyStashAsync(row, pop: true);
    }

    private async void OnStashApply(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Stashes.Selected is { } row)
            await ViewModel.ApplyStashAsync(row, pop: false);
    }

    private async void OnStashDrop(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Stashes.Selected is { } row)
            await ViewModel.DropStashAsync(row);
    }

    // ─── File History: change or blame ────────────────────────────────────

    private void OnFileHistoryShowChange(object sender, RoutedEventArgs e) => ViewModel.FileHistory.ShowBlame = false;

    private void OnFileHistoryShowBlame(object sender, RoutedEventArgs e) => ViewModel.FileHistory.ShowBlame = true;

    private void UpdateFileHistoryTabs()
    {
        var blame = ViewModel.FileHistory.ShowBlame;
        foreach (var (tab, active) in new[] { (FileHistoryChangeTab, !blame), (FileHistoryBlameTab, blame) })
        {
            tab.Background = active
                ? ThemeService.ContentBrush("RaisedBrush")
                : new SolidColorBrush(Colors.Transparent);
            tab.Foreground = active
                ? ThemeService.ContentBrush("TextPrimaryBrush")
                : ThemeService.ContentBrush("TextSecondaryBrush");
        }
    }

    private void OnBlameCommitDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BlameRowViewModel { CanShowCommit: true } row })
            return;
        e.Handled = true;
        ViewModel.Page = AppPage.History;
        if (!ViewModel.SelectCommit(row.Hash))
            ViewModel.ReportNotLoaded(row.Hash[..7]);
    }

    // ─── Dialogs ──────────────────────────────────────────────────────────

    /// <summary>Pick one of a few choices (radio buttons); -1 when cancelled.</summary>
    private async Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> choices)
    {
        var options = new RadioButtons { SelectedIndex = 0 };
        foreach (var choice in choices)
            options.Items.Add(choice);
        var panel = new StackPanel { Spacing = 12, MinWidth = 400 };
        panel.Children.Add(
            new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeService.ContentBrush("TextSecondaryBrush"),
            }
        );
        panel.Children.Add(options);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            RequestedTheme = ThemeService.ContentTheme,
            Title = title,
            Content = panel,
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? options.SelectedIndex : -1;
    }

    /// <summary>Tag name (validated) and an optional message; null when cancelled.</summary>
    private async Task<(string Name, string Message)?> PromptTagAsync(
        string title,
        string message,
        Func<string, Task<string?>> validate
    )
    {
        var mono = (FontFamily)Microsoft.UI.Xaml.Application.Current.Resources["MonoFont"];
        var name = new TextBox { PlaceholderText = "v1.2.0", FontFamily = mono };
        var note = new TextBox
        {
            PlaceholderText = "Message (optional, makes it an annotated tag)",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 72,
        };
        var error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = ThemeService.ContentBrush("RedTextBrush"),
            Visibility = Visibility.Collapsed,
        };
        var panel = new StackPanel { Spacing = 10, MinWidth = 400 };
        panel.Children.Add(
            new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeService.ContentBrush("TextSecondaryBrush"),
            }
        );
        panel.Children.Add(name);
        panel.Children.Add(note);
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            RequestedTheme = ThemeService.ContentTheme,
            Title = title,
            Content = panel,
            PrimaryButtonText = "Create tag",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                if (await validate(name.Text.Trim()) is { } problem)
                {
                    error.Text = problem;
                    error.Visibility = Visibility.Visible;
                    args.Cancel = true;
                }
            }
            finally
            {
                deferral.Complete();
            }
        };
        name.TextChanged += (_, _) => error.Visibility = Visibility.Collapsed;
        dialog.Opened += (_, _) => name.Focus(FocusState.Programmatic);
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? (name.Text.Trim(), note.Text.Trim()) : null;
    }
}

public sealed partial class MainWindow
{
    private void OnOpenExplorer(object sender, RoutedEventArgs e) => ViewModel.OpenExternal(ExternalTool.Explorer);

    private void OnOpenTerminal(object sender, RoutedEventArgs e) => ViewModel.OpenExternal(ExternalTool.Terminal);

    private void OnOpenEditor(object sender, RoutedEventArgs e) => ViewModel.OpenExternal(ExternalTool.Editor);

    private void OnOpenSelectedInEditor(object sender, RoutedEventArgs e)
    {
        if (Changes.SelectedPath is { } path)
            ViewModel.OpenExternal(ExternalTool.Editor, path);
    }
}

public sealed partial class MainWindow
{
    private void OnToggleWrap(object sender, RoutedEventArgs e)
    {
        ViewModel.WrapDiffLines = !ViewModel.WrapDiffLines;
        UpdateWrapButtons();
    }

    private void UpdateWrapButtons()
    {
        var on = ThemeService.ContentBrush("ElevatedBrush");
        var off = new SolidColorBrush(Colors.Transparent);
        WrapButton.Background = ViewModel.WrapDiffLines ? on : off;
        ChangesWrapButton.Background = ViewModel.WrapDiffLines ? on : off;
    }
}

public sealed partial class MainWindow
{
    private void OnConflictChoose(object sender, RoutedEventArgs e)
    {
        if (
            sender is FrameworkElement { Tag: string tag, DataContext: ConflictBlockViewModel block }
            && Enum.TryParse<ConflictChoice>(tag, out var choice)
        )
            block.Choice = choice;
    }

    private void OnConflictAllOurs(object sender, RoutedEventArgs e) =>
        ViewModel.ConflictEditor.ChooseAll(ConflictChoice.Ours);

    private void OnConflictAllTheirs(object sender, RoutedEventArgs e) =>
        ViewModel.ConflictEditor.ChooseAll(ConflictChoice.Theirs);

    private async void OnConflictSave(object sender, RoutedEventArgs e) =>
        await ViewModel.SaveConflictResolutionAsync();
}

/// <summary>The interactive-rebase planner dialog.</summary>
public sealed partial class MainWindow
{
    private async void OnCommitEditFromHere(object sender, RoutedEventArgs e)
    {
        if (MenuCommit is { } row)
            await ViewModel.EditCommitsFromAsync(row);
    }

    private async Task<bool> PlanRebaseAsync(RebasePlannerViewModel planner)
    {
        var list = new ListView
        {
            ItemsSource = planner.Steps,
            ItemTemplate = (DataTemplate)Root.Resources["RebaseStepTemplate"],
            SelectionMode = ListViewSelectionMode.None,
            CanReorderItems = true,
            CanDragItems = true,
            AllowDrop = true,
            MaxHeight = 420,
        };
        var problem = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = ThemeService.ContentBrush("OrangeTextBrush"),
        };
        var panel = new StackPanel { Spacing = 10, Width = 640 };
        panel.Children.Add(
            new TextBlock
            {
                Text = planner.Intro,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeService.ContentBrush("TextSecondaryBrush"),
            }
        );
        panel.Children.Add(list);
        panel.Children.Add(problem);

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            RequestedTheme = ThemeService.ContentTheme,
            Title = $"Edit commits on {planner.Branch}",
            Content = panel,
            PrimaryButtonText = "Rewrite commits",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        // ContentDialog caps its width; let the planner use the room it needs.
        dialog.Resources["ContentDialogMaxWidth"] = 720.0;

        void Sync()
        {
            problem.Text = planner.Problem;
            problem.Visibility = planner.HasProblem ? Visibility.Visible : Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = planner.CanRun;
        }
        planner.PropertyChanged += (_, _) => Sync();
        Sync();
        return await dialog.ShowAsync() == ContentDialogResult.Primary && planner.CanRun;
    }
}

/// <summary>GitHub: open the branch's pull request, or start one from Compare.</summary>
public sealed partial class MainWindow
{
    private void OnOpenPullRequest(object sender, RoutedEventArgs e) => ViewModel.OpenPullRequest();

    private async void OnCreatePullRequest(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Compare is { Head: { } head, Base: { } @base })
            await ViewModel.CreatePullRequestAsync(BranchOnRemote(head), BranchOnRemote(@base));
    }

    /// <summary>"origin/main" → "main": GitHub's compare page wants branch names, not remote-tracking refs.</summary>
    private static string BranchOnRemote(string reference) =>
        reference.StartsWith("origin/", StringComparison.Ordinal) ? reference["origin/".Length..] : reference;
}

/// <summary>Bisect: marks from the History menu and the banner.</summary>
public sealed partial class MainWindow
{
    private async void OnBisectMarkCommit(object sender, RoutedEventArgs e)
    {
        if (
            MenuCommit is { } row
            && sender is FrameworkElement { Tag: string tag }
            && Enum.TryParse<BisectMark>(tag, out var mark)
        )
            await ViewModel.MarkBisectAsync(row, mark);
    }

    private async void OnBisectMarkTested(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<BisectMark>(tag, out var mark))
            await ViewModel.MarkBisectAsync(null, mark);
    }

    private void OnBisectShowCulprit(object sender, RoutedEventArgs e) => ViewModel.ShowBisectCulprit();

    private async void OnBisectEnd(object sender, RoutedEventArgs e) => await ViewModel.EndBisectAsync();
}
