using CentralGit.Application.Features.Branches;
using CentralGit.Application.Git;
using CentralGit.Domain.Accounts;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Operations;
using CentralGit.Infrastructure.Git;

namespace CentralGit.Infrastructure.Tests;

public class BranchOperationsTests
{
    [Fact]
    public async Task Switches_tracks_renames_and_deletes_against_real_git()
    {
        var root = Directory.CreateTempSubdirectory("centralgit-branch-").FullName;
        try
        {
            var git = new GitRunner();
            string origin = Path.Combine(root, "origin.git"),
                work = Path.Combine(root, "work"),
                other = Path.Combine(root, "other");
            async Task<string> Git(string dir, params string[] args) => await git.RunCheckedAsync(dir, args);
            Task<GitResult> Run(string dir, IReadOnlyList<string> args) => git.RunAsync(dir, args);
            async Task Configure(string dir)
            {
                await Git(dir, "config", "user.name", "Test");
                await Git(dir, "config", "user.email", "test@example.dev");
                await Git(dir, "config", "commit.gpgsign", "false");
            }

            await Git(root, "init", "-q", "--bare", "-b", "main", origin);
            await Git(root, "clone", "-q", origin, other);
            await Configure(other);
            File.WriteAllText(Path.Combine(other, "a.txt"), "main\n");
            await Git(other, "add", ".");
            await Git(other, "commit", "-qm", "main");
            await Git(other, "push", "-q", "origin", "main");
            await Git(other, "switch", "-qc", "feature/remote");
            File.WriteAllText(Path.Combine(other, "a.txt"), "feature\n");
            await Git(other, "commit", "-qam", "feature");
            await Git(other, "push", "-q", "origin", "feature/remote");

            await Git(root, "clone", "-q", origin, work);
            await Configure(work);
            var repo = new GitRepository(work, git);

            // Remote branches, without origin/HEAD.
            var remotes = await repo.GetRemoteBranchesAsync();
            Assert.Contains(
                remotes,
                r => r is { Name: "origin/feature/remote", Remote: "origin", ShortName: "feature/remote" }
            );
            Assert.DoesNotContain(remotes, r => r.ShortName == "HEAD");
            Assert.All(remotes, r => Assert.NotNull(r.LastCommit));

            // Validation goes through git itself.
            var branches = new BranchService(repo, repo);
            Assert.Null(await branches.ValidateNewNameAsync("topic/ok-1"));
            Assert.Equal(BranchNames.Invalid("bad..name"), await branches.ValidateNewNameAsync("bad..name"));
            Assert.NotNull(await branches.ValidateNewNameAsync("has space"));
            Assert.NotNull(await branches.ValidateNewNameAsync("main")); // exists

            // Track the remote branch, then come back to main.
            Assert.True((await Run(work, BranchOperations.Track("origin/feature/remote"))).Succeeded);
            var tracked = (await repo.GetLocalBranchesAsync()).Single(b => b.Name == "feature/remote");
            Assert.True(tracked.IsHead);
            Assert.Equal("origin/feature/remote", tracked.Upstream);
            Assert.NotNull(tracked.LastCommit);
            Assert.True((await Run(work, BranchOperations.Switch("main"))).Succeeded);

            // Changes that conflict with the target block the switch; stashing clears the way.
            File.WriteAllText(Path.Combine(work, "a.txt"), "local edit\n");
            var blocked = await Run(work, BranchOperations.Switch("feature/remote"));
            Assert.False(blocked.Succeeded);
            Assert.Equal(SuggestedFix.CommitOrStash, RemoteOperations.Explain(blocked).Fix);
            Assert.True((await Run(work, BranchOperations.StashBeforeSwitch("feature/remote"))).Succeeded);
            Assert.True((await Run(work, BranchOperations.Switch("feature/remote"))).Succeeded);
            Assert.Contains(
                "CentralGit: changes left behind when switching to feature/remote",
                await Git(work, "stash", "list")
            );

            // Create, name clash, rename.
            Assert.True((await Run(work, BranchOperations.Create("topic"))).Succeeded);
            var clash = await Run(work, BranchOperations.Create("topic", "main"));
            Assert.Equal(SuggestedFix.PickAnotherName, RemoteOperations.Explain(clash).Fix);
            Assert.True((await Run(work, BranchOperations.Rename("topic", "topic-renamed"))).Succeeded);

            // Unmerged work: -d refuses with an explanation, -D deletes.
            File.WriteAllText(Path.Combine(work, "b.txt"), "only here\n");
            await Git(work, "add", ".");
            await Git(work, "commit", "-qm", "unmerged");
            await Git(work, "switch", "-q", "main");
            var refused = await Run(work, BranchOperations.Delete("topic-renamed", force: false));
            Assert.Equal(SuggestedFix.ForceDelete, RemoteOperations.Explain(refused).Fix);
            Assert.True((await Run(work, BranchOperations.Delete("topic-renamed", force: true))).Succeeded);
            Assert.DoesNotContain(await repo.GetLocalBranchesAsync(), b => b.Name == "topic-renamed");
        }
        finally
        {
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }
}
