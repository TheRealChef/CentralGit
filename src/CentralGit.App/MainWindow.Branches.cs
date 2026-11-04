using CentralGit.App.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CentralGit.App;

/// <summary>Branch picker (Ctrl+B), sidebar branch actions and the dialogs they need.</summary>
public sealed partial class MainWindow
{
    public BranchPickerViewModel BranchPicker { get; }

    private void InitializeBranches()
    {
        ViewModel.ConfirmDestructive = ConfirmAsync;
        ViewModel.PromptText = PromptTextAsync;
        Root.KeyboardAccelerators.Add(Accelerator(Windows.System.VirtualKey.B, (_, a) =>
        {
            a.Handled = true;
            if (ViewModel.RepositoryPath is not null)
                BranchFlyout.ShowAt(BranchButton);
        }));
    }

    // ─── Picker ───────────────────────────────────────────────────────────

    private async void OnBranchFlyoutOpening(object? sender, object e) => await BranchPicker.OpenAsync();

    private void OnBranchFlyoutOpened(object? sender, object e) =>
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => BranchQuery.Focus(FocusState.Programmatic));

    private async void OnBranchQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Down:
                BranchPicker.Move(1);
                ScrollBranchSelection();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Up:
                BranchPicker.Move(-1);
                ScrollBranchSelection();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Enter:
                e.Handled = true;
                await ActivateBranchAsync(null);
                break;
        }
    }

    private async void OnBranchResultClick(object sender, ItemClickEventArgs e) =>
        await ActivateBranchAsync(e.ClickedItem as BranchPickItemViewModel);

    private async Task ActivateBranchAsync(BranchPickItemViewModel? item)
    {
        var work = BranchPicker.ActivateAsync(item);
        // Close right away for real work so the Operations Center can show progress; stay open on a bad name.
        if (item?.Kind != BranchPickKind.Create && (item ?? BranchPicker.Selected) is not null)
            BranchFlyout.Hide();
        if (await work)
            BranchFlyout.Hide();
    }

    private void ScrollBranchSelection()
    {
        if (BranchPicker.Selected is { } selected)
            BranchResults.ScrollIntoView(selected);
    }

    // ─── Sidebar ──────────────────────────────────────────────────────────

    /// <summary>Click: a folder opens or closes; a branch shows its latest commit in History.</summary>
    private void OnBranchRowClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not BranchItemViewModel row)
            return;
        if (row.IsFolder)
            ViewModel.ToggleFolder(row.FullName);
        else
            ViewModel.RevealBranch(row);
    }

    private async void OnBranchDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BranchItemViewModel { IsBranch: true, IsHead: false } branch })
        {
            e.Handled = true;
            await ViewModel.SwitchToAsync(branch.FullName);
        }
    }

    private void OnBranchReveal(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name } && ViewModel.Branches.FirstOrDefault(b => b.IsBranch && b.FullName == name) is { } row)
            ViewModel.RevealBranch(row);
    }

    private async void OnBranchSwitch(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name })
            await ViewModel.SwitchToAsync(name);
    }

    private async void OnBranchNewFrom(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name })
            await ViewModel.NewBranchAsync(name, name);
    }

    private async void OnBranchRename(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name })
            await ViewModel.RenameBranchAsync(name);
    }

    private async void OnBranchDelete(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name })
            await ViewModel.DeleteBranchAsync(name, force: false);
    }

    // ─── Dialogs ──────────────────────────────────────────────────────────

    private async Task<bool> ConfirmAsync(string title, string message, string button)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = button,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>A one-line text dialog; <paramref name="validate"/> returns an error sentence, or null when the text is fine.</summary>
    private async Task<string?> PromptTextAsync(string title, string message, string initial, string button, Func<string, Task<string?>> validate)
    {
        var box = new TextBox { Text = initial, FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["MonoFont"] };
        box.SelectAll();
        var error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["RedTextBrush"],
            Visibility = Visibility.Collapsed,
        };
        var panel = new StackPanel { Spacing = 10, MinWidth = 380 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextSecondaryBrush"] });
        panel.Children.Add(box);
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = title,
            Content = panel,
            PrimaryButtonText = button,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                if (await validate(box.Text.Trim()) is { } problem)
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
        box.TextChanged += (_, _) => error.Visibility = Visibility.Collapsed;
        dialog.Opened += (_, _) => box.Focus(FocusState.Programmatic);

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Text.Trim() : null;
    }
}
