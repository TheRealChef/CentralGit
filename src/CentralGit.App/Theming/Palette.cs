using Windows.UI;

namespace CentralGit.App.Theming;

/// <summary>
/// Colors for view models (graph lanes, chips, row tints). <see cref="Content"/> is the theme of the
/// pages; <see cref="Chrome"/> the theme of the title bar, sidebar and status bar, which stay dark in
/// the light theme unless the user turns that off. Set by <see cref="ThemeService"/>; view models
/// read them when they're built, so a theme change rebuilds them.
/// </summary>
public static class Palette
{
    public static ThemeColors Content { get; internal set; } = ThemeColors.Dark;
    public static ThemeColors Chrome { get; internal set; } = ThemeColors.Dark;

    public static Color Violet => Content.Violet;
    public static Color Teal => Content.Teal;
    public static Color Orange => Content.Orange;
    public static Color Sand => Content.Sand;
    public static Color Slate => Content.Slate;
    public static Color SlateAlt => Content.SlateAlt;
    public static Color Surface => Content.Surface;
    public static Color TextPrimary => Content.TextPrimary;
    public static Color TextSecondary => Content.TextSecondary;
    public static Color TextMuted => Content.TextMuted;
    public static Color VioletText => Content.VioletText;
    public static Color OrangeText => Content.OrangeText;
    public static readonly Color Transparent = Color.FromArgb(0, 0, 0, 0);
    public static Color Green => Content.Green;
    public static Color GreenText => Content.GreenText;
    public static Color Red => Content.Red;
    public static Color RedText => Content.RedText;
    public static Color Info => Content.Info;
    public static Color Raised => Content.Raised;

    // Diff: whole-line tint, plus a stronger tint for the exact characters that changed.
    // Light backgrounds need a little more tint for the same visibility.
    private static double Tint(double dark) => Content.IsDark ? dark : dark * 1.25;

    public static Color AddedLine => WithAlpha(Green, Tint(0.10));
    public static Color AddedGutter => WithAlpha(Green, Tint(0.16));
    public static Color AddedChange => WithAlpha(Green, Tint(0.28));
    public static Color DeletedLine => WithAlpha(Red, Tint(0.10));
    public static Color DeletedGutter => WithAlpha(Red, Tint(0.16));
    public static Color DeletedChange => WithAlpha(Red, Tint(0.30));
    public static Color Filler => Content.Filler;

    public static Color SyntaxPlain => Content.SyntaxPlain;
    public static Color SyntaxKeyword => Content.SyntaxKeyword;
    public static Color SyntaxType => Content.SyntaxType;
    public static Color SyntaxString => Content.SyntaxString;
    public static Color SyntaxComment => Content.SyntaxComment;
    public static Color SyntaxNumber => Content.SyntaxNumber;
    public static Color SyntaxFunction => Content.SyntaxFunction;
    public static Color SyntaxPunctuation => Content.SyntaxPunctuation;

    /// <summary>
    /// Lanes without a meaning get neutral slates; lane 0 is usually the trunk.
    /// Meaningful colors (violet for the current branch) are applied on top by the colorizer.
    /// </summary>
    public static Color LaneDefault(int lane) => lane == 0 ? Teal : (lane % 2 == 1 ? Slate : SlateAlt);

    public static Color WithAlpha(Color c, double alpha) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

    /// <summary>Straight blend, for deriving shades (accent ramps) from a theme color.</summary>
    public static Color Mix(Color a, Color b, double t) =>
        Color.FromArgb(
            (byte)Math.Round(a.A + (b.A - a.A) * t),
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t)
        );
}
