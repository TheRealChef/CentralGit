using CentralGit.Application.Git;
using CentralGit.Domain.Commits;

namespace CentralGit.Infrastructure.Git;

/// <summary>Compares two refs: commits on each side and the files the head side changes since their merge base.</summary>
public static class CompareReader
{
    public static async Task<Comparison> CompareAsync(
        GitRunner git,
        string path,
        string baseRef,
        string headRef,
        int limit,
        CancellationToken ct = default
    )
    {
        // Resolve both names first; everything after works on hashes, so no name is read as an option.
        var baseHash = await ResolveAsync(git, path, baseRef, ct).ConfigureAwait(false);
        var headHash = await ResolveAsync(git, path, headRef, ct).ConfigureAwait(false);

        var mergeBaseResult = await git.RunAsync(path, ["merge-base", baseHash, headHash], ct).ConfigureAwait(false);
        var mergeBase = mergeBaseResult.Succeeded ? mergeBaseResult.StandardOutput.Trim() : null;

        var countsTask = git.RunCheckedAsync(
            path,
            ["rev-list", "--left-right", "--count", $"{baseHash}...{headHash}"],
            ct
        );
        var aheadTask = LogAsync(git, path, $"{baseHash}..{headHash}", limit, ct);
        var behindTask = LogAsync(git, path, $"{headHash}..{baseHash}", limit, ct);
        var from = mergeBase ?? baseHash;
        var nameStatusTask = git.RunCheckedAsync(path, ["diff", "-M", "-z", "--name-status", from, headHash], ct);
        var numstatTask = git.RunCheckedAsync(path, ["diff", "-M", "-z", "--numstat", from, headHash], ct);

        var (behindCount, aheadCount) = ParseCounts(await countsTask.ConfigureAwait(false));
        var files = CommitDetailsReader.MergeFileLists(
            CommitDetailsReader.ParseNameStatus(await nameStatusTask.ConfigureAwait(false)),
            CommitDetailsReader.ParseNumstat(await numstatTask.ConfigureAwait(false))
        );

        return new Comparison(
            baseRef,
            headRef,
            baseHash,
            headHash,
            mergeBase,
            await aheadTask.ConfigureAwait(false),
            await behindTask.ConfigureAwait(false),
            aheadCount,
            behindCount,
            files
        );
    }

    private static async Task<string> ResolveAsync(GitRunner git, string path, string name, CancellationToken ct)
    {
        var result = await git.RunAsync(
                path,
                ["rev-parse", "--verify", "--quiet", "--end-of-options", name + "^{commit}"],
                ct
            )
            .ConfigureAwait(false);
        return result.Succeeded && result.StandardOutput.Trim() is { Length: > 0 } hash
            ? hash
            : throw new GitException(result with { StandardError = $"'{name}' isn't a branch, tag or commit here." });
    }

    private static async Task<IReadOnlyList<CommitInfo>> LogAsync(
        GitRunner git,
        string path,
        string range,
        int limit,
        CancellationToken ct
    )
    {
        var output = await git.RunCheckedAsync(
                path,
                [
                    "log",
                    "--topo-order",
                    "--decorate=full",
                    $"--format={CommitLog.Format}",
                    $"--max-count={limit}",
                    range,
                ],
                ct
            )
            .ConfigureAwait(false);
        return output.Split('\u001e').Select(CommitLog.Parse).OfType<CommitInfo>().ToList();
    }

    /// <summary><c>rev-list --left-right --count A...B</c>: "only-in-A\tonly-in-B".</summary>
    internal static (int Left, int Right) ParseCounts(string output)
    {
        var parts = output.Trim().Split('\t', ' ');
        return parts.Length >= 2 && int.TryParse(parts[0], out var left) && int.TryParse(parts[^1], out var right)
            ? (left, right)
            : (0, 0);
    }
}
