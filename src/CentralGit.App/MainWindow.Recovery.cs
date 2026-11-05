using CentralGit.App.Theming;
using CentralGit.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace CentralGit.App;

/// <summary>The Recovery page: lost/everything tabs, restoring a commit as a branch.</summary>
public sealed partial class MainWindow
{
    private RecoveryViewModel Recovery => ViewModel.Recovery;

    private void InitializeRecovery()
    {
        Recovery.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RecoveryViewModel.ShowLostOnly) or "")
                UpdateRecoveryTabs();
        };
        UpdateRecoveryTabs();
    }

    private void UpdateRecoveryTabs()
    {
        foreach (
            var (tab, active) in new[]
            {
                (RecoveryLostTab, Recovery.ShowLostOnly),
                (RecoveryAllTab, !Recovery.ShowLostOnly),
            }
        )
        {
            tab.Background = active
                ? ThemeService.ContentBrush("RaisedBrush")
                : new SolidColorBrush(Colors.Transparent);
            tab.Foreground = active
                ? ThemeService.ContentBrush("TextPrimaryBrush")
                : ThemeService.ContentBrush("TextSecondaryBrush");
        }
    }

    private void OnShowRecovery(object sender, RoutedEventArgs e) => ShowPage(AppPage.Recovery);

    private async void OnRecoveryRefresh(object sender, RoutedEventArgs e) => await ViewModel.LoadRecoveryAsync();

    private void OnRecoveryShowLost(object sender, RoutedEventArgs e) => Recovery.ShowLostOnly = true;

    private void OnRecoveryShowAll(object sender, RoutedEventArgs e) => Recovery.ShowLostOnly = false;

    private async void OnRecoveryRestore(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecoveryRowViewModel row })
            await ViewModel.RestoreAsBranchAsync(row);
    }

    private void OnRecoveryShowInHistory(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RecoveryRowViewModel row })
            return;
        if (ViewModel.ShowInHistory(row))
            DispatcherQueue.TryEnqueue(FocusSelectedCommit);
        else
            ViewModel.ReportNotLoaded(row.ShortHash);
    }
}
