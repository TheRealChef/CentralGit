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
}
