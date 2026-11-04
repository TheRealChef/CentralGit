namespace CentralGit.Core.Diff;

public enum DiffRowKind { HunkHeader, Gap, Lines }

/// <summary>One side of a split row, or the single line of a unified row.</summary>
public sealed record DiffCell(DiffLine Line, IReadOnlyList<TextSpan> Changes);

/// <summary>
/// A row of the diff view. Split rows fill Left/Right (either may be null for a filler);
/// unified rows only use <see cref="Left"/>. <see cref="ChangeBlock"/> numbers each run of
/// changes so the view can jump between them (Alt+Up/Down).
/// </summary>
public sealed record DiffRow(DiffRowKind Kind, DiffCell? Left, DiffCell? Right, string Text, int ChangeBlock);

public static class DiffLayout
{
    public static IReadOnlyList<DiffRow> Split(FileDiff diff) => Build(diff, split: true);

    public static IReadOnlyList<DiffRow> Unified(FileDiff diff) => Build(diff, split: false);

    private static IReadOnlyList<DiffRow> Build(FileDiff diff, bool split)
    {
        var rows = new List<DiffRow>();
        var block = 0;
        int? previousOldEnd = null;

        foreach (var hunk in diff.Hunks)
        {
            if (previousOldEnd is { } end && hunk.OldStart > end)
                rows.Add(new DiffRow(DiffRowKind.Gap, null, null, Gap(hunk.OldStart - end, end, hunk.OldStart - 1), -1));
            else if (previousOldEnd is null && hunk.OldStart > 1)
                rows.Add(new DiffRow(DiffRowKind.Gap, null, null, Gap(hunk.OldStart - 1, 1, hunk.OldStart - 1), -1));

            rows.Add(new DiffRow(DiffRowKind.HunkHeader, null, null, hunk.Header, -1));

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
                while (i < lines.Count && lines[i].Kind == DiffLineKind.Deleted) deleted.Add(lines[i++]);
                while (i < lines.Count && lines[i].Kind == DiffLineKind.Added) added.Add(lines[i++]);

                var removedSpans = new IReadOnlyList<TextSpan>[deleted.Count];
                var addedSpans = new IReadOnlyList<TextSpan>[added.Count];
                for (var k = 0; k < deleted.Count; k++) removedSpans[k] = [];
                for (var k = 0; k < added.Count; k++) addedSpans[k] = [];
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
                        rows.Add(new DiffRow(DiffRowKind.Lines, new DiffCell(deleted[k], removedSpans[k]), null, "", block));
                    for (var k = 0; k < added.Count; k++)
                        rows.Add(new DiffRow(DiffRowKind.Lines, new DiffCell(added[k], addedSpans[k]), null, "", block));
                }

                block++;
            }

            previousOldEnd = hunk.OldStart + hunk.OldCount;
        }

        return rows;
    }

    private static string Gap(int count, int from, int to) =>
        count == 1 ? $"1 unchanged line · {from}" : $"{count} unchanged lines · {from}–{to}";
}
