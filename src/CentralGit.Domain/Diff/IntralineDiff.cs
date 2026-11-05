namespace CentralGit.Domain.Diff;

public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>
/// Word-level differences between an old and a new version of a line, so the diff view can
/// emphasize just what changed. Token LCS; gives up (returns no spans) when lines are too
/// different for highlighting to help.
/// </summary>
public static class IntralineDiff
{
    private const int MaxCells = 60_000;

    public static (IReadOnlyList<TextSpan> Removed, IReadOnlyList<TextSpan> Added) Compute(
        string oldText,
        string newText
    )
    {
        var a = Tokenize(oldText);
        var b = Tokenize(newText);
        if (a.Count == 0 || b.Count == 0 || (long)a.Count * b.Count > MaxCells)
            return ([], []);

        // LCS table over tokens.
        var lcs = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
        for (var j = b.Count - 1; j >= 0; j--)
            lcs[i, j] = a[i].Text == b[j].Text ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var removed = new List<TextSpan>();
        var added = new List<TextSpan>();
        int x = 0,
            y = 0,
            commonChars = 0;
        while (x < a.Count && y < b.Count)
        {
            if (a[x].Text == b[y].Text)
            {
                commonChars += a[x].Text.Length;
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
                Add(removed, a[x++]);
            else
                Add(added, b[y++]);
        }
        while (x < a.Count)
            Add(removed, a[x++]);
        while (y < b.Count)
            Add(added, b[y++]);

        // Mostly rewritten lines: the line background already says it all.
        var meaningful = oldText.Trim().Length + newText.Trim().Length;
        if (meaningful > 0 && commonChars * 2.0 / Math.Max(oldText.Length + newText.Length, 1) < 0.3)
            return ([], []);

        return (removed, added);
    }

    private static void Add(List<TextSpan> spans, Token t)
    {
        if (spans.Count > 0 && spans[^1].End == t.Start)
            spans[^1] = new TextSpan(spans[^1].Start, spans[^1].Length + t.Text.Length);
        else
            spans.Add(new TextSpan(t.Start, t.Text.Length));
    }

    private readonly record struct Token(int Start, string Text);

    /// <summary>Identifier runs, whitespace runs, and single punctuation characters.</summary>
    private static List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < s.Length)
        {
            var start = i;
            if (char.IsLetterOrDigit(s[i]) || s[i] == '_')
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_'))
                    i++;
            else if (char.IsWhiteSpace(s[i]))
                while (i < s.Length && char.IsWhiteSpace(s[i]))
                    i++;
            else
                i++;
            tokens.Add(new Token(start, s[start..i]));
        }
        return tokens;
    }
}
