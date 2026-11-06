using CentralGit.Application.Git;
using CentralGit.Domain.Health;

namespace CentralGit.Infrastructure.Git;

/// <summary>Reads the raw facts the Health page judges: refs, merge state, size and activity, configuration.</summary>
public static class HealthReader
{
    public static async Task<BranchRefs> ReadBranchRefsAsync(GitRunner git, string path, CancellationToken ct = default)
    {
        var refsTask = git.RunCheckedAsync(
            path,
            [
                "for-each-ref",
                "--format=%(refname)%1f%(symref)%1f%(committerdate:unix)%1f%(authorname)%1f%(upstream:short)%1f%(upstream:track)%1f%(HEAD)",
                "refs/heads",
                "refs/remotes",
            ],
            ct
        );
        var originHeadTask = git.RunAsync(path, ["symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD"], ct);
        var (local, remote) = ParseRefs(await refsTask.ConfigureAwait(false));
        var originHeadResult = await originHeadTask.ConfigureAwait(false);
        var originHead =
            originHeadResult.Succeeded && originHeadResult.StandardOutput.Trim() is { Length: > 0 } h ? h : null;
        return new BranchRefs(local, remote, originHead);
    }

    /// <summary>Full ref names merged into <paramref name="target"/>; empty if git can't tell.</summary>
    public static async Task<IReadOnlyCollection<string>> GetRefsMergedIntoAsync(
        GitRunner git,
        string path,
        string target,
        CancellationToken ct = default
    )
    {
        var result = await git.RunAsync(
                path,
                ["for-each-ref", $"--merged={target}", "--format=%(refname)", "refs/heads", "refs/remotes"],
                ct
            )
            .ConfigureAwait(false);
        return result.Succeeded
            ? result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
    }

