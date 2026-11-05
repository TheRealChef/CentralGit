using CentralGit.Application.Ports;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Diff;

namespace CentralGit.Application.Features.History;

/// <summary>Where the repository stands before history is drawn: status, HEAD, and what's in or out of sync.</summary>
public sealed record HistoryState(WorkingTreeStatus Status, string? Head, SyncSets Sync);

/// <summary>One page of history; <see cref="HasMore"/> when the page was full.</summary>
public sealed record HistoryPage(IReadOnlyList<CommitInfo> Commits, bool HasMore);

public interface IHistoryService
{
    Task<HistoryState> ReadStateAsync(CancellationToken ct = default);
    Task<HistoryPage> ReadPageAsync(int pageSize, int skip = 0, CancellationToken ct = default);

    /// <summary>For the <c>path:</c> filter: every commit, on any ref, that touches the path.</summary>
    Task<IReadOnlySet<string>> FindCommitsTouchingAsync(string path, CancellationToken ct = default);

    Task<CommitDetails> GetCommitAsync(string hash, CancellationToken ct = default);

    /// <summary>One file of a commit, against its first parent (or the empty tree for a root commit).</summary>
    Task<FileDiff> GetFileDiffAsync(CommitDetails commit, ChangedFile file, CancellationToken ct = default);
}

public sealed class HistoryService(IHistorySource repository) : IHistoryService
{
    public async Task<HistoryState> ReadStateAsync(CancellationToken ct = default)
    {
        var statusTask = repository.GetStatusAsync(ct);
        var headTask = repository.GetHeadHashAsync(ct);
        var status = await statusTask.ConfigureAwait(false);
        var head = await headTask.ConfigureAwait(false);
        var sync = await repository.GetSyncSetsAsync(status.Branch.Upstream, ct).ConfigureAwait(false);
        return new HistoryState(status, head, sync);
    }

    public async Task<HistoryPage> ReadPageAsync(int pageSize, int skip = 0, CancellationToken ct = default)
    {
        var commits = await repository.GetHistoryAsync(pageSize, skip, ct).ConfigureAwait(false);
        return new HistoryPage(commits, commits.Count >= pageSize);
    }

    public Task<IReadOnlySet<string>> FindCommitsTouchingAsync(string path, CancellationToken ct = default) =>
        repository.GetCommitsTouchingAsync(path, ct);

    public Task<CommitDetails> GetCommitAsync(string hash, CancellationToken ct = default) =>
        repository.GetCommitDetailsAsync(hash, ct);

    public Task<FileDiff> GetFileDiffAsync(CommitDetails commit, ChangedFile file, CancellationToken ct = default) =>
        repository.GetFileDiffAsync(commit.DiffBase, commit.Hash, file, ct);
}
