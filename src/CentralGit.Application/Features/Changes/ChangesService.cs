using CentralGit.Application.Git;
using CentralGit.Application.Ports;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Diff;

namespace CentralGit.Application.Features.Changes;

/// <summary>Line counts for the Changes lists: per side of the index, and for untracked files read from disk.</summary>
public sealed record ChangeCounts(
    IReadOnlyDictionary<string, LineCounts> Unstaged,
    IReadOnlyDictionary<string, LineCounts> Staged,
    IReadOnlyDictionary<string, LineCounts?> Untracked
);

/// <summary>A commit attempt: <see cref="StagingFailed"/> when staging everything first didn't work.</summary>
public sealed record CommitOutcome(GitResult Result, bool StagingFailed);

public interface IChangesService
{
    Task<WorkingTreeStatus> ReadStatusAsync(CancellationToken ct = default);

    /// <summary>Two numstat calls plus a line count for each untracked file (git has no diff for them).</summary>
    Task<ChangeCounts> ReadCountsAsync(IEnumerable<FileChange> untracked, CancellationToken ct = default);

    Task<FileDiff> DiffAsync(FileChange change, ChangeSide side, CancellationToken ct = default);

    Task<GitResult> StageAsync(IEnumerable<string> paths);
    Task<GitResult> StageAllAsync();
    Task<GitResult> UnstageAsync(IEnumerable<string> paths, bool hasHead);
    Task<GitResult> UnstageAllAsync(bool hasHead);
    Task<GitResult> ApplyHunkToIndexAsync(FileDiff diff, int hunk, ChangeSide side);
    Task<GitResult> DiscardHunkAsync(FileDiff diff, int hunk);

    /// <summary>Stages (unstaged side) or unstages (staged side) only the chosen lines of a hunk.</summary>
    Task<GitResult> ApplyLinesToIndexAsync(FileDiff diff, int hunk, IReadOnlySet<int> lines, ChangeSide side);

    /// <summary>Throws away only the chosen unstaged lines of a hunk. Destructive: ask first.</summary>
    Task<GitResult> DiscardLinesAsync(FileDiff diff, int hunk, IReadOnlySet<int> lines);
    Task<GitResult> DiscardAsync(FileChange change);

    /// <summary>
    /// Commits the index. With <paramref name="stageEverything"/> (nothing staged yet) all changes are
    /// staged first — the composer's "commit everything" path.
    /// </summary>
    Task<CommitOutcome> CommitAsync(string summary, string body, bool amend, bool stageEverything);

    /// <summary>HEAD's message split for the composer, for amending; null before the first commit.</summary>
    Task<(string Summary, string Body)?> LastMessageAsync();
}

public sealed class ChangesService(IWorkingTreeSource repository) : IChangesService
{
    public Task<WorkingTreeStatus> ReadStatusAsync(CancellationToken ct = default) => repository.GetStatusAsync(ct);

    public async Task<ChangeCounts> ReadCountsAsync(IEnumerable<FileChange> untracked, CancellationToken ct = default)
    {
        var unstaged = await repository.NumstatAsync(ChangeSide.Unstaged, ct).ConfigureAwait(false);
        var staged = await repository.NumstatAsync(ChangeSide.Staged, ct).ConfigureAwait(false);
        var files = untracked
            .Where(f => f.Staged == ChangeKind.Untracked)
            .DistinctBy(f => f.Path)
            .ToDictionary(f => f.Path, f => repository.CountLines(f.Path));
        return new ChangeCounts(unstaged, staged, files);
    }

    public Task<FileDiff> DiffAsync(FileChange change, ChangeSide side, CancellationToken ct = default) =>
        repository.DiffAsync(change, side, ct);

    public Task<GitResult> StageAsync(IEnumerable<string> paths) => repository.StageAsync(paths);

    public Task<GitResult> StageAllAsync() => repository.StageAllAsync();

    public Task<GitResult> UnstageAsync(IEnumerable<string> paths, bool hasHead) =>
        repository.UnstageAsync(paths, hasHead);

    public Task<GitResult> UnstageAllAsync(bool hasHead) => repository.UnstageAllAsync(hasHead);

    public Task<GitResult> ApplyHunkToIndexAsync(FileDiff diff, int hunk, ChangeSide side) =>
        repository.ApplyHunkToIndexAsync(diff, hunk, side);

    public Task<GitResult> DiscardHunkAsync(FileDiff diff, int hunk) => repository.DiscardHunkAsync(diff, hunk);

    public Task<GitResult> ApplyLinesToIndexAsync(FileDiff diff, int hunk, IReadOnlySet<int> lines, ChangeSide side) =>
        repository.ApplyLinesToIndexAsync(diff, hunk, lines, side);

    public Task<GitResult> DiscardLinesAsync(FileDiff diff, int hunk, IReadOnlySet<int> lines) =>
        repository.DiscardLinesAsync(diff, hunk, lines);

    public Task<GitResult> DiscardAsync(FileChange change) => repository.DiscardAsync(change);

    public async Task<CommitOutcome> CommitAsync(string summary, string body, bool amend, bool stageEverything)
    {
        if (stageEverything)
        {
            var staged = await repository.StageAllAsync().ConfigureAwait(false);
            if (!staged.Succeeded)
                return new CommitOutcome(staged, StagingFailed: true);
        }
        var result = await repository.CommitAsync(CommitMessage.Compose(summary, body), amend).ConfigureAwait(false);
        return new CommitOutcome(result, StagingFailed: false);
    }

    public async Task<(string Summary, string Body)?> LastMessageAsync() =>
        await repository.LastMessageAsync().ConfigureAwait(false) is { } message ? CommitMessage.Split(message) : null;
}
