namespace CentralGit.Domain.Health;

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
    string? Upstream = null
);

public sealed record BranchAssessment(
    BranchFacts Branch,
    BranchVerdict Verdict,
    string? MergedInto,
    TimeSpan? Age,
    bool IsProtected
)
{
    public string Name => Branch.Name;

    /// <summary>Something the user can delete without a second thought (or with one, for abandoned work).</summary>
    public bool IsCleanupCandidate =>
        !IsProtected
        && !Branch.IsHead
        && Verdict is BranchVerdict.Merged or BranchVerdict.Gone or BranchVerdict.Abandoned;

    /// <summary>Deleting it can't lose commits (they're in <see cref="MergedInto"/>).</summary>
    public bool IsSafeToDelete => Verdict == BranchVerdict.Merged;
}

public sealed record RemoteUrl(string Name, string Url);

public enum FindingLevel
{
    Good,
    Info,
    Attention,
}

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
    DateTimeOffset? LastCommit
);

/// <summary>Every branch as read from the refs, plus where <c>origin/HEAD</c> points (the remote's default branch).</summary>
public sealed record BranchRefs(
    IReadOnlyList<BranchFacts> Local,
    IReadOnlyList<BranchFacts> Remote,
    string? OriginHead
);

/// <summary>The branches judged: the trunk, the integration branches checked against, and a verdict per branch.</summary>
public sealed record BranchOverview(
    string? Trunk,
    IReadOnlyList<string> Targets,
    IReadOnlyList<BranchAssessment> Local,
    IReadOnlyList<BranchAssessment> Remote
);

