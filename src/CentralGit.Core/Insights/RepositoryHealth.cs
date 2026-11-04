using CentralGit.Core.Git;

namespace CentralGit.Core.Insights;

/// <summary>Why a branch is (or isn't) worth cleaning up, strongest reason first.</summary>
public enum BranchVerdict
{
    /// <summary>Recent work, or a branch the team builds from.</summary>
    Active,
    /// <summary>All its commits are already in an integration branch: deleting loses nothing.</summary>
    Merged,
    /// <summary>Its upstream was deleted on the remote — usually merged through a squash/rebase PR.</summary>
    Gone,
    /// <summary>Not merged, and nobody has committed to it for months.</summary>
    Abandoned,
    /// <summary>Not merged, quiet for a few weeks: worth a look, not yet a cleanup candidate.</summary>
    Aging,
}

/// <summary>One branch (local or remote-tracking) with the facts used to judge it.</summary>
public sealed record BranchFacts(
    string Name,
    bool IsRemote,
    DateTimeOffset? LastCommit,
    string? LastAuthor,
    bool IsHead = false,
    bool UpstreamGone = false,
    string? Upstream = null);

public sealed record BranchAssessment(BranchFacts Branch, BranchVerdict Verdict, string? MergedInto, TimeSpan? Age, bool IsProtected)
{
    public string Name => Branch.Name;

    /// <summary>Something the user can delete without a second thought (or with one, for abandoned work).</summary>
    public bool IsCleanupCandidate => !IsProtected && !Branch.IsHead && Verdict is BranchVerdict.Merged or BranchVerdict.Gone or BranchVerdict.Abandoned;

    /// <summary>Deleting it can't lose commits (they're in <see cref="MergedInto"/>).</summary>
    public bool IsSafeToDelete => Verdict == BranchVerdict.Merged;
}

public sealed record RemoteUrl(string Name, string Url);

public enum FindingLevel { Good, Info, Attention }

/// <summary>A diagnostic line: what we saw, and what to do about it.</summary>
public sealed record HealthFinding(FindingLevel Level, string Title, string Detail);

public sealed record RepositoryStats(
    int Commits,
    int CommitsLast30Days,
    int ContributorsLast90Days,
    long SizeKiB,
    int LooseObjects,
    int Packs,
    int Stashes,
    int Worktrees,
    DateTimeOffset? LastFetch,
    DateTimeOffset? LastCommit);

public sealed record HealthReport(
    string? Trunk,
    IReadOnlyList<string> IntegrationBranches,
    IReadOnlyList<BranchAssessment> Local,
    IReadOnlyList<BranchAssessment> Remote,
    RepositoryStats Stats,
    IReadOnlyList<RemoteUrl> Remotes,
    string? GitVersion,
    string? UserName,
    string? UserEmail,
    IReadOnlyList<HealthFinding> Findings);

/// <summary>
/// Repository diagnostics for the Health page: branch hygiene (merged, gone, abandoned branches),
/// size and activity numbers, and configuration problems worth fixing.
/// </summary>
public static class RepositoryHealth
{
    /// <summary>No commits for this long: "aging".</summary>
    public static readonly TimeSpan AgingAfter = TimeSpan.FromDays(30);

    /// <summary>No commits for this long and not merged: "abandoned", a cleanup candidate.</summary>
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromDays(90);

    /// <summary>Branch names that are long-lived integration lines, never cleanup candidates.</summary>
    private static readonly string[] IntegrationNames =
        ["main", "master", "develop", "development", "dev", "trunk", "staging", "production", "prod", "release"];

