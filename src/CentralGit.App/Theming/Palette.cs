using Windows.UI;

namespace CentralGit.App.Theming;

/// <summary>Code-side mirror of Themes/Tokens.xaml for colors computed at runtime (graph lanes, ref chips).</summary>
public static class Palette
{
    public static readonly Color Violet = Hex(0x9B83C9);
    public static readonly Color Teal = Hex(0x78A6A5);
    public static readonly Color Orange = Hex(0xD98A57);
    public static readonly Color Sand = Hex(0xB8A887);
    public static readonly Color Slate = Hex(0x8C95A6);
    public static readonly Color SlateAlt = Hex(0x7E8899);
    public static readonly Color Surface = Hex(0x23262C);
    public static readonly Color TextPrimary = Hex(0xEDEBE8);
    public static readonly Color TextSecondary = Hex(0xADB1B9);
    public static readonly Color TextMuted = Hex(0x8B929D);
    public static readonly Color VioletText = Hex(0xB9A6DE);
    public static readonly Color OrangeText = Hex(0xE2A57E);
    public static readonly Color Transparent = Color.FromArgb(0, 0, 0, 0);

    /// <summary>
    /// Lanes without a meaning get neutral slates; lane 0 is usually the trunk.
    /// Meaningful colors (violet for the current branch) are applied on top by the colorizer.
    /// </summary>
    public static Color LaneDefault(int lane) => lane == 0 ? Teal : (lane % 2 == 1 ? Slate : SlateAlt);

    public static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B);

    private static Color Hex(uint rgb) => Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
