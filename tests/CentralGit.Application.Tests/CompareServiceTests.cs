using CentralGit.Application.Features.Compare;

namespace CentralGit.Application.Tests;

public class CompareServiceTests
{
    private static readonly CompareService Service = new(new StubRepository(@"C:\repo"));

    [Fact]
    public void A_feature_branch_is_compared_with_the_local_trunk()
    {
        var choices = Service.Choices(["feature/x", "main"], ["origin/main"], "feature/x", "origin/feature/x");
        Assert.Equal(("main", "feature/x"), (choices.Base, choices.Head));
        Assert.Equal(["feature/x", "main", "origin/main"], choices.Refs);
    }

    [Fact]
    public void Without_a_local_trunk_the_remote_one_is_used()
    {
        var choices = Service.Choices(["topic"], ["origin/develop", "origin/topic"], "topic", null);
        Assert.Equal("origin/develop", choices.Base);
    }

    [Fact]
    public void On_the_trunk_it_is_compared_with_its_upstream()
    {
        var choices = Service.Choices(["main", "topic"], ["origin/main"], "main", "origin/main");
        Assert.Equal(("origin/main", "main"), (choices.Base, choices.Head));
    }

    [Fact]
    public void On_a_trunk_without_upstream_another_branch_is_compared_against_it()
    {
        var choices = Service.Choices(["feature/discounts", "main"], [], "main", null);
        Assert.Equal(("main", "feature/discounts"), (choices.Base, choices.Head));
    }

    [Fact]
    public void With_no_trunk_or_upstream_any_other_ref_will_do()
    {
        var choices = Service.Choices(["a", "b"], [], "a", null);
        Assert.Equal(("b", "a"), (choices.Base, choices.Head));
    }

    [Fact]
    public void Detached_head_compares_a_branch_against_the_trunk()
    {
        var choices = Service.Choices(["main", "topic"], [], null, null);
        Assert.Equal(("main", "topic"), (choices.Base, choices.Head));
    }
}
