using CentralGit.Domain.Branches;

namespace CentralGit.Domain.Tests;

public class BranchNamesTests
{
    [Theory]
    [InlineData("Fix login bug", "fix-login-bug")]
    [InlineData("feature/New Thing!", "feature/new-thing")]
    [InlineData("  --weird--  ", "weird")]
    public void Suggests_branch_names(string text, string expected) =>
        Assert.Equal(expected, BranchNames.Suggest(text));

    [Theory]
    [InlineData("", "Type a name for the branch.")]
    [InlineData("has space", "Branch names can't contain spaces — use - or / instead.")]
    [InlineData("topic/ok-1", null)]
    public void Prechecks_names_before_git_does(string name, string? problem) =>
        Assert.Equal(problem, BranchNames.Precheck(name));
}
