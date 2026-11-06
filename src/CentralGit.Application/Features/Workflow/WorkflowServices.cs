using CentralGit.Application.Git;
using CentralGit.Application.Operations;
using CentralGit.Application.Ports;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Diff;
using CentralGit.Domain.Workflow;

namespace CentralGit.Application.Features.Workflow;

/// <summary>Commit actions (cherry-pick, revert, reset), merge and rebase, and finishing what stopped half-way.</summary>
public interface ICommitActionService
{
    IPreparedOperation CherryPick(CommitInfo commit);
    IPreparedOperation Revert(CommitInfo commit);
    IPreparedOperation Reset(string commit, ResetMode mode);
    IPreparedOperation Merge(string branch);
    IPreparedOperation Rebase(string onto);

    /// <summary>The commits from <paramref name="commit"/> to HEAD, oldest first, ready to plan a rewrite.</summary>
    Task<RebaseRange> GetRebaseRangeAsync(string commit, CancellationToken ct = default);

    IPreparedOperation InteractiveRebase(RebaseRange range, IReadOnlyList<RebaseStep> steps);

    Task<BisectState> GetBisectAsync(CancellationToken ct = default);
    IPreparedOperation BisectStart();
    IPreparedOperation BisectMark(string? commit, BisectMark mark);
    IPreparedOperation BisectReset();
    IPreparedOperation Continue(InProgress state);
    IPreparedOperation Abort(InProgress state);

    /// <summary>A merge, rebase, cherry-pick or revert waiting for the user, if any.</summary>
    Task<InProgress> GetInProgressAsync(CancellationToken ct = default);

    /// <summary>Takes one side of a conflicted file whole and marks it resolved.</summary>
    Task<GitResult> ResolveConflictAsync(string path, ConflictSide side, CancellationToken ct = default);

    /// <summary>A conflicted file split at its markers; null when it can't be read as text.</summary>
    Task<ConflictText?> ReadConflictAsync(string path, CancellationToken ct = default);

    /// <summary>Writes the file with each conflict replaced by the chosen side(s) and marks it resolved.</summary>
    Task<GitResult> SaveResolutionAsync(
        string path,
        ConflictText text,
        IReadOnlyList<ConflictChoice> choices,
        CancellationToken ct = default
    );
}

public sealed class CommitActionService(IWorkflowSource repository, IOperationSource operations) : ICommitActionService
{
    public IPreparedOperation CherryPick(CommitInfo commit) =>
        operations.Prepare(new CherryPick(commit.Hash, commit.IsMerge));

    public IPreparedOperation Revert(CommitInfo commit) =>
        operations.Prepare(new Operations.Revert(commit.Hash, commit.IsMerge));

    public IPreparedOperation Reset(string commit, ResetMode mode) => operations.Prepare(new Reset(commit, mode));

    public IPreparedOperation Merge(string branch) => operations.Prepare(new Merge(branch));

    public IPreparedOperation Rebase(string onto) => operations.Prepare(new Rebase(onto));

    public Task<RebaseRange> GetRebaseRangeAsync(string commit, CancellationToken ct = default) =>
        repository.GetRebaseRangeAsync(commit, ct);

    public IPreparedOperation InteractiveRebase(RebaseRange range, IReadOnlyList<RebaseStep> steps) =>
        operations.Prepare(new InteractiveRebase(range.Base, steps));

    public Task<BisectState> GetBisectAsync(CancellationToken ct = default) => repository.GetBisectAsync(ct);

    public IPreparedOperation BisectStart() => operations.Prepare(new BisectStart());

    public IPreparedOperation BisectMark(string? commit, BisectMark mark) =>
        operations.Prepare(new BisectMarkCommit(commit, mark));

    public IPreparedOperation BisectReset() => operations.Prepare(new BisectReset());

    public IPreparedOperation Continue(InProgress state) => operations.Prepare(new ContinueInProgress(state));

    public IPreparedOperation Abort(InProgress state) => operations.Prepare(new AbortInProgress(state));

    public Task<InProgress> GetInProgressAsync(CancellationToken ct = default) => repository.GetInProgressAsync(ct);

    public Task<GitResult> ResolveConflictAsync(string path, ConflictSide side, CancellationToken ct = default) =>
        repository.ResolveConflictAsync(path, side, ct);

    public async Task<ConflictText?> ReadConflictAsync(string path, CancellationToken ct = default) =>
        await repository.ReadWorkingFileAsync(path, ct).ConfigureAwait(false) is { } text
            ? ConflictText.Parse(text)
            : null;

