namespace CentralGit.Core.Git;

public sealed record LocalBranch(string Name, string? Upstream, int Ahead, int Behind, bool IsHead, bool UpstreamGone, DateTimeOffset? LastCommit = null, string? Tip = null);

/// <summary>Where each commit stands relative to the current branch's upstream.</summary>
public enum CommitSync { None, Incoming, Outgoing }

/// <summary>Incoming (fetched, not in HEAD) and outgoing (in HEAD, not pushed) commit hashes.</summary>
public sealed record SyncSets(string? Upstream, IReadOnlySet<string> Incoming, IReadOnlySet<string> Outgoing)
{
    public static readonly SyncSets Empty = new(null, new HashSet<string>(), new HashSet<string>());

    public CommitSync Of(string hash) =>
        Incoming.Contains(hash) ? CommitSync.Incoming : Outgoing.Contains(hash) ? CommitSync.Outgoing : CommitSync.None;
}

/// <summary>A remote-tracking branch, e.g. <c>origin/feature/x</c> (Remote "origin", ShortName "feature/x").</summary>
public sealed record RemoteBranch(string Name, string Remote, string ShortName, DateTimeOffset? LastCommit);

/// <summary>Read operations for one repository. Writes (commit, push, …) go through the Operations layer later.</summary>
public sealed class GitRepository(string path, GitRunner? git = null)
{
    private readonly GitRunner _git = git ?? new GitRunner();

    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
    public GitRunner Git => _git;

    public async Task<WorkingTreeStatus> GetStatusAsync(CancellationToken ct = default) =>
        WorkingTreeStatus.Parse(await _git.RunCheckedAsync(Path, WorkingTreeStatus.Arguments, ct).ConfigureAwait(false));

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
        return new SyncSets(upstream, Hashes(await incoming.ConfigureAwait(false)), Hashes(await outgoing.ConfigureAwait(false)));

        static HashSet<string> Hashes(GitResult r) => r.Succeeded
            ? r.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal)
            : [];
    }

    public async Task<IReadOnlyList<CommitInfo>> GetHistoryAsync(int maxCount, int skip = 0, CancellationToken ct = default)
    {
        var commits = new List<CommitInfo>(Math.Max(maxCount, 0));
        await foreach (var commit in CommitLog.ReadAsync(_git, Path, maxCount, skip, ct).ConfigureAwait(false))
            commits.Add(commit);
        return commits;
    }

    /// <summary>Hashes of commits (any ref) that touch <paramref name="pathSpec"/>, for the path filter.</summary>
    public async Task<IReadOnlySet<string>> GetCommitsTouchingAsync(string pathSpec, CancellationToken ct = default)
    {
        var output = await _git.RunCheckedAsync(Path, ["log", "--all", "--format=%H", "--", pathSpec], ct).ConfigureAwait(false);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<string>> GetRemotesAsync(CancellationToken ct = default) =>
        (await _git.RunCheckedAsync(Path, ["remote"], ct).ConfigureAwait(false))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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

    public async Task<string?> GetHeadHashAsync(CancellationToken ct = default)
    {
        var result = await _git.RunAsync(Path, ["rev-parse", "--verify", "--quiet", "HEAD"], ct).ConfigureAwait(false);
        return result.Succeeded ? result.StandardOutput.Trim() : null;
    }

    public async Task<IReadOnlyList<LocalBranch>> GetLocalBranchesAsync(CancellationToken ct = default)
    {
        var output = await _git.RunCheckedAsync(Path,
            ["for-each-ref", "--format=%(HEAD)%1f%(refname:short)%1f%(upstream:short)%1f%(upstream:track)%1f%(committerdate:unix)%1f%(objectname)", "refs/heads"], ct)
            .ConfigureAwait(false);

        var branches = new List<LocalBranch>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\u001f');
            if (f.Length < 4)
                continue;
            var (ahead, behind, gone) = ParseTrack(f[3]);
            branches.Add(new LocalBranch(f[1], f[2].Length == 0 ? null : f[2], ahead, behind, f[0] == "*", gone, f.Length > 4 ? UnixTime(f[4]) : null, f.Length > 5 ? f[5] : null));
        }
        return branches;
    }

    /// <summary>Remote-tracking branches, without the symbolic <c>origin/HEAD</c>.</summary>
    public async Task<IReadOnlyList<RemoteBranch>> GetRemoteBranchesAsync(CancellationToken ct = default)
    {
        var output = await _git.RunCheckedAsync(Path,
            ["for-each-ref", "--format=%(refname)%1f%(symref)%1f%(committerdate:unix)", "refs/remotes"], ct).ConfigureAwait(false);

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

        int ahead = 0, behind = 0;
        foreach (var part in track.Trim('[', ']').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(' ');
            if (kv.Length != 2 || !int.TryParse(kv[1], out var n))
                continue;
            if (kv[0] == "ahead") ahead = n;
            else if (kv[0] == "behind") behind = n;
        }
        return (ahead, behind, false);
    }
}
