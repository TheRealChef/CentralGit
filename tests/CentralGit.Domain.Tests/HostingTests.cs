using CentralGit.Domain.Hosting;

namespace CentralGit.Domain.Tests;

public class HostingTests
{
    [Theory]
    [InlineData("https://github.com/TheRealChef/CentralGit.git")]
    [InlineData("https://github.com/TheRealChef/CentralGit")]
    [InlineData("https://user@github.com/TheRealChef/CentralGit.git")]
    [InlineData("git@github.com:TheRealChef/CentralGit.git")]
    [InlineData("ssh://git@github.com/TheRealChef/CentralGit.git")]
    public void Parses_github_remotes(string url) =>
        Assert.Equal(new HostedRepository("github.com", "TheRealChef", "CentralGit"), HostedRepository.Parse(url));

    [Theory]
    [InlineData("https://gitlab.com/a/b.git")]
    [InlineData("D:\\repos\\other")]
    [InlineData("https://github.com/only-owner")]
    [InlineData("")]
    public void Ignores_everything_else(string url) => Assert.Null(HostedRepository.Parse(url));

    [Fact]
    public void Compare_url_keeps_slashes_in_branch_names() =>
        Assert.Equal(
            "https://github.com/o/r/compare/main...feature/a%20b?expand=1",
            new HostedRepository("github.com", "o", "r").CompareUrl("main", "feature/a b")
        );

    [Fact]
    public void Checks_combine_failure_over_pending_over_success()
    {
        Assert.Equal(CheckState.None, Checks.Combine([]));
        Assert.Equal(
            CheckState.Success,
            Checks.Combine([new("a", "completed", "success"), new("b", "completed", "skipped")])
        );
        Assert.Equal(
            CheckState.Pending,
            Checks.Combine([new("a", "completed", "success"), new("b", "in_progress", null)])
        );
        Assert.Equal(CheckState.Failure, Checks.Combine([new("a", "completed", "failure"), new("b", "queued", null)]));
    }
}
