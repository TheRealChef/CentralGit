using CentralGit.Application.Features.Branches;
using CentralGit.Application.Features.Changes;
using CentralGit.Application.Features.Compare;
using CentralGit.Application.Features.Health;
using CentralGit.Application.Features.History;
using CentralGit.Application.Features.Hosting;
using CentralGit.Application.Features.Recovery;
using CentralGit.Application.Features.Sync;
using CentralGit.Application.Features.Workflow;
using CentralGit.Application.Ports;
using CentralGit.Application.Settings;
using CentralGit.Domain.Repositories;

namespace CentralGit.Application.Features.Repositories;

/// <summary>The feature services of one open repository. Opening another repository makes a new session.</summary>
public sealed class RepositorySession(IGitRepository repository, TimeProvider time, IHostingProvider? hosting = null)
{
    public string Path => repository.Path;
    public string Name => repository.Name;

    public IHistoryService History { get; } = new HistoryService(repository);
    public IChangesService Changes { get; } = new ChangesService(repository);
    public IBranchService Branches { get; } = new BranchService(repository, repository);
    public ISyncService Sync { get; } = new SyncService(repository, repository);
    public IHealthService Health { get; } = new HealthService(repository, time);
    public IRecoveryService Recovery { get; } = new RecoveryService(repository, repository);
    public ICompareService Compare { get; } = new CompareService(repository);
    public ICommitActionService Actions { get; } = new CommitActionService(repository, repository);
    public ITagService Tags { get; } = new TagService(repository, repository);
    public IStashService Stashes { get; } = new StashService(repository, repository, repository);
    public IBlameService Blame { get; } = new BlameService(repository);
    public IHostingService Hosting { get; } = new HostingService(repository, hosting ?? new NoHostingProvider());

    /// <summary>Keeps git's commit-graph cache fresh (at most daily) so big histories page quickly.</summary>
    public Task<bool> RefreshCommitGraphAsync(CancellationToken ct = default) =>
        repository.RefreshCommitGraphAsync(TimeSpan.FromDays(1), ct);
}

public sealed record LocatedRepositories(string? Selected, IReadOnlyList<DiscoveredRepository> Discovered);

public interface IRepositoryService
{
    /// <summary>
    /// Finds repositories under the scan folders and decides which one to open on launch: an explicit path,
    /// else the repository containing the current directory, else the most recently opened, else the most recently touched.
    /// </summary>
    LocatedRepositories Locate(string? requestedPath);

    /// <summary>Repositories under the scan folders from Settings › Repositories.</summary>
    IReadOnlyList<DiscoveredRepository> Discover(AppSettings settings, CancellationToken ct = default);

    bool IsRepository(string path);

    /// <summary>Whether a scan folder is still there (a removed drive or deleted folder shows as missing).</summary>
    bool FolderExists(string path);

    /// <summary>Opens a repository and remembers it as recently opened.</summary>
    RepositorySession Open(string path);

    /// <summary>For the switcher: instant from an earlier look, or null.</summary>
    RepositoryGlance? CachedGlance(string path);

    Task<RepositoryGlance?> GlanceAsync(string path, CancellationToken ct);
}

public sealed class RepositoryService(
    IGitRepositoryFactory repositories,
    IRepositoryFileSystem files,
    IRepositoryGlances glances,
    ISettingsStore settings,
    TimeProvider time,
    IHostingProvider hosting
) : IRepositoryService
{
    public IReadOnlyList<DiscoveredRepository> Discover(AppSettings settings, CancellationToken ct = default)
    {
        var options = settings.ToScanOptions();
        return settings
            .ScanFolders.SelectMany(root => files.Scan(root, options, ct))
            .DistinctBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public LocatedRepositories Locate(string? requestedPath)
    {
        var current = settings.Current;
        var discovered = Discover(current);

        var selected =
            files.FindEnclosingRepository(requestedPath)
            ?? files.FindEnclosingRepository(files.CurrentDirectory)
            ?? current.Recent.Select(r => r.Path).FirstOrDefault(files.IsRepository)
            ?? discovered
                .Where(r => !r.IsWorktree)
                .OrderByDescending(r => files.LastActivity(r.Path))
                .Select(r => r.Path)
                .FirstOrDefault();

        return new LocatedRepositories(selected, discovered);
    }

    public bool IsRepository(string path) => files.IsRepository(path);

    public bool FolderExists(string path) => files.FolderExists(path);

    public RepositorySession Open(string path)
    {
        var session = new RepositorySession(repositories.Open(path), time, hosting);
        settings.Update(s => s.WithOpened(path, time.GetLocalNow()));
        return session;
    }

    public RepositoryGlance? CachedGlance(string path) => glances.Cached(path);

    public Task<RepositoryGlance?> GlanceAsync(string path, CancellationToken ct) => glances.GetAsync(path, ct);
}
