using CentralGit.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace CentralGit.App;

/// <summary>
/// Which theme each region runs: the pages follow Settings › Appearance; the title bar, sidebar and
/// status bar ("chrome") may stay dark in the light theme. Flyouts take the theme of the region they open from.
/// </summary>
public sealed partial class MainWindow
{
    private void InitializeTheme()
    {
        ApplyTheme();
        ThemeService.WatchSystem(DispatcherQueue);
        ThemeService.Changed += async () =>
        {
            ApplyTheme();
            await ViewModel.RefreshColorsAsync();
        };

        // Flyouts live in their own popup tree and don't inherit a region's RequestedTheme.
        ThemeFlyout(BranchFlyout, chrome: true);
        ThemeFlyout(OperationsFlyout, chrome: true);
    }

    private void ApplyTheme()
    {
        Root.RequestedTheme = ThemeService.ContentTheme;
        AppTitleBar.RequestedTheme = Sidebar.RequestedTheme = StatusBar.RequestedTheme = ThemeService.ChromeTheme;

        var chrome = Palette.Chrome;
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonForegroundColor = chrome.TextSecondary;
        titleBar.ButtonInactiveForegroundColor = chrome.TextMuted;
        titleBar.ButtonHoverBackgroundColor = chrome.Raised;
        titleBar.ButtonHoverForegroundColor = chrome.TextPrimary;
        titleBar.ButtonPressedBackgroundColor = chrome.Elevated;
        titleBar.ButtonPressedForegroundColor = chrome.TextPrimary;

        // Brushes assigned from code don't follow ThemeResource; reassign them.
        UpdateNavigation();
        UpdateDiffToggle();
        UpdateChangesDiffToggle();
        UpdateHealthTabs();
        UpdateRecoveryTabs();
        UpdateCompareTabs();
        UpdateSettingsNav();
        StatusLine.Foreground = ThemeService.ChromeBrush(
            ViewModel.StatusIsError ? "RedTextBrush" : "TextSecondaryBrush"
        );
    }

    private static void ThemeFlyout(Flyout flyout, bool chrome)
    {
        var original = flyout.FlyoutPresenterStyle;
        flyout.Opening += (_, _) =>
        {
            var theme = chrome ? ThemeService.ChromeTheme : ThemeService.ContentTheme;
            var style = new Style(typeof(FlyoutPresenter)) { BasedOn = original };
            style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, theme));
            flyout.FlyoutPresenterStyle = style;
        };
    }
}
