using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Diff;

namespace CentralGit.Infrastructure.Git;

/// <summary>Reads unified diffs (<c>git diff</c> without colour, external tools or textconv).</summary>
public static class DiffParser
{
    /// <summary>Loads the diff of one file between two revisions (or a revision and the empty tree).</summary>
    public static async Task<FileDiff> LoadAsync(
        GitRunner git,
        string repo,
        string baseRev,
        string rev,
        ChangedFile file,
        CancellationToken ct = default
    )
    {
        List<string> args =
        [
            "diff",
            "--no-color",
            "--no-ext-diff",
            "--no-textconv",
            "-M",
            "-U3",
            baseRev,
            rev,
            "--",
            file.Path,
        ];
        if (file.OldPath is { } old)
            args.Add(old);

        return Parse(await git.RunCheckedAsync(repo, args, ct).ConfigureAwait(false));
    }

    private static readonly Regex HunkHeader = new(
        @"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@ ?(.*)$",
        RegexOptions.Compiled
    );

    /// <summary>Parses unified diff output for a single file.</summary>
    public static FileDiff Parse(string text)
    {
        var hunks = new List<DiffHunk>();
        List<DiffLine>? lines = null;
        var raw = new StringBuilder();
        var header = new StringBuilder();
        int oldStart = 0,
            oldCount = 0,
            newStart = 0,
            newCount = 0,
            oldNo = 0,
            newNo = 0;
        var section = "";
        var binary = false;

        void Flush()
        {
            if (lines is not null)
                hunks.Add(
                    new DiffHunk(oldStart, oldCount, newStart, newCount, section, lines) { Raw = raw.ToString() }
                );
            raw.Clear();
        }

        var all = text.Split('\n');
        // The output ends with a newline, so the final element is empty and not a line.
        var count = all.Length > 0 && all[^1].Length == 0 ? all.Length - 1 : all.Length;
        for (var n = 0; n < count; n++)
        {
            var original = all[n];
            var line = original.EndsWith('\r') ? original[..^1] : original;

            var m = HunkHeader.Match(line);
            if (m.Success)
            {
                Flush();
                oldStart = Int(m.Groups[1].Value);
                oldCount = m.Groups[2].Success ? Int(m.Groups[2].Value) : 1;
                newStart = Int(m.Groups[3].Value);
                newCount = m.Groups[4].Success ? Int(m.Groups[4].Value) : 1;
                section = m.Groups[5].Value.Trim();
                (oldNo, newNo) = (oldStart, newStart);
                lines = [];
                raw.Append(original).Append('\n');
                continue;
            }

            if (lines is null)
            {
                if (hunks.Count == 0)
                    header.Append(original).Append('\n');
                if (
                    line.StartsWith("Binary files ", StringComparison.Ordinal)
                    || line.StartsWith("GIT binary patch", StringComparison.Ordinal)
                )
                    binary = true;
                continue;
            }

            if (line.Length == 0)
            {
                // An empty context line always carries its leading space; a bare one is not part of the hunk.
                continue;
            }

            switch (line[0])
            {
                case ' ':
                    lines.Add(new DiffLine(DiffLineKind.Context, oldNo++, newNo++, line[1..]));
                    break;
                case '-':
                    lines.Add(new DiffLine(DiffLineKind.Deleted, oldNo++, null, line[1..]));
                    break;
                case '+':
                    lines.Add(new DiffLine(DiffLineKind.Added, null, newNo++, line[1..]));
                    break;
                case '\\':
                    break; // "\ No newline at end of file": kept in Raw only
                default:
                    // Next file header (shouldn't happen for single-file diffs): stop collecting.
                    Flush();
                    lines = null;
                    continue;
            }
            raw.Append(original).Append('\n');
        }

        Flush();
        return new FileDiff(hunks, binary) { FileHeader = header.ToString() };
    }

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
}
