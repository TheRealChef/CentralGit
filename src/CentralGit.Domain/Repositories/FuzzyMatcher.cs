namespace CentralGit.Domain.Repositories;

/// <summary>
/// Subsequence matching for the repository switcher (Ctrl+P): every query character must
/// appear in order. Scores favor matches at word starts ("cg" → "central-git"), consecutive
/// runs, and matches early in the text. Higher is better; null means no match.
/// </summary>
public static class FuzzyMatcher
{
    public static int? Score(string query, string text)
    {
        query = query.Trim();
        if (query.Length == 0)
            return 0;
        if (query.Length > text.Length)
            return null;

        var score = 0;
        var t = 0;
        var previousMatch = -2;
        foreach (var q in query)
        {
            if (q == ' ')
                continue;

            // Prefer the next word-start occurrence of this character, else the next occurrence.
            var found = -1;
            for (var i = t; i < text.Length; i++)
            {
                if (char.ToLowerInvariant(text[i]) != char.ToLowerInvariant(q))
                    continue;
                if (found < 0)
                    found = i;
                if (i == previousMatch + 1)
                {
                    found = i; // continuing a run beats jumping to a later word start
                    break;
                }
                if (IsWordStart(text, i))
                {
                    found = i;
                    break;
                }
            }
            if (found < 0)
                return null;

            score += 1;
            if (found == previousMatch + 1)
                score += 5;
            if (IsWordStart(text, found))
                score += 8;
            if (found == 0)
                score += 4;
            previousMatch = found;
            t = found + 1;
        }

        // Shorter texts and earlier matches rank higher among equals.
        return score * 100 - text.Length;
    }

    private static bool IsWordStart(string s, int i) =>
        i == 0 || s[i - 1] is '-' or '_' or '.' or ' ' or '/' or '\\' || (char.IsUpper(s[i]) && char.IsLower(s[i - 1]));
}
