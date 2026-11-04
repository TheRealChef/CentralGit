using CentralGit.App.Theming;
using CentralGit.Core.Diff;
using CentralGit.Core.Text;
using Windows.UI;

namespace CentralGit.App.Diff;

public readonly record struct ColoredSpan(int Start, int Length, Color Color);

/// <summary>
/// One line of code ready to render: tabs expanded, syntax runs colored, changed characters
/// marked. Built off the UI thread; <see cref="CodeText"/> turns it into Runs on a TextBlock.
/// </summary>
public sealed class CodeLine(string text, IReadOnlyList<ColoredSpan> runs, IReadOnlyList<TextSpan> highlights, Color highlightColor)
{
    private const int TabSize = 4;

    public string Text { get; } = text;
    public IReadOnlyList<ColoredSpan> Runs { get; } = runs;
    public IReadOnlyList<TextSpan> Highlights { get; } = highlights;
    public Color HighlightColor { get; } = highlightColor;

    public static CodeLine Build(string raw, SyntaxLanguage language, IReadOnlyList<TextSpan> changes, Color highlightColor)
    {
        // Expand tabs so columns line up, and remember where each original char landed.
        var map = new int[raw.Length + 1];
        var sb = new System.Text.StringBuilder(raw.Length + 8);
        for (var i = 0; i < raw.Length; i++)
        {
            map[i] = sb.Length;
            if (raw[i] == '\t')
                sb.Append(' ', TabSize - sb.Length % TabSize);
            else
                sb.Append(raw[i]);
        }
        map[raw.Length] = sb.Length;

        TextSpan Remap(int start, int length) => new(map[start], map[start + length] - map[start]);

        var runs = SyntaxHighlighter.Highlight(raw, language)
            .Where(t => t.Length > 0)
            .Select(t => { var s = Remap(t.Start, t.Length); return new ColoredSpan(s.Start, s.Length, ColorOf(t.Kind)); })
            .ToList();
        var highlights = changes.Select(c => Remap(c.Start, c.Length)).ToList();

        return new CodeLine(sb.ToString(), runs, highlights, highlightColor);
    }

    private static Color ColorOf(TokenKind kind) => kind switch
    {
        TokenKind.Keyword => Palette.SyntaxKeyword,
        TokenKind.Type => Palette.SyntaxType,
        TokenKind.String => Palette.SyntaxString,
        TokenKind.Comment => Palette.SyntaxComment,
        TokenKind.Number => Palette.SyntaxNumber,
        TokenKind.Function => Palette.SyntaxFunction,
        TokenKind.Punctuation => Palette.SyntaxPunctuation,
        TokenKind.Tag => Palette.SyntaxType,
        TokenKind.Attribute => Palette.SyntaxKeyword,
        _ => Palette.SyntaxPlain,
    };
}
