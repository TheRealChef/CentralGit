using CentralGit.App.Theming;
using CentralGit.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace CentralGit.App;

/// <summary>The Compare page: tabs, swapping the refs, jumping to a commit.</summary>
public sealed partial class MainWindow
{
    private CompareViewModel Compare => ViewModel.Compare;

    private void InitializeCompare()
    {
        Compare.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CompareViewModel.ShowFiles) or "")
                UpdateCompareTabs();
        };
        UpdateCompareTabs();
    }

    private void UpdateCompareTabs()
    {
        foreach (
            var (tab, active) in new[] { (CompareFilesTab, Compare.ShowFiles), (CompareCommitsTab, !Compare.ShowFiles) }
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

    private void OnShowCompare(object sender, RoutedEventArgs e) => ShowPage(AppPage.Compare);

    private async void OnBranchCompare(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name })
            await ViewModel.CompareBranchAsync(name);
    }

    private void OnCompareSwap(object sender, RoutedEventArgs e) => Compare.Swap();

    private void OnCompareShowFiles(object sender, RoutedEventArgs e) => Compare.ShowFiles = true;

    private void OnCompareShowCommits(object sender, RoutedEventArgs e) => Compare.ShowFiles = false;

    private void OnCompareCommitClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string hash })
            return;
        ViewModel.Page = AppPage.History;
        if (ViewModel.SelectCommit(hash))
            DispatcherQueue.TryEnqueue(FocusSelectedCommit);
        else
            ViewModel.ReportNotLoaded(hash[..Math.Min(9, hash.Length)]);
    }
}
