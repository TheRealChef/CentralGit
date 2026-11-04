using CentralGit.Core.Git;

namespace CentralGit.Core.Tests;

public class SyncSetsTests
{
    [Fact]
    public async Task Separates_incoming_and_outgoing_commits()
    {
        var root = Directory.CreateTempSubdirectory("centralgit-sync-").FullName;
        try
        {
            var git = new GitRunner();
            string origin = Path.Combine(root, "origin.git"), me = Path.Combine(root, "me"), teammate = Path.Combine(root, "teammate");
            async Task<string> Git(string dir, params string[] args) => (await git.RunCheckedAsync(dir, args)).Trim();
            async Task Setup(string dir)
            {
                await Git(dir, "config", "user.name", "Test");
                await Git(dir, "config", "user.email", "test@example.dev");
                await Git(dir, "config", "commit.gpgsign", "false");
            }
            async Task<string> Commit(string dir, string file)
            {
                File.WriteAllText(Path.Combine(dir, file), file);
                await Git(dir, "add", ".");
                await Git(dir, "commit", "-qm", file);
                return await Git(dir, "rev-parse", "HEAD");
            }

            await Git(root, "init", "-q", "--bare", "-b", "main", origin);
            await Git(root, "clone", "-q", origin, me);
            await Setup(me);
            var shared = await Commit(me, "base.txt");
            await Git(me, "push", "-q", "-u", "origin", "main");

            await Git(root, "clone", "-q", origin, teammate);
            await Setup(teammate);
            var theirs = await Commit(teammate, "theirs.txt");
            await Git(teammate, "push", "-q", "origin", "main");

            var mine = await Commit(me, "mine.txt");
            await Git(me, "fetch", "-q");

            var sync = await new GitRepository(me).GetSyncSetsAsync("origin/main");
            Assert.Equal(CommitSync.Incoming, sync.Of(theirs));
            Assert.Equal(CommitSync.Outgoing, sync.Of(mine));
            Assert.Equal(CommitSync.None, sync.Of(shared));

            Assert.Same(SyncSets.Empty, await new GitRepository(me).GetSyncSetsAsync(null));
        }
        finally
        {
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }
}
