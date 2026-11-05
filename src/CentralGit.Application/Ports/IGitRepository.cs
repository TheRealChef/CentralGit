using CentralGit.Application.Git;
using CentralGit.Application.Operations;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Diff;
using CentralGit.Domain.Health;

namespace CentralGit.Application.Ports;

/// <summary>History and commit contents.</summary>
public interface IHistorySource
{
    Task<WorkingTreeStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>The commit HEAD points at; null before the first commit.</summary>
    Task<string?> GetHeadHashAsync(CancellationToken ct = default);

    /// <summary>Commits between HEAD and <paramref name="upstream"/>; empty without an upstream.</summary>
    Task<SyncSets> GetSyncSetsAsync(string? upstream, CancellationToken ct = default);

    /// <summary>All refs, topological order, newest first.</summary>
    Task<IReadOnlyList<CommitInfo>> GetHistoryAsync(int maxCount, int skip = 0, CancellationToken ct = default);

    /// <summary>Hashes of commits (any ref) that touch <paramref name="pathSpec"/>.</summary>
    Task<IReadOnlySet<string>> GetCommitsTouchingAsync(string pathSpec, CancellationToken ct = default);

    Task<CommitDetails> GetCommitDetailsAsync(string hash, CancellationToken ct = default);

    Task<FileDiff> GetFileDiffAsync(
        string baseRevision,
        string revision,
        ChangedFile file,
        CancellationToken ct = default
    );
}

/// <summary>Branches, remotes and the configuration that decides how to sync.</summary>
public interface IBranchSource
{
    Task<IReadOnlyList<LocalBranch>> GetLocalBranchesAsync(CancellationToken ct = default);

    /// <summary>Remote-tracking branches, without the symbolic <c>origin/HEAD</c>.</summary>
    Task<IReadOnlyList<RemoteBranch>> GetRemoteBranchesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetRemotesAsync(CancellationToken ct = default);

    /// <summary>A config value only if the user set it (global, local or worktree) — not installer defaults.</summary>
    Task<string?> GetUserConfigAsync(string key, CancellationToken ct = default);

    /// <summary>Whether git accepts <paramref name="name"/> as a branch name.</summary>
    Task<bool> IsValidBranchNameAsync(string name, CancellationToken ct = default);
}

/// <summary>The working tree and index: what changed, staging, discarding, committing.</summary>
public interface IWorkingTreeSource
{
    Task<WorkingTreeStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>The diff of one file on one side of the index. Untracked files diff against nothing.</summary>
    Task<FileDiff> DiffAsync(FileChange change, ChangeSide side, CancellationToken ct = default);

    /// <summary>Line counts per path on one side of the index. Untracked files aren't included.</summary>
    Task<IReadOnlyDictionary<string, LineCounts>> NumstatAsync(ChangeSide side, CancellationToken ct = default);

    /// <summary>Lines in a file git has no diff for (untracked); null when unreadable or huge.</summary>
    LineCounts? CountLines(string relativePath);

    Task<GitResult> StageAsync(IEnumerable<string> paths, CancellationToken ct = default);
    Task<GitResult> StageAllAsync(CancellationToken ct = default);

    /// <summary>Before the first commit (<paramref name="hasHead"/> false) files leave the index instead.</summary>
    Task<GitResult> UnstageAsync(IEnumerable<string> paths, bool hasHead, CancellationToken ct = default);
    Task<GitResult> UnstageAllAsync(bool hasHead, CancellationToken ct = default);

    /// <summary>Stages a hunk of an unstaged diff, or unstages a hunk of a staged diff.</summary>
    Task<GitResult> ApplyHunkToIndexAsync(FileDiff diff, int hunk, ChangeSide side, CancellationToken ct = default);

    /// <summary>Throws away one unstaged hunk. Destructive: ask first.</summary>
    Task<GitResult> DiscardHunkAsync(FileDiff diff, int hunk, CancellationToken ct = default);

    /// <summary>Throws away unstaged changes to a file; untracked files are deleted. Destructive: ask first.</summary>
    Task<GitResult> DiscardAsync(FileChange change, CancellationToken ct = default);

    Task<GitResult> CommitAsync(string message, bool amend, CancellationToken ct = default);

    /// <summary>HEAD's full message, for amending; null before the first commit.</summary>
    Task<string?> LastMessageAsync(CancellationToken ct = default);
}

/// <summary>The raw facts the Health page judges.</summary>
public interface IHealthSource
{
    Task<BranchRefs> ReadBranchRefsAsync(CancellationToken ct = default);

    /// <summary>Full ref names (refs/heads/x, refs/remotes/origin/x) already contained in <paramref name="target"/>.</summary>
    Task<IReadOnlyCollection<string>> GetRefsMergedIntoAsync(string target, CancellationToken ct = default);

    Task<RepositoryFacts> ReadFactsAsync(DateTimeOffset now, CancellationToken ct = default);
}

/// <summary>Turns an intent into something the Operations Center can run.</summary>
public interface IOperationSource
{
    IPreparedOperation Prepare(GitOperation operation);
}

/// <summary>One repository on disk, reached however Infrastructure reaches it (the git CLI).</summary>
public interface IGitRepository : IHistorySource, IBranchSource, IWorkingTreeSource, IHealthSource, IOperationSource
{
    string Path { get; }
    string Name { get; }
}

public interface IGitRepositoryFactory
{
    IGitRepository Open(string path);
}
