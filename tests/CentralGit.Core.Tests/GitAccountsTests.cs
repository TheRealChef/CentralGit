using CentralGit.Core.Git;

namespace CentralGit.Core.Tests;

public class GitAccountsTests
{
    [Theory]
    [InlineData("git:https://github.com", "mara", "https", "github.com", null, "mara")]
    [InlineData("git:https://dev.azure.com/AxWebDevelopment", "h.kuhar", "https", "dev.azure.com", "AxWebDevelopment", "h.kuhar")]
    [InlineData("git:https://bob@gitlab.example.com:8443", null, "https", "gitlab.example.com:8443", null, "bob")]
    public void Parses_gcm_targets(string target, string? user, string protocol, string host, string? path, string expectedUser)
    {
        Assert.Equal(new StoredCredential(protocol, host, path, expectedUser), StoredCredential.FromTarget(target, user));
    }

    [Theory]
    [InlineData("MicrosoftAccount:user=x")]
    [InlineData("git:ssh://github.com")]
    [InlineData("git:not a url")]
    public void Ignores_other_targets(string target) => Assert.Null(StoredCredential.FromTarget(target, "u"));

    [Fact]
    public void Writes_credential_protocol_input()
    {
        var c = new StoredCredential("https", "dev.azure.com", "org", "me");
        Assert.Equal("protocol=https\nhost=dev.azure.com\npath=org\nusername=me\n\n", c.ToCredentialInput());
        Assert.Equal("https://dev.azure.com/org", c.Url);
    }
}
