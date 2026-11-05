using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;

namespace CentralGit.Infrastructure.Git;

/// <summary>A file's commits across renames (<c>git log --follow</c>), and the tracked files to pick from.</summary>
public static class FileHistoryReader
{
    public static async Task<IReadOnlyList<FileRevision>> ReadAsync(
        GitRunner git,
        string path,
        string file,
        int limit,
        CancellationToken ct = default
    )
    {
        var output = await git.RunCheckedAsync(
                path,
                [
                    "log",
                    "--follow",
                    "-M",
                    $"--max-count={limit}",
                    "--format=%x1e%H%x1f%P%x1f%an%x1f%at%x1f%s",
                    "--name-status",
                    "-z",
                    "--",
                    file,
                ],
                ct
            )
            .ConfigureAwait(false);
        return Parse(output);
    }

    /// <summary>Records are "\x1e" + fields + "\0\n" + name-status entries ("M\0path\0", "R087\0old\0new\0").</summary>
    internal static IReadOnlyList<FileRevision> Parse(string output)
    {
        var revisions = new List<FileRevision>();
        foreach (var record in output.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
        {
            var end = record.IndexOf('\0');
            var header = (end < 0 ? record : record[..end]).Split('\u001f');
            if (header.Length < 5)
                continue;
            var changes = end < 0 ? [] : CommitDetailsReader.ParseNameStatus(record[(end + 1)..].TrimStart('\n'));
            if (changes.Count == 0)
                continue; // e.g. a merge that git lists without a change to this file
            var (kind, file, oldPath) = changes[0];
            var parent = header[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var when = long.TryParse(header[3], out var unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix)
                : DateTimeOffset.MinValue;
            revisions.Add(new FileRevision(header[0], parent, file, oldPath, kind, header[2], when, header[4]));
        }
        return revisions;
    }

    /// <summary>Every tracked file at HEAD, as repository-relative paths with forward slashes.</summary>
    public static async Task<IReadOnlyList<string>> ListFilesAsync(GitRunner git, string path, CancellationToken ct)
    {
        var result = await git.RunAsync(path, ["ls-files", "-z"], ct).ConfigureAwait(false);
        return result.Succeeded ? result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries) : [];
    }
}
