using System.Collections.Concurrent;
using CentralGit.Application.Ports;
using CentralGit.Domain.Repositories;

namespace CentralGit.Infrastructure.Git;

/// <summary>
/// Status of many repositories for the switcher. Results are cached briefly and at most four
/// git processes run at once, so opening the switcher over 100 repositories stays light.
/// Untracked files are counted per directory (<c>-unormal</c>), which is much faster on big trees.
/// </summary>
public sealed class RepositoryStatusCache(GitRunner git) : IRepositoryGlances
{
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, (RepositoryGlance Glance, DateTime At)> _cache = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly SemaphoreSlim _gate = new(4);

    public RepositoryGlance? Cached(string path) => _cache.TryGetValue(path, out var entry) ? entry.Glance : null;

    public async Task<RepositoryGlance?> GetAsync(string path, CancellationToken ct)
    {
        if (_cache.TryGetValue(path, out var entry) && DateTime.UtcNow - entry.At < Fresh)
            return entry.Glance;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await git.RunAsync(
                    path,
                    ["status", "--porcelain=v2", "--branch", "-z", "--untracked-files=normal"],
                    ct
                )
                .ConfigureAwait(false);
            if (!result.Succeeded)
                return null;
            var status = StatusParser.Parse(result.StandardOutput);
            var glance = new RepositoryGlance(status.ChangedFileCount, status.Branch.Ahead, status.Branch.Behind);
            _cache[path] = (glance, DateTime.UtcNow);
            return glance;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forget a repository's entry (after we changed it ourselves).</summary>
    public void Invalidate(string path) => _cache.TryRemove(path, out _);
}
