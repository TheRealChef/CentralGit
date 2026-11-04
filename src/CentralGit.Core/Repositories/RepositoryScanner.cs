namespace CentralGit.Core.Repositories;

public sealed record ScanOptions
{
    public int MaxDepth { get; init; } = 3;
    public bool IncludeNested { get; init; }
    public bool IncludeWorktrees { get; init; } = true;

    public IReadOnlySet<string> ExcludedFolderNames { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "node_modules", "bin", "obj", ".vs", "dist", "packages" };
}

public sealed record DiscoveredRepository(string Path, string Name, bool IsWorktree);

/// <summary>
/// Finds Git repositories below the scan folders configured in Settings › Repositories.
/// A folder is a repository when it contains a <c>.git</c> directory, or a <c>.git</c> file (worktree / submodule).
/// </summary>
public static class RepositoryScanner
{
    public static IEnumerable<DiscoveredRepository> Scan(string root, ScanOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new ScanOptions();
        if (!Directory.Exists(root))
            yield break;

        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((Path.GetFullPath(root), 0));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (dir, depth) = pending.Pop();

            var marker = Classify(dir);
            if (marker is not GitMarker.None)
            {
                var isWorktree = marker is GitMarker.File;
                if (!isWorktree || options.IncludeWorktrees)
                    yield return new DiscoveredRepository(dir, System.IO.Path.GetFileName(dir), isWorktree);

                if (!options.IncludeNested)
                    continue;
            }

            if (depth >= options.MaxDepth)
                continue;

            foreach (var child in SafeEnumerate(dir).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = System.IO.Path.GetFileName(child);
                if (name.Equals(".git", StringComparison.OrdinalIgnoreCase) || options.ExcludedFolderNames.Contains(name))
                    continue;
                pending.Push((child, depth + 1));
            }
        }
    }

    private enum GitMarker { None, Directory, File }

    private static GitMarker Classify(string dir)
    {
        var dotGit = System.IO.Path.Combine(dir, ".git");
        if (Directory.Exists(dotGit))
            return GitMarker.Directory;
        return File.Exists(dotGit) ? GitMarker.File : GitMarker.None;
    }

    private static IEnumerable<string> SafeEnumerate(string dir)
    {
        try
        {
            return Directory.EnumerateDirectories(dir, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            }).ToList();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}
