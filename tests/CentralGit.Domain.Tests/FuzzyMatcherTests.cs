using CentralGit.Domain.Repositories;

namespace CentralGit.Domain.Tests;

public class FuzzyMatcherTests
{
    [Theory]
    [InlineData("cg", "centralGit")]
    [InlineData("clics", "Clics_Portal")]
    [InlineData("axui", "axess-ui-kit")]
    [InlineData("", "anything")]
    public void Matches_subsequences(string query, string text) => Assert.NotNull(FuzzyMatcher.Score(query, text));

    [Theory]
    [InlineData("xyz", "centralGit")]
    [InlineData("tgc", "centralGit")] // order matters
    public void Rejects_non_matches(string query, string text) => Assert.Null(FuzzyMatcher.Score(query, text));

    [Fact]
    public void Ranks_word_starts_and_exact_prefixes_first()
    {
        string[] repos = ["Vacation2.0", "VacationApp", "ax-ai-reporting", "AxCore", "axess-ui-kit"];

        Assert.Equal("AxCore", Best("axc", repos));
        Assert.Equal("axess-ui-kit", Best("aui", repos));
        Assert.Equal("ax-ai-reporting", Best("axair", repos));
    }

    [Fact]
    public void Prefers_the_shorter_name_for_the_same_match()
    {
        Assert.Equal("CLICS", Best("clics", ["Clics_Portal", "CLICS"]));
    }

    private static string Best(string query, IEnumerable<string> texts) =>
        texts
            .Select(t => (t, s: FuzzyMatcher.Score(query, t)))
            .Where(x => x.s is not null)
            .OrderByDescending(x => x.s)
            .First()
            .t;
}
