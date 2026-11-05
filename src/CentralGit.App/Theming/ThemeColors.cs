using Windows.UI;

namespace CentralGit.App.Theming;

/// <summary>
/// One complete color set. The single source of truth: XAML brushes (<see cref="ThemeResources"/>),
/// stock-control overrides and the view models' <see cref="Palette"/> are all built from it,
/// so a custom theme later is just another instance of this record.
/// </summary>
public sealed record ThemeColors
{
    public required string Name { get; init; }
    public required bool IsDark { get; init; }

    // Surfaces: hierarchy by tone. Dark: elevation gets lighter. Light: elevation gets whiter.
    public required Color Deep { get; init; }
    public required Color Window { get; init; }
    public required Color Surface { get; init; }
    public required Color Raised { get; init; }
    public required Color Elevated { get; init; }
    public required Color Hover { get; init; }
    public required Color Seam { get; init; }
    public required Color Divider { get; init; }
    public required Color Border { get; init; }
    public required Color BorderHover { get; init; }
    public required Color Code { get; init; }

    /// <summary>Docked panels that sit one step above the content (the History inspector).</summary>
    public required Color Panel { get; init; }

    /// <summary>Bars that control the content below them (pane toolbars, the diff header): slightly raised.</summary>
    public required Color Toolbar { get; init; }

    /// <summary>The lit top edge of a raised surface: a translucent hairline, never a shadow.</summary>
    public required Color Highlight { get; init; }
    public required Color HunkHeader { get; init; }
    public required Color Filler { get; init; }
    public required Color Scrim { get; init; }

    /// <summary>Hover/press wash laid over any surface (white on dark, black on light).</summary>
    public required Color Overlay { get; init; }

    public required Color TextPrimary { get; init; }
    public required Color TextSecondary { get; init; }
    public required Color TextMuted { get; init; }
    public required Color TextFaint { get; init; }
    public required Color TextBranch { get; init; }
    public required Color LineNumber { get; init; }
    public required Color HunkRange { get; init; }

    /// <summary>Text on a filled violet button.</summary>
    public required Color OnAccent { get; init; }

    // Accents: one meaning each. *Text variants are tuned to read as text on Surface.
    public required Color Violet { get; init; }
    public required Color VioletText { get; init; }
    public required Color VioletHighlight { get; init; }
    public required Color Orange { get; init; }
    public required Color OrangeText { get; init; }
    public required Color Green { get; init; }
    public required Color GreenText { get; init; }
    public required Color Teal { get; init; }
    public required Color Red { get; init; }
    public required Color RedText { get; init; }
    public required Color Info { get; init; }
    public required Color Sand { get; init; }
    public required Color Slate { get; init; }
    public required Color SlateAlt { get; init; }

    // Ref chips in the history.
    public required Color HeadChipText { get; init; }
    public required Color RefChipText { get; init; }
    public required Color TagChipText { get; init; }
    public required Color TagChipBack { get; init; }
    public required Color TagChipBorder { get; init; }

    // Syntax: quiet; never competes with the meaning accents.
    public required Color SyntaxPlain { get; init; }
    public required Color SyntaxKeyword { get; init; }
    public required Color SyntaxType { get; init; }
    public required Color SyntaxString { get; init; }
    public required Color SyntaxComment { get; init; }
    public required Color SyntaxNumber { get; init; }
    public required Color SyntaxFunction { get; init; }
    public required Color SyntaxPunctuation { get; init; }

