using CentralGit.Domain.Commits;

namespace CentralGit.Domain.Tests;

public class CommitMessageTests
{
    [Theory]
    [InlineData("Fix it", "", "Fix it")]
    [InlineData("  Fix it ", "\n\nBecause.\n", "Fix it\n\nBecause.")]
    [InlineData(
        "Fix it",
        "# Why\rTotals ignored quantity.\r\rDetails",
        "Fix it\n\n# Why\nTotals ignored quantity.\n\nDetails"
    )] // WinUI TextBox
    [InlineData("Fix it", "a\r\nb", "Fix it\n\na\nb")]
    public void Composes_messages(string summary, string body, string expected) =>
        Assert.Equal(expected, CommitMessage.Compose(summary, body));

    [Fact]
    public void Splits_messages() =>
        Assert.Equal(("Fix it", "# Why\nBecause."), CommitMessage.Split("Fix it\r\n\r\n# Why\r\nBecause.\r\n"));
}
