using System.ComponentModel;
using CentralGit.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CentralGit.App;

/// <summary>The Changes page: navigation, staging, hunk actions and the commit composer.</summary>
public sealed partial class MainWindow
{
    private double _changesDiffOffset;
    private ScrollViewer? _changesDiffScroller;

    private ChangesViewModel Changes => ViewModel.Changes;

    private void InitializeChanges()
    {
        Root.KeyboardAccelerators.Add(Accelerator(Windows.System.VirtualKey.Number1, (_, a) => { ShowPage(AppPage.History); a.Handled = true; }));
        Root.KeyboardAccelerators.Add(Accelerator(Windows.System.VirtualKey.Number2, (_, a) => { ShowPage(AppPage.Changes); a.Handled = true; }));
        Root.KeyboardAccelerators.Add(Accelerator(Windows.System.VirtualKey.Number3, (_, a) => { ShowPage(AppPage.Health); a.Handled = true; }));
        var refresh = new KeyboardAccelerator { Key = Windows.System.VirtualKey.F5 };
        refresh.Invoked += async (_, a) =>
        {
            if (ViewModel.ShowChanges) { a.Handled = true; await Changes.RefreshAsync(); }
            else if (ViewModel.ShowHealth) { a.Handled = true; await ViewModel.LoadHealthAsync(); }
        };
        Root.KeyboardAccelerators.Add(refresh);

        Changes.Confirm = (title, message) => ConfirmAsync(title, message, "Discard");
        ViewModel.PropertyChanged += OnShellPageChanged;
        Changes.PropertyChanged += OnChangesPropertyChanged;
        UpdateNavigation();
        UpdateChangesDiffToggle();

        // Coming back from the editor is when files have changed: refresh then.
        Activated += async (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated && ViewModel.RepositoryPath is not null && _initialized)
                await Changes.RefreshAsync();
        };

        ChangesDiffList.Loaded += (_, _) =>
        {
            _changesDiffScroller = FindDescendant<ScrollViewer>(ChangesDiffList);
            if (_changesDiffScroller is not null)
                _changesDiffScroller.ViewChanged += (_, _) => _changesDiffOffset = _changesDiffScroller.VerticalOffset;
        };
    }

    private void ShowPage(AppPage page)
    {
        ViewModel.Page = page;
        if (page == AppPage.Changes)
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                // Straight to typing when there's something to commit; otherwise to the file list.
                if (Changes.HasStaged && Changes.Summary.Length == 0)
                    SummaryBox.Focus(FocusState.Keyboard);
                else if (Changes.HasUnstaged)
                    UnstagedList.Focus(FocusState.Keyboard);
            });
        else if (page == AppPage.History)
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, FocusSelectedCommit);
    }

    private void OnShowHistory(object sender, RoutedEventArgs e) => ShowPage(AppPage.History);

    private void OnShowChanges(object sender, RoutedEventArgs e) => ShowPage(AppPage.Changes);

    private void OnShowHealth(object sender, RoutedEventArgs e) => ShowPage(AppPage.Health);

    private void OnShellPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.Page))
            UpdateNavigation();
    }

    private void UpdateNavigation()
    {
        var page = ViewModel.Page;
        Style(NavHistory, NavHistoryIcon, NavHistoryText, active: page == AppPage.History);
        Style(NavChanges, NavChangesIcon, NavChangesText, active: page == AppPage.Changes);
        Style(NavHealth, NavHealthIcon, NavHealthText, active: page == AppPage.Health);

        static void Style(Button button, FontIcon icon, TextBlock text, bool active)
        {
            var resources = Application.Current.Resources;
            button.Background = active ? (Brush)resources["RaisedBrush"] : new SolidColorBrush(Colors.Transparent);
            icon.Foreground = active ? (Brush)resources["VioletBrush"] : (Brush)resources["TextSecondaryBrush"];
            text.Foreground = active ? (Brush)resources["TextPrimaryBrush"] : (Brush)resources["TextSecondaryBrush"];
            text.FontWeight = active ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
    }

    private void OnChangesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChangesViewModel.IsSplit):
                UpdateChangesDiffToggle();
                break;
            case nameof(ChangesViewModel.DiffRows) when Changes.KeepScroll && _changesDiffScroller is { } scroller:
                // Staging a hunk re-renders the file; stay where the reader was.
                var offset = _changesDiffOffset;
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => scroller.ChangeView(null, offset, null, disableAnimation: true));
                break;
        }
    }

    private void UpdateChangesDiffToggle()
    {
        var on = (Brush)Application.Current.Resources["ElevatedBrush"];
        var off = new SolidColorBrush(Colors.Transparent);
        ChangesSplitButton.Background = Changes.IsSplit ? on : off;
        ChangesUnifiedButton.Background = Changes.IsSplit ? off : on;
    }

    private void OnChangesSplit(object sender, RoutedEventArgs e) => Changes.IsSplit = true;

    private void OnChangesUnified(object sender, RoutedEventArgs e) => Changes.IsSplit = false;

    private async void OnRefreshChanges(object sender, RoutedEventArgs e) => await Changes.RefreshAsync();

    private async void OnStageAll(object sender, RoutedEventArgs e) => await Changes.StageAllAsync();

    private async void OnUnstageAll(object sender, RoutedEventArgs e) => await Changes.UnstageAllAsync();

    private async void OnToggleWorkingFile(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WorkingFileViewModel file })
            await Changes.ToggleAsync(file);
    }

    private async void OnToggleSelectedFile(object sender, RoutedEventArgs e) => await Changes.ToggleSelectedAsync();

    private async void OnWorkingFileDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is ListView { SelectedItem: WorkingFileViewModel file })
            await Changes.ToggleAsync(file);
    }

    private async void OnWorkingFileKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Space && sender is ListView { SelectedItem: WorkingFileViewModel file })
        {
            e.Handled = true;
            await Changes.ToggleAsync(file);
            // Keep the keyboard in the list the file came from, so Space-Space-Space walks it.
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => ((ListView)sender).Focus(FocusState.Keyboard));
        }
        else if (e.Key == Windows.System.VirtualKey.Delete && sender is ListView { SelectedItem: WorkingFileViewModel { IsStaged: false } })
        {
            e.Handled = true;
            await Changes.DiscardSelectedAsync();
        }
    }

    private async void OnHunkAction(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int hunk })
            await Changes.ApplyHunkAsync(hunk);
    }

    private async void OnDiscardHunk(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int hunk })
            await Changes.DiscardHunkAsync(hunk);
    }

    private async void OnDiscardFile(object sender, RoutedEventArgs e) => await Changes.DiscardSelectedAsync();

    private async void OnCommit(object sender, RoutedEventArgs e) => await CommitAndRefocusAsync();

    private async void OnComposerKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (e.Key == Windows.System.VirtualKey.Enter && ctrl)
        {
            e.Handled = true;
            await CommitAndRefocusAsync();
        }
        else if (e.Key == Windows.System.VirtualKey.Enter && ReferenceEquals(sender, SummaryBox))
        {
            // Enter in the one-line summary moves on to the description, like a form.
            e.Handled = true;
            FocusManager.TryMoveFocus(FocusNavigationDirection.Next, new FindNextElementOptions { SearchRoot = Root.XamlRoot.Content });
        }
    }

    private async Task CommitAndRefocusAsync()
    {
        await Changes.CommitAsync();
        if (!Changes.HasError)
            SummaryBox.Focus(FocusState.Programmatic);
    }
}
