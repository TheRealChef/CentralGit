using CentralGit.Core.Diff;
using CentralGit.Core.Git;
using CentralGit.Core.Operations;

namespace CentralGit.Core.Tests;

public class WorkingTreeTests
{
    [Fact]
    public void Raw_hunks_keep_carriage_returns_and_no_newline_markers()
    {
        const string text =
            "diff --git a/a.txt b/a.txt\nindex 1..2 100644\n--- a/a.txt\n+++ b/a.txt\n" +
            "@@ -1,2 +1,2 @@\n one\r\n-two\r\n+TWO\r\n" +
            "@@ -9 +9 @@\n-end\n\\ No newline at end of file\n+END\n\\ No newline at end of file\n";
        var diff = FileDiff.Parse(text);

        Assert.Equal(2, diff.Hunks.Count);
        Assert.Equal("diff --git a/a.txt b/a.txt\nindex 1..2 100644\n--- a/a.txt\n+++ b/a.txt\n", diff.FileHeader);
        Assert.Equal("@@ -1,2 +1,2 @@\n one\r\n-two\r\n+TWO\r\n", diff.Hunks[0].Raw);
        Assert.EndsWith("+END\n\\ No newline at end of file\n", diff.PatchFor(1));
        Assert.Equal("two", diff.Hunks[0].Lines[1].Text); // display text has no CR
    }

    [Theory]
    [InlineData("Fix it", "", "Fix it")]
    [InlineData("  Fix it ", "\n\nBecause.\n", "Fix it\n\nBecause.")]
    [InlineData("Fix it", "# Why\rTotals ignored quantity.\r\rDetails", "Fix it\n\n# Why\nTotals ignored quantity.\n\nDetails")] // WinUI TextBox
    [InlineData("Fix it", "a\r\nb", "Fix it\n\na\nb")]
    public void Composes_messages(string summary, string body, string expected) =>
        Assert.Equal(expected, WorkingTree.ComposeMessage(summary, body));

    [Fact]
    public void Splits_messages() =>
        Assert.Equal(("Fix it", "# Why\nBecause."), WorkingTree.SplitMessage("Fix it\r\n\r\n# Why\r\nBecause.\r\n"));

