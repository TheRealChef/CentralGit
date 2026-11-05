using CentralGit.Application.Git;
using CentralGit.Domain.Accounts;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Infrastructure.Git;

namespace CentralGit.Infrastructure.Tests;

public class GitAccountsTests
{
    [Theory]
    [InlineData("git:https://github.com", "mara", "https", "github.com", null, "mara")]
    [InlineData(
        "git:https://dev.azure.com/AxWebDevelopment",
        "h.kuhar",
        "https",
        "dev.azure.com",
        "AxWebDevelopment",
        "h.kuhar"
    )]
    [InlineData("git:https://bob@gitlab.example.com:8443", null, "https", "gitlab.example.com:8443", null, "bob")]
    public void Parses_gcm_targets(
        string target,
        string? user,
        string protocol,
        string host,
        string? path,
        string expectedUser
    )
    {
        Assert.Equal(
            new StoredCredential(protocol, host, path, expectedUser),
            Infrastructure.Windows.CredentialVault.FromTarget(target, user)
        );
    }

    [Theory]
    [InlineData("MicrosoftAccount:user=x")]
    [InlineData("git:ssh://github.com")]
    [InlineData("git:not a url")]
    public void Ignores_other_targets(string target) =>
        Assert.Null(Infrastructure.Windows.CredentialVault.FromTarget(target, "u"));

    [Fact]
    public void Writes_credential_protocol_input()
    {
        var c = new StoredCredential("https", "dev.azure.com", "org", "me");
        Assert.Equal("protocol=https\nhost=dev.azure.com\npath=org\nusername=me\n\n", GitAccounts.CredentialInput(c));
        Assert.Equal("https://dev.azure.com/org", c.Url);
    }
}
