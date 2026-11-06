using System.Globalization;
using CentralGit.Application.Git;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Workflow;

namespace CentralGit.Infrastructure.Git;

/// <summary>Reads stopped operations, tags, the stash and blame; resolves conflicts by taking a side.</summary>
public static class WorkflowReader
{
    // Marker files git leaves in the git directory while an operation waits for the user.
    private static readonly (string File, InProgress State)[] Markers =
    [
        ("rebase-merge", InProgress.Rebase),
        ("rebase-apply", InProgress.Rebase),
        ("MERGE_HEAD", InProgress.Merge),
        ("CHERRY_PICK_HEAD", InProgress.CherryPick),
        ("REVERT_HEAD", InProgress.Revert),
    ];

    public static async Task<InProgress> GetInProgressAsync(GitRunner git, string path, CancellationToken ct = default)
    {
        // --git-path resolves each marker inside the real git directory (worktrees and submodules included).
        var result = await git.RunAsync(
                path,
                ["rev-parse", .. Markers.SelectMany(m => new[] { "--git-path", m.File })],
                ct
            )
            .ConfigureAwait(false);
        if (!result.Succeeded)
            return InProgress.None;
        var paths = result.StandardOutput.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        for (var i = 0; i < Math.Min(paths.Length, Markers.Length); i++)
        {
            var full = Path.IsPathRooted(paths[i]) ? paths[i] : Path.Combine(path, paths[i]);
            if (File.Exists(full) || Directory.Exists(full))
                return Markers[i].State;
        }
        return InProgress.None;
    }

    public static async Task<GitResult> ResolveConflictAsync(
        GitRunner git,
        string path,
        string file,
        ConflictSide side,
        CancellationToken ct = default
    )
    {
        var literal = ":(literal)" + file;
        var take = await git.RunAsync(
                path,
                ["checkout", side == ConflictSide.Ours ? "--ours" : "--theirs", "--", literal],
                ct
            )
            .ConfigureAwait(false);
        // A side that deleted the file has nothing to check out: resolving means removing it.
        if (!take.Succeeded && take.StandardError.Contains("does not have", StringComparison.OrdinalIgnoreCase))
            return await git.RunAsync(path, ["rm", "-q", "--", literal], ct).ConfigureAwait(false);
        return take.Succeeded ? await git.RunAsync(path, ["add", "--", literal], ct).ConfigureAwait(false) : take;
    }

