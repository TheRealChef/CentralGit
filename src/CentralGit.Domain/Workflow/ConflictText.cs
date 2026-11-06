using System.Text;

namespace CentralGit.Domain.Workflow;

/// <summary>A piece of a conflicted file: text both sides agree on, or one conflict.</summary>
public abstract record ConflictSegment;

/// <summary>Lines outside any conflict, line endings included.</summary>
public sealed record CommonSegment(string Text) : ConflictSegment;

/// <summary>
/// One <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> … <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> block. Ours is the checked-out side (HEAD), theirs the
/// one coming in; <see cref="Base"/> is only there with <c>merge.conflictStyle=diff3/zdiff3</c>.
/// </summary>
public sealed record ConflictHunk(
    int Index,
    string Ours,
    string? Base,
    string Theirs,
    string OursLabel,
    string TheirsLabel
) : ConflictSegment;

public enum ConflictChoice
{
    Unresolved,
    Ours,
    Theirs,
    OursThenTheirs,
    TheirsThenOurs,
}

/// <summary>A working-tree file with conflict markers, split into segments and put back together.</summary>
public sealed record ConflictText(IReadOnlyList<ConflictSegment> Segments)
{
    private const int MarkerSize = 7;

    public IReadOnlyList<ConflictHunk> Conflicts { get; } = Segments.OfType<ConflictHunk>().ToList();

    /// <summary>
    /// Splits <paramref name="text"/> at the markers. An unterminated block (a marker typed by hand,
    /// or a file someone half-fixed) is kept as plain text rather than guessed at.
    /// </summary>
    public static ConflictText Parse(string text)
    {
        var segments = new List<ConflictSegment>();
        var common = new StringBuilder();
        var lines = SplitKeepingEndings(text);
        var i = 0;
        while (i < lines.Count)
        {
            if (!IsMarker(lines[i], '<'))
            {
                common.Append(lines[i++]);
                continue;
            }
            if (TryReadConflict(lines, i, segments.OfType<ConflictHunk>().Count(), out var hunk, out var next))
            {
                if (common.Length > 0)
                {
                    segments.Add(new CommonSegment(common.ToString()));
                    common.Clear();
                }
                segments.Add(hunk);
                i = next;
            }
            else
            {
                common.Append(lines[i++]);
            }
        }
        if (common.Length > 0)
            segments.Add(new CommonSegment(common.ToString()));
        return new ConflictText(segments);
    }

    /// <summary>The file with each conflict replaced by the chosen side(s).</summary>
    public string Resolve(IReadOnlyList<ConflictChoice> choices)
    {
        if (choices.Count != Conflicts.Count || choices.Any(c => c == ConflictChoice.Unresolved))
            throw new ArgumentException("Every conflict needs a choice.", nameof(choices));
        var sb = new StringBuilder();
        foreach (var segment in Segments)
        {
            switch (segment)
            {
                case CommonSegment c:
                    sb.Append(c.Text);
                    break;
                case ConflictHunk h:
                    sb.Append(
                        choices[h.Index] switch
                        {
                            ConflictChoice.Ours => h.Ours,
                            ConflictChoice.Theirs => h.Theirs,
                            ConflictChoice.OursThenTheirs => Join(h.Ours, h.Theirs),
                            _ => Join(h.Theirs, h.Ours),
                        }
                    );
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Two blocks one after the other, making sure the first ends its last line.</summary>
    private static string Join(string first, string second)
    {
        if (first.Length == 0 || second.Length == 0 || first.EndsWith('\n'))
            return first + second;
        return first + (second.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n") + second;
    }

    private static bool TryReadConflict(List<string> lines, int start, int index, out ConflictHunk hunk, out int next)
    {
        hunk = null!;
        next = start;
        var ours = new StringBuilder();
        StringBuilder? @base = null;
        var theirs = new StringBuilder();
        var current = ours;
        var sawSeparator = false;
        for (var i = start + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!sawSeparator && IsMarker(line, '|'))
            {
                current = @base = new StringBuilder();
            }
            else if (!sawSeparator && IsMarker(line, '='))
            {
                sawSeparator = true;
                current = theirs;
            }
            else if (sawSeparator && IsMarker(line, '>'))
            {
                hunk = new ConflictHunk(
                    index,
                    ours.ToString(),
                    @base?.ToString(),
                    theirs.ToString(),
                    Label(lines[start]),
                    Label(line)
                );
                next = i + 1;
                return true;
            }
            else if (IsMarker(line, '<'))
            {
                return false; // a new block before this one closed: not a conflict we understand
            }
            else
            {
                current.Append(line);
            }
        }
        return false;
    }

    /// <summary>Seven marker characters at the start of the line, then the end of the line or a space.</summary>
    private static bool IsMarker(string line, char c)
    {
        if (line.Length < MarkerSize)
            return false;
        for (var i = 0; i < MarkerSize; i++)
            if (line[i] != c)
                return false;
        return line.Length == MarkerSize || line[MarkerSize] is ' ' or '\r' or '\n';
    }

    private static string Label(string markerLine) => markerLine[MarkerSize..].Trim();

    private static List<string> SplitKeepingEndings(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
                continue;
            lines.Add(text[start..(i + 1)]);
            start = i + 1;
        }
        if (start < text.Length)
            lines.Add(text[start..]);
        return lines;
    }
}
