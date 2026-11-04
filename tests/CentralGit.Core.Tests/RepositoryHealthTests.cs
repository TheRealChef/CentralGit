using CentralGit.Core.Git;
using CentralGit.Core.Insights;
using CentralGit.Core.Operations;

namespace CentralGit.Core.Tests;

public class RepositoryHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static BranchFacts Local(string name, int daysOld, bool head = false, bool gone = false) =>
        new(name, false, Now.AddDays(-daysOld), "Ana", head, gone);

    [Fact]
    public void Classifies_branches_strongest_reason_first()
    {
        var merged = new Dictionary<string, string>
        {
            ["refs/heads/feature/done"] = "origin/main",
            ["refs/heads/main"] = "origin/main",
            ["refs/remotes/origin/feature/done"] = "origin/main",
        };
        var result = RepositoryHealth.Assess(
        [
            Local("main", 200),
            Local("feature/done", 3),
            Local("feature/squashed", 10, gone: true),
            Local("feature/old", 120),
            Local("feature/quiet", 40),
            Local("feature/fresh", 2),
            Local("release/2.1", 300),
            Local("feature/mine", 150, head: true),
            new("origin/feature/done", true, Now.AddDays(-3), "Ana"),
        ], merged, Now).ToDictionary(a => a.Name);

        Assert.Equal(BranchVerdict.Active, result["main"].Verdict);
        Assert.True(result["main"].IsProtected);
        Assert.Equal(BranchVerdict.Merged, result["feature/done"].Verdict);
        Assert.Equal("origin/main", result["feature/done"].MergedInto);
        Assert.True(result["feature/done"].IsSafeToDelete);
        Assert.Equal(BranchVerdict.Gone, result["feature/squashed"].Verdict);
        Assert.Equal(BranchVerdict.Abandoned, result["feature/old"].Verdict);
        Assert.Equal(BranchVerdict.Aging, result["feature/quiet"].Verdict);
        Assert.False(result["feature/quiet"].IsCleanupCandidate);
        Assert.Equal(BranchVerdict.Active, result["feature/fresh"].Verdict);
        Assert.False(result["release/2.1"].IsCleanupCandidate);
        Assert.False(result["feature/mine"].IsCleanupCandidate); // checked out
        Assert.Equal(BranchVerdict.Merged, result["origin/feature/done"].Verdict);
    }

    [Fact]
    public void Prefers_remote_integration_branches_with_trunk_first()
    {
        var (trunk, targets) = RepositoryHealth.PickIntegrationBranches(
            ["main", "develop", "feature/x"], ["origin/main", "origin/develop", "origin/feature/x"], "origin/develop");
        Assert.Equal("origin/develop", trunk);
        Assert.Equal(["origin/develop", "origin/main"], targets);

        var (localTrunk, localTargets) = RepositoryHealth.PickIntegrationBranches(["master", "topic"], [], null);
        Assert.Equal("master", localTrunk);
        Assert.Equal(["master"], localTargets);
    }

    [Theory]
    [InlineData(0.5, "today", "0d")]
    [InlineData(5, "5 days", "5d")]
    [InlineData(40, "5 weeks", "5w")]
    [InlineData(125, "4 months", "4mo")]
    [InlineData(800, "2 years", "2y")]
    public void Describes_ages(double days, string text, string compact)
    {
        Assert.Equal(text, RepositoryHealth.DescribeAge(TimeSpan.FromDays(days)));
        Assert.Equal(compact, RepositoryHealth.ShortAge(TimeSpan.FromDays(days)));
    }

    [Fact]
    public void Parses_count_objects_and_remote_urls()
    {
        var (size, loose, packs) = RepositoryHealth.ParseCountObjects("count: 12\nsize: 48\nin-pack: 900\npacks: 2\nsize-pack: 2000\nprune-packable: 0\ngarbage: 0\nsize-garbage: 0\n");
        Assert.Equal((2048L, 12, 2), (size, loose, packs));

        var urls = RepositoryHealth.ParseRemoteUrls("remote.origin.url https://example.dev/a.git\nremote.my.fork.url git@host:me/a.git\n");
        Assert.Equal([new RemoteUrl("origin", "https://example.dev/a.git"), new RemoteUrl("my.fork", "git@host:me/a.git")], urls);
    }

    [Fact]
    public async Task Finds_merged_gone_and_abandoned_branches_in_a_real_repository()
    {
        var root = Directory.CreateTempSubdirectory("centralgit-health-").FullName;
        try
        {
            var git = new GitRunner();
            string origin = Path.Combine(root, "origin.git"), work = Path.Combine(root, "work");
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
            var report = await RepositoryHealth.LoadAsync(git, work, later);
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
            var deleted = await git.RunAsync(work, BranchOperations.DeleteMany(["feature/merged", "feature/squashed", "feature/unmerged"]));
            Assert.True(deleted.Succeeded, deleted.StandardError);
            var after = await RepositoryHealth.AssessBranchesAsync(git, work, later);
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
