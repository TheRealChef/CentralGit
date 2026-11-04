using CentralGit.Core.Repositories;

namespace CentralGit.App.Services;

public sealed record LocatedRepositories(string? Selected, IReadOnlyList<DiscoveredRepository> Discovered);

/// <summary>
/// Decides which repository to open on launch until Settings › Repositories is persisted:
/// an explicit path, else the repository containing the current directory, else the most
/// recently touched repository under the default scan folders.
/// </summary>
public static class RepositoryLocator
{
    public static IReadOnlyList<string> DefaultScanFolders { get; } =
    [
        @"D:\Source\Repos",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos"),
    ];

    public static LocatedRepositories Locate(string? requestedPath)
    {
        var discovered = DefaultScanFolders
            .SelectMany(root => RepositoryScanner.Scan(root))
            .DistinctBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selected =
            FindEnclosingRepository(requestedPath)
            ?? FindEnclosingRepository(Environment.CurrentDirectory)
            ?? discovered
                .Where(r => !r.IsWorktree)
                .OrderByDescending(r => LastActivity(r.Path))
                .Select(r => r.Path)
                .FirstOrDefault();

        return new LocatedRepositories(selected, discovered);
    }

    private static string? FindEnclosingRepository(string? start)
    {
        if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
            return null;

        for (var dir = new DirectoryInfo(Path.GetFullPath(start)); dir is not null; dir = dir.Parent)
        {
            var dotGit = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(dotGit) || File.Exists(dotGit))
                return dir.FullName;
        }
        return null;
    }

    private static DateTime LastActivity(string repo)
    {
        var index = Path.Combine(repo, ".git", "index");
        return File.Exists(index) ? File.GetLastWriteTimeUtc(index) : Directory.GetLastWriteTimeUtc(repo);
    }
}
