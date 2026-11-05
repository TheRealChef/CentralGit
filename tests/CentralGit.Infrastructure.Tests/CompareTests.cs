using CentralGit.Application.Features.Compare;
using CentralGit.Application.Git;
using CentralGit.Domain.Changes;
using CentralGit.Infrastructure.Git;

namespace CentralGit.Infrastructure.Tests;

public class CompareTests
{
    [Fact]
    public void Left_right_counts_parse()
    {
        Assert.Equal((5, 3), CompareReader.ParseCounts("5\t3\n"));
        Assert.Equal((0, 0), CompareReader.ParseCounts(""));
    }

    [Fact]
    public async Task Compares_a_branch_with_main_from_their_merge_base()
    {
        var root = Directory.CreateTempSubdirectory("centralgit-compare-").FullName;
        try
        {
            var git = new GitRunner();
            async Task<string> Git(params string[] args) => await git.RunCheckedAsync(root, args);
            async Task Commit(string file, string text, string message)
            {
                File.WriteAllText(Path.Combine(root, file), text);
                await Git("add", ".");
                await Git("commit", "-qm", message);
            }

            await Git("init", "-q", "-b", "main");
            await Git("config", "user.name", "Test");
            await Git("config", "user.email", "test@example.dev");
            await Git("config", "commit.gpgsign", "false");
            await Commit("shared.txt", "one\n", "Base");
            await Git("switch", "-qc", "topic");
            await Commit("feature.txt", "a\nb\n", "Add feature");
            await Commit("shared.txt", "one\ntwo\n", "Extend shared");
            await Git("switch", "-q", "main");
            await Commit("main-only.txt", "m\n", "Main moves on");

            var service = new CompareService(new GitRepository(root, git));
            var comparison = await service.CompareAsync("main", "topic");

            Assert.Equal((2, 1), (comparison.AheadCount, comparison.BehindCount));
            Assert.Equal(["Extend shared", "Add feature"], comparison.Ahead.Select(c => c.Subject));
            Assert.Equal(["Main moves on"], comparison.Behind.Select(c => c.Subject));
            Assert.Equal("topic is 2 commits ahead and 1 commit behind main.", comparison.Summary);

            // Only what topic changes: main's own new file isn't part of it.
            Assert.Equal(
                [("feature.txt", ChangeKind.Added, 2), ("shared.txt", ChangeKind.Modified, 1)],
                comparison.Files.OrderBy(f => f.Path).Select(f => (f.Path, f.Kind, f.Added))
            );
            var diff = await service.GetFileDiffAsync(comparison, comparison.Files.Single(f => f.Path == "shared.txt"));
            Assert.Contains(diff.Hunks.SelectMany(h => h.Lines), l => l.Text == "two");

            var same = await service.CompareAsync("main", "main");
            Assert.True(same.IsSame);

            var missing = await Assert.ThrowsAsync<GitException>(() => service.CompareAsync("main", "--nope"));
            Assert.Contains("isn't a branch", missing.Message);
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }
}
