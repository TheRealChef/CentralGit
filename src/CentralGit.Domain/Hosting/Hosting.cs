namespace CentralGit.Domain.Hosting;

/// <summary>A repository on a code host (GitHub), worked out from a remote URL.</summary>
public sealed record HostedRepository(string Host, string Owner, string Name)
{
    public string WebUrl => $"https://{Host}/{Owner}/{Name}";

    /// <summary>GitHub's "open a pull request" page for <paramref name="head"/> into <paramref name="base"/>.</summary>
    public string CompareUrl(string @base, string head) =>
        $"{WebUrl}/compare/{Escape(@base)}...{Escape(head)}?expand=1";

    public string CommitUrl(string hash) => $"{WebUrl}/commit/{hash}";

    /// <summary>
    /// https://github.com/o/r(.git), git@github.com:o/r(.git) and ssh://git@github.com/o/r(.git);
    /// null for anything else (other hosts aren't supported yet).
    /// </summary>
    public static HostedRepository? Parse(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
            return null;
        var url = remoteUrl.Trim();
        string host,
            path;
        if (url.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return null;
            host = uri.Host;
            path = uri.AbsolutePath;
        }
        else if (url.IndexOf(':') is var colon and > 0 && !url.Contains('\\'))
        {
            // scp-like: [user@]host:owner/name
            host = url[..colon];
            host = host[(host.LastIndexOf('@') + 1)..];
            path = url[(colon + 1)..];
        }
        else
        {
            return null;
        }
        if (!host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return null;
        var parts = path.Trim('/').Split('/');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return null;
        var name = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        return name.Length == 0 ? null : new HostedRepository("github.com", parts[0], name);
    }

    /// <summary>Branch names keep their slashes; everything else that needs escaping is escaped.</summary>
    private static string Escape(string branch) => string.Join('/', branch.Split('/').Select(Uri.EscapeDataString));
}

public enum CheckState
{
    None,
    Pending,
    Success,
    Failure,
}

/// <summary>An open pull request for a branch.</summary>
public sealed record PullRequest(int Number, string Title, string Url, bool IsDraft, string Author, string BaseBranch);

/// <summary>One CI check run: its status (queued/in_progress/completed) and, once completed, its conclusion.</summary>
public sealed record CheckRun(string Name, string Status, string? Conclusion);

public static class Checks
{
    /// <summary>
    /// Any failure wins; otherwise anything unfinished means pending; otherwise success.
    /// Neutral and skipped runs count as passing; no runs at all is None.
    /// </summary>
    public static CheckState Combine(IEnumerable<CheckRun> runs)
    {
        var list = runs.ToList();
        if (list.Count == 0)
            return CheckState.None;
        if (
            list.Any(r =>
                r.Conclusion is "failure" or "timed_out" or "cancelled" or "action_required" or "startup_failure"
            )
        )
            return CheckState.Failure;
        if (list.Any(r => r.Status != "completed"))
            return CheckState.Pending;
        return CheckState.Success;
    }
}
