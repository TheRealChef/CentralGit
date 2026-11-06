namespace CentralGit.Domain.Diff;

public enum DiffLineKind
{
    Context,
    Added,
    Deleted,
}

public sealed record DiffLine(DiffLineKind Kind, int? OldNumber, int? NewNumber, string Text);

public sealed record DiffHunk(
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount,
    string Section,
    IReadOnlyList<DiffLine> Lines
)
{
    public string Header =>
        $"@@ -{OldStart},{OldCount} +{NewStart},{NewCount} @@" + (Section.Length > 0 ? "  " + Section : "");

    /// <summary>The hunk exactly as git printed it (header, CRs and "\ No newline" markers included), for patches.</summary>
    public string Raw { get; init; } = "";
}

public sealed record FileDiff(IReadOnlyList<DiffHunk> Hunks, bool IsBinary)
{
    public static readonly FileDiff Empty = new([], false);

    /// <summary>The file header git printed before the first hunk ("diff --git", "index", "---", "+++", …).</summary>
    public string FileHeader { get; init; } = "";

    /// <summary>A patch containing only hunk <paramref name="index"/>, suitable for <c>git apply</c>.</summary>
    public string PatchFor(int index) => FileHeader + Hunks[index].Raw;

    /// <summary>
    /// A patch with only some changed lines of hunk <paramref name="index"/> (indexes into its
    /// <see cref="DiffHunk.Lines"/>), for <c>git apply --recount</c>. Unselected changes must not
    /// happen, so they're rewritten to match the side the patch is applied to: applied forward
    /// (staging) the old side is there, so unselected additions are dropped and unselected deletions
    /// stay as context; applied with <paramref name="reverse"/> (unstaging, discarding) the new side
    /// is there, so it's the other way round.
    /// </summary>
    public string PatchForLines(int index, IReadOnlySet<int> selected, bool reverse)
    {
        var raw = Hunks[index].Raw.Split('\n');
        var patch = new System.Text.StringBuilder(FileHeader);
        patch.Append(raw[0]).Append('\n'); // the @@ header; --recount fixes its counts
        var line = -1;
        var keptPrevious = true;
        foreach (var text in raw.Skip(1))
        {
            if (text.Length == 0)
                continue;
            switch (text[0])
            {
                case ' ':
                    line++;
                    patch.Append(text).Append('\n');
                    keptPrevious = true;
                    break;
                case '+' or '-':
                    line++;
                    var drops = text[0] == (reverse ? '-' : '+');
                    if (selected.Contains(line))
                        patch.Append(text).Append('\n');
                    else if (!drops)
                        patch.Append(' ').Append(text, 1, text.Length - 1).Append('\n');
                    keptPrevious = selected.Contains(line) || !drops;
                    break;
                case '\\':
                    // "\ No newline at end of file" belongs to the line before it.
                    if (keptPrevious)
                        patch.Append(text).Append('\n');
                    break;
            }
        }
        return patch.ToString();
    }
}
