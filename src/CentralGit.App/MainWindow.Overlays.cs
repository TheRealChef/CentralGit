using CentralGit.App.Theming;
using CentralGit.App.ViewModels;
using CentralGit.Application.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Storage.Pickers;

namespace CentralGit.App;

/// <summary>The repository switcher (Ctrl+P) and the Settings page.</summary>
public sealed partial class MainWindow
{
    private static KeyboardAccelerator Accelerator(
        Windows.System.VirtualKey key,
        TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> invoked
    )
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = Windows.System.VirtualKeyModifiers.Control };
        accelerator.Invoked += invoked;
        return accelerator;
    }

    // ─── Switcher ──────────────────────────────────────────────────────────

    private void OpenSwitcher()
    {
        Switcher.Reset();
        SwitcherOverlay.Visibility = Visibility.Visible;
        SwitcherBox.Focus(FocusState.Programmatic);
    }

    private void CloseSwitcher()
    {
        SwitcherOverlay.Visibility = Visibility.Collapsed;
        if (SettingsPage.Visibility != Visibility.Visible)
            FocusSelectedCommit();
    }

    private async Task OpenFromSwitcherAsync(SwitcherItemViewModel? item)
    {
        if (item is null)
            return;
        SwitcherOverlay.Visibility = Visibility.Collapsed;
        CloseSettings();
        await ViewModel.OpenRepositoryAsync(item.Path);
    }

    private void OnOpenSwitcher(object sender, RoutedEventArgs e) => OpenSwitcher();

    private async void OnSwitcherKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Down:
                Switcher.Move(+1);
                SwitcherList.ScrollIntoView(Switcher.Selected);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Up:
                Switcher.Move(-1);
                SwitcherList.ScrollIntoView(Switcher.Selected);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Enter:
                e.Handled = true;
                await OpenFromSwitcherAsync(Switcher.Selected);
                break;
        }
    }

    private async void OnSwitcherItemClick(object sender, ItemClickEventArgs e) =>
        await OpenFromSwitcherAsync(e.ClickedItem as SwitcherItemViewModel);

    private void OnSwitcherBackdropTapped(object sender, TappedRoutedEventArgs e) => CloseSwitcher();

    private void OnSwitcherCardTapped(object sender, TappedRoutedEventArgs e) => e.Handled = true; // clicks inside stay inside

    private void OnSwitcherManage(object sender, RoutedEventArgs e)
    {
        SwitcherOverlay.Visibility = Visibility.Collapsed;
        OpenSettings(SettingsSection.Repositories);
    }

    // ─── Settings ──────────────────────────────────────────────────────────

    private void OpenSettings(SettingsSection section)
    {
        SwitcherOverlay.Visibility = Visibility.Collapsed;
        Settings.Open(section);
        SettingsPage.Visibility = Visibility.Visible;
        SettingsScroller.ChangeView(null, 0, null, disableAnimation: true);
        UpdateSettingsNav();
    }

    private void CloseSettings()
    {
        if (SettingsPage.Visibility != Visibility.Visible)
            return;
        Settings.CommitSkipNames();
        SettingsPage.Visibility = Visibility.Collapsed;
        FocusSelectedCommit();
    }

    private void UpdateSettingsNav()
    {
        var on = ThemeService.ContentBrush("RaisedBrush");
        var off = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        SettingsRepositoriesNav.Background = Settings.IsRepositories ? on : off;
        SettingsAccountsNav.Background = Settings.IsAccounts ? on : off;
        SettingsAppearanceNav.Background = Settings.IsAppearance ? on : off;
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e) => OpenSettings(SettingsSection.Repositories);

    private void OnCloseSettings(object sender, RoutedEventArgs e) => CloseSettings();

    private void OnSettingsRepositories(object sender, RoutedEventArgs e)
    {
        Settings.Section = SettingsSection.Repositories;
        UpdateSettingsNav();
    }

    private void OnSettingsAccounts(object sender, RoutedEventArgs e)
    {
        Settings.Section = SettingsSection.Accounts;
        UpdateSettingsNav();
    }

    private void OnSettingsAppearance(object sender, RoutedEventArgs e)
    {
        Settings.Section = SettingsSection.Appearance;
        UpdateSettingsNav();
    }

    private void OnThemeChoice(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<AppTheme>(tag, out var theme))
            Settings.SelectTheme(theme);
    }

    private async void OnAddScanFolder(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        // Unpackaged apps must tell the picker which window owns it.
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (await picker.PickSingleFolderAsync() is { } folder)
            await Settings.AddScanFolderAsync(folder.Path);
    }

    private async void OnRemoveScanFolder(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
            await Settings.RemoveScanFolderAsync(path);
    }

    private async void OnRescan(object sender, RoutedEventArgs e) => await Settings.RescanAsync();

    private void OnDepthDown(object sender, RoutedEventArgs e) => Settings.ChangeDepth(-1);

    private void OnDepthUp(object sender, RoutedEventArgs e) => Settings.ChangeDepth(+1);

    private void OnSkipNamesCommitted(object sender, RoutedEventArgs e) => Settings.CommitSkipNames();

    private void OnSkipNamesKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            Settings.CommitSkipNames();
            e.Handled = true;
        }
    }

    private async void OnSignIn(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url })
            await Settings.SignInAsync(url);
    }

    private async void OnSignInOther(object sender, RoutedEventArgs e)
    {
        if (Settings.OtherHost.Trim().Length > 0)
            await Settings.SignInAsync(Settings.OtherHost);
    }

    private async void OnSignOut(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AccountViewModel account })
            await Settings.SignOutAsync(account);
    }

    private async void OnSaveIdentity(object sender, RoutedEventArgs e) => await Settings.SaveIdentityAsync();
}
