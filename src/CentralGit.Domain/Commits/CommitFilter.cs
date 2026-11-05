namespace CentralGit.Domain.Commits;

/// <summary>
/// The history filter box. Plain text matches subject, author name/email and hash prefix;
/// <c>author:</c> narrows to people; <c>path:</c> (or anything with a slash) asks git which
/// commits touched that path, across all refs.
/// </summary>
public sealed record CommitFilter(string? Text, string? Author, string? Path)
{
    public bool IsEmpty => Text is null && Author is null && Path is null;

    public static CommitFilter Parse(string? query)
    {
        query = query?.Trim() ?? "";
        if (query.Length == 0)
            return new CommitFilter(null, null, null);

        if (query.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
            return new CommitFilter(null, null, NullIfEmpty(query[5..]));
        if (query.StartsWith("author:", StringComparison.OrdinalIgnoreCase))
            return new CommitFilter(null, NullIfEmpty(query[7..]), null);
        if (!query.Contains(' ') && (query.Contains('/') || query.Contains('\\')))
            return new CommitFilter(null, null, query.Replace('\\', '/'));

        return new CommitFilter(query, null, null);
    }

    /// <summary>In-memory part of the filter (everything except the path).</summary>
    public bool Matches(CommitInfo c)
    {
        if (Author is { } a)
            return Contains(c.Author.Name, a) || Contains(c.Author.Email, a);

        if (Text is not { } t)
            return true;

        return Contains(c.Subject, t)
            || Contains(c.Author.Name, t)
            || Contains(c.Author.Email, t)
            || (t.Length >= 4 && c.Hash.StartsWith(t, StringComparison.OrdinalIgnoreCase))
            || c.Refs.Any(r => Contains(r, t));
    }

    private static bool Contains(string s, string part) => s.Contains(part, StringComparison.CurrentCultureIgnoreCase);

    private static string? NullIfEmpty(string s) => s.Trim() is { Length: > 0 } t ? t : null;
}
