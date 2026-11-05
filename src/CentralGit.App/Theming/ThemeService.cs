using CentralGit.Application.Ports;
using CentralGit.Application.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace CentralGit.App.Theming;

/// <summary>
/// Turns <see cref="ThemeColors"/> into the "Dark" and "Light" theme dictionaries that every
/// <c>{ThemeResource}</c> resolves against, and decides which one each region uses: the pages follow
/// the chosen theme; the chrome (title bar, sidebar, status bar) can stay dark in the light theme.
/// Switching is live: regions flip <c>RequestedTheme</c>, view models rebuild their colors.
/// </summary>
public static class ThemeService
{
    private static ResourceDictionary? _dark,
        _light;
    private static UISettings? _uiSettings;
    private static ISettingsStore? _settings;

    private static ISettingsStore Settings =>
        _settings ?? throw new InvalidOperationException("ThemeService.Use was not called.");

    /// <summary>Where the chosen theme is read from. The composition root calls this first.</summary>
    public static void Use(ISettingsStore settings) => _settings = settings;

    /// <summary>Keys Tokens.xaml lacks (a brush added to ThemeColors but not declared there). Empty when all is well.</summary>
    public static List<string> Missing { get; } = [];

    public static ElementTheme ContentTheme { get; private set; } = ElementTheme.Dark;
    public static ElementTheme ChromeTheme { get; private set; } = ElementTheme.Dark;

    /// <summary>Raised on the UI thread after the resolved theme changed (setting or Windows mode).</summary>
    public static event Action? Changed;

    /// <summary>
    /// Colors the placeholder brushes declared in Themes/Tokens.xaml's "Dark" and "Light" dictionaries.
    /// Call before any window exists.
    /// </summary>
    public static void Initialize(ResourceDictionary appResources)
    {
        var tokens = appResources.MergedDictionaries.First(d =>
            d.Source?.AbsolutePath.EndsWith("/Themes/Tokens.xaml", StringComparison.OrdinalIgnoreCase) == true
        );
        // WinUI files the "Dark" dictionary under "Default".
        _dark = (ResourceDictionary)(
            tokens.ThemeDictionaries.TryGetValue("Dark", out var d) ? d : tokens.ThemeDictionaries["Default"]
        );
        _light = (ResourceDictionary)tokens.ThemeDictionaries["Light"];
        Fill(_dark, ThemeColors.Dark);
        Fill(_light, ThemeColors.Light);
        if (Missing.Count > 0)
            CrashLog.Write(
                new InvalidOperationException("Tokens.xaml lacks theme keys: " + string.Join(", ", Missing))
            );
        Resolve(Settings.Current);
    }

    /// <summary>The Application-level theme (flyouts, tooltips and dialogs before a window applies its own).</summary>
    public static ApplicationTheme StartupApplicationTheme =>
        IsLight(Settings.Current.Theme) ? ApplicationTheme.Light : ApplicationTheme.Dark;

    /// <summary>Re-reads the settings (and Windows' mode for "System"); raises <see cref="Changed"/> if anything moved.</summary>
    public static void Refresh()
    {
        var before = (ContentTheme, ChromeTheme);
        Resolve(Settings.Current);
        if ((ContentTheme, ChromeTheme) != before)
            Changed?.Invoke();
    }

    /// <summary>Follows Windows' light/dark switch while "System" is selected.</summary>
    public static void WatchSystem(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        _uiSettings ??= new UISettings();
        _uiSettings.ColorValuesChanged += (_, _) =>
            dispatcher.TryEnqueue(() =>
            {
                if (Settings.Current.Theme == AppTheme.System)
                    Refresh();
            });
    }

    /// <summary>A brush for code-behind in the pages. Assign again on <see cref="Changed"/>.</summary>
    public static Brush ContentBrush(string key) => Lookup(key, ContentTheme);

    /// <summary>A brush for code-behind in the title bar, sidebar or status bar.</summary>
    public static Brush ChromeBrush(string key) => Lookup(key, ChromeTheme);

    /// <summary>A brush matching an element's current theme (only reliable once it has rendered in it).</summary>
    public static Brush Brush(string key, FrameworkElement scope) => Lookup(key, scope.ActualTheme);

    private static Brush Lookup(string key, ElementTheme theme)
    {
        var dict = theme == ElementTheme.Light ? _light : _dark;
        return dict is not null && dict.TryGetValue(key, out var value) && value is Brush b
            ? b
            : (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];
    }

    private static void Resolve(AppSettings settings)
    {
        var light = IsLight(settings.Theme);
        ContentTheme = light ? ElementTheme.Light : ElementTheme.Dark;
        ChromeTheme = light && !settings.DarkChromeInLight ? ElementTheme.Light : ElementTheme.Dark;
        Palette.Content = light ? ThemeColors.Light : ThemeColors.Dark;
        Palette.Chrome = ChromeTheme == ElementTheme.Light ? ThemeColors.Light : ThemeColors.Dark;
    }

    private static bool IsLight(AppTheme theme) =>
        theme switch
        {
            AppTheme.Light => true,
            AppTheme.System => SystemIsLight(),
            _ => false,
        };

