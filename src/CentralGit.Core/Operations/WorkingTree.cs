using CentralGit.Core.Diff;
using CentralGit.Core.Git;

namespace CentralGit.Core.Operations;

/// <summary>Which side of the index a working-tree diff shows.</summary>
public enum ChangeSide { Unstaged, Staged }

/// <summary>
/// Staging, unstaging, discarding and committing. Paths always go through <c>:(literal)</c>
/// pathspecs so names with <c>*</c>, <c>?</c> or <c>[</c> mean exactly that file.
/// </summary>
public sealed class WorkingTree(GitRunner git, string repositoryPath)
{
    private static readonly string[] DiffOptions = ["--no-color", "--no-ext-diff", "--no-textconv", "-U3"];

    public static string Literal(string path) => ":(literal)" + path;

    private static IEnumerable<string> Literals(IEnumerable<string> paths) => paths.Select(Literal);

    /// <summary>The diff of one file on one side of the index. Untracked files diff against nothing.</summary>
    public async Task<FileDiff> DiffAsync(FileChange change, ChangeSide side, CancellationToken ct = default)
    {
        if (side == ChangeSide.Unstaged && change.Staged == ChangeKind.Untracked)
        {
            // --no-index exits with 1 when the files differ, which they always do here.
            var result = await git.RunAsync(repositoryPath, ["diff", .. DiffOptions, "--no-index", "--", "/dev/null", change.Path], ct).ConfigureAwait(false);
            if (result.ExitCode is not (0 or 1))
                throw new GitException(result);
            return FileDiff.Parse(result.StandardOutput);
        }

        List<string> args = ["diff", .. DiffOptions];
        if (side == ChangeSide.Staged)
            args.AddRange(["--cached", "-M"]);
        args.AddRange(["--", Literal(change.Path)]);
        if (side == ChangeSide.Staged && change.OriginalPath is { } original)
            args.Add(Literal(original));
        return FileDiff.Parse(await git.RunCheckedAsync(repositoryPath, args, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Added/deleted line counts per path on one side of the index (binary files: null counts).
    /// Untracked files aren't in git's diff; the caller counts their lines.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, LineCounts>> NumstatAsync(ChangeSide side, CancellationToken ct = default)
    {
        List<string> args = ["diff", "--numstat", "-z", "--no-color", "--no-ext-diff", "--no-textconv"];
        if (side == ChangeSide.Staged)
            args.AddRange(["--cached", "-M"]);
        return ParseNumstat(await git.RunCheckedAsync(repositoryPath, args, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Parses <c>git diff --numstat -z</c>: "added\tdeleted\tpath\0", or for a rename
    /// "added\tdeleted\t\0old\0new\0". Binary files report "-" for both counts.
    /// </summary>
    public static IReadOnlyDictionary<string, LineCounts> ParseNumstat(string output)
    {
        var result = new Dictionary<string, LineCounts>(StringComparer.Ordinal);
        var parts = output.Split('\0');
        for (var i = 0; i < parts.Length; i++)
        {
            var fields = parts[i].Split('\t');
            if (fields.Length < 3)
                continue;
            var path = fields[2];
            if (path.Length == 0 && i + 2 < parts.Length)
            {
                i += 2; // rename: old path, then new path
                path = parts[i];
            }
            result[path] = fields[0] == "-" ? LineCounts.Binary : new LineCounts(Count(fields[0]), Count(fields[1]));
        }
        return result;

        static int Count(string s) => int.TryParse(s, out var n) ? n : 0;
    }

    /// <summary>Stages files as they are on disk, deletions included.</summary>
    public Task<GitResult> StageAsync(IEnumerable<string> paths, CancellationToken ct = default) =>
        git.RunAsync(repositoryPath, ["add", "-A", "--", .. Literals(paths)], ct);

    public Task<GitResult> StageAllAsync(CancellationToken ct = default) =>
        git.RunAsync(repositoryPath, ["add", "-A"], ct);

    /// <summary>
    /// Takes files out of the index again. Before the first commit there is no HEAD to restore
    /// from, so the files are removed from the index instead (they stay on disk).
    /// </summary>
    public Task<GitResult> UnstageAsync(IEnumerable<string> paths, bool hasHead, CancellationToken ct = default) =>
        hasHead
            ? git.RunAsync(repositoryPath, ["restore", "--staged", "--", .. Literals(paths)], ct)
            : git.RunAsync(repositoryPath, ["rm", "--cached", "-r", "-q", "--", .. Literals(paths)], ct);

    public Task<GitResult> UnstageAllAsync(bool hasHead, CancellationToken ct = default) =>
        hasHead
            ? git.RunAsync(repositoryPath, ["reset", "-q"], ct)
            : git.RunAsync(repositoryPath, ["rm", "--cached", "-r", "-q", "--", "."], ct);

    /// <summary>
    /// Stages one hunk of an unstaged diff, or unstages one hunk of a staged diff, by applying
    /// just that hunk to the index.
    /// </summary>
    public Task<GitResult> ApplyHunkToIndexAsync(FileDiff diff, int hunk, ChangeSide side, CancellationToken ct = default)
    {
        List<string> args = ["apply", "--cached", "--whitespace=nowarn", "--recount"];
        if (side == ChangeSide.Staged)
            args.Add("--reverse");
        args.Add("-");
        return git.RunAsync(repositoryPath, args, ct, diff.PatchFor(hunk));
    }

    /// <summary>Throws away one unstaged hunk from the working tree. Destructive: ask first.</summary>
    public Task<GitResult> DiscardHunkAsync(FileDiff diff, int hunk, CancellationToken ct = default) =>
        git.RunAsync(repositoryPath, ["apply", "--reverse", "--whitespace=nowarn", "--recount", "-"], ct, diff.PatchFor(hunk));

    /// <summary>
    /// Throws away unstaged changes to a file: tracked files go back to their staged content,
    /// untracked files are deleted. Destructive: ask first.
    /// </summary>
    public Task<GitResult> DiscardAsync(FileChange change, CancellationToken ct = default) =>
        change.Staged == ChangeKind.Untracked
            ? git.RunAsync(repositoryPath, ["clean", "-f", "-q", "--", Literal(change.Path)], ct)
            : git.RunAsync(repositoryPath, ["restore", "--worktree", "--", Literal(change.Path)], ct);

    /// <summary>
    /// Commits the index with <paramref name="message"/> passed on stdin. Cleanup is
    /// "whitespace" so lines starting with # (Markdown headings, issue numbers) survive.
    /// </summary>
    public Task<GitResult> CommitAsync(string message, bool amend, CancellationToken ct = default)
    {
        List<string> args = ["commit", "--cleanup=whitespace", "-F", "-"];
        if (amend)
            args.Add("--amend");
        return git.RunAsync(repositoryPath, args, ct, message);
    }

    /// <summary>The full message of HEAD, for amending; null before the first commit.</summary>
    public async Task<string?> LastMessageAsync(CancellationToken ct = default)
    {
        var result = await git.RunAsync(repositoryPath, ["log", "-1", "--format=%B"], ct).ConfigureAwait(false);
        return result.Succeeded ? result.StandardOutput.TrimEnd() : null;
    }

    /// <summary>Joins a summary and an optional body the way git expects (blank line between).</summary>
    public static string ComposeMessage(string summary, string body)
    {
        // Text boxes on Windows use \r (WinUI) or \r\n for line breaks; git wants \n.
        static string Lf(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');
        summary = Lf(summary).Replace('\n', ' ').Trim();
        body = Lf(body).Trim();
        return body.Length == 0 ? summary : summary + "\n\n" + body;
    }

    /// <summary>Splits a message into summary (first paragraph, joined) and body.</summary>
    public static (string Summary, string Body) SplitMessage(string message)
    {
        message = message.Replace("\r\n", "\n").Trim();
        var gap = message.IndexOf("\n\n", StringComparison.Ordinal);
        return gap < 0
            ? (message.Replace('\n', ' '), "")
            : (message[..gap].Replace('\n', ' '), message[(gap + 2)..].Trim());
    }
}

/// <summary>Lines added and deleted in one file; null counts for binary files.</summary>
public sealed record LineCounts(int? Added, int? Deleted)
{
    public static readonly LineCounts Binary = new(null, null);
    public bool IsBinary => Added is null;
}
