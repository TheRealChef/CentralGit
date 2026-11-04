using CentralGit.Core.Git;

namespace CentralGit.Core.Operations;

/// <summary>Command lines for switching, creating, renaming and deleting branches.</summary>
public static class BranchOperations
{
    /// <summary>Switches to a local branch. Uncommitted changes come along when they don't conflict.</summary>
    public static IReadOnlyList<string> Switch(string branch) => ["switch", "--progress", branch];

    /// <summary>Creates a local branch that tracks <paramref name="remoteBranch"/> (e.g. origin/feature) and switches to it.</summary>
    public static IReadOnlyList<string> Track(string remoteBranch) => ["switch", "--progress", "--track", remoteBranch];

    /// <summary>Creates a branch at <paramref name="startPoint"/> (default HEAD) and switches to it.</summary>
    public static IReadOnlyList<string> Create(string name, string? startPoint = null) =>
        startPoint is null ? ["switch", "--progress", "-c", name] : ["switch", "--progress", "-c", name, startPoint];

    public static IReadOnlyList<string> Rename(string from, string to) => ["branch", "-m", from, to];

    /// <summary><c>-d</c> refuses to delete unmerged work; <c>-D</c> (force) doesn't. Force only after asking.</summary>
    public static IReadOnlyList<string> Delete(string name, bool force) => ["branch", force ? "-D" : "-d", name];

    /// <summary>Deletes several local branches at once (cleanup); the caller has checked or confirmed each.</summary>
    public static IReadOnlyList<string> DeleteMany(IEnumerable<string> names) => ["branch", "-D", .. names];

    /// <summary>Deletes branches on a remote server — affects everyone using it.</summary>
    public static IReadOnlyList<string> DeleteRemote(string remote, IEnumerable<string> shortNames) => ["push", "--progress", remote, "--delete", .. shortNames];

    /// <summary>Puts uncommitted changes (untracked files too) in the stash, labelled with why.</summary>
    public static IReadOnlyList<string> StashBeforeSwitch(string target) =>
        ["stash", "push", "--include-untracked", "-m", $"CentralGit: changes left behind when switching to {target}"];

    /// <summary>
    /// Asks git whether <paramref name="name"/> is a valid branch name. Returns null when it is,
    /// otherwise a sentence saying what's wrong.
    /// </summary>
    public static async Task<string?> ValidateNameAsync(GitRunner git, string repositoryPath, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Type a name for the branch.";
        if (name.Any(char.IsWhiteSpace))
            return "Branch names can't contain spaces — use - or / instead.";
        var result = await git.RunAsync(repositoryPath, ["check-ref-format", "--branch", name], ct).ConfigureAwait(false);
        return result.Succeeded ? null : $"“{name}” isn't a valid branch name (no .., ~, ^, :, ?, *, [ or trailing . or /).";
    }

    /// <summary>A branch-name-ish version of free text: "Fix login bug" → "fix-login-bug".</summary>
    public static string Suggest(string text)
    {
        var chars = text.Trim().Select(c => char.IsLetterOrDigit(c) || c is '/' or '-' or '_' or '.' ? char.ToLowerInvariant(c) : '-').ToArray();
        var name = new string(chars);
        while (name.Contains("--", StringComparison.Ordinal))
            name = name.Replace("--", "-", StringComparison.Ordinal);
        return name.Trim('-', '/', '.');
    }
}
