using CentralGit.App.Theming;
using CentralGit.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CentralGit.App;

/// <summary>The Health page: refresh, local/remote tabs, selection and cleanup.</summary>
public sealed partial class MainWindow
{
    private HealthViewModel Health => ViewModel.Health;

    private void InitializeHealth()
    {
        Health.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HealthViewModel.ShowRemote))
                UpdateHealthTabs();
        };
        UpdateHealthTabs();
    }

    private void UpdateHealthTabs()
    {
        foreach (
            var (tab, active) in new[] { (HealthLocalTab, !Health.ShowRemote), (HealthRemoteTab, Health.ShowRemote) }
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

    private async void OnHealthRefresh(object sender, RoutedEventArgs e) => await ViewModel.LoadHealthAsync();

    private void OnHealthShowLocal(object sender, RoutedEventArgs e) => Health.ShowRemote = false;

    private void OnHealthShowRemote(object sender, RoutedEventArgs e) => Health.ShowRemote = true;

    private void OnHealthSelectMerged(object sender, RoutedEventArgs e) => Health.SelectMerged();

    private void OnHealthSelectNone(object sender, RoutedEventArgs e) => Health.SelectAll(false);

    private async void OnHealthDelete(object sender, RoutedEventArgs e) =>
        await ViewModel.DeleteSelectedBranchesAsync();
}
