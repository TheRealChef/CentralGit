using CentralGit.Application.Git;
using CentralGit.Application.Ports;
using CentralGit.Domain.Accounts;

namespace CentralGit.Infrastructure.Git;

/// <summary>
/// Identity and credentials. CentralGit never stores secrets itself: sign-in and sign-out go
/// through <c>git credential</c>, i.e. whatever helper the user configured (normally GCM).
/// </summary>
public sealed class GitAccounts(GitRunner git) : IGitAccounts
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public async Task<GitTools> GetToolsAsync(CancellationToken ct = default)
    {
        var gitVersion = await git.RunAsync(Home, ["--version"], ct).ConfigureAwait(false);
        var gcm = await git.RunAsync(Home, ["credential-manager", "--version"], ct).ConfigureAwait(false);
        return new GitTools(
            gitVersion.StandardOutput.Replace("git version", "", StringComparison.Ordinal).Trim(),
            gcm.Succeeded ? gcm.StandardOutput.Trim().Split(' ', '+')[0] : null
        );
    }

    public async Task<string> GetGlobalAsync(string key, CancellationToken ct = default)
    {
        var result = await git.RunAsync(Home, ["config", "--global", "--get", key], ct).ConfigureAwait(false);
        return result.Succeeded ? result.StandardOutput.Trim() : "";
    }

    public async Task SetGlobalAsync(string key, string value, CancellationToken ct = default)
    {
        string[] args = value.Length == 0 ? ["config", "--global", "--unset", key] : ["config", "--global", key, value];
        var result = await git.RunAsync(Home, args, ct).ConfigureAwait(false);
        // --unset of a key that does not exist exits 5: nothing to do.
        if (!result.Succeeded && !(value.Length == 0 && result.ExitCode == 5))
            throw new GitException(result);
    }

    /// <summary>
    /// Asks the credential helper for credentials for <paramref name="url"/> (GCM shows its
    /// browser/device sign-in) and, on success, tells it to keep them.
    /// </summary>
    public async Task<string?> SignInAsync(string url, CancellationToken ct = default)
    {
        var uri = new Uri(url);
        var input =
            $"protocol={uri.Scheme}\nhost={uri.Authority}\n"
            + (uri.AbsolutePath.Trim('/') is { Length: > 0 } p ? $"path={p}\n" : "")
            + "\n";

        var fill = await git.RunAsync(Home, ["credential", "fill"], ct, input).ConfigureAwait(false);
        if (!fill.Succeeded)
            throw new GitException(fill);

        await git.RunCheckedAsync(Home, ["credential", "approve"], ct, fill.StandardOutput).ConfigureAwait(false);
        return fill
            .StandardOutput.Split('\n')
            .FirstOrDefault(l => l.StartsWith("username=", StringComparison.Ordinal))
            ?[9..];
    }

    /// <summary>Tells the credential helper to forget a stored credential.</summary>
    public Task SignOutAsync(StoredCredential credential, CancellationToken ct = default) =>
        git.RunCheckedAsync(Home, ["credential", "reject"], ct, CredentialInput(credential));

    /// <summary>The key=value block <c>git credential</c> reads on stdin.</summary>
    internal static string CredentialInput(StoredCredential credential, bool includeUser = true)
    {
        var s = $"protocol={credential.Protocol}\nhost={credential.Host}\n";
        if (credential.Path is not null)
            s += $"path={credential.Path}\n";
        if (includeUser && credential.UserName.Length > 0)
            s += $"username={credential.UserName}\n";
        return s + "\n";
    }
}
