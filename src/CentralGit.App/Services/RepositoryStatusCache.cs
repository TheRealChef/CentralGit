using System.Collections.Concurrent;
using CentralGit.Core.Git;

namespace CentralGit.App.Services;

/// <summary>A repository's state at a glance: uncommitted files and ahead/behind its upstream.</summary>
public sealed record RepositoryGlance(int ChangedFiles, int Ahead, int Behind);

/// <summary>
/// Status of many repositories for the switcher. Results are cached briefly and at most four
/// git processes run at once, so opening the switcher over 100 repositories stays light.
/// Untracked files are counted per directory (<c>-unormal</c>), which is much faster on big trees.
/// </summary>
public static class RepositoryStatusCache
{
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, (RepositoryGlance Glance, DateTime At)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(4);
    private static readonly GitRunner Git = new();

    public static RepositoryGlance? Cached(string path) =>
        Cache.TryGetValue(path, out var entry) ? entry.Glance : null;

    public static async Task<RepositoryGlance?> GetAsync(string path, CancellationToken ct)
    {
        if (Cache.TryGetValue(path, out var entry) && DateTime.UtcNow - entry.At < Fresh)
            return entry.Glance;

        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await Git.RunAsync(path, ["status", "--porcelain=v2", "--branch", "-z", "--untracked-files=normal"], ct).ConfigureAwait(false);
            if (!result.Succeeded)
                return null;
            var status = WorkingTreeStatus.Parse(result.StandardOutput);
            var glance = new RepositoryGlance(status.ChangedFileCount, status.Branch.Ahead, status.Branch.Behind);
            Cache[path] = (glance, DateTime.UtcNow);
            return glance;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Forget a repository's entry (after we changed it ourselves).</summary>
    public static void Invalidate(string path) => Cache.TryRemove(path, out _);
}
