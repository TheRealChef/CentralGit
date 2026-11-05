namespace CentralGit.Domain.Branches;

public sealed record LocalBranch(
    string Name,
    string? Upstream,
    int Ahead,
    int Behind,
    bool IsHead,
    bool UpstreamGone,
    DateTimeOffset? LastCommit = null,
    string? Tip = null
);

/// <summary>A remote-tracking branch, e.g. <c>origin/feature/x</c> (Remote "origin", ShortName "feature/x").</summary>
public sealed record RemoteBranch(string Name, string Remote, string ShortName, DateTimeOffset? LastCommit);

/// <summary>HEAD and how it relates to its upstream.</summary>
public sealed record BranchStatus(string? Head, string? Oid, string? Upstream, int Ahead, int Behind)
{
    public bool IsDetached => Head is null;
}

/// <summary>Where each commit stands relative to the current branch's upstream.</summary>
public enum CommitSync
{
    None,
    Incoming,
    Outgoing,
}

/// <summary>Incoming (fetched, not in HEAD) and outgoing (in HEAD, not pushed) commit hashes.</summary>
public sealed record SyncSets(string? Upstream, IReadOnlySet<string> Incoming, IReadOnlySet<string> Outgoing)
{
    public static readonly SyncSets Empty = new(null, new HashSet<string>(), new HashSet<string>());

    public CommitSync Of(string hash) =>
        Incoming.Contains(hash) ? CommitSync.Incoming
        : Outgoing.Contains(hash) ? CommitSync.Outgoing
        : CommitSync.None;
}