    public Task<GitResult> SaveResolutionAsync(
        string path,
        ConflictText text,
        IReadOnlyList<ConflictChoice> choices,
        CancellationToken ct = default
    ) => repository.SaveResolvedFileAsync(path, text.Resolve(choices), ct);
}

public interface ITagService
{
    Task<IReadOnlyList<Tag>> GetTagsAsync(CancellationToken ct = default);

    /// <summary>An error sentence, or null when <paramref name="name"/> can be used for a new tag.</summary>
    Task<string?> ValidateNewNameAsync(string name, CancellationToken ct = default);

    /// <summary>With a message the tag is annotated (it records who tagged it and when); without, lightweight.</summary>
    IPreparedOperation Create(string name, string commit, string? message);
    IPreparedOperation Delete(string name);
    IPreparedOperation Push(string remote, string name);
    IPreparedOperation DeleteOnRemote(string remote, string name);
}

public sealed class TagService(IWorkflowSource repository, IOperationSource operations) : ITagService
{
    public Task<IReadOnlyList<Tag>> GetTagsAsync(CancellationToken ct = default) => repository.GetTagsAsync(ct);

    public async Task<string?> ValidateNewNameAsync(string name, CancellationToken ct = default)
    {
        if (TagNames.Precheck(name) is { } problem)
            return problem;
        if (!await repository.IsValidTagNameAsync(name, ct).ConfigureAwait(false))
            return $"Git doesn't allow “{name}” as a tag name (no ~ ^ : ? * [ \\, “..” or a trailing dot).";
        var tags = await repository.GetTagsAsync(ct).ConfigureAwait(false);
        return tags.Any(t => t.Name == name) ? $"A tag called {name} already exists." : null;
    }

    public IPreparedOperation Create(string name, string commit, string? message) =>
        operations.Prepare(new CreateTag(name, commit, string.IsNullOrWhiteSpace(message) ? null : message.Trim()));

    public IPreparedOperation Delete(string name) => operations.Prepare(new DeleteTag(name));

    public IPreparedOperation Push(string remote, string name) => operations.Prepare(new PushTag(remote, name));

    public IPreparedOperation DeleteOnRemote(string remote, string name) =>
        operations.Prepare(new DeleteRemoteTag(remote, name));
}

public interface IStashService
{
    Task<IReadOnlyList<Stash>> GetStashesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<StashFile>> GetFilesAsync(Stash stash, CancellationToken ct = default);
    Task<FileDiff> GetFileDiffAsync(StashFile file, CancellationToken ct = default);

    /// <summary>Everything (untracked files too when asked), or only <paramref name="paths"/>.</summary>
    IPreparedOperation Save(string message, bool includeUntracked, IReadOnlyList<string> paths);
    IPreparedOperation Apply(Stash stash);
    IPreparedOperation Pop(Stash stash);
    IPreparedOperation Drop(Stash stash);
}

public sealed class StashService(IWorkflowSource repository, IHistorySource history, IOperationSource operations)
    : IStashService
{
    public Task<IReadOnlyList<Stash>> GetStashesAsync(CancellationToken ct = default) => repository.GetStashesAsync(ct);

    public Task<IReadOnlyList<StashFile>> GetFilesAsync(Stash stash, CancellationToken ct = default) =>
        repository.GetStashFilesAsync(stash, ct);

    public Task<FileDiff> GetFileDiffAsync(StashFile file, CancellationToken ct = default) =>
        history.GetFileDiffAsync(file.Base, file.Revision, file.File, ct);

    public IPreparedOperation Save(string message, bool includeUntracked, IReadOnlyList<string> paths) =>
        operations.Prepare(new StashSave(message.Trim(), includeUntracked, paths));

    public IPreparedOperation Apply(Stash stash) => operations.Prepare(new StashApply(stash.Name, Pop: false));

    public IPreparedOperation Pop(Stash stash) => operations.Prepare(new StashApply(stash.Name, Pop: true));

    public IPreparedOperation Drop(Stash stash) => operations.Prepare(new StashDrop(stash.Name));
}

public interface IBlameService
{
    /// <summary>Who last changed each line; <paramref name="revision"/> null = the working tree.</summary>
    Task<IReadOnlyList<BlameLine>> BlameAsync(string path, string? revision, CancellationToken ct = default);
}

public sealed class BlameService(IWorkflowSource repository) : IBlameService
{
    public Task<IReadOnlyList<BlameLine>> BlameAsync(string path, string? revision, CancellationToken ct = default) =>
        repository.BlameAsync(path, revision, ct);
}
