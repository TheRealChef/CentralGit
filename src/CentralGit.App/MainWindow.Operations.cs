using CentralGit.App.Theming;
using CentralGit.App.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace CentralGit.App;

/// <summary>Operations Center wiring: force-push confirmation, the recent-operations panel, background fetch.</summary>
public sealed partial class MainWindow
{
    private DispatcherQueueTimer? _backgroundSync;

    private void InitializeOperations()
    {
        ViewModel.ConfirmForcePush = ConfirmForcePushAsync;
        ViewModel.AccountsRequested += () =>
        {
            OperationsFlyout.Hide();
            OpenSettings(SettingsSection.Accounts);
        };

        // A failure is easier to understand with the details in view: open the panel for it.
        ViewModel.Operations.PropertyChanged += (_, e) =>
        {
            if (
                e.PropertyName == nameof(OperationsViewModel.IsBusy)
                && ViewModel.Operations is { IsIdle: true, Latest: { IsFailed: true, IsBackground: false } }
                && !OperationsFlyout.IsOpen
            )
                OperationsFlyout.ShowAt(OperationsButton);
        };

        // Ticks every minute; the view model decides whether a fetch is due.
        _backgroundSync = DispatcherQueue.CreateTimer();
        _backgroundSync.Interval = TimeSpan.FromMinutes(1);
        _backgroundSync.Tick += async (_, _) => await ViewModel.BackgroundTickAsync();
        _backgroundSync.Start();
        Closed += (_, _) => _backgroundSync.Stop();
    }

    private async Task<bool> ConfirmForcePushAsync(string target)
    {
        OperationsFlyout.Hide();
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            RequestedTheme = ThemeService.ContentTheme,
            Title = "Force push?",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text =
                    $"This replaces {target} with your branch. Commits on the remote that you don't have will be removed from it.\n\n"
                    + "CentralGit uses --force-with-lease: if someone pushed since your last fetch, git refuses instead of overwriting their work.",
            },
            PrimaryButtonText = "Force push",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void OnOperationAction(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: OperationAction action } button || FindOperation(button) is not { } operation)
            return;
        if (action.Kind != "accounts")
            OperationsFlyout.Hide();
        await ViewModel.RunActionAsync(operation, action);
    }

    private void OnCancelOperation(object sender, RoutedEventArgs e) => ViewModel.Operations.Cancel();

    private void OnCopyOperationOutput(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: OperationViewModel op })
            return;
        var package = new DataPackage();
        package.SetText($"> {op.CommandLine}\n{op.Output}");
        Clipboard.SetContent(package);
    }

    /// <summary>The operation a template element belongs to (the nearest ancestor whose data is one).</summary>
    private static OperationViewModel? FindOperation(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: OperationViewModel op })
                return op;
        }
        return null;
    }
}
