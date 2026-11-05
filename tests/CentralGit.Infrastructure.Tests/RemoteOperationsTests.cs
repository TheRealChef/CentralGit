using CentralGit.Application.Git;
using CentralGit.Domain.Accounts;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Operations;
using CentralGit.Infrastructure.Git;

namespace CentralGit.Infrastructure.Tests;

public class RemoteOperationsTests
{
    private static GitResult Failed(string stderr) => new(["push"], 1, "", stderr, TimeSpan.Zero);

    [Theory]
    [InlineData(
        " ! [rejected]        main -> main (fetch first)\nerror: failed to push some refs",
        SuggestedFix.PullFirst
    )]
    [InlineData(" ! [rejected]        main -> main (non-fast-forward)", SuggestedFix.PullFirst)]
    [InlineData(
        "hint: You have divergent branches and need to specify how to reconcile them.\nfatal: Need to specify how to reconcile divergent branches.",
        SuggestedFix.ChooseReconcile
    )]
    [InlineData("fatal: Not possible to fast-forward, aborting.", SuggestedFix.ChooseReconcile)]
    [InlineData("fatal: The current branch feature/x has no upstream branch.", SuggestedFix.PublishBranch)]
    [InlineData("fatal: Authentication failed for 'https://github.com/a/b.git/'", SuggestedFix.SignIn)]
    [InlineData(
        "fatal: could not read Username for 'https://dev.azure.com': terminal prompts disabled",
        SuggestedFix.SignIn
    )]
    [InlineData(
        "error: Your local changes to the following files would be overwritten by merge:\n\ta.txt",
        SuggestedFix.CommitOrStash
    )]
    [InlineData(
        "CONFLICT (content): Merge conflict in a.txt\nAutomatic merge failed; fix conflicts",
        SuggestedFix.ResolveConflicts
    )]
    [InlineData("fatal: unable to access 'https://x/': Could not resolve host: x", SuggestedFix.CheckConnection)]
    [InlineData("fatal: something new and unexpected", SuggestedFix.None)]
    public void Explains_common_failures(string stderr, SuggestedFix fix)
    {
        var explanation = RemoteOperations.Explain(Failed(stderr));
        Assert.Equal(fix, explanation.Fix);
        Assert.False(string.IsNullOrWhiteSpace(explanation.Detail));
    }

    [Fact]
    public void Unknown_errors_surface_the_fatal_line()
    {
        Assert.Equal(
            "fatal: something new",
            RemoteOperations.Explain(Failed("hint: x\nfatal: something new\n")).Detail
        );
    }

    [Theory]
    [InlineData("Receiving objects:  45% (450/1000), 1.20 MiB | 2.00 MiB/s", "Receiving objects", 45)]
    [InlineData("remote: Counting objects: 100% (12/12), done.", "Counting objects", 100)]
    [InlineData("Writing objects:   7% (1/14)", "Writing objects", 7)]
    [InlineData("From https://github.com/a/b", "From https://github.com/a/b", null)]
    public void Parses_progress_lines(string line, string phase, int? percent)
    {
        Assert.Equal(new GitProgress(phase, percent), RemoteOperations.ParseProgress(line));
    }

    [Fact]
    public void Push_publishes_branches_without_upstream()
    {
        Assert.Equal(["push", "--progress"], RemoteOperations.Push("main", hasUpstream: true, "origin"));
        Assert.Equal(
            ["push", "--progress", "-u", "origin", "feature/x"],
            RemoteOperations.Push("feature/x", hasUpstream: false, "origin")
        );
        Assert.Equal(
            ["push", "--progress", "--force-with-lease"],
            RemoteOperations.Push("main", true, "origin", forceWithLease: true)
        );
    }

    /// <summary>End to end: a clone that falls behind, a rejected push, then pull and push again.</summary>
    [Fact]
    public async Task Rejected_push_is_explained_and_recovers_after_pull()
    {
        var root = Directory.CreateTempSubdirectory("centralgit-remote-").FullName;
        try
        {
            var git = new GitRunner();
            string origin = Path.Combine(root, "origin.git"),
                a = Path.Combine(root, "a"),
                b = Path.Combine(root, "b");
            async Task Git(string dir, params string[] args) => await git.RunCheckedAsync(dir, args);
            async Task Configure(string dir)
            {
                await Git(dir, "config", "user.name", "Test");
                await Git(dir, "config", "user.email", "test@example.dev");
                await Git(dir, "config", "commit.gpgsign", "false");
            }

            await Git(root, "init", "--bare", "-b", "main", origin);
            await Git(root, "clone", origin, a);
            await Configure(a);
            File.WriteAllText(Path.Combine(a, "one.txt"), "1");
            await Git(a, "add", ".");
            await Git(a, "commit", "-m", "one");
            await Git(a, "push", "-u", "origin", "main");

            await Git(root, "clone", origin, b);
            await Configure(b);
            File.WriteAllText(Path.Combine(b, "two.txt"), "2");
            await Git(b, "add", ".");
            await Git(b, "commit", "-m", "two");
            await Git(b, "push");

            File.WriteAllText(Path.Combine(a, "three.txt"), "3");
            await Git(a, "add", ".");
            await Git(a, "commit", "-m", "three");

            // Only the user's own config counts as a pull policy; a local setting is found.
            var repo = new GitRepository(a, git);
            Assert.Null(await repo.GetUserConfigAsync("centralgit.unset"));
            await Git(a, "config", "pull.ff", "only");
            Assert.Equal("only", await repo.GetUserConfigAsync("pull.ff"));
            await Git(a, "config", "--unset", "pull.ff");

            var lines = new List<string>();
            var push = await git.RunWithProgressAsync(a, RemoteOperations.Push("main", true, "origin"), lines.Add);
            Assert.False(push.Succeeded);
            Assert.Equal(SuggestedFix.PullFirst, RemoteOperations.Explain(push).Fix);

            var ffOnly = await git.RunWithProgressAsync(a, RemoteOperations.Pull(PullMode.FastForwardOnly), _ => { });
            Assert.False(ffOnly.Succeeded);
            Assert.Equal(SuggestedFix.ChooseReconcile, RemoteOperations.Explain(ffOnly).Fix);

            var rebase = await git.RunWithProgressAsync(a, RemoteOperations.Pull(PullMode.Rebase), _ => { });
            Assert.True(rebase.Succeeded, rebase.StandardError);
            var again = await git.RunWithProgressAsync(a, RemoteOperations.Push("main", true, "origin"), _ => { });
            Assert.True(again.Succeeded, again.StandardError);
        }
        finally
        {
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }
}
