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
    public static readonly Color Green = Hex(0x78AD91);
    public static readonly Color GreenText = Hex(0x8FC0A6);
    public static readonly Color Red = Hex(0xD46F6A);
    public static readonly Color RedText = Hex(0xDE8C87);
    public static readonly Color Info = Hex(0x8EA3BF);
    public static readonly Color Raised = Hex(0x2A2E35);

    // Diff: whole-line tint, plus a stronger tint for the exact characters that changed.
    public static readonly Color AddedLine = WithAlpha(Green, 0.10);
    public static readonly Color AddedGutter = WithAlpha(Green, 0.16);
    public static readonly Color AddedChange = WithAlpha(Green, 0.28);
    public static readonly Color DeletedLine = WithAlpha(Red, 0.10);
    public static readonly Color DeletedGutter = WithAlpha(Red, 0.16);
    public static readonly Color DeletedChange = WithAlpha(Red, 0.30);
    public static readonly Color Filler = Hex(0x1E2126);

    // Syntax: quiet, warm-neutral; never competes with the meaning accents.
    public static readonly Color SyntaxPlain = Hex(0xD9D6D0);
    public static readonly Color SyntaxKeyword = Hex(0xB9A6DE);
    public static readonly Color SyntaxType = Hex(0x9DB3CF);
    public static readonly Color SyntaxString = Hex(0xC4B48F);
    public static readonly Color SyntaxComment = Hex(0x7D848F);
    public static readonly Color SyntaxNumber = Hex(0xC9A98B);
    public static readonly Color SyntaxFunction = Hex(0xE6E1D8);
    public static readonly Color SyntaxPunctuation = Hex(0x9EA4AE);

    /// <summary>
    /// Lanes without a meaning get neutral slates; lane 0 is usually the trunk.
    /// Meaningful colors (violet for the current branch) are applied on top by the colorizer.
    /// </summary>
    public static Color LaneDefault(int lane) => lane == 0 ? Teal : (lane % 2 == 1 ? Slate : SlateAlt);

    public static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B);

    private static Color Hex(uint rgb) => Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
