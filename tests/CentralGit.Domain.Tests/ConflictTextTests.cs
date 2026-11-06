using CentralGit.Domain.Workflow;

namespace CentralGit.Domain.Tests;

public class ConflictTextTests
{
    private const string Merge =
        "a\n<<<<<<< HEAD\nmine\n=======\ntheirs\n>>>>>>> feature\nb\n<<<<<<< HEAD\nx\n||||||| base\no\n=======\ny\n>>>>>>> feature\n";

    [Fact]
    public void Parses_conflicts_between_common_text()
    {
        var text = ConflictText.Parse(Merge);

        Assert.Equal(4, text.Segments.Count);
        Assert.Equal(2, text.Conflicts.Count);
        var first = text.Conflicts[0];
        Assert.Equal(
            ("mine\n", "theirs\n", "HEAD", "feature"),
            (first.Ours, first.Theirs, first.OursLabel, first.TheirsLabel)
        );
        Assert.Null(first.Base);
        Assert.Equal("o\n", text.Conflicts[1].Base);
    }

    [Theory]
    [InlineData(ConflictChoice.Ours, ConflictChoice.Theirs, "a\nmine\nb\ny\n")]
    [InlineData(ConflictChoice.OursThenTheirs, ConflictChoice.TheirsThenOurs, "a\nmine\ntheirs\nb\ny\nx\n")]
    public void Resolves_with_the_chosen_sides(ConflictChoice first, ConflictChoice second, string expected) =>
        Assert.Equal(expected, ConflictText.Parse(Merge).Resolve([first, second]));

    [Fact]
    public void Keeps_crlf_line_endings()
    {
        var text = ConflictText.Parse("a\r\n<<<<<<< HEAD\r\nm\r\n=======\r\nt\r\n>>>>>>> x\r\n");
        Assert.Equal("a\r\nt\r\n", text.Resolve([ConflictChoice.Theirs]));
    }

    [Fact]
    public void Joining_sides_ends_the_first_line_when_the_file_had_no_final_newline()
    {
        var text = ConflictText.Parse("<<<<<<< HEAD\nm\n=======\nt\n>>>>>>> x");
        Assert.Equal("m\nt\n", text.Resolve([ConflictChoice.OursThenTheirs]));
    }

    [Fact]
    public void An_unclosed_marker_stays_plain_text()
    {
        const string half = "a\n<<<<<<< HEAD\nm\n=======\nt\n";
        var text = ConflictText.Parse(half);
        Assert.Empty(text.Conflicts);
        Assert.Equal(half, text.Resolve([]));
    }

    [Fact]
    public void Resolving_needs_every_choice() =>
        Assert.Throws<ArgumentException>(() =>
            ConflictText.Parse(Merge).Resolve([ConflictChoice.Ours, ConflictChoice.Unresolved])
        );
}
