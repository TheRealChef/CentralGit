using CentralGit.Application.Git;
using CentralGit.Application.Operations;
using CentralGit.Application.Ports;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Diff;
using CentralGit.Domain.Health;
using CentralGit.Domain.Recovery;
using CentralGit.Domain.Workflow;
using CentralGit.Infrastructure.FileSystem;

namespace CentralGit.Infrastructure.Git;

/// <summary>One repository through the git CLI: every read and write the Application's ports ask for.</summary>
public sealed class GitRepository(string path, GitRunner? git = null) : IGitRepository
{
    private readonly GitRunner _git = git ?? new GitRunner();
    private WorkingTree? _tree;

    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
    public GitRunner Git => _git;
    private WorkingTree Tree => _tree ??= new WorkingTree(_git, Path);

    // ─── History ──────────────────────────────────────────────────────────

    public async Task<WorkingTreeStatus> GetStatusAsync(CancellationToken ct = default) =>
        StatusParser.Parse(await _git.RunCheckedAsync(Path, StatusParser.Arguments, ct).ConfigureAwait(false));

    /// <summary>
    /// Commits that differ between HEAD and its upstream: <c>Incoming</c> were fetched but aren't in
    /// your branch yet; <c>Outgoing</c> are yours and haven't reached the remote. Empty without an upstream.
    /// </summary>
    public async Task<SyncSets> GetSyncSetsAsync(string? upstream, CancellationToken ct = default)
    {
        if (upstream is null)
            return SyncSets.Empty;
        const int limit = 5000;
        var incoming = _git.RunAsync(Path, ["rev-list", $"--max-count={limit}", "HEAD..@{upstream}", "--"], ct);
        var outgoing = _git.RunAsync(Path, ["rev-list", $"--max-count={limit}", "@{upstream}..HEAD", "--"], ct);
        return new SyncSets(
            upstream,
            Hashes(await incoming.ConfigureAwait(false)),
            Hashes(await outgoing.ConfigureAwait(false))
        );

        static HashSet<string> Hashes(GitResult r) =>
            r.Succeeded
                ? r
                    .StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.Ordinal)
                : [];
    }

    public async Task<IReadOnlyList<CommitInfo>> GetHistoryAsync(
        int maxCount,
        int skip = 0,
        CancellationToken ct = default
    )
    {
        var commits = new List<CommitInfo>(Math.Max(maxCount, 0));
        await foreach (var commit in CommitLog.ReadAsync(_git, Path, maxCount, skip, ct).ConfigureAwait(false))
            commits.Add(commit);
        return commits;
    }

    public async Task<IReadOnlySet<string>> GetCommitsTouchingAsync(string pathSpec, CancellationToken ct = default)
    {
        var output = await _git.RunCheckedAsync(Path, ["log", "--all", "--format=%H", "--", pathSpec], ct)
            .ConfigureAwait(false);
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    public async Task<string?> GetHeadHashAsync(CancellationToken ct = default)
    {
        var result = await _git.RunAsync(Path, ["rev-parse", "--verify", "--quiet", "HEAD"], ct).ConfigureAwait(false);
        return result.Succeeded ? result.StandardOutput.Trim() : null;
    }

    public Task<CommitDetails> GetCommitDetailsAsync(string hash, CancellationToken ct = default) =>
        CommitDetailsReader.LoadAsync(_git, Path, hash, ct);

    public Task<FileDiff> GetFileDiffAsync(
        string baseRevision,
        string revision,
        ChangedFile file,
        CancellationToken ct = default
    ) => DiffParser.LoadAsync(_git, Path, baseRevision, revision, file, ct);

    public Task<IReadOnlyList<FileRevision>> GetFileHistoryAsync(
        string file,
        int limit,
        CancellationToken ct = default
    ) => FileHistoryReader.ReadAsync(_git, Path, file, limit, ct);

    public Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken ct = default) =>
        FileHistoryReader.ListFilesAsync(_git, Path, ct);

    public Task<Comparison> CompareAsync(string baseRef, string headRef, int limit, CancellationToken ct = default) =>
        CompareReader.CompareAsync(_git, Path, baseRef, headRef, limit, ct);

    // ─── Branches & configuration ─────────────────────────────────────────

    public async Task<IReadOnlyList<string>> GetRemotesAsync(CancellationToken ct = default) =>
        (await _git.RunCheckedAsync(Path, ["remote"], ct).ConfigureAwait(false)).Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

    /// <summary>A config value as git resolves it for this repository (local, global, system), or null.</summary>
    public async Task<string?> GetConfigAsync(string key, CancellationToken ct = default)
    {
        var result = await _git.RunAsync(Path, ["config", "--get", key], ct).ConfigureAwait(false);
        return result.Succeeded ? result.StandardOutput.Trim() : null;
    }

    /// <summary>
    /// A config value only if the user set it (global, local or worktree config). Installer defaults in
    /// system config — Git for Windows writes <c>pull.rebase=false</c> there — are not a choice the user made.
    /// </summary>
    public async Task<string?> GetUserConfigAsync(string key, CancellationToken ct = default)
    {
        var result = await _git.RunAsync(Path, ["config", "--show-scope", "--get", key], ct).ConfigureAwait(false);
        if (!result.Succeeded)
            return null;
        var line = result.StandardOutput.TrimEnd('\r', '\n');
        var tab = line.IndexOf('\t');
        if (tab < 0)
            return null;
        return line[..tab] is "global" or "local" or "worktree" ? line[(tab + 1)..] : null;
    }

    public async Task<bool> IsValidBranchNameAsync(string name, CancellationToken ct = default) =>
        (await _git.RunAsync(Path, ["check-ref-format", "--branch", name], ct).ConfigureAwait(false)).Succeeded;

    public async Task<IReadOnlyList<LocalBranch>> GetLocalBranchesAsync(CancellationToken ct = default)
    {
        var output = await _git.RunCheckedAsync(
                Path,
                [
                    "for-each-ref",
                    "--format=%(HEAD)%1f%(refname:short)%1f%(upstream:short)%1f%(upstream:track)%1f%(committerdate:unix)%1f%(objectname)",
                    "refs/heads",
                ],
                ct
            )
            .ConfigureAwait(false);

        var branches = new List<LocalBranch>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\u001f');
            if (f.Length < 4)
                continue;
            var (ahead, behind, gone) = ParseTrack(f[3]);
            branches.Add(
                new LocalBranch(
                    f[1],
                    f[2].Length == 0 ? null : f[2],
                    ahead,
                    behind,
                    f[0] == "*",
                    gone,
                    f.Length > 4 ? UnixTime(f[4]) : null,
                    f.Length > 5 ? f[5] : null
                )
            );
        }
        return branches;
    }

    public async Task<IReadOnlyList<RemoteBranch>> GetRemoteBranchesAsync(CancellationToken ct = default)
    {
        var output = await _git.RunCheckedAsync(
                Path,
                ["for-each-ref", "--format=%(refname)%1f%(symref)%1f%(committerdate:unix)", "refs/remotes"],
                ct
            )
            .ConfigureAwait(false);

        var branches = new List<RemoteBranch>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\u001f');
            if (f.Length < 3 || f[1].Length > 0 || !f[0].StartsWith("refs/remotes/", StringComparison.Ordinal))
                continue;
            var name = f[0]["refs/remotes/".Length..];
            var slash = name.IndexOf('/');
            if (slash <= 0)
                continue;
            branches.Add(new RemoteBranch(name, name[..slash], name[(slash + 1)..], UnixTime(f[2])));
        }
        return branches;
    }

    private static DateTimeOffset? UnixTime(string s) =>
        long.TryParse(s, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;

    /// <summary>Parses <c>%(upstream:track)</c>: "", "[gone]", "[ahead 1]", "[behind 4]", "[ahead 1, behind 4]".</summary>
    internal static (int Ahead, int Behind, bool Gone) ParseTrack(string track)
    {
        if (track.Contains("gone", StringComparison.Ordinal))
            return (0, 0, true);

        int ahead = 0,
            behind = 0;
        foreach (
            var part in track
                .Trim('[', ']')
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        )
        {
            var kv = part.Split(' ');
            if (kv.Length != 2 || !int.TryParse(kv[1], out var n))
                continue;
            if (kv[0] == "ahead")
                ahead = n;
            else if (kv[0] == "behind")
                behind = n;
        }
        return (ahead, behind, false);
    }

    // ─── Working tree ─────────────────────────────────────────────────────

    public Task<FileDiff> DiffAsync(FileChange change, ChangeSide side, CancellationToken ct = default) =>
        Tree.DiffAsync(change, side, ct);

    public Task<IReadOnlyDictionary<string, LineCounts>> NumstatAsync(
        ChangeSide side,
        CancellationToken ct = default
    ) => Tree.NumstatAsync(side, ct);

    public LineCounts? CountLines(string relativePath) => LineCounter.Count(System.IO.Path.Combine(Path, relativePath));

    public Task<GitResult> StageAsync(IEnumerable<string> paths, CancellationToken ct = default) =>
        Tree.StageAsync(paths, ct);

    public Task<GitResult> StageAllAsync(CancellationToken ct = default) => Tree.StageAllAsync(ct);

    public Task<GitResult> UnstageAsync(IEnumerable<string> paths, bool hasHead, CancellationToken ct = default) =>
        Tree.UnstageAsync(paths, hasHead, ct);

    public Task<GitResult> UnstageAllAsync(bool hasHead, CancellationToken ct = default) =>
        Tree.UnstageAllAsync(hasHead, ct);

    public Task<GitResult> ApplyHunkToIndexAsync(
        FileDiff diff,
        int hunk,
        ChangeSide side,
        CancellationToken ct = default
    ) => Tree.ApplyHunkToIndexAsync(diff, hunk, side, ct);

    public Task<GitResult> DiscardHunkAsync(FileDiff diff, int hunk, CancellationToken ct = default) =>
        Tree.DiscardHunkAsync(diff, hunk, ct);

    public Task<GitResult> ApplyLinesToIndexAsync(
        FileDiff diff,
        int hunk,
        IReadOnlySet<int> lines,
        ChangeSide side,
        CancellationToken ct = default
    ) => Tree.ApplyLinesToIndexAsync(diff, hunk, lines, side, ct);

    public Task<GitResult> DiscardLinesAsync(
        FileDiff diff,
        int hunk,
        IReadOnlySet<int> lines,
        CancellationToken ct = default
    ) => Tree.DiscardLinesAsync(diff, hunk, lines, ct);

    public Task<GitResult> DiscardAsync(FileChange change, CancellationToken ct = default) =>
        Tree.DiscardAsync(change, ct);

    // ─── Workflow: stopped operations, tags, stash, blame ─────────────────

    public Task<InProgress> GetInProgressAsync(CancellationToken ct = default) =>
        WorkflowReader.GetInProgressAsync(_git, Path, ct);

    public Task<GitResult> ResolveConflictAsync(string path, ConflictSide side, CancellationToken ct = default) =>
        WorkflowReader.ResolveConflictAsync(_git, Path, path, side, ct);

    public Task<string?> ReadWorkingFileAsync(string path, CancellationToken ct = default) =>
        WorkflowReader.ReadWorkingFileAsync(Path, path, ct);

    public Task<GitResult> SaveResolvedFileAsync(string path, string text, CancellationToken ct = default) =>
        WorkflowReader.SaveResolvedFileAsync(_git, Path, path, text, ct);

    public Task<RebaseRange> GetRebaseRangeAsync(string commit, CancellationToken ct = default) =>
        WorkflowReader.GetRebaseRangeAsync(_git, Path, commit, ct);

    public Task<BisectState> GetBisectAsync(CancellationToken ct = default) =>
        WorkflowReader.GetBisectAsync(_git, Path, ct);

    public async Task<bool> RefreshCommitGraphAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        // A single file (git gc) or a split chain (git maintenance / our own writes): either counts.
        var paths = await _git.RunCheckedAsync(
                Path,
                [
                    "rev-parse",
                    "--git-path",
                    "objects/info/commit-graph",
                    "--git-path",
                    "objects/info/commit-graphs/commit-graph-chain",
                ],
                ct
            )
            .ConfigureAwait(false);
        var newest = paths
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => System.IO.Path.Combine(Path, p))
            .Where(File.Exists)
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        if (DateTime.UtcNow - newest < maxAge)
            return false;
        var write = await _git.RunAsync(
                Path,
                ["commit-graph", "write", "--reachable", "--split", "--changed-paths", "--no-progress"],
                ct
            )
            .ConfigureAwait(false);
        return write.Succeeded;
    }

    public Task<IReadOnlyList<Tag>> GetTagsAsync(CancellationToken ct = default) =>
        WorkflowReader.GetTagsAsync(_git, Path, ct);

    public Task<bool> IsValidTagNameAsync(string name, CancellationToken ct = default) =>
        WorkflowReader.IsValidTagNameAsync(_git, Path, name, ct);

    public Task<IReadOnlyList<Stash>> GetStashesAsync(CancellationToken ct = default) =>
        WorkflowReader.GetStashesAsync(_git, Path, ct);

    public Task<IReadOnlyList<StashFile>> GetStashFilesAsync(Stash stash, CancellationToken ct = default) =>
        WorkflowReader.GetStashFilesAsync(_git, Path, stash, ct);

    public Task<IReadOnlyList<BlameLine>> BlameAsync(string path, string? revision, CancellationToken ct = default) =>
        WorkflowReader.BlameAsync(_git, Path, path, revision, ct);

    public Task<GitResult> CommitAsync(string message, bool amend, CancellationToken ct = default) =>
        Tree.CommitAsync(message, amend, ct);

    public Task<string?> LastMessageAsync(CancellationToken ct = default) => Tree.LastMessageAsync(ct);

    // ─── Health ───────────────────────────────────────────────────────────

    public Task<BranchRefs> ReadBranchRefsAsync(CancellationToken ct = default) =>
        HealthReader.ReadBranchRefsAsync(_git, Path, ct);

    public Task<IReadOnlyCollection<string>> GetRefsMergedIntoAsync(string target, CancellationToken ct = default) =>
        HealthReader.GetRefsMergedIntoAsync(_git, Path, target, ct);

    public Task<RepositoryFacts> ReadFactsAsync(DateTimeOffset now, CancellationToken ct = default) =>
        HealthReader.ReadFactsAsync(_git, Path, now, ct);

    // ─── Recovery ─────────────────────────────────────────────────────────

    public Task<IReadOnlyList<ReflogEntry>> ReadReflogAsync(int limit, CancellationToken ct = default) =>
        ReflogReader.ReadAsync(_git, Path, limit, ct);

    public Task<IReadOnlySet<string>> FindUnreachableAsync(
        IEnumerable<string> hashes,
        CancellationToken ct = default
    ) => ReflogReader.FindUnreachableAsync(_git, Path, hashes, ct);

    // ─── Operations ───────────────────────────────────────────────────────

    public IPreparedOperation Prepare(GitOperation operation) =>
        new PreparedOperation(_git, Path, OperationArguments.For(operation));
}

public sealed class GitRepositoryFactory(GitRunner git) : IGitRepositoryFactory
{
    public IGitRepository Open(string path) => new GitRepository(path, git);
}