    [Fact]
    public async Task Stages_unstages_discards_and_commits_against_real_git()
    {
        var root = Directory.CreateTempSubdirectory("centralgit-wt-").FullName;
        try
        {
            var git = new GitRunner();
            var tree = new WorkingTree(git, root);
            async Task<string> Git(params string[] args) => await git.RunCheckedAsync(root, args);
            async Task<WorkingTreeStatus> Status() => WorkingTreeStatus.Parse(await Git([.. WorkingTreeStatus.Arguments]));
            string Read(string name) => File.ReadAllText(Path.Combine(root, name));
            void Write(string name, string text) => File.WriteAllText(Path.Combine(root, name), text);

            await Git("init", "-q", "-b", "main");
            await Git("config", "user.name", "Test");
            await Git("config", "user.email", "test@example.dev");
            await Git("config", "commit.gpgsign", "false");
            await Git("config", "core.autocrlf", "false");

            // Before the first commit: stage, then unstage without a HEAD to restore from.
            var lines = Enumerable.Range(1, 30).Select(i => $"line {i}\r\n").ToArray();
            Write("a.txt", string.Concat(lines));
            Write("odd[1].txt", "x");
            Assert.True((await tree.StageAsync(["a.txt", "odd[1].txt"])).Succeeded);
            Assert.True((await tree.UnstageAsync(["odd[1].txt"], hasHead: false)).Succeeded);
            var status = await Status();
            Assert.Contains(status.Changes, c => c.Path == "odd[1].txt" && c.Staged == ChangeKind.Untracked);
            Assert.True((await tree.StageAsync(["odd[1].txt"])).Succeeded); // literal pathspec: not a glob

            var commit = await tree.CommitAsync(WorkingTree.ComposeMessage("Initial", "# Notes\nKept."), amend: false);
            Assert.True(commit.Succeeded, commit.StandardError);
            Assert.Equal("Initial\n\n# Notes\nKept.", await tree.LastMessageAsync());

            // Two separate edits (CRLF content) give two hunks; stage only the second.
            lines[1] = "line 2 changed\r\n";
            lines[27] = "line 28 changed\r\n";
            Write("a.txt", string.Concat(lines));
            var change = (await Status()).Changes.Single(c => c.Path == "a.txt");
            var unstaged = await tree.DiffAsync(change, ChangeSide.Unstaged);
            Assert.Equal(2, unstaged.Hunks.Count);

            var staged = await tree.ApplyHunkToIndexAsync(unstaged, 1, ChangeSide.Unstaged);
            Assert.True(staged.Succeeded, staged.StandardError);
            var cached = await tree.DiffAsync(change, ChangeSide.Staged);
            Assert.Single(cached.Hunks);
            Assert.Contains(cached.Hunks[0].Lines, l => l.Text == "line 28 changed");

            // Unstage that hunk again, then discard the first hunk from disk.
            Assert.True((await tree.ApplyHunkToIndexAsync(cached, 0, ChangeSide.Staged)).Succeeded);
            Assert.Empty((await tree.DiffAsync(change, ChangeSide.Staged)).Hunks);
            var discard = await tree.DiscardHunkAsync(unstaged, 0);
            Assert.True(discard.Succeeded, discard.StandardError);
            Assert.DoesNotContain("line 2 changed", Read("a.txt"));
            Assert.Contains("line 28 changed\r\n", Read("a.txt"));

            // Untracked files: diff against nothing, stage as a hunk, discard deletes.
            Write("new.txt", "hello\n");
            var untracked = (await Status()).Changes.Single(c => c.Path == "new.txt");
            var newDiff = await tree.DiffAsync(untracked, ChangeSide.Unstaged);
            Assert.Single(newDiff.Hunks);
            Assert.True((await tree.ApplyHunkToIndexAsync(newDiff, 0, ChangeSide.Unstaged)).Succeeded);
            Assert.Contains((await Status()).Changes, c => c.Path == "new.txt" && c.Staged == ChangeKind.Added);
            Assert.True((await tree.UnstageAsync(["new.txt"], hasHead: true)).Succeeded);
            Assert.True((await tree.DiscardAsync((await Status()).Changes.Single(c => c.Path == "new.txt"))).Succeeded);
            Assert.False(File.Exists(Path.Combine(root, "new.txt")));

            // Stage everything and amend.
            Assert.True((await tree.StageAllAsync()).Succeeded);
            var amend = await tree.CommitAsync("Initial, amended", amend: true);
            Assert.True(amend.Succeeded, amend.StandardError);
            Assert.Equal("1", (await Git("rev-list", "--count", "HEAD")).Trim());
            Assert.Empty((await Status()).Changes);
        }
        finally
        {
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }
}

public class ReadabilityTests
{
    [Fact]
    public void Parses_numstat_with_renames_and_binaries()
    {
        var counts = WorkingTree.ParseNumstat("3\t1\tsrc/a.cs\0-\t-\tlogo.png\0" + "2\t0\t\0old/name.cs\0new/name.cs\0");
        Assert.Equal(new LineCounts(3, 1), counts["src/a.cs"]);
        Assert.True(counts["logo.png"].IsBinary);
        Assert.Equal(new LineCounts(2, 0), counts["new/name.cs"]);
        Assert.False(counts.ContainsKey("old/name.cs"));
    }

    [Theory]
    [InlineData("@@ -16 +16,2 @@\n-a\n+b\n+c\n", "Lines 16–17")]
    [InlineData("@@ -6,6 +6,7 @@\n x\n x\n x\n+new\n x\n x\n x\n", "Line 9")]
    [InlineData("@@ -4,5 +4,2 @@\n x\n-a\n-b\n-c\n x\n", "Removed lines 5–7")]
    public void Describes_hunks_in_words(string hunk, string expected) =>
        Assert.Equal(expected, DiffLayout.DescribeRange(FileDiff.Parse(hunk).Hunks[0]));
}
