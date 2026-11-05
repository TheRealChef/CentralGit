using CentralGit.Application.Features.Recovery;
using CentralGit.Domain.Recovery;
using CentralGit.Infrastructure.Git;

namespace CentralGit.Infrastructure.Tests;

public class RecoveryTests
{
    [Fact]
    public void Reflog_records_parse_with_their_dates()
    {
        var entries = ReflogReader.Parse(
            "aaa\u001fHEAD@{1793878683}\u001fcommit: Second\u001fSecond\u001fAna\u001e\n"
                + "bbb\u001fHEAD@{1793805482}\u001fcheckout: moving from main to topic\u001fFirst\u001fAna\u001e\n"
        );
        Assert.Equal(2, entries.Count);
        Assert.Equal((0, "aaa", ReflogAction.Commit), (entries[0].Index, entries[0].Hash, entries[0].Action));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793878683), entries[0].When);
        Assert.Equal("Switched from main to topic", entries[1].Description);
    }

    [Fact]
    public async Task A_commit_lost_by_a_hard_reset_is_found_and_restored()
    {
        var root = Directory.CreateTempSubdirectory("centralgit-recovery-").FullName;
        try
        {
            var git = new GitRunner();
            async Task<string> Git(params string[] args) => await git.RunCheckedAsync(root, args);
            await Git("init", "-q", "-b", "main");
            await Git("config", "user.name", "Test");
            await Git("config", "user.email", "test@example.dev");
            await Git("config", "commit.gpgsign", "false");
            File.WriteAllText(Path.Combine(root, "a.txt"), "one\n");
            await Git("add", ".");
            await Git("commit", "-qm", "First");
            File.WriteAllText(Path.Combine(root, "a.txt"), "two\n");
            await Git("commit", "-qam", "Add the important part");
            var lostHash = (await Git("rev-parse", "HEAD")).Trim();
            await Git("reset", "-q", "--hard", "HEAD~1");

            var repo = new GitRepository(root, git);
            var recovery = new RecoveryService(repo, repo);
            var list = await recovery.LoadAsync();

            var lost = Assert.Single(list.Lost);
            Assert.Equal(lostHash, lost.Entry.Hash);
            Assert.Equal("Add the important part", lost.Entry.Subject);
            Assert.Contains(list.All, p => p.Entry.Action == ReflogAction.Reset && p.IsOnBranch);

            var name = recovery.SuggestBranchName(lost);
            Assert.Equal("recovered/add-the-important-part", name);
            var outcome = await recovery.RestoreAsBranch(name, lost).RunAsync(_ => { }, CancellationToken.None);
            Assert.True(outcome.Result.Succeeded, outcome.Result.StandardError);

            Assert.Equal(lostHash, (await Git("rev-parse", name)).Trim());
            Assert.Equal("main", (await Git("branch", "--show-current")).Trim()); // didn't switch
            Assert.Empty((await recovery.LoadAsync()).Lost);
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal); // git marks objects read-only
            Directory.Delete(root, recursive: true);
        }
    }
}
