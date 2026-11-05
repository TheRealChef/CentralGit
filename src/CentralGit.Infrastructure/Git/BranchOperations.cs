namespace CentralGit.Infrastructure.Git;

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
    public static IReadOnlyList<string> DeleteRemote(string remote, IEnumerable<string> shortNames) =>
        ["push", "--progress", remote, "--delete", .. shortNames];

    /// <summary>Puts uncommitted changes (untracked files too) in the stash, labelled with why.</summary>
    public static IReadOnlyList<string> StashBeforeSwitch(string target) =>
        ["stash", "push", "--include-untracked", "-m", $"CentralGit: changes left behind when switching to {target}"];
}
