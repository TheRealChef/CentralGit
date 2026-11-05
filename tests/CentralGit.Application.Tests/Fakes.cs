using CentralGit.Application.Git;
using CentralGit.Application.Operations;
using CentralGit.Application.Ports;
using CentralGit.Application.Settings;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Diff;
using CentralGit.Domain.Health;
using CentralGit.Domain.Operations;
using CentralGit.Domain.Repositories;

namespace CentralGit.Application.Tests;

/// <summary>Branches and configuration held in memory.</summary>
internal sealed class FakeBranches : IBranchSource
{
    public List<LocalBranch> Local { get; } = [];
    public List<RemoteBranch> Remote { get; } = [];
    public List<string> Remotes { get; } = [];
    public Dictionary<string, string> UserConfig { get; } = [];
    public Func<string, bool> GitAcceptsName { get; set; } = _ => true;

    public Task<IReadOnlyList<LocalBranch>> GetLocalBranchesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LocalBranch>>(Local);

    public Task<IReadOnlyList<RemoteBranch>> GetRemoteBranchesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RemoteBranch>>(Remote);

    public Task<IReadOnlyList<string>> GetRemotesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Remotes);

    public Task<string?> GetUserConfigAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(UserConfig.GetValueOrDefault(key));

    public Task<bool> IsValidBranchNameAsync(string name, CancellationToken ct = default) =>
        Task.FromResult(GitAcceptsName(name));

    public static LocalBranch Branch(string name, string? upstream = null) => new(name, upstream, 0, 0, false, false);
}

/// <summary>Remembers what was prepared; running succeeds without doing anything.</summary>
internal sealed class RecordingOperations : IOperationSource
{
    public List<GitOperation> Prepared { get; } = [];

    public IPreparedOperation Prepare(GitOperation operation)
    {
        Prepared.Add(operation);
        return new Ready(operation);
    }

    private sealed class Ready(GitOperation operation) : IPreparedOperation
    {
        public string CommandLine => operation.ToString();

        public Task<OperationOutcome> RunAsync(Action<GitProgress> onProgress, CancellationToken cancellationToken) =>
            Task.FromResult(new OperationOutcome(new GitResult([], 0, "", "", TimeSpan.Zero), false, null));
    }
}

internal sealed class FakeHealth : IHealthSource
{
    public BranchRefs Refs { get; set; } = new([], [], null);
    public Dictionary<string, string[]> Merged { get; } = [];
    public List<string> Asked { get; } = [];

    public Task<BranchRefs> ReadBranchRefsAsync(CancellationToken ct = default) => Task.FromResult(Refs);

    public Task<IReadOnlyCollection<string>> GetRefsMergedIntoAsync(string target, CancellationToken ct = default)
    {
        lock (Asked)
            Asked.Add(target);
        return Task.FromResult<IReadOnlyCollection<string>>(Merged.GetValueOrDefault(target) ?? []);
    }

    public Task<RepositoryFacts> ReadFactsAsync(DateTimeOffset now, CancellationToken ct = default) =>
        Task.FromResult(
            new RepositoryFacts(
                new RepositoryStats(10, 2, 1, 100, 0, 1, 0, 1, now, now),
                [new RemoteUrl("origin", "https://example.dev/a.git")],
                "2.50",
                "Ana",
                "ana@example.dev"
            )
        );
}

/// <summary>A working tree whose git answers are scripted; every call is logged.</summary>
internal sealed class FakeWorkingTree : IWorkingTreeSource
{
    public List<string> Calls { get; } = [];
    public GitResult StageAllResult { get; set; } = Ok;
    public string? LastMessage { get; set; }
    public Dictionary<string, LineCounts?> FileLines { get; } = [];

    public static GitResult Ok => new([], 0, "", "", TimeSpan.Zero);

    public static GitResult Failed(string error) => new(["add", "-A"], 128, "", error, TimeSpan.Zero);

    public Task<WorkingTreeStatus> GetStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new WorkingTreeStatus(new BranchStatus("main", null, null, 0, 0), []));

    public Task<FileDiff> DiffAsync(FileChange change, ChangeSide side, CancellationToken ct = default) =>
        Task.FromResult(FileDiff.Empty);

    public Task<IReadOnlyDictionary<string, LineCounts>> NumstatAsync(ChangeSide side, CancellationToken ct = default)
    {
        Calls.Add($"numstat {side}");
        return Task.FromResult<IReadOnlyDictionary<string, LineCounts>>(
            new Dictionary<string, LineCounts> { [$"{side}.cs"] = new(1, 2) }
        );
    }

    public LineCounts? CountLines(string relativePath)
    {
        Calls.Add($"count {relativePath}");
        return FileLines.GetValueOrDefault(relativePath);
    }

    public Task<GitResult> StageAsync(IEnumerable<string> paths, CancellationToken ct = default) => Log("stage");

    public Task<GitResult> StageAllAsync(CancellationToken ct = default)
    {
        Calls.Add("stage all");
        return Task.FromResult(StageAllResult);
    }

    public Task<GitResult> UnstageAsync(IEnumerable<string> paths, bool hasHead, CancellationToken ct = default) =>
        Log("unstage");

    public Task<GitResult> UnstageAllAsync(bool hasHead, CancellationToken ct = default) => Log("unstage all");

    public Task<GitResult> ApplyHunkToIndexAsync(
        FileDiff diff,
        int hunk,
        ChangeSide side,
        CancellationToken ct = default
    ) => Log("apply");

    public Task<GitResult> DiscardHunkAsync(FileDiff diff, int hunk, CancellationToken ct = default) =>
        Log("discard hunk");

    public Task<GitResult> DiscardAsync(FileChange change, CancellationToken ct = default) => Log("discard");

    public Task<GitResult> CommitAsync(string message, bool amend, CancellationToken ct = default) =>
        Log($"commit{(amend ? " --amend" : "")}: {message}");

    public Task<string?> LastMessageAsync(CancellationToken ct = default) => Task.FromResult(LastMessage);

    private Task<GitResult> Log(string call)
    {
        Calls.Add(call);
        return Task.FromResult(Ok);
    }
}

internal sealed class MemorySettings(AppSettings initial) : ISettingsStore
{
    public AppSettings Current { get; private set; } = initial;

    public void Update(Func<AppSettings, AppSettings> change) => Current = change(Current);
}

/// <summary>A disk with a few repositories on it.</summary>
internal sealed class FakeFileSystem : IRepositoryFileSystem
{
    public Dictionary<string, DateTime> Repositories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string CurrentDirectory { get; set; } = @"C:\Windows\System32";

    public IEnumerable<DiscoveredRepository> Scan(string root, ScanOptions options, CancellationToken ct = default) =>
        Repositories
            .Keys.Where(p => p.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            .Select(p => new DiscoveredRepository(p, Path.GetFileName(p), false));

    public bool IsRepository(string path) => Repositories.ContainsKey(path);

    public bool FolderExists(string path) => true;

    public string? FindEnclosingRepository(string? start) =>
        start is null
            ? null
            : Repositories
                .Keys.Where(r => start.StartsWith(r, StringComparison.OrdinalIgnoreCase))
                .MaxBy(r => r.Length);

    public DateTime LastActivity(string repositoryPath) => Repositories[repositoryPath];
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