    public static bool IsIntegrationName(string shortName) =>
        IntegrationNames.Contains(shortName, StringComparer.OrdinalIgnoreCase)
        || shortName.StartsWith("release/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Judges each branch. <paramref name="mergedInto"/> maps a full ref name (refs/heads/x,
    /// refs/remotes/origin/x) to the integration branch that already contains it.
    /// </summary>
    public static IReadOnlyList<BranchAssessment> Assess(
        IEnumerable<BranchFacts> branches, IReadOnlyDictionary<string, string> mergedInto, DateTimeOffset now)
    {
        var result = new List<BranchAssessment>();
        foreach (var b in branches)
        {
            var shortName = b.IsRemote && b.Name.IndexOf('/') is var slash and > 0 ? b.Name[(slash + 1)..] : b.Name;
            var isProtected = IsIntegrationName(shortName);
            TimeSpan? age = b.LastCommit is { } last ? now - last : null;
            var refName = (b.IsRemote ? "refs/remotes/" : "refs/heads/") + b.Name;
            mergedInto.TryGetValue(refName, out var into);
            if (into == b.Name)
                into = null; // an integration branch is trivially "merged into" itself

            var verdict = isProtected ? BranchVerdict.Active
                : into is not null ? BranchVerdict.Merged
                : b.UpstreamGone ? BranchVerdict.Gone
                : age >= AbandonedAfter ? BranchVerdict.Abandoned
                : age >= AgingAfter ? BranchVerdict.Aging
                : BranchVerdict.Active;
            result.Add(new BranchAssessment(b, verdict, verdict == BranchVerdict.Merged ? into : null, age, isProtected));
        }
        return result;
    }

    /// <summary>"3 days", "5 weeks", "4 months", "2 years".</summary>
    public static string DescribeAge(TimeSpan age) => age.TotalDays switch
    {
        < 1 => "today",
        < 2 => "1 day",
        < 14 => $"{(int)age.TotalDays} days",
        < 60 => $"{(int)(age.TotalDays / 7)} weeks",
        < 730 => $"{(int)Math.Round(age.TotalDays / 30.4)} months",
        _ => $"{(int)(age.TotalDays / 365.25)} years",
    };

    /// <summary>Compact form for badges: "5d", "6w", "4mo", "2y".</summary>
    public static string ShortAge(TimeSpan age) => age.TotalDays switch
    {
        < 14 => $"{Math.Max(0, (int)age.TotalDays)}d",
        < 60 => $"{(int)(age.TotalDays / 7)}w",
        < 730 => $"{(int)(age.TotalDays / 30.4)}mo",
        _ => $"{(int)(age.TotalDays / 365.25)}y",
    };

    // ─── Loading ──────────────────────────────────────────────────────────

    /// <summary>Integration branches present in the repo, preferring the remote copy (it's the shared truth).</summary>
    public static (string? Trunk, IReadOnlyList<string> Targets) PickIntegrationBranches(
        IReadOnlyCollection<string> localNames, IReadOnlyCollection<string> remoteNames, string? originHead)
    {
        var targets = new List<string>();
        foreach (var name in IntegrationNames)
        {
            var remote = remoteNames.FirstOrDefault(r => r.Equals("origin/" + name, StringComparison.OrdinalIgnoreCase))
                ?? remoteNames.FirstOrDefault(r => r.IndexOf('/') is var s and > 0 && r[(s + 1)..].Equals(name, StringComparison.OrdinalIgnoreCase));
            if (remote is not null)
                targets.Add(remote);
            else if (localNames.FirstOrDefault(l => l.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } local)
                targets.Add(local);
        }
        if (originHead is not null && !targets.Contains(originHead))
            targets.Insert(0, originHead);
        var trunk = originHead ?? targets.FirstOrDefault();
        // Trunk first: it's what "merged into" names when a branch is in several.
        if (trunk is not null)
        {
            targets.Remove(trunk);
            targets.Insert(0, trunk);
        }
        return (trunk, targets);
    }

    /// <summary>Branch facts and merge state only — cheap enough to run on every reload.</summary>
    public static async Task<(string? Trunk, IReadOnlyList<string> Targets, IReadOnlyList<BranchAssessment> Local, IReadOnlyList<BranchAssessment> Remote)>
        AssessBranchesAsync(GitRunner git, string path, DateTimeOffset now, CancellationToken ct = default)
    {
        var refsTask = git.RunCheckedAsync(path,
            ["for-each-ref", "--format=%(refname)%1f%(symref)%1f%(committerdate:unix)%1f%(authorname)%1f%(upstream:short)%1f%(upstream:track)%1f%(HEAD)",
             "refs/heads", "refs/remotes"], ct);
        var originHeadTask = git.RunAsync(path, ["symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD"], ct);
        var (local, remote) = ParseRefs(await refsTask.ConfigureAwait(false));
        var originHeadResult = await originHeadTask.ConfigureAwait(false);
        var originHead = originHeadResult.Succeeded && originHeadResult.StandardOutput.Trim() is { Length: > 0 } h ? h : null;

        var (trunk, targets) = PickIntegrationBranches(local.Select(b => b.Name).ToList(), remote.Select(b => b.Name).ToList(), originHead);

        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        var lists = await Task.WhenAll(targets.Select(t =>
            git.RunAsync(path, ["for-each-ref", $"--merged={t}", "--format=%(refname)", "refs/heads", "refs/remotes"], ct))).ConfigureAwait(false);
        for (var i = 0; i < targets.Count; i++)
        {
            if (!lists[i].Succeeded)
                continue;
            foreach (var refName in lists[i].StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                merged.TryAdd(refName, targets[i]);
        }

        return (trunk, targets, Assess(local, merged, now), Assess(remote, merged, now));
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
                local.Add(new BranchFacts(f[0]["refs/heads/".Length..], false, last, author, f[6] == "*",
                    f[5].Contains("gone", StringComparison.Ordinal), f[4].Length > 0 ? f[4] : null));
            else if (f[0].StartsWith("refs/remotes/", StringComparison.Ordinal))
                remote.Add(new BranchFacts(f[0]["refs/remotes/".Length..], true, last, author));
        }
        return (local, remote);
    }

    /// <summary>Everything the Health page shows.</summary>
    public static async Task<HealthReport> LoadAsync(GitRunner git, string path, DateTimeOffset now, CancellationToken ct = default)
    {
        var branchesTask = AssessBranchesAsync(git, path, now, ct);
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

        var (trunk, targets, local, remote) = await branchesTask.ConfigureAwait(false);
        var commits = int.TryParse((await countTask.ConfigureAwait(false)).StandardOutput.Trim(), out var c) ? c : 0;

        var (last30, contributors) = ParseRecent((await recentTask.ConfigureAwait(false)).StandardOutput, now);
        var objects = ParseCountObjects((await objectsTask.ConfigureAwait(false)).StandardOutput);
        var stashResult = await stashTask.ConfigureAwait(false);
        var stashes = stashResult.Succeeded ? stashResult.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length : 0;
        var worktreeResult = await worktreeTask.ConfigureAwait(false);
        var worktrees = worktreeResult.Succeeded ? worktreeResult.StandardOutput.Split('\0').Count(l => l.StartsWith("worktree ", StringComparison.Ordinal)) : 1;

        DateTimeOffset? lastFetch = null;
        var gitDir = (await gitDirTask.ConfigureAwait(false)).StandardOutput.Trim();
        if (gitDir.Length > 0 && File.Exists(Path.Combine(gitDir, "FETCH_HEAD")))
            lastFetch = File.GetLastWriteTimeUtc(Path.Combine(gitDir, "FETCH_HEAD"));

        var remotes = ParseRemoteUrls((await remotesTask.ConfigureAwait(false)).StandardOutput);
        var version = (await versionTask.ConfigureAwait(false)).StandardOutput.Trim() is { Length: > 0 } v ? v.Replace("git version ", "") : null;
        var userName = Trimmed(await nameTask.ConfigureAwait(false));
        var userEmail = Trimmed(await emailTask.ConfigureAwait(false));
        DateTimeOffset? lastCommit = long.TryParse((await lastCommitTask.ConfigureAwait(false)).StandardOutput.Trim(), out var lc)
            ? DateTimeOffset.FromUnixTimeSeconds(lc) : null;

        var stats = new RepositoryStats(commits, last30, contributors, objects.SizeKiB, objects.Loose, objects.Packs, stashes, worktrees, lastFetch, lastCommit);
        var findings = Diagnose(stats, local, remote, remotes, userName, userEmail, trunk, now);
        return new HealthReport(trunk, targets, local, remote, stats, remotes, version, userName, userEmail, findings);

        static string? Trimmed(GitResult r) => r.Succeeded && r.StandardOutput.Trim() is { Length: > 0 } s ? s : null;
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

    internal static IReadOnlyList<HealthFinding> Diagnose(
        RepositoryStats stats, IReadOnlyList<BranchAssessment> local, IReadOnlyList<BranchAssessment> remote,
        IReadOnlyList<RemoteUrl> remotes, string? userName, string? userEmail, string? trunk, DateTimeOffset now)
    {
        var findings = new List<HealthFinding>();

        var localCandidates = local.Count(b => b.IsCleanupCandidate);
        var remoteCandidates = remote.Count(b => b.IsCleanupCandidate);
        if (localCandidates + remoteCandidates > 0)
            findings.Add(new(FindingLevel.Attention, $"{Plural(localCandidates + remoteCandidates, "branch", "branches")} could be cleaned up",
                $"{localCandidates} local, {remoteCandidates} on the remote — merged, deleted upstream, or untouched for {DescribeAge(AbandonedAfter)}. See Branch cleanup below."));
        else
            findings.Add(new(FindingLevel.Good, "No stale branches", "Every branch is either active or a long-lived integration branch."));

        if (userName is null || userEmail is null)
            findings.Add(new(FindingLevel.Attention, "Your Git identity isn't set",
                "Commits need user.name and user.email. Set them with git config --global user.name / user.email."));

        if (remotes.Count == 0)
            findings.Add(new(FindingLevel.Info, "No remote", "This repository isn't connected to a server, so nothing is backed up or shared."));
        else if (stats.LastFetch is not { } fetched)
            findings.Add(new(FindingLevel.Attention, "Never fetched", "Fetch to see your team's latest branches and to make cleanup decisions on current data."));
        else if (now - fetched > TimeSpan.FromDays(7))
            findings.Add(new(FindingLevel.Attention, $"Last fetched {DescribeAge(now - fetched)} ago",
                "Remote branches below may be out of date. Fetch (it also prunes branches deleted on the server)."));
        else
            findings.Add(new(FindingLevel.Good, "Remote data is fresh", $"Last fetched {(now - fetched).TotalHours switch { < 1 => "under an hour", < 48 => $"{(int)(now - fetched).TotalHours} hours", _ => DescribeAge(now - fetched) }} ago."));

        if (trunk is null && remotes.Count > 0)
            findings.Add(new(FindingLevel.Info, "No main branch found",
                "Couldn't find origin/HEAD, main or master, so merged branches can't be detected. Run git remote set-head origin --auto."));

        if (stats.Stashes >= 5)
            findings.Add(new(FindingLevel.Attention, $"{stats.Stashes} stashes",
                "Stashes are easy to forget. Apply what you still need and drop the rest."));

        if (stats.LooseObjects > 5000 || stats.Packs > 50)
            findings.Add(new(FindingLevel.Info, "Repository could be optimized",
                $"{stats.LooseObjects:N0} loose objects in {stats.Packs} packs. git maintenance run --task=gc compacts them (Git also does this on its own eventually)."));

        if (stats.SizeKiB > 2 * 1024 * 1024)
            findings.Add(new(FindingLevel.Info, $"Large repository ({FormatSize(stats.SizeKiB)})",
                "Big binaries in history slow down clones. Git LFS keeps them out of the repository."));

        return findings;
    }

    public static string FormatSize(long kib) => kib switch
    {
        < 1024 => $"{kib} KB",
        < 1024 * 1024 => $"{kib / 1024.0:0.#} MB",
        _ => $"{kib / 1024.0 / 1024.0:0.##} GB",
    };

    private static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
}