    public static bool SystemIsLight()
    {
        try
        {
            var bg = (_uiSettings ??= new UISettings()).GetColorValue(UIColorType.Background);
            return bg.R + bg.G + bg.B > 3 * 128;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Writes every key for one theme. Existing brushes are recolored in place, so elements already
    /// holding them update too — the hook for live custom themes later.
    /// </summary>
    private static void Fill(ResourceDictionary d, ThemeColors t)
    {
        void B(string key, Color c)
        {
            // Every key is declared in Tokens.xaml; recolor in place (adding keys from code breaks stock lookups).
            if (d.TryGetValue(key, out var existing) && existing is SolidColorBrush brush)
                brush.Color = c;
            else
                Missing.Add($"{t.Name}:{key}={(existing is null ? "absent" : existing.GetType().Name)}");
        }
        var alpha = Palette.WithAlpha;

        // Ours (Tokens.xaml and MainWindow.xaml use these keys).
        B("DeepBrush", t.Deep);
        B("WindowBrush", t.Window);
        B("SurfaceBrush", t.Surface);
        B("RaisedBrush", t.Raised);
        B("ElevatedBrush", t.Elevated);
        B("HoverBrush", t.Hover);
        B("SeamBrush", t.Seam);
        B("DividerBrush", t.Divider);
        B("ControlBorderBrush", t.Border);
        B("CodeBrush", t.Code);

        // Surface system, low to high: Recessed (code wells) < Surface (content) < Panel (docked
        // inspector) < Toolbar (bars over content) < Raised/Elevated (chips, selected nav).
        // Raised surfaces get a Highlight hairline on top; seams separate, nothing casts a shadow.
        B("RecessedBrush", t.Code);
        B("PanelBrush", t.Panel);
        B("ToolbarBrush", t.Toolbar);
        B("HighlightBrush", t.Highlight);
        B("BandBrush", alpha(t.Overlay, t.IsDark ? 0.022 : 0.028));
        B("SplitterHoverBrush", alpha(t.Violet, 0.55));
        B("SplitterGripBrush", t.IsDark ? t.TextFaint : t.BorderHover);
        B("HunkHeaderBrush", t.HunkHeader);
        B("FillerBrush", t.Filler);
        B("ScrimBrush", t.Scrim);
        B("OverlayBrush", t.Overlay);
        B("TextPrimaryBrush", t.TextPrimary);
        B("TextSecondaryBrush", t.TextSecondary);
        B("TextMutedBrush", t.TextMuted);
        B("TextFaintBrush", t.TextFaint);
        B("LineNumberBrush", t.LineNumber);
        B("HunkRangeBrush", t.HunkRange);
        B("SyntaxPlainBrush", t.SyntaxPlain);
        B("OnAccentBrush", t.OnAccent);
        B("VioletBrush", t.Violet);
        B("VioletTextBrush", t.IsDark ? t.VioletHighlight : t.VioletText);
        B("VioletHighlightBrush", t.VioletHighlight);
        B("VioletTintBrush", alpha(t.Violet, t.IsDark ? 0.17 : 0.14));
        B("OrangeBrush", t.Orange);
        B("OrangeTextBrush", t.OrangeText);
        B("OrangeTintBrush", alpha(t.Orange, t.IsDark ? 0.12 : 0.13));
        B("GreenTextBrush", t.GreenText);
        B("RedTextBrush", t.RedText);
        B("RedTintBrush", alpha(t.Red, 0.12));
        B("TealBrush", t.Teal);
        B("InfoBrush", t.Info);

        var transparent = Palette.Transparent;
        B("ButtonBackground", transparent);
        B("ButtonBackgroundPointerOver", t.Raised);
        B("ButtonBackgroundPressed", t.Elevated);
        B("ButtonBackgroundDisabled", transparent);
        B("ButtonBorderBrush", transparent);
        B("ButtonBorderBrushPointerOver", transparent);
        B("ButtonBorderBrushPressed", transparent);
        B("ButtonBorderBrushDisabled", transparent);
        B("ButtonForeground", t.TextSecondary);
        B("ButtonForegroundPointerOver", t.TextPrimary);
        B("ButtonForegroundPressed", t.TextPrimary);
        B("ButtonForegroundDisabled", t.IsDark ? Color.FromArgb(255, 0x5A, 0x60, 0x6B) : t.TextFaint);

        // Selection: a restrained violet wash plus the violet indicator pill; hover stays neutral.
        B("ListViewItemBackgroundPointerOver", alpha(t.Overlay, t.IsDark ? 0.05 : 0.04));
        B("ListViewItemBackgroundPressed", alpha(t.Overlay, t.IsDark ? 0.08 : 0.07));
        B("ListViewItemBackgroundSelected", alpha(t.Violet, t.IsDark ? 0.14 : 0.12));
        B("ListViewItemBackgroundSelectedPointerOver", alpha(t.Violet, t.IsDark ? 0.18 : 0.16));
        B("ListViewItemBackgroundSelectedPressed", alpha(t.Violet, t.IsDark ? 0.22 : 0.20));
        B("ListViewItemSelectionIndicatorBrush", t.Violet);

        var field = t.IsDark ? t.Window : t.Code;
        B("TextControlBackground", field);
        B("TextControlBackgroundPointerOver", field);
        B("TextControlBackgroundFocused", field);
        B("TextControlBorderBrush", t.Border);
        B("TextControlBorderBrushPointerOver", t.BorderHover);
        B("TextControlBorderBrushFocused", t.Violet);
        B("TextControlPlaceholderForeground", t.TextMuted);
        B("TextControlPlaceholderForegroundPointerOver", t.TextMuted);
        B("TextControlPlaceholderForegroundFocused", t.TextMuted);
    }
}
