using System.Globalization;
using System.Text.RegularExpressions;
using CentralGit.Core.Git;

namespace CentralGit.Core.Diff;

public enum DiffLineKind { Context, Added, Deleted }

public sealed record DiffLine(DiffLineKind Kind, int? OldNumber, int? NewNumber, string Text);

public sealed record DiffHunk(int OldStart, int OldCount, int NewStart, int NewCount, string Section, IReadOnlyList<DiffLine> Lines)
{
    public string Header => $"@@ -{OldStart},{OldCount} +{NewStart},{NewCount} @@" + (Section.Length > 0 ? "  " + Section : "");
}

public sealed record FileDiff(IReadOnlyList<DiffHunk> Hunks, bool IsBinary)
{
    public static readonly FileDiff Empty = new([], false);

    /// <summary>Loads the diff of one file between two revisions (or a revision and the empty tree).</summary>
    public static async Task<FileDiff> LoadAsync(
        GitRunner git, string repo, string baseRev, string rev, ChangedFile file, CancellationToken ct = default)
    {
        List<string> args = ["diff", "--no-color", "--no-ext-diff", "--no-textconv", "-M", "-U3", baseRev, rev, "--", file.Path];
        if (file.OldPath is { } old)
            args.Add(old);

        return Parse(await git.RunCheckedAsync(repo, args, ct).ConfigureAwait(false));
    }

    private static readonly Regex HunkHeader = new(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@ ?(.*)$", RegexOptions.Compiled);

    /// <summary>Parses unified diff output for a single file. File headers before the first hunk are skipped.</summary>
    public static FileDiff Parse(string text)
    {
        var hunks = new List<DiffHunk>();
        List<DiffLine>? lines = null;
        int oldStart = 0, oldCount = 0, newStart = 0, newCount = 0, oldNo = 0, newNo = 0;
        var section = "";
        var binary = false;

        void Flush()
        {
            if (lines is not null)
                hunks.Add(new DiffHunk(oldStart, oldCount, newStart, newCount, section, lines));
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.EndsWith('\r') ? raw[..^1] : raw;

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
                continue;
            }

            if (lines is null)
            {
                if (line.StartsWith("Binary files ", StringComparison.Ordinal) || line.StartsWith("GIT binary patch", StringComparison.Ordinal))
                    binary = true;
                continue;
            }

            if (line.Length == 0)
            {
                // A bare empty line can only be the trailing newline of the output.
                continue;
            }

            switch (line[0])
            {
                case ' ': lines.Add(new DiffLine(DiffLineKind.Context, oldNo++, newNo++, line[1..])); break;
                case '-': lines.Add(new DiffLine(DiffLineKind.Deleted, oldNo++, null, line[1..])); break;
                case '+': lines.Add(new DiffLine(DiffLineKind.Added, null, newNo++, line[1..])); break;
                case '\\': break; // "\ No newline at end of file"
                default:
                    // Next file header (shouldn't happen for single-file diffs): stop collecting.
                    Flush();
                    lines = null;
                    break;
            }
        }

        Flush();
        return new FileDiff(hunks, binary);
    }

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
}
