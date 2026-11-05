using CentralGit.Application.Features.History;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Infrastructure.Git;

namespace CentralGit.Infrastructure.Tests;

public class FileHistoryTests
{
    [Fact]
    public async Task Follows_a_file_across_a_rename_and_diffs_each_revision()
    {
        var root = Directory.CreateTempSubdirectory("centralgit-filehistory-").FullName;
        try
        {
            var git = new GitRunner();
            async Task<string> Git(params string[] args) => await git.RunCheckedAsync(root, args);
            await Git("init", "-q", "-b", "main");
            await Git("config", "user.name", "Test");
            await Git("config", "user.email", "test@example.dev");
            await Git("config", "commit.gpgsign", "false");

            Directory.CreateDirectory(Path.Combine(root, "src"));
            var lines = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"line {i}")) + "\n";
            File.WriteAllText(Path.Combine(root, "old.txt"), lines);
            File.WriteAllText(Path.Combine(root, "other.txt"), "x\n");
            await Git("add", ".");
            await Git("commit", "-qm", "Add old");
            File.WriteAllText(Path.Combine(root, "other.txt"), "y\n");
            await Git("commit", "-qam", "Unrelated");
            await Git("mv", "old.txt", "src/new.txt");
            await Git("commit", "-qm", "Move it");
            File.WriteAllText(Path.Combine(root, "src", "new.txt"), lines.Replace("line 3\n", "line three\n"));
            await Git("commit", "-qam", "Edit it");

            var history = new HistoryService(new GitRepository(root, git));
            var revisions = await history.GetFileHistoryAsync("src/new.txt");

            Assert.Equal(["Edit it", "Move it", "Add old"], revisions.Select(r => r.Subject));
            Assert.Equal(("src/new.txt", ChangeKind.Modified), (revisions[0].Path, revisions[0].Kind));
            Assert.Equal(
                ("src/new.txt", "old.txt", ChangeKind.Renamed),
                (revisions[1].Path, revisions[1].OldPath, revisions[1].Kind)
            );
            Assert.Equal(("old.txt", ChangeKind.Added), (revisions[2].Path, revisions[2].Kind));
            Assert.Null(revisions[2].Parent);
            Assert.Equal(CommitDetails.EmptyTree, revisions[2].DiffBase);

            var edit = await history.GetRevisionDiffAsync(revisions[0]);
            var hunk = Assert.Single(edit.Hunks);
            Assert.Contains(hunk.Lines, l => l.Text == "line three");

            var added = await history.GetRevisionDiffAsync(revisions[2]);
            Assert.Equal(20, added.Hunks.Sum(h => h.Lines.Count));

            var files = await history.ListFilesAsync();
            Assert.Equal(["other.txt", "src/new.txt"], files.Order());
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Matching_prefers_hits_on_the_file_name()
    {
        var history = new HistoryService(new GitRepository("."));
        string[] files =
        [
            "src/Health/RepositoryHealth.cs",
            "src/Repositories/Scanner.cs",
            "docs/health.md",
            "tests/RepositoryHealthTests.cs",
        ];
        var matches = history.MatchFiles(files, "RepoHealth");
        Assert.Equal("src/Health/RepositoryHealth.cs", matches[0]);
        Assert.Contains("tests/RepositoryHealthTests.cs", matches);
        Assert.DoesNotContain("docs/health.md", matches);
        Assert.Empty(history.MatchFiles(files, "  "));
    }
}
