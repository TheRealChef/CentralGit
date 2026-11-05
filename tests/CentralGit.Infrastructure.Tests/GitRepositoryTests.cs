using CentralGit.Application.Git;
using CentralGit.Domain.Accounts;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Infrastructure.Git;

namespace CentralGit.Infrastructure.Tests;

public class GitRepositoryTests
{
    [Theory]
    [InlineData("", 0, 0, false)]
    [InlineData("[gone]", 0, 0, true)]
    [InlineData("[ahead 1]", 1, 0, false)]
    [InlineData("[behind 4]", 0, 4, false)]
    [InlineData("[ahead 1, behind 4]", 1, 4, false)]
    public void Parses_upstream_tracking(string track, int ahead, int behind, bool gone)
    {
        Assert.Equal((ahead, behind, gone), GitRepository.ParseTrack(track));
    }

    /// <summary>End-to-end against a real temporary repository, so the git CLI contract is exercised too.</summary>
    [Fact]
    public async Task Reads_history_status_and_branches_from_a_real_repository()
    {
        var dir = Directory.CreateTempSubdirectory("centralgit-test-").FullName;
        try
        {
            var git = new GitRunner();
            async Task Git(params string[] args) => await git.RunCheckedAsync(dir, args);

            await Git("init", "-b", "main");
            await Git("config", "user.name", "Test");
            await Git("config", "user.email", "test@example.dev");
            await Git("config", "commit.gpgsign", "false");
            File.WriteAllText(Path.Combine(dir, "a.txt"), "one");
            await Git("add", ".");
            await Git("commit", "-m", "First");
            File.WriteAllText(Path.Combine(dir, "a.txt"), "two");
            await Git("commit", "-am", "Second");
            File.WriteAllText(Path.Combine(dir, "b.txt"), "untracked");

            var repo = new GitRepository(dir, git);
            var history = await repo.GetHistoryAsync(100);
            var status = await repo.GetStatusAsync();
            var branches = await repo.GetLocalBranchesAsync();

            Assert.Equal(["Second", "First"], history.Select(c => c.Subject));
            Assert.Contains("refs/heads/main", history[0].Refs);
            Assert.Equal("main", status.Branch.Head);
            Assert.Single(status.Changes, c => c.Path == "b.txt" && c.Staged == ChangeKind.Untracked);
            Assert.Single(branches, b => b.Name == "main" && b.IsHead);
        }
        finally
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, recursive: true);
        }
    }
}