    public static readonly ThemeColors Dark = new()
    {
        Name = "Dark",
        IsDark = true,
        Deep = Hex(0x17191D),
        Window = Hex(0x1D2025),
        Surface = Hex(0x23262C),
        Raised = Hex(0x2A2E35),
        Elevated = Hex(0x363B43),
        Hover = Hex(0x404650),
        Seam = Hex(0x101114),
        Divider = Hex(0x2B2F36),
        Border = Hex(0x2E3239),
        BorderHover = Hex(0x3A3F47),
        Code = Hex(0x202328),
        Panel = Hex(0x25282E),
        Toolbar = Hex(0x282B31),
        Highlight = Hex(0xFFFFFF, 0x0C),
        HunkHeader = Hex(0x262A31),
        Filler = Hex(0x1E2126),
        Scrim = Hex(0x101114, 0x99),
        Overlay = Hex(0xFFFFFF),
        TextPrimary = Hex(0xEDEBE8),
        TextSecondary = Hex(0xADB1B9),
        TextMuted = Hex(0x8B929D),
        TextFaint = Hex(0x4B515B),
        TextBranch = Hex(0xD5D7DB),
        LineNumber = Hex(0x6F7682),
        HunkRange = Hex(0xB7C5D8),
        OnAccent = Hex(0x17191D),
        Violet = Hex(0x9B83C9),
        VioletText = Hex(0xB9A6DE),
        VioletHighlight = Hex(0xCDBEEB),
        Orange = Hex(0xD98A57),
        OrangeText = Hex(0xE2A57E),
        Green = Hex(0x78AD91),
        GreenText = Hex(0x8FC0A6),
        Teal = Hex(0x78A6A5),
        Red = Hex(0xD46F6A),
        RedText = Hex(0xDE8C87),
        Info = Hex(0x8EA3BF),
        Sand = Hex(0xB8A887),
        Slate = Hex(0x8C95A6),
        SlateAlt = Hex(0x7E8899),
        HeadChipText = Hex(0xE2D8F5),
        RefChipText = Hex(0xE0E2E6),
        TagChipText = Hex(0xD2C8B3),
        TagChipBack = Hex(0xFFFFFF, 10),
        TagChipBorder = Hex(0xFFFFFF, 20),
        SyntaxPlain = Hex(0xD9D6D0),
        SyntaxKeyword = Hex(0xB9A6DE),
        SyntaxType = Hex(0x9DB3CF),
        SyntaxString = Hex(0xC4B48F),
        SyntaxComment = Hex(0x7D848F),
        SyntaxNumber = Hex(0xC9A98B),
        SyntaxFunction = Hex(0xE6E1D8),
        SyntaxPunctuation = Hex(0x9EA4AE),
    };

    /// <summary>Warm paper tones, not pure white; accents deepened so they hold 4.5:1 as text.</summary>
    public static readonly ThemeColors Light = new()
    {
        Name = "Light",
        IsDark = false,
        Deep = Hex(0xE2DFD9),
        Window = Hex(0xECEAE5),
        Surface = Hex(0xF5F4F0),
        Raised = Hex(0xE6E3DD),
        Elevated = Hex(0xDCD8D1),
        Hover = Hex(0xD3CFC7),
        Seam = Hex(0xD3CFC8),
        Divider = Hex(0xE0DDD7),
        Border = Hex(0xCFCBC4),
        BorderHover = Hex(0xB9B4AC),
        Code = Hex(0xF8F7F4),
        Panel = Hex(0xF0EEE9),
        Toolbar = Hex(0xEDEBE6),
        Highlight = Hex(0xFFFFFF, 0xB3),
        HunkHeader = Hex(0xE9E6E0),
        Filler = Hex(0xEEECE7),
        Scrim = Hex(0x2A2620, 0x66),
        Overlay = Hex(0x000000),
        TextPrimary = Hex(0x1E2127),
        TextSecondary = Hex(0x484E58),
        TextMuted = Hex(0x666C76),
        TextFaint = Hex(0xA9A59E),
        TextBranch = Hex(0x2C3038),
        LineNumber = Hex(0x8E939B),
        HunkRange = Hex(0x3C5A7E),
        OnAccent = Hex(0xFFFFFF),
        Violet = Hex(0x7457AD),
        VioletText = Hex(0x5E4499),
        VioletHighlight = Hex(0x9B83C9),
        Orange = Hex(0xC2692F),
        OrangeText = Hex(0x99501F),
        Green = Hex(0x4A8A68),
        GreenText = Hex(0x2E6B4B),
        Teal = Hex(0x3F807F),
        Red = Hex(0xC04842),
        RedText = Hex(0xA13530),
        Info = Hex(0x506A8E),
        Sand = Hex(0x8F7E58),
        Slate = Hex(0x687183),
        SlateAlt = Hex(0x5A6475),
        HeadChipText = Hex(0x4A3384),
        RefChipText = Hex(0x30343C),
        TagChipText = Hex(0x6E5B30),
        TagChipBack = Hex(0x000000, 10),
        TagChipBorder = Hex(0x000000, 28),
        SyntaxPlain = Hex(0x2A2D33),
        SyntaxKeyword = Hex(0x6A4BA6),
        SyntaxType = Hex(0x33649A),
        SyntaxString = Hex(0x80621B),
        SyntaxComment = Hex(0x7B828C),
        SyntaxNumber = Hex(0x9C5526),
        SyntaxFunction = Hex(0x1E2127),
        SyntaxPunctuation = Hex(0x5A606A),
    };

    private static Color Hex(uint rgb, byte alpha = 0xFF) =>
        Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
