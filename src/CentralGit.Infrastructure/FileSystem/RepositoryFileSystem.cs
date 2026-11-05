using CentralGit.Application.Ports;
using CentralGit.Domain.Repositories;

namespace CentralGit.Infrastructure.FileSystem;

/// <summary>Repositories as folders: a <c>.git</c> directory, or a <c>.git</c> file (worktree / submodule).</summary>
public sealed class RepositoryFileSystem : IRepositoryFileSystem
{
    public IEnumerable<DiscoveredRepository> Scan(string root, ScanOptions options, CancellationToken ct = default) =>
        RepositoryScanner.Scan(root, options, ct);

    public bool IsRepository(string path) =>
        Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git"));

    public bool FolderExists(string path) => Directory.Exists(path);

    public string? FindEnclosingRepository(string? start)
    {
        if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
            return null;

        for (var dir = new DirectoryInfo(Path.GetFullPath(start)); dir is not null; dir = dir.Parent)
            if (IsRepository(dir.FullName))
                return dir.FullName;
        return null;
    }

    public DateTime LastActivity(string repositoryPath)
    {
        var index = Path.Combine(repositoryPath, ".git", "index");
        return File.Exists(index) ? File.GetLastWriteTimeUtc(index) : Directory.GetLastWriteTimeUtc(repositoryPath);
    }

    public string CurrentDirectory => Environment.CurrentDirectory;
}
