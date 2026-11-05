using CentralGit.Application.Git;
using CentralGit.Application.Operations;
using CentralGit.Application.Ports;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Diff;
using CentralGit.Domain.Health;

namespace CentralGit.Application.Tests;

/// <summary>A repository nobody reads from: only its path and name are real.</summary>
internal sealed class StubRepository(string path) : IGitRepository
{
    public string Path => path;
    public string Name => System.IO.Path.GetFileName(path);

    private static Task<T> No<T>() => throw new NotSupportedException("The stub repository has no git behind it.");

    public Task<WorkingTreeStatus> GetStatusAsync(CancellationToken ct = default) => No<WorkingTreeStatus>();

    public Task<string?> GetHeadHashAsync(CancellationToken ct = default) => No<string?>();

    public Task<SyncSets> GetSyncSetsAsync(string? upstream, CancellationToken ct = default) => No<SyncSets>();

    public Task<IReadOnlyList<CommitInfo>> GetHistoryAsync(
        int maxCount,
        int skip = 0,
        CancellationToken ct = default
    ) => No<IReadOnlyList<CommitInfo>>();

    public Task<IReadOnlySet<string>> GetCommitsTouchingAsync(string pathSpec, CancellationToken ct = default) =>
        No<IReadOnlySet<string>>();

    public Task<CommitDetails> GetCommitDetailsAsync(string hash, CancellationToken ct = default) =>
        No<CommitDetails>();

    public Task<FileDiff> GetFileDiffAsync(
        string baseRevision,
        string revision,
        ChangedFile file,
        CancellationToken ct = default
    ) => No<FileDiff>();

    public Task<IReadOnlyList<LocalBranch>> GetLocalBranchesAsync(CancellationToken ct = default) =>
        No<IReadOnlyList<LocalBranch>>();

    public Task<IReadOnlyList<RemoteBranch>> GetRemoteBranchesAsync(CancellationToken ct = default) =>
        No<IReadOnlyList<RemoteBranch>>();

    public Task<IReadOnlyList<string>> GetRemotesAsync(CancellationToken ct = default) => No<IReadOnlyList<string>>();

    public Task<string?> GetUserConfigAsync(string key, CancellationToken ct = default) => No<string?>();

    public Task<bool> IsValidBranchNameAsync(string name, CancellationToken ct = default) => No<bool>();

    public Task<FileDiff> DiffAsync(FileChange change, ChangeSide side, CancellationToken ct = default) =>
        No<FileDiff>();

    public Task<IReadOnlyDictionary<string, LineCounts>> NumstatAsync(
        ChangeSide side,
        CancellationToken ct = default
    ) => No<IReadOnlyDictionary<string, LineCounts>>();

    public LineCounts? CountLines(string relativePath) => throw new NotSupportedException();

    public Task<GitResult> StageAsync(IEnumerable<string> paths, CancellationToken ct = default) => No<GitResult>();

    public Task<GitResult> StageAllAsync(CancellationToken ct = default) => No<GitResult>();

    public Task<GitResult> UnstageAsync(IEnumerable<string> paths, bool hasHead, CancellationToken ct = default) =>
        No<GitResult>();

    public Task<GitResult> UnstageAllAsync(bool hasHead, CancellationToken ct = default) => No<GitResult>();

    public Task<GitResult> ApplyHunkToIndexAsync(
        FileDiff diff,
        int hunk,
        ChangeSide side,
        CancellationToken ct = default
    ) => No<GitResult>();

    public Task<GitResult> DiscardHunkAsync(FileDiff diff, int hunk, CancellationToken ct = default) => No<GitResult>();

    public Task<GitResult> DiscardAsync(FileChange change, CancellationToken ct = default) => No<GitResult>();

    public Task<GitResult> CommitAsync(string message, bool amend, CancellationToken ct = default) => No<GitResult>();

    public Task<string?> LastMessageAsync(CancellationToken ct = default) => No<string?>();

    public Task<BranchRefs> ReadBranchRefsAsync(CancellationToken ct = default) => No<BranchRefs>();

    public Task<IReadOnlyCollection<string>> GetRefsMergedIntoAsync(string target, CancellationToken ct = default) =>
        No<IReadOnlyCollection<string>>();

    public Task<RepositoryFacts> ReadFactsAsync(DateTimeOffset now, CancellationToken ct = default) =>
        No<RepositoryFacts>();

    public IPreparedOperation Prepare(GitOperation operation) => throw new NotSupportedException();
}