    internal static (List<BranchFacts> Local, List<BranchFacts> Remote) ParseRefs(string output)
    {
        var local = new List<BranchFacts>();
        var remote = new List<BranchFacts>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.TrimEnd('\r').Split('\u001f');
            if (f.Length < 7 || f[1].Length > 0)
                continue; // symbolic refs (origin/HEAD)
            DateTimeOffset? last = long.TryParse(f[2], out var unix) ? DateTimeOffset.FromUnixTimeSeconds(unix) : null;
            var author = f[3].Length > 0 ? f[3] : null;
            if (f[0].StartsWith("refs/heads/", StringComparison.Ordinal))
                local.Add(
                    new BranchFacts(
                        f[0]["refs/heads/".Length..],
                        false,
                        last,
                        author,
                        f[6] == "*",
                        f[5].Contains("gone", StringComparison.Ordinal),
                        f[4].Length > 0 ? f[4] : null
                    )
                );
            else if (f[0].StartsWith("refs/remotes/", StringComparison.Ordinal))
                remote.Add(new BranchFacts(f[0]["refs/remotes/".Length..], true, last, author));
        }
        return (local, remote);
    }

    public static async Task<RepositoryFacts> ReadFactsAsync(
        GitRunner git,
        string path,
        DateTimeOffset now,
        CancellationToken ct = default
    )
    {
        var countTask = git.RunAsync(path, ["rev-list", "--count", "HEAD"], ct);
        var recentTask = git.RunAsync(path, ["log", "--since=90.days", "--format=%ct%x1f%aE", "HEAD"], ct);
        var objectsTask = git.RunAsync(path, ["count-objects", "-v"], ct);
        var stashTask = git.RunAsync(path, ["stash", "list", "--format=%gd"], ct);
        var worktreeTask = git.RunAsync(path, ["worktree", "list", "--porcelain", "-z"], ct);
        var gitDirTask = git.RunAsync(path, ["rev-parse", "--absolute-git-dir"], ct);
        var remotesTask = git.RunAsync(path, ["config", "--get-regexp", @"^remote\..*\.url$"], ct);
        var versionTask = git.RunAsync(path, ["version"], ct);
        var nameTask = git.RunAsync(path, ["config", "user.name"], ct);
        var emailTask = git.RunAsync(path, ["config", "user.email"], ct);
        var lastCommitTask = git.RunAsync(path, ["log", "-1", "--format=%ct", "HEAD"], ct);
        var submodulesTask = ReadSubmodulesAsync(git, path, ct);
        var lfsTask = ReadLfsAsync(git, path, ct);

        var commits = int.TryParse((await countTask.ConfigureAwait(false)).StandardOutput.Trim(), out var c) ? c : 0;

        var (last30, contributors) = ParseRecent((await recentTask.ConfigureAwait(false)).StandardOutput, now);
        var objects = ParseCountObjects((await objectsTask.ConfigureAwait(false)).StandardOutput);
        var stashResult = await stashTask.ConfigureAwait(false);
        var stashes = stashResult.Succeeded
            ? stashResult.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length
            : 0;
        var worktreeResult = await worktreeTask.ConfigureAwait(false);
        var worktrees = worktreeResult.Succeeded
            ? worktreeResult.StandardOutput.Split('\0').Count(l => l.StartsWith("worktree ", StringComparison.Ordinal))
            : 1;

        DateTimeOffset? lastFetch = null;
        var gitDir = (await gitDirTask.ConfigureAwait(false)).StandardOutput.Trim();
        if (gitDir.Length > 0 && File.Exists(Path.Combine(gitDir, "FETCH_HEAD")))
            lastFetch = File.GetLastWriteTimeUtc(Path.Combine(gitDir, "FETCH_HEAD"));

        var remotes = ParseRemoteUrls((await remotesTask.ConfigureAwait(false)).StandardOutput);
        var version = (await versionTask.ConfigureAwait(false)).StandardOutput.Trim() is { Length: > 0 } v
            ? v.Replace("git version ", "")
            : null;
        var userName = Trimmed(await nameTask.ConfigureAwait(false));
        var userEmail = Trimmed(await emailTask.ConfigureAwait(false));
        DateTimeOffset? lastCommit = long.TryParse(
            (await lastCommitTask.ConfigureAwait(false)).StandardOutput.Trim(),
            out var lc
        )
            ? DateTimeOffset.FromUnixTimeSeconds(lc)
            : null;

        var stats = new RepositoryStats(
            commits,
            last30,
            contributors,
            objects.SizeKiB,
            objects.Loose,
            objects.Packs,
            stashes,
            worktrees,
            lastFetch,
            lastCommit
        );
        return new RepositoryFacts(
            stats,
            remotes,
            version,
            userName,
            userEmail,
            await submodulesTask.ConfigureAwait(false),
            await lfsTask.ConfigureAwait(false)
        );

        static string? Trimmed(GitResult r) => r.Succeeded && r.StandardOutput.Trim() is { Length: > 0 } s ? s : null;
    }

    /// <summary>
    /// Gitlinks (mode 160000) from the index, each compared with what is checked out in its folder.
    /// Only looked at when there is a <c>.gitmodules</c>; capped so a monorepo of submodules stays quick.
    /// </summary>
    internal static async Task<IReadOnlyList<SubmoduleInfo>> ReadSubmodulesAsync(
        GitRunner git,
        string path,
        CancellationToken ct
    )
    {
        if (!File.Exists(Path.Combine(path, ".gitmodules")))
            return [];
        var staged = await git.RunAsync(path, ["ls-files", "-s", "-z"], ct).ConfigureAwait(false);
        if (!staged.Succeeded)
            return [];
        var result = new List<SubmoduleInfo>();
        foreach (var entry in staged.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            // "<mode> <object> <stage>\t<path>"
            if (!entry.StartsWith("160000 ", StringComparison.Ordinal) || entry.IndexOf('\t') is not (var tab and > 0))
                continue;
            var recorded = entry[7..entry.IndexOf(' ', 7)];
            var sub = entry[(tab + 1)..];
            var folder = Path.Combine(path, sub);
            string? checkedOut = null;
            // Without its own .git, running git in the folder would answer for the parent repository.
            if (File.Exists(Path.Combine(folder, ".git")) || Directory.Exists(Path.Combine(folder, ".git")))
            {
                var head = await git.RunAsync(folder, ["rev-parse", "--verify", "-q", "HEAD"], ct)
                    .ConfigureAwait(false);
                checkedOut = head.Succeeded ? head.StandardOutput.Trim() : null;
            }
            result.Add(new SubmoduleInfo(sub, recorded, checkedOut));
            if (result.Count >= 100)
                break;
        }
        return result;
    }

    private const string LfsPointerStart = "version https://git-lfs.github.com/spec/v1";

    /// <summary>Files whose attributes route them through LFS, and how many are still pointer files on disk.</summary>
    internal static async Task<LfsInfo?> ReadLfsAsync(GitRunner git, string path, CancellationToken ct)
    {
        var tracked = await git.RunAsync(path, ["ls-files", "-z", "--", ":(attr:filter=lfs)"], ct)
            .ConfigureAwait(false);
        if (!tracked.Succeeded)
            return null;
        var files = tracked.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (files.Length == 0)
            return new LfsInfo(0, false, 0);
        var installed = (await git.RunAsync(path, ["lfs", "version"], ct).ConfigureAwait(false)).Succeeded;
        var pointers = 0;
        var buffer = new byte[LfsPointerStart.Length];
        foreach (var file in files.Take(500))
        {
            try
            {
                await using var stream = File.OpenRead(Path.Combine(path, file));
                var read = await stream
                    .ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct)
                    .ConfigureAwait(false);
                if (read == buffer.Length && System.Text.Encoding.ASCII.GetString(buffer) == LfsPointerStart)
                    pointers++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return new LfsInfo(files.Length, installed, pointers);
    }

    internal static (int Last30Days, int Contributors) ParseRecent(string output, DateTimeOffset now)
    {
        var last30 = 0;
        var authors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.TrimEnd('\r').Split('\u001f');
            if (f.Length < 2)
                continue;
            authors.Add(f[1]);
            if (long.TryParse(f[0], out var t) && now - DateTimeOffset.FromUnixTimeSeconds(t) <= TimeSpan.FromDays(30))
                last30++;
        }
        return (last30, authors.Count);
    }

    /// <summary><c>count-objects -v</c>: "key: value" lines, sizes in KiB.</summary>
    internal static (long SizeKiB, int Loose, int Packs) ParseCountObjects(string output)
    {
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = line.IndexOf(':');
            if (colon > 0 && long.TryParse(line[(colon + 1)..].Trim(), out var n))
                values[line[..colon].Trim()] = n;
        }
        long Get(string k) => values.TryGetValue(k, out var v) ? v : 0;
        return (Get("size") + Get("size-pack"), (int)Get("count"), (int)Get("packs"));
    }

    internal static IReadOnlyList<RemoteUrl> ParseRemoteUrls(string output)
    {
        var list = new List<RemoteUrl>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var space = line.IndexOf(' ');
            if (space <= 0 || !line.StartsWith("remote.", StringComparison.Ordinal))
                continue;
            var key = line[..space];
            list.Add(new RemoteUrl(key["remote.".Length..^".url".Length], line[(space + 1)..]));
        }
        return list;
    }
}