/// <summary>Numbers and configuration read from the repository, before any judgement.</summary>
public sealed record RepositoryFacts(
    RepositoryStats Stats,
    IReadOnlyList<RemoteUrl> Remotes,
    string? GitVersion,
    string? UserName,
    string? UserEmail
);

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
    IReadOnlyList<HealthFinding> Findings
);

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
    [
        "main",
        "master",
        "develop",
        "development",
        "dev",
        "trunk",
        "staging",
        "production",
        "prod",
        "release",
    ];

    public static bool IsIntegrationName(string shortName) =>
        IntegrationNames.Contains(shortName, StringComparer.OrdinalIgnoreCase)
        || shortName.StartsWith("release/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Judges each branch. <paramref name="mergedInto"/> maps a full ref name (refs/heads/x,
    /// refs/remotes/origin/x) to the integration branch that already contains it.
    /// </summary>
    public static IReadOnlyList<BranchAssessment> Assess(
        IEnumerable<BranchFacts> branches,
        IReadOnlyDictionary<string, string> mergedInto,
        DateTimeOffset now
    )
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

            var verdict =
                isProtected ? BranchVerdict.Active
                : into is not null ? BranchVerdict.Merged
                : b.UpstreamGone ? BranchVerdict.Gone
                : age >= AbandonedAfter ? BranchVerdict.Abandoned
                : age >= AgingAfter ? BranchVerdict.Aging
                : BranchVerdict.Active;
            result.Add(
                new BranchAssessment(b, verdict, verdict == BranchVerdict.Merged ? into : null, age, isProtected)
            );
        }
        return result;
    }

    /// <summary>"3 days", "5 weeks", "4 months", "2 years".</summary>
    public static string DescribeAge(TimeSpan age) =>
        age.TotalDays switch
        {
            < 1 => "today",
            < 2 => "1 day",
            < 14 => $"{(int)age.TotalDays} days",
            < 60 => $"{(int)(age.TotalDays / 7)} weeks",
            < 730 => $"{(int)Math.Round(age.TotalDays / 30.4)} months",
            _ => $"{(int)(age.TotalDays / 365.25)} years",
        };

    /// <summary>Compact form for badges: "5d", "6w", "4mo", "2y".</summary>
    public static string ShortAge(TimeSpan age) =>
        age.TotalDays switch
        {
            < 14 => $"{Math.Max(0, (int)age.TotalDays)}d",
            < 60 => $"{(int)(age.TotalDays / 7)}w",
            < 730 => $"{(int)(age.TotalDays / 30.4)}mo",
            _ => $"{(int)(age.TotalDays / 365.25)}y",
        };

    // ─── Integration branches and the report ─────────────────────────────

    /// <summary>Integration branches present in the repo, preferring the remote copy (it's the shared truth).</summary>
    public static (string? Trunk, IReadOnlyList<string> Targets) PickIntegrationBranches(
        IReadOnlyCollection<string> localNames,
        IReadOnlyCollection<string> remoteNames,
        string? originHead
    )
    {
        var targets = new List<string>();
        foreach (var name in IntegrationNames)
        {
            var remote =
                remoteNames.FirstOrDefault(r => r.Equals("origin/" + name, StringComparison.OrdinalIgnoreCase))
                ?? remoteNames.FirstOrDefault(r =>
                    r.IndexOf('/') is var s and > 0 && r[(s + 1)..].Equals(name, StringComparison.OrdinalIgnoreCase)
                );
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

    /// <summary>The integration branches to check <paramref name="refs"/> against, trunk first.</summary>
    public static (string? Trunk, IReadOnlyList<string> Targets) PickIntegrationBranches(BranchRefs refs) =>
        PickIntegrationBranches(
            refs.Local.Select(b => b.Name).ToList(),
            refs.Remote.Select(b => b.Name).ToList(),
            refs.OriginHead
        );

    /// <summary>
    /// Judges every branch against the integration branches. <paramref name="mergedByTarget"/> holds, per
    /// target from <see cref="PickIntegrationBranches(BranchRefs)"/>, the full ref names it already contains.
    /// </summary>
    public static BranchOverview Review(
        BranchRefs refs,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> mergedByTarget,
        DateTimeOffset now
    )
    {
        var (trunk, targets) = PickIntegrationBranches(refs);
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var target in targets)
            if (mergedByTarget.TryGetValue(target, out var refNames))
                foreach (var refName in refNames)
                    merged.TryAdd(refName, target); // trunk first: it's what "merged into" names when a branch is in several
        return new BranchOverview(trunk, targets, Assess(refs.Local, merged, now), Assess(refs.Remote, merged, now));
    }

    /// <summary>Everything the Health page shows, from the branch review and the repository's raw facts.</summary>
    public static HealthReport Report(BranchOverview branches, RepositoryFacts facts, DateTimeOffset now) =>
        new(
            branches.Trunk,
            branches.Targets,
            branches.Local,
            branches.Remote,
            facts.Stats,
            facts.Remotes,
            facts.GitVersion,
            facts.UserName,
            facts.UserEmail,
            Diagnose(
                facts.Stats,
                branches.Local,
                branches.Remote,
                facts.Remotes,
                facts.UserName,
                facts.UserEmail,
                branches.Trunk,
                now
            )
        );

    public static IReadOnlyList<HealthFinding> Diagnose(
        RepositoryStats stats,
        IReadOnlyList<BranchAssessment> local,
        IReadOnlyList<BranchAssessment> remote,
        IReadOnlyList<RemoteUrl> remotes,
        string? userName,
        string? userEmail,
        string? trunk,
        DateTimeOffset now
    )
    {
        var findings = new List<HealthFinding>();

        var localCandidates = local.Count(b => b.IsCleanupCandidate);
        var remoteCandidates = remote.Count(b => b.IsCleanupCandidate);
        if (localCandidates + remoteCandidates > 0)
            findings.Add(
                new(
                    FindingLevel.Attention,
                    $"{Plural(localCandidates + remoteCandidates, "branch", "branches")} could be cleaned up",
                    $"{localCandidates} local, {remoteCandidates} on the remote — merged, deleted upstream, or untouched for {DescribeAge(AbandonedAfter)}. See Branch cleanup below."
                )
            );
        else
            findings.Add(
                new(
                    FindingLevel.Good,
                    "No stale branches",
                    "Every branch is either active or a long-lived integration branch."
                )
            );

        if (userName is null || userEmail is null)
            findings.Add(
                new(
                    FindingLevel.Attention,
                    "Your Git identity isn't set",
                    "Commits need user.name and user.email. Set them with git config --global user.name / user.email."
                )
            );

        if (remotes.Count == 0)
            findings.Add(
                new(
                    FindingLevel.Info,
                    "No remote",
                    "This repository isn't connected to a server, so nothing is backed up or shared."
                )
            );
        else if (stats.LastFetch is not { } fetched)
            findings.Add(
                new(
                    FindingLevel.Attention,
                    "Never fetched",
                    "Fetch to see your team's latest branches and to make cleanup decisions on current data."
                )
            );
        else if (now - fetched > TimeSpan.FromDays(7))
            findings.Add(
                new(
                    FindingLevel.Attention,
                    $"Last fetched {DescribeAge(now - fetched)} ago",
                    "Remote branches below may be out of date. Fetch (it also prunes branches deleted on the server)."
                )
            );
        else
            findings.Add(
                new(
                    FindingLevel.Good,
                    "Remote data is fresh",
                    $"Last fetched {(now - fetched).TotalHours switch { < 1 => "under an hour", < 48 => $"{(int)(now - fetched).TotalHours} hours", _ => DescribeAge(now - fetched) }} ago."
                )
            );

        if (trunk is null && remotes.Count > 0)
            findings.Add(
                new(
                    FindingLevel.Info,
                    "No main branch found",
                    "Couldn't find origin/HEAD, main or master, so merged branches can't be detected. Run git remote set-head origin --auto."
                )
            );

        if (stats.Stashes >= 5)
            findings.Add(
                new(
                    FindingLevel.Attention,
                    $"{stats.Stashes} stashes",
                    "Stashes are easy to forget. Apply what you still need and drop the rest."
                )
            );

        if (stats.LooseObjects > 5000 || stats.Packs > 50)
            findings.Add(
                new(
                    FindingLevel.Info,
                    "Repository could be optimized",
                    $"{stats.LooseObjects:N0} loose objects in {stats.Packs} packs. git maintenance run --task=gc compacts them (Git also does this on its own eventually)."
                )
            );

        if (stats.SizeKiB > 2 * 1024 * 1024)
            findings.Add(
                new(
                    FindingLevel.Info,
                    $"Large repository ({FormatSize(stats.SizeKiB)})",
                    "Big binaries in history slow down clones. Git LFS keeps them out of the repository."
                )
            );

        return findings;
    }

    public static string FormatSize(long kib) =>
        kib switch
        {
            < 1024 => $"{kib} KB",
            < 1024 * 1024 => $"{kib / 1024.0:0.#} MB",
            _ => $"{kib / 1024.0 / 1024.0:0.##} GB",
        };

    private static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
}