    /// <summary>The working-tree file as UTF-8 text (BOM stripped); null when it is missing or looks binary.</summary>
    public static async Task<string?> ReadWorkingFileAsync(string path, string file, CancellationToken ct = default)
    {
        var full = Path.Combine(path, file);
        if (!File.Exists(full))
            return null;
        var bytes = await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false);
        if (Array.IndexOf(bytes, (byte)0) >= 0)
            return null; // NUL bytes: binary (or UTF-16, which conflict markers don't work with anyway)
        var start = HasBom(bytes) ? 3 : 0;
        return System.Text.Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }

    /// <summary>Writes the resolved text (keeping a UTF-8 BOM if the file had one), then stages it.</summary>
    public static async Task<GitResult> SaveResolvedFileAsync(
        GitRunner git,
        string path,
        string file,
        string text,
        CancellationToken ct = default
    )
    {
        var full = Path.Combine(path, file);
        var bom = File.Exists(full) && HasBom(await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false));
        await File.WriteAllTextAsync(full, text, new System.Text.UTF8Encoding(bom), ct).ConfigureAwait(false);
        return await git.RunAsync(path, ["add", "--", ":(literal)" + file], ct).ConfigureAwait(false);
    }

    private static bool HasBom(byte[] bytes) => bytes is [0xEF, 0xBB, 0xBF, ..];

    /// <summary>
    /// Read from refs, not git's messages: <c>refs/bisect/bad</c>, <c>refs/bisect/good-*</c>, and the
    /// candidates still between them (<c>rev-list --count bad --not goods</c>).
    /// </summary>
    public static async Task<BisectState> GetBisectAsync(GitRunner git, string path, CancellationToken ct = default)
    {
        var start = await git.RunAsync(path, ["rev-parse", "--git-path", "BISECT_START"], ct).ConfigureAwait(false);
        if (!start.Succeeded || !File.Exists(Path.Combine(path, start.StandardOutput.Trim())))
            return BisectState.Inactive;
        var refs = await git.RunCheckedAsync(
                path,
                ["for-each-ref", "--format=%(refname)%1f%(objectname)", "refs/bisect/"],
                ct
            )
            .ConfigureAwait(false);
        string? bad = null;
        var good = new List<string>();
        foreach (var line in refs.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split('\x1f');
            if (fields.Length < 2)
                continue;
            if (fields[0] == "refs/bisect/bad")
                bad = fields[1];
            else if (fields[0].StartsWith("refs/bisect/good-", StringComparison.Ordinal))
                good.Add(fields[1]);
        }
        var head = await git.RunAsync(path, ["rev-parse", "--verify", "-q", "HEAD"], ct).ConfigureAwait(false);
        var candidates = 0;
        if (bad is not null && good.Count > 0)
        {
            var count = await git.RunCheckedAsync(path, ["rev-list", "--count", bad, "--not", .. good], ct)
                .ConfigureAwait(false);
            candidates = int.TryParse(count.Trim(), CultureInfo.InvariantCulture, out var n) ? n : 0;
        }
        return new BisectState(true, bad, good, head.Succeeded ? head.StandardOutput.Trim() : null, candidates);
    }

    public static async Task<RebaseRange> GetRebaseRangeAsync(
        GitRunner git,
        string path,
        string commit,
        CancellationToken ct = default
    )
    {
        var parent = await git.RunAsync(path, ["rev-parse", "--verify", "-q", commit + "^"], ct).ConfigureAwait(false);
        var @base = parent.Succeeded ? parent.StandardOutput.Trim() : null;
        var output = await git.RunCheckedAsync(
                path,
                ["log", "--reverse", "--format=%H%x1f%P%x1f%s%x1e", @base is null ? "HEAD" : $"{@base}..HEAD", "--"],
                ct
            )
            .ConfigureAwait(false);
        var steps = new List<RebaseStep>();
        var hasMerges = false;
        foreach (var record in output.Split('\x1e', StringSplitOptions.TrimEntries))
        {
            var fields = record.Split('\x1f');
            if (fields.Length < 3)
                continue;
            hasMerges |= fields[1].Contains(' ', StringComparison.Ordinal);
            steps.Add(new RebaseStep(fields[0], fields[2]));
        }
        return new RebaseRange(@base, steps, hasMerges);
    }

    public static async Task<IReadOnlyList<Tag>> GetTagsAsync(
        GitRunner git,
        string path,
        CancellationToken ct = default
    )
    {
        var output = await git.RunCheckedAsync(
                path,
                [
                    "for-each-ref",
                    "--sort=-creatordate",
                    "--format=%(refname:short)%1f%(objecttype)%1f%(objectname)%1f%(*objectname)%1f%(creatordate:unix)%1f%(contents:subject)%1e",
                    "refs/tags",
                ],
                ct
            )
            .ConfigureAwait(false);
        return ParseTags(output);
    }

    internal static IReadOnlyList<Tag> ParseTags(string output)
    {
        var tags = new List<Tag>();
        foreach (var record in output.Split('\u001e'))
        {
            var f = record.Trim('\r', '\n').Split('\u001f');
            if (f.Length < 6 || f[0].Length == 0)
                continue;
            var annotated = f[1] == "tag";
            // An annotated tag points at a tag object; the commit is what that object peels to.
            var commit = annotated && f[3].Length > 0 ? f[3] : f[2];
            DateTimeOffset? when = long.TryParse(f[4], NumberStyles.None, CultureInfo.InvariantCulture, out var unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix)
                : null;
            tags.Add(new Tag(f[0], commit, annotated, annotated ? f[5] : "", when));
        }
        return tags;
    }

    public static async Task<bool> IsValidTagNameAsync(GitRunner git, string path, string name, CancellationToken ct) =>
        (await git.RunAsync(path, ["check-ref-format", $"refs/tags/{name}"], ct).ConfigureAwait(false)).Succeeded;

    public static async Task<IReadOnlyList<Stash>> GetStashesAsync(
        GitRunner git,
        string path,
        CancellationToken ct = default
    )
    {
        var result = await git.RunAsync(path, ["stash", "list", "--format=%H%x1f%ct%x1f%gs%x1e"], ct)
            .ConfigureAwait(false);
        return result.Succeeded ? ParseStashes(result.StandardOutput) : [];
    }

    internal static IReadOnlyList<Stash> ParseStashes(string output)
    {
        var stashes = new List<Stash>();
        foreach (var record in output.Split('\u001e'))
        {
            var f = record.Trim('\r', '\n').Split('\u001f');
            if (f.Length < 3 || f[0].Length == 0)
                continue;
            var when = long.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out var unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix)
                : DateTimeOffset.MinValue;
            var (branch, message) = Stash.ParseSubject(f[2]);
            stashes.Add(new Stash(stashes.Count, f[0], message, branch, when));
        }
        return stashes;
    }

    public static async Task<IReadOnlyList<StashFile>> GetStashFilesAsync(
        GitRunner git,
        string path,
        Stash stash,
        CancellationToken ct = default
    )
    {
        // A stash is a merge commit: parent 1 = the commit it was made on, parent 3 (optional) = untracked files.
        var baseRev = stash.Commit + "^1";
        var files = (await ChangedAsync(git, path, baseRev, stash.Commit, ct).ConfigureAwait(false))
            .Select(f => new StashFile(f, baseRev, stash.Commit, IsUntracked: false))
            .ToList();

        var untracked = stash.Commit + "^3";
        var hasUntracked = await git.RunAsync(path, ["rev-parse", "--verify", "--quiet", untracked + "^{commit}"], ct)
            .ConfigureAwait(false);
        if (hasUntracked.Succeeded)
        {
            var emptyTree = (
                await git.RunCheckedAsync(path, ["hash-object", "-t", "tree", "--stdin"], ct, standardInput: "")
                    .ConfigureAwait(false)
            ).Trim();
            files.AddRange(
                (await ChangedAsync(git, path, emptyTree, untracked, ct).ConfigureAwait(false)).Select(
                    f => new StashFile(f, emptyTree, untracked, IsUntracked: true)
                )
            );
        }
        return files.OrderBy(f => f.File.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<IReadOnlyList<ChangedFile>> ChangedAsync(
        GitRunner git,
        string path,
        string from,
        string to,
        CancellationToken ct
    )
    {
        var nameStatus = git.RunCheckedAsync(path, ["diff", "-M", "-z", "--name-status", from, to], ct);
        var numstat = git.RunCheckedAsync(path, ["diff", "-M", "-z", "--numstat", from, to], ct);
        return CommitDetailsReader.MergeFileLists(
            CommitDetailsReader.ParseNameStatus(await nameStatus.ConfigureAwait(false)),
            CommitDetailsReader.ParseNumstat(await numstat.ConfigureAwait(false))
        );
    }

    public static async Task<IReadOnlyList<BlameLine>> BlameAsync(
        GitRunner git,
        string path,
        string file,
        string? revision,
        CancellationToken ct = default
    )
    {
        List<string> args = ["blame", "--porcelain"];
        if (revision is not null)
            args.Add(revision);
        args.AddRange(["--", file]);
        return ParseBlame(await git.RunCheckedAsync(path, args, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// <c>blame --porcelain</c>: per line a "hash orig final [count]" header, the commit's details
    /// the first time that commit appears, then the line itself after a tab.
    /// </summary>
    internal static IReadOnlyList<BlameLine> ParseBlame(string output)
    {
        var commits = new Dictionary<string, (string Author, DateTimeOffset When, string Summary)>(
            StringComparer.Ordinal
        );
        var lines = new List<BlameLine>();
        string? hash = null;
        var final = 0;
        string author = "",
            summary = "";
        var when = DateTimeOffset.MinValue;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith('\t'))
            {
                if (hash is null)
                    continue;
                if (!commits.TryGetValue(hash, out var info))
                    commits[hash] = info = (author, when, summary);
                lines.Add(new BlameLine(final, hash, info.Author, info.When, info.Summary, line[1..]));
                hash = null;
                continue;
            }
            var space = line.IndexOf(' ');
            var key = space < 0 ? line : line[..space];
            var value = space < 0 ? "" : line[(space + 1)..];
            if (hash is null && key.Length >= 40 && key.All(Uri.IsHexDigit))
            {
                hash = key;
                var parts = value.Split(' ');
                final = parts.Length > 1 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var n) ? n : 0;
                (author, when, summary) = commits.TryGetValue(hash, out var known)
                    ? known
                    : ("", DateTimeOffset.MinValue, "");
                continue;
            }
            switch (key)
            {
                case "author":
                    author = value;
                    break;
                case "author-time" when long.TryParse(value, CultureInfo.InvariantCulture, out var unix):
                    when = DateTimeOffset.FromUnixTimeSeconds(unix);
                    break;
                case "summary":
                    summary = value;
                    break;
            }
        }
        return lines;
    }
}
