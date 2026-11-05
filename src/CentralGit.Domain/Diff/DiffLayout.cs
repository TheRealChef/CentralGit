namespace CentralGit.Domain.Diff;

public enum DiffRowKind
{
    HunkHeader,
    Gap,
    Lines,
}

/// <summary>One side of a split row, or the single line of a unified row.</summary>
public sealed record DiffCell(DiffLine Line, IReadOnlyList<TextSpan> Changes);

/// <summary>
/// A row of the diff view. Split rows fill Left/Right (either may be null for a filler);
/// unified rows only use <see cref="Left"/>. <see cref="ChangeBlock"/> numbers each run of
/// changes so the view can jump between them (Alt+Up/Down).
/// </summary>
public sealed record DiffRow(DiffRowKind Kind, DiffCell? Left, DiffCell? Right, string Text, int ChangeBlock)
{
    /// <summary>For hunk headers: the index of the hunk in <see cref="FileDiff.Hunks"/>; otherwise -1.</summary>
    public int Hunk { get; init; } = -1;

    /// <summary>For hunk headers: where the change is, in words ("Lines 14–20").</summary>
    public string Range { get; init; } = "";

    /// <summary>For hunk headers: the enclosing function or class git found, if any.</summary>
    public string Section { get; init; } = "";
}

public static class DiffLayout
{
    public static IReadOnlyList<DiffRow> Split(FileDiff diff) => Build(diff, split: true);

    public static IReadOnlyList<DiffRow> Unified(FileDiff diff) => Build(diff, split: false);

    private static IReadOnlyList<DiffRow> Build(FileDiff diff, bool split)
    {
        var rows = new List<DiffRow>();
        var block = 0;
        int? previousOldEnd = null;

        for (var hunkIndex = 0; hunkIndex < diff.Hunks.Count; hunkIndex++)
        {
            var hunk = diff.Hunks[hunkIndex];
            if (previousOldEnd is { } end && hunk.OldStart > end)
                rows.Add(
                    new DiffRow(DiffRowKind.Gap, null, null, Gap(hunk.OldStart - end, end, hunk.OldStart - 1), -1)
                );
            else if (previousOldEnd is null && hunk.OldStart > 1)
                rows.Add(new DiffRow(DiffRowKind.Gap, null, null, Gap(hunk.OldStart - 1, 1, hunk.OldStart - 1), -1));

            rows.Add(
                new DiffRow(DiffRowKind.HunkHeader, null, null, hunk.Header, -1)
                {
                    Hunk = hunkIndex,
                    Range = DescribeRange(hunk),
                    Section = hunk.Section,
                }
            );

            var lines = hunk.Lines;
            var i = 0;
            while (i < lines.Count)
            {
                if (lines[i].Kind == DiffLineKind.Context)
                {
                    var cell = new DiffCell(lines[i], []);
                    rows.Add(new DiffRow(DiffRowKind.Lines, cell, split ? cell : null, "", -1));
                    i++;
                    continue;
                }

                // A run of deletions followed by additions: pair them up for intraline highlighting.
                var deleted = new List<DiffLine>();
                var added = new List<DiffLine>();
                while (i < lines.Count && lines[i].Kind == DiffLineKind.Deleted)
                    deleted.Add(lines[i++]);
                while (i < lines.Count && lines[i].Kind == DiffLineKind.Added)
                    added.Add(lines[i++]);

                var removedSpans = new IReadOnlyList<TextSpan>[deleted.Count];
                var addedSpans = new IReadOnlyList<TextSpan>[added.Count];
                for (var k = 0; k < deleted.Count; k++)
                    removedSpans[k] = [];
                for (var k = 0; k < added.Count; k++)
                    addedSpans[k] = [];
                for (var k = 0; k < Math.Min(deleted.Count, added.Count); k++)
                    (removedSpans[k], addedSpans[k]) = IntralineDiff.Compute(deleted[k].Text, added[k].Text);

                if (split)
                {
                    for (var k = 0; k < Math.Max(deleted.Count, added.Count); k++)
                    {
                        var left = k < deleted.Count ? new DiffCell(deleted[k], removedSpans[k]) : null;
                        var right = k < added.Count ? new DiffCell(added[k], addedSpans[k]) : null;
                        rows.Add(new DiffRow(DiffRowKind.Lines, left, right, "", block));
                    }
                }
                else
                {
                    for (var k = 0; k < deleted.Count; k++)
                        rows.Add(
                            new DiffRow(DiffRowKind.Lines, new DiffCell(deleted[k], removedSpans[k]), null, "", block)
                        );
                    for (var k = 0; k < added.Count; k++)
                        rows.Add(
                            new DiffRow(DiffRowKind.Lines, new DiffCell(added[k], addedSpans[k]), null, "", block)
                        );
                }

                block++;
            }

            previousOldEnd = hunk.OldStart + hunk.OldCount;
        }

        return rows;
    }

    /// <summary>
    /// The changed lines of a hunk, without its context: "Line 9", "Lines 16–17" (new file's
    /// numbering), or "Removed lines 4–6" (old numbering) when nothing was added.
    /// </summary>
    public static string DescribeRange(DiffHunk hunk)
    {
        var added = hunk.Lines.Where(l => l.Kind == DiffLineKind.Added).Select(l => l.NewNumber!.Value).ToList();
        if (added.Count > 0)
            return Span("Line", "Lines", added.Min(), added.Max());
        var deleted = hunk.Lines.Where(l => l.Kind == DiffLineKind.Deleted).Select(l => l.OldNumber!.Value).ToList();
        return deleted.Count > 0
            ? Span("Removed line", "Removed lines", deleted.Min(), deleted.Max())
            : $"Line {hunk.NewStart}";

        static string Span(string one, string many, int from, int to) =>
            from == to ? $"{one} {from}" : $"{many} {from}–{to}";
    }

    private static string Gap(int count, int from, int to) =>
        count == 1 ? $"1 unchanged line · {from}" : $"{count} unchanged lines · {from}–{to}";
}
