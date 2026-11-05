using System.Collections.ObjectModel;
using CentralGit.App.Theming;
using CentralGit.Application.Ports;
using CentralGit.Application.Settings;
using Windows.UI;

namespace CentralGit.App.ViewModels;

/// <summary>A theme tile in Settings › Appearance: a miniature of the window in that theme.</summary>
public sealed class ThemeChoiceViewModel(
    AppTheme theme,
    string title,
    string detail,
    ThemeColors chrome,
    ThemeColors content,
    bool selected
)
{
    public AppTheme Theme { get; } = theme;
    public string Title { get; } = title;
    public string Detail { get; } = detail;
    public bool IsSelected { get; } = selected;
    public string Tag => Theme.ToString();
    public string AutomationName => $"{Title} theme{(IsSelected ? ", selected" : "")}";

    public Color ChromeColor { get; } = chrome.Deep;
    public Color SidebarColor { get; } = chrome.Window;
    public Color SidebarLine { get; } = chrome.TextFaint;
    public Color ContentColor { get; } = content.Surface;
    public Color ContentLine { get; } = content.TextMuted;
    public Color ContentStrong { get; } = content.TextSecondary;
    public Color Accent { get; } = content.Violet;
    public Color Attention { get; } = content.Orange;
    public Color Seam { get; } = content.Seam;
    public Color BorderColor { get; } = selected ? content.Violet : Palette.Transparent;
}

public sealed partial class SettingsViewModel
{
    public ObservableCollection<ThemeChoiceViewModel> ThemeChoices { get; } = [];

    /// <summary>Only matters while the pages are light.</summary>
    public bool DarkChromeInLight
    {
        get => settings.Current.DarkChromeInLight;
        set
        {
            if (value == settings.Current.DarkChromeInLight)
                return;
            settings.Update(s => s with { DarkChromeInLight = value });
            ThemeService.Refresh();
            OnPropertyChanged();
            RefreshThemeChoices();
        }
    }

    public bool CanChooseChrome => ThemeService.ContentTheme == Microsoft.UI.Xaml.ElementTheme.Light;

    public void SelectTheme(AppTheme theme)
    {
        if (theme == settings.Current.Theme)
            return;
        settings.Update(s => s with { Theme = theme });
        ThemeService.Refresh();
        RefreshThemeChoices();
    }

    private void RefreshThemeChoices()
    {
        var current = settings.Current.Theme;
        var lightChrome = settings.Current.DarkChromeInLight ? ThemeColors.Dark : ThemeColors.Light;
        ThemeChoices.Clear();
        ThemeChoices.Add(
            new(
                AppTheme.Dark,
                "Dark",
                "Graphite surfaces, easy on the eyes",
                ThemeColors.Dark,
                ThemeColors.Dark,
                current == AppTheme.Dark
            )
        );
        ThemeChoices.Add(
            new(
                AppTheme.Light,
                "Light",
                "Warm paper pages, more contrast in daylight",
                lightChrome,
                ThemeColors.Light,
                current == AppTheme.Light
            )
        );
        var windowsLight = ThemeService.SystemIsLight();
        ThemeChoices.Add(
            new(
                AppTheme.System,
                "Match Windows",
                $"Follows your Windows mode (now {(windowsLight ? "light" : "dark")})",
                windowsLight ? lightChrome : ThemeColors.Dark,
                windowsLight ? ThemeColors.Light : ThemeColors.Dark,
                current == AppTheme.System
            )
        );
        OnPropertyChanged(nameof(CanChooseChrome));
    }
}
