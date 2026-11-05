namespace CentralGit.Domain.Branches;

/// <summary>What makes a branch name usable, before git's own <c>check-ref-format</c> has its say.</summary>
public static class BranchNames
{
    /// <summary>A sentence saying what's wrong with <paramref name="name"/>, or null if git should judge it.</summary>
    public static string? Precheck(string name) =>
        string.IsNullOrWhiteSpace(name) ? "Type a name for the branch."
        : name.Any(char.IsWhiteSpace) ? "Branch names can't contain spaces — use - or / instead."
        : null;

    /// <summary>What to say when git rejects a name.</summary>
    public static string Invalid(string name) =>
        $"“{name}” isn't a valid branch name (no .., ~, ^, :, ?, *, [ or trailing . or /).";

    /// <summary>A branch-name-ish version of free text: "Fix login bug" → "fix-login-bug".</summary>
    public static string Suggest(string text)
    {
        var chars = text.Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is '/' or '-' or '_' or '.' ? char.ToLowerInvariant(c) : '-')
            .ToArray();
        var name = new string(chars);
        while (name.Contains("--", StringComparison.Ordinal))
            name = name.Replace("--", "-", StringComparison.Ordinal);
        return name.Trim('-', '/', '.');
    }
}
