using CentralGit.Core.Git;

namespace CentralGit.Core.Tests;

public class CommitFilterTests
{
    private static readonly CommitInfo Commit = new(
        "a1b2c3d4e5f6", [],
        new Signature("Ana Horvat", "ana@example.dev", DateTimeOffset.UnixEpoch),
        new Signature("Ana Horvat", "ana@example.dev", DateTimeOffset.UnixEpoch),
        ["refs/heads/feature/login"], "Fix token refresh on resume");

    [Theory]
    [InlineData("", null, null, null)]
    [InlineData("token", "token", null, null)]
    [InlineData("author:ana", null, "ana", null)]
    [InlineData("path:src/App", null, null, "src/App")]
    [InlineData("src\\Core", null, null, "src/Core")]
    [InlineData("fix a/b thing", "fix a/b thing", null, null)]
    public void Parses_queries(string query, string? text, string? author, string? path)
    {
        Assert.Equal(new CommitFilter(text, author, path), CommitFilter.Parse(query));
    }

    [Theory]
    [InlineData("TOKEN", true)]
    [InlineData("horvat", true)]
    [InlineData("a1b2", true)]
    [InlineData("a1b", false)]       // hash prefixes need 4+ characters
    [InlineData("login", true)]      // ref names
    [InlineData("author:example.dev", true)]
    [InlineData("author:token", false)]
    [InlineData("nothing", false)]
    public void Matches_commits(string query, bool expected)
    {
        Assert.Equal(expected, CommitFilter.Parse(query).Matches(Commit));
    }
}
