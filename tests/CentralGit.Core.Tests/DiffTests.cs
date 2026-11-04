using CentralGit.Core.Diff;
using CentralGit.Core.Git;
using CentralGit.Core.Text;

namespace CentralGit.Core.Tests;

public class DiffTests
{
    private const string Sample =
        "diff --git a/f.cs b/f.cs\n" +
        "index 111..222 100644\n" +
        "--- a/f.cs\n" +
        "+++ b/f.cs\n" +
        "@@ -10,4 +10,5 @@ class Foo\n" +
        " keep\n" +
        "-var x = 1;\n" +
        "+var x = 2;\n" +
        "+added\n" +
        " keep2\n" +
        " keep3\n" +
        "\\ No newline at end of file\n" +
        "@@ -40 +41 @@\n" +
        "-old\n" +
        "+new\n";

    [Fact]
    public void Parses_hunks_and_line_numbers()
    {
        var diff = FileDiff.Parse(Sample);

        Assert.False(diff.IsBinary);
        Assert.Equal(2, diff.Hunks.Count);

        var h = diff.Hunks[0];
        Assert.Equal((10, 4, 10, 5, "class Foo"), (h.OldStart, h.OldCount, h.NewStart, h.NewCount, h.Section));
        Assert.Equal(6, h.Lines.Count);
        Assert.Equal(new DiffLine(DiffLineKind.Context, 10, 10, "keep"), h.Lines[0]);
        Assert.Equal(new DiffLine(DiffLineKind.Deleted, 11, null, "var x = 1;"), h.Lines[1]);
        Assert.Equal(new DiffLine(DiffLineKind.Added, null, 11, "var x = 2;"), h.Lines[2]);
        Assert.Equal(new DiffLine(DiffLineKind.Added, null, 12, "added"), h.Lines[3]);
        Assert.Equal(new DiffLine(DiffLineKind.Context, 13, 14, "keep3"), h.Lines[5]);

        var h2 = diff.Hunks[1];
        Assert.Equal((40, 1, 41, 1), (h2.OldStart, h2.OldCount, h2.NewStart, h2.NewCount));
    }

    [Fact]
    public void Detects_binary_files()
    {
        var diff = FileDiff.Parse("diff --git a/x.png b/x.png\nindex 1..2 100644\nBinary files a/x.png and b/x.png differ\n");
        Assert.True(diff.IsBinary);
        Assert.Empty(diff.Hunks);
    }

    [Fact]
    public void Split_layout_pairs_deletions_with_additions_and_adds_gaps()
    {
        var rows = DiffLayout.Split(FileDiff.Parse(Sample));

        Assert.Equal(DiffRowKind.Gap, rows[0].Kind);
        Assert.Equal("9 unchanged lines · 1–9", rows[0].Text);
        Assert.Equal(DiffRowKind.HunkHeader, rows[1].Kind);

        var paired = rows[3];
        Assert.Equal("var x = 1;", paired.Left!.Line.Text);
        Assert.Equal("var x = 2;", paired.Right!.Line.Text);
        Assert.Equal(0, paired.ChangeBlock);
        Assert.Equal([new TextSpan(8, 1)], paired.Left.Changes);
        Assert.Equal([new TextSpan(8, 1)], paired.Right.Changes);

        var filler = rows[4];
        Assert.Null(filler.Left);
        Assert.Equal("added", filler.Right!.Line.Text);

        Assert.Contains(rows, r => r.Kind == DiffRowKind.Gap && r.Text == "26 unchanged lines · 14–39");
        Assert.Equal(1, rows[^1].ChangeBlock);
    }

    [Fact]
    public void Unified_layout_lists_deletions_before_additions()
    {
        var rows = DiffLayout.Unified(FileDiff.Parse(Sample)).Where(r => r.Kind == DiffRowKind.Lines).ToList();
        Assert.All(rows, r => Assert.Null(r.Right));
        Assert.Equal(["keep", "var x = 1;", "var x = 2;", "added", "keep2", "keep3", "old", "new"], rows.Select(r => r.Left!.Line.Text));
    }

