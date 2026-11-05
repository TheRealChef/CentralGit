using System.Globalization;
using CentralGit.Application.Git;
using CentralGit.Domain.Recovery;

namespace CentralGit.Infrastructure.Git;

/// <summary>The HEAD reflog and which of its commits no ref holds any more.</summary>
public static class ReflogReader
{
    public static async Task<IReadOnlyList<ReflogEntry>> ReadAsync(
        GitRunner git,
        string path,
        int limit,
        CancellationToken ct = default
    )
    {
        // --date=unix turns the selector into HEAD@{<seconds>}; the entry's index is its position.
        var result = await git.RunAsync(
                path,
                [
                    "log",
                    "-g",
                    $"--max-count={limit}",
                    "--date=unix",
                    "--format=%H%x1f%gd%x1f%gs%x1f%s%x1f%an%x1e",
                    "HEAD",
                ],
                ct
            )
            .ConfigureAwait(false);
        // A repository without commits has no HEAD reflog: nothing to recover, not an error.
        return result.Succeeded ? Parse(result.StandardOutput) : [];
    }

    internal static IReadOnlyList<ReflogEntry> Parse(string output)
    {
        var entries = new List<ReflogEntry>();
        foreach (var record in output.Split('\u001e'))
        {
            var f = record.Trim('\r', '\n').Split('\u001f');
            if (f.Length < 5 || f[0].Length == 0)
                continue;
            entries.Add(new ReflogEntry(entries.Count, f[0], ParseWhen(f[1]), f[2], f[3], f[4]));
        }
        return entries;
    }

    private static DateTimeOffset ParseWhen(string selector)
    {
        var open = selector.IndexOf("@{", StringComparison.Ordinal);
        return
            open >= 0
            && long.TryParse(
                selector.AsSpan(open + 2).TrimEnd('}'),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var unix
            )
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : DateTimeOffset.MinValue;
    }

    /// <summary>Of <paramref name="hashes"/>, those no branch, tag, remote branch or stash contains.</summary>
    public static async Task<IReadOnlySet<string>> FindUnreachableAsync(
        GitRunner git,
        string path,
        IEnumerable<string> hashes,
        CancellationToken ct = default
    )
    {
        var wanted = hashes.Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
            return new HashSet<string>();
        var result = await git.RunAsync(
                path,
                ["rev-list", "--stdin", "--not", "--all"],
                ct,
                string.Join('\n', wanted) + "\n"
            )
            .ConfigureAwait(false);
        if (!result.Succeeded)
            throw new GitException(result);
        var lost = result
            .StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        lost.IntersectWith(wanted);
        return lost;
    }
}
