using CentralGit.Domain.Health;

namespace CentralGit.Domain.Tests;

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
        var result = RepositoryHealth
            .Assess(
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
                ],
                merged,
                Now
            )
            .ToDictionary(a => a.Name);

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
            ["main", "develop", "feature/x"],
            ["origin/main", "origin/develop", "origin/feature/x"],
            "origin/develop"
        );
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
}
