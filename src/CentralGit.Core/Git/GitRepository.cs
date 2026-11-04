namespace CentralGit.Core.Git;

public sealed record LocalBranch(string Name, string? Upstream, int Ahead, int Behind, bool IsHead, bool UpstreamGone);

/// <summary>Read operations for one repository. Writes (commit, push, …) go through the Operations layer later.</summary>
public sealed class GitRepository(string path, GitRunner? git = null)
{
    private readonly GitRunner _git = git ?? new GitRunner();

    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
    public GitRunner Git => _git;

    public async Task<WorkingTreeStatus> GetStatusAsync(CancellationToken ct = default) =>
        WorkingTreeStatus.Parse(await _git.RunCheckedAsync(Path, WorkingTreeStatus.Arguments, ct).ConfigureAwait(false));

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

    public async Task<string?> GetHeadHashAsync(CancellationToken ct = default)
    {
        var result = await _git.RunAsync(Path, ["rev-parse", "--verify", "--quiet", "HEAD"], ct).ConfigureAwait(false);
        return result.Succeeded ? result.StandardOutput.Trim() : null;
    }

    public async Task<IReadOnlyList<LocalBranch>> GetLocalBranchesAsync(CancellationToken ct = default)
    {
        var output = await _git.RunCheckedAsync(Path,
            ["for-each-ref", "--format=%(HEAD)%1f%(refname:short)%1f%(upstream:short)%1f%(upstream:track)", "refs/heads"], ct)
            .ConfigureAwait(false);

        var branches = new List<LocalBranch>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\u001f');
            if (f.Length < 4)
                continue;
            var (ahead, behind, gone) = ParseTrack(f[3]);
            branches.Add(new LocalBranch(f[1], f[2].Length == 0 ? null : f[2], ahead, behind, f[0] == "*", gone));
        }
        return branches;
    }

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
