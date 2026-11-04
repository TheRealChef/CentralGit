using CentralGit.Core.Repositories;

namespace CentralGit.App.Services;

public sealed record LocatedRepositories(string? Selected, IReadOnlyList<DiscoveredRepository> Discovered);

/// <summary>
/// Finds repositories under the scan folders from Settings › Repositories, and decides which
/// one to open on launch: an explicit path, else the repository containing the current
/// directory, else the most recently opened one, else the most recently touched one.
/// </summary>
public static class RepositoryLocator
{
    public static IReadOnlyList<DiscoveredRepository> Discover(AppSettings settings, CancellationToken ct = default)
    {
        var options = settings.ToScanOptions();
        return settings.ScanFolders
            .SelectMany(root => RepositoryScanner.Scan(root, options, ct))
            .DistinctBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static LocatedRepositories Locate(string? requestedPath)
    {
        var settings = SettingsStore.Current;
        var discovered = Discover(settings);

        var selected =
            FindEnclosingRepository(requestedPath)
            ?? FindEnclosingRepository(Environment.CurrentDirectory)
            ?? settings.Recent.Select(r => r.Path).FirstOrDefault(IsRepository)
            ?? discovered
                .Where(r => !r.IsWorktree)
                .OrderByDescending(r => LastActivity(r.Path))
                .Select(r => r.Path)
                .FirstOrDefault();

        return new LocatedRepositories(selected, discovered);
    }

    public static bool IsRepository(string path) =>
        Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git"));

    public static string? FindEnclosingRepository(string? start)
    {
        if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
            return null;

        for (var dir = new DirectoryInfo(Path.GetFullPath(start)); dir is not null; dir = dir.Parent)
            if (IsRepository(dir.FullName))
                return dir.FullName;
        return null;
    }

    private static DateTime LastActivity(string repo)
    {
        var index = Path.Combine(repo, ".git", "index");
        return File.Exists(index) ? File.GetLastWriteTimeUtc(index) : Directory.GetLastWriteTimeUtc(repo);
    }
}
