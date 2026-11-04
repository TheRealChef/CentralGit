using System.Globalization;
using CentralGit.App.Theming;
using CentralGit.Core.Diff;
using CentralGit.Core.Text;
using Windows.UI;

namespace CentralGit.App.Diff;

/// <summary>
/// One rendered row of the diff list. Split rows use Left/Right; unified rows put the code in
/// Left and show both line numbers. Immutable and color-only, so it can be built off the UI thread.
/// </summary>
public sealed class DiffRowViewModel
{
    private DiffRowViewModel(DiffRowKind kind, bool isSplit, string text)
    {
        Kind = kind;
        IsSplit = isSplit;
        Text = text;
    }

    public DiffRowKind Kind { get; }
    public bool IsSplit { get; }
    public string Text { get; }

    public string LeftNumber { get; private init; } = "";
    public string RightNumber { get; private init; } = "";
    public CodeLine? Left { get; private init; }
    public CodeLine? Right { get; private init; }
    public Color LeftBackground { get; private init; } = Palette.Transparent;
    public Color RightBackground { get; private init; } = Palette.Transparent;
    public Color LeftGutter { get; private init; } = Palette.Transparent;
    public Color RightGutter { get; private init; } = Palette.Transparent;
    public string Sign { get; private init; } = "";
    public Color SignColor { get; private init; } = Palette.TextMuted;

    /// <summary>Hunk headers only: which hunk, and what the Changes page offers for it.</summary>
    public int HunkIndex { get; private init; } = -1;
    public string HunkRange { get; private init; } = "";
    public string HunkSection { get; private init; } = "";
    public string HunkActionLabel { get; private init; } = "";
    public bool HasHunkAction => HunkActionLabel.Length > 0;
    public bool CanDiscardHunk { get; private init; }

    public static IReadOnlyList<DiffRowViewModel> Build(
        IReadOnlyList<DiffRow> rows, bool split, SyntaxLanguage language, HunkActions actions = HunkActions.None) =>
        rows.Select(r => r.Kind == DiffRowKind.Lines
                ? split ? SplitRow(r, language) : UnifiedRow(r, language)
                : new DiffRowViewModel(r.Kind, split, r.Text)
                {
                    HunkIndex = r.Hunk,
                    HunkRange = r.Range,
                    HunkSection = r.Section.Length > 0 ? "in " + r.Section : "",
                    HunkActionLabel = r.Kind != DiffRowKind.HunkHeader ? "" : actions switch
                    {
                        HunkActions.Stage => "Stage hunk",
                        HunkActions.Unstage => "Unstage hunk",
                        _ => "",
                    },
                    CanDiscardHunk = r.Kind == DiffRowKind.HunkHeader && actions == HunkActions.Stage,
                })
            .ToList();

    private static DiffRowViewModel SplitRow(DiffRow row, SyntaxLanguage language)
    {
        var (lCode, lBack, lGutter) = Side(row.Left, language);
        var (rCode, rBack, rGutter) = Side(row.Right, language);
        return new DiffRowViewModel(DiffRowKind.Lines, true, "")
        {
            LeftNumber = Number(row.Left?.Line.OldNumber),
            RightNumber = Number(row.Right?.Line.NewNumber),
            Left = lCode,
            Right = rCode,
            LeftBackground = lBack,
            RightBackground = rBack,
            LeftGutter = lGutter,
            RightGutter = rGutter,
        };
    }

    private static DiffRowViewModel UnifiedRow(DiffRow row, SyntaxLanguage language)
    {
        var cell = row.Left!;
        var (code, back, gutter) = Side(cell, language);
        return new DiffRowViewModel(DiffRowKind.Lines, false, "")
        {
            LeftNumber = Number(cell.Line.OldNumber),
            RightNumber = Number(cell.Line.NewNumber),
            Left = code,
            LeftBackground = back,
            LeftGutter = gutter,
            Sign = cell.Line.Kind switch { DiffLineKind.Added => "+", DiffLineKind.Deleted => "−", _ => "" },
            SignColor = cell.Line.Kind == DiffLineKind.Added ? Palette.GreenText : Palette.RedText,
        };
    }

    private static (CodeLine? Code, Color Background, Color Gutter) Side(DiffCell? cell, SyntaxLanguage language)
    {
        if (cell is null)
            return (null, Palette.Filler, Palette.Filler);

        return cell.Line.Kind switch
        {
            DiffLineKind.Added => (CodeLine.Build(cell.Line.Text, language, cell.Changes, Palette.AddedChange), Palette.AddedLine, Palette.AddedGutter),
            DiffLineKind.Deleted => (CodeLine.Build(cell.Line.Text, language, cell.Changes, Palette.DeletedChange), Palette.DeletedLine, Palette.DeletedGutter),
            _ => (CodeLine.Build(cell.Line.Text, language, [], Palette.Transparent), Palette.Transparent, Palette.Transparent),
        };
    }

    private static string Number(int? n) => n?.ToString(CultureInfo.InvariantCulture) ?? "";
}

/// <summary>What a hunk header offers: nothing (history), stage + discard (unstaged), unstage (staged).</summary>
public enum HunkActions { None, Stage, Unstage }