    [Fact]
    public void Intraline_skips_lines_that_were_rewritten()
    {
        var (removed, added) = IntralineDiff.Compute("return customer.Name;", "throw new InvalidOperationException();");
        Assert.Empty(removed);
        Assert.Empty(added);
    }

    [Theory]
    [InlineData("M\0a.cs\0R087\0old.cs\0new.cs\0A\0b.cs\0")]
    public void Parses_name_status_with_renames(string output)
    {
        var files = CommitDetails.ParseNameStatus(output);
        Assert.Equal(
            [(ChangeKind.Modified, "a.cs", (string?)null), (ChangeKind.Renamed, "new.cs", "old.cs"), (ChangeKind.Added, "b.cs", null)],
            files);
    }

    [Fact]
    public void Parses_numstat_with_renames_and_binaries()
    {
        var stats = CommitDetails.ParseNumstat("3\t1\ta.cs\0" + "2\t2\t\0old.cs\0new.cs\0" + "-\t-\timg.png\0");
        Assert.Equal((3, 1, false), stats["a.cs"]);
        Assert.Equal((2, 2, false), stats["new.cs"]);
        Assert.Equal((0, 0, true), stats["img.png"]);
    }

    [Fact]
    public void Splits_subject_from_body()
    {
        Assert.Equal(("Fix it", "Longer\nexplanation"), CommitDetails.SplitMessage("Fix it\n\nLonger\nexplanation\n"));
        Assert.Equal(("Only subject", ""), CommitDetails.SplitMessage("Only subject\n"));
    }

    [Fact]
    public void Highlights_csharp_tokens()
    {
        const string line = "public string Name => \"x\"; // note";
        var tokens = SyntaxHighlighter.Highlight(line, SyntaxLanguage.CLike);

        string Text(SyntaxToken t) => line.Substring(t.Start, t.Length);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Keyword && Text(t) == "public");
        Assert.Contains(tokens, t => t.Kind == TokenKind.Type && Text(t) == "Name");
        Assert.Contains(tokens, t => t.Kind == TokenKind.String && Text(t) == "\"x\"");
        Assert.Contains(tokens, t => t.Kind == TokenKind.Comment && Text(t) == "// note");
        Assert.Equal(line.Length, tokens.Sum(t => t.Length));
    }

    [Fact]
    public async Task Loads_commit_details_and_file_diff_from_a_real_repository()
    {
        var dir = Directory.CreateTempSubdirectory("centralgit-diff-").FullName;
        try
        {
            var git = new GitRunner();
            async Task Git(params string[] args) => await git.RunCheckedAsync(dir, args);

            await Git("init", "-b", "main");
            await Git("config", "user.name", "Test");
            await Git("config", "user.email", "test@example.dev");
            await Git("config", "commit.gpgsign", "false");
            File.WriteAllText(Path.Combine(dir, "a.txt"), "one\ntwo\nthree\n");
            await Git("add", ".");
            await Git("commit", "-m", "Root");
            File.WriteAllText(Path.Combine(dir, "a.txt"), "one\nTWO\nthree\nfour\n");
            await Git("commit", "-am", "Change\n\nWith a body.");

            var head = (await git.RunCheckedAsync(dir, ["rev-parse", "HEAD"])).Trim();
            var details = await CommitDetails.LoadAsync(git, dir, head);

            Assert.Equal("Change", details.Subject);
            Assert.Equal("With a body.", details.Body);
            Assert.Single(details.Parents);
            var file = Assert.Single(details.Files);
            Assert.Equal(("a.txt", ChangeKind.Modified, 2, 1), (file.Path, file.Kind, file.Added, file.Deleted));

            var diff = await FileDiff.LoadAsync(git, dir, details.DiffBase, details.Hash, file);
            var lines = Assert.Single(diff.Hunks).Lines;
            Assert.Contains(lines, l => l is { Kind: DiffLineKind.Added, Text: "TWO", NewNumber: 2 });

            var root = await CommitDetails.LoadAsync(git, dir, details.Parents[0]);
            Assert.Empty(root.Parents);
            Assert.Equal(ChangeKind.Added, Assert.Single(root.Files).Kind);
        }
        finally
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, recursive: true);
        }
    }
}
