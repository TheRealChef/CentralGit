using CentralGit.Application.Features.Health;
using CentralGit.Domain.Health;
using CentralGit.Infrastructure.Git;

namespace CentralGit.Infrastructure.Tests;

public class RepositoryHealthTests
{
    [Fact]
    public void Parses_count_objects_and_remote_urls()
    {
        var (size, loose, packs) = HealthReader.ParseCountObjects(
            "count: 12\nsize: 48\nin-pack: 900\npacks: 2\nsize-pack: 2000\nprune-packable: 0\ngarbage: 0\nsize-garbage: 0\n"
        );
        Assert.Equal((2048L, 12, 2), (size, loose, packs));

        var urls = HealthReader.ParseRemoteUrls(
            "remote.origin.url https://example.dev/a.git\nremote.my.fork.url git@host:me/a.git\n"
        );
        Assert.Equal(
            [new RemoteUrl("origin", "https://example.dev/a.git"), new RemoteUrl("my.fork", "git@host:me/a.git")],
            urls
        );
    }

    [Fact]
    public async Task Finds_merged_gone_and_abandoned_branches_in_a_real_repository()
    {
        var root = Directory.CreateTempSubdirectory("centralgit-health-").FullName;
        try
        {
            var git = new GitRunner();
            string origin = Path.Combine(root, "origin.git"),
                work = Path.Combine(root, "work");
            async Task Git(string dir, params string[] args) => await git.RunCheckedAsync(dir, args);
            async Task Commit(string file, string text, string message)
            {
                File.WriteAllText(Path.Combine(work, file), text);
                await Git(work, "add", ".");
                await Git(work, "commit", "-qm", message);
            }

            await Git(root, "init", "-q", "--bare", "-b", "main", origin);
            await Git(root, "clone", "-q", origin, work);
            await Git(work, "config", "user.name", "Test");
            await Git(work, "config", "user.email", "test@example.dev");
            await Git(work, "config", "commit.gpgsign", "false");
            await Commit("a.txt", "1\n", "initial");
            await Git(work, "push", "-q", "-u", "origin", "main");

            // feature/merged: merged into main and pushed.
            await Git(work, "switch", "-qc", "feature/merged");
            await Commit("b.txt", "merged\n", "merged work");
            await Git(work, "switch", "-q", "main");
            await Git(work, "merge", "-q", "--no-ff", "-m", "merge", "feature/merged");
            await Git(work, "push", "-q", "origin", "main");

            // feature/squashed: pushed, then deleted on the server (as a squash-merged PR would be).
            await Git(work, "switch", "-qc", "feature/squashed");
            await Commit("c.txt", "squash\n", "squashed work");
            await Git(work, "push", "-q", "-u", "origin", "feature/squashed");
            await Git(work, "push", "-q", "origin", "--delete", "feature/squashed");
            await Git(work, "fetch", "-q", "--prune");

            // feature/unmerged: never merged.
            await Git(work, "switch", "-qc", "feature/unmerged", "main");
            await Commit("d.txt", "wip\n", "wip");
            await Git(work, "switch", "-q", "main");

            var later = DateTimeOffset.UtcNow.AddDays(120);
            var health = new HealthService(new GitRepository(work, git), new FixedClock(later));
            var report = (await health.LoadReportAsync()).Report;
            var local = report.Local.ToDictionary(b => b.Name);

            Assert.Equal("origin/main", report.Trunk);
            Assert.Equal(BranchVerdict.Merged, local["feature/merged"].Verdict);
            Assert.Equal("origin/main", local["feature/merged"].MergedInto);
            Assert.Equal(BranchVerdict.Gone, local["feature/squashed"].Verdict);
            Assert.Equal(BranchVerdict.Abandoned, local["feature/unmerged"].Verdict);
            Assert.True(local["main"].IsProtected);
            Assert.Equal(3, report.Stats.Commits); // initial, merged work, merge
            Assert.Equal(1, report.Stats.ContributorsLast90Days);
            Assert.NotNull(report.Stats.LastFetch);
            Assert.Contains(report.Findings, f => f.Title.Contains("could be cleaned up"));

            // Cleanup: local branches in one command.
            var deleted = await git.RunAsync(
                work,
                BranchOperations.DeleteMany(["feature/merged", "feature/squashed", "feature/unmerged"])
            );
            Assert.True(deleted.Succeeded, deleted.StandardError);
            var after = await health.ReviewBranchesAsync();
            Assert.Equal(["main"], after.Local.Select(b => b.Name));
        }
        finally
        {
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }
}
