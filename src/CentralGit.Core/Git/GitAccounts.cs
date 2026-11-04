namespace CentralGit.Core.Git;

/// <summary>A credential Git Credential Manager has stored, e.g. target "git:https://dev.azure.com/org".</summary>
public sealed record StoredCredential(string Protocol, string Host, string? Path, string UserName)
{
    public string Url => Path is null ? $"{Protocol}://{Host}" : $"{Protocol}://{Host}/{Path}";

    /// <summary>
    /// Parses a Windows Credential Manager target written by GCM: <c>git:https://host[/path]</c>,
    /// optionally with <c>user@</c> before the host. Returns null for anything else.
    /// </summary>
    public static StoredCredential? FromTarget(string target, string? userName)
    {
        if (!target.StartsWith("git:", StringComparison.Ordinal) ||
            !Uri.TryCreate(target[4..], UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http"))
            return null;

        var user = uri.UserInfo.Length > 0 ? Uri.UnescapeDataString(uri.UserInfo) : userName ?? "";
        var path = uri.AbsolutePath.Trim('/');
        return new StoredCredential(uri.Scheme, uri.Authority, path.Length == 0 ? null : path, user);
    }

    /// <summary>The key=value block <c>git credential</c> reads on stdin.</summary>
    internal string ToCredentialInput(bool includeUser = true)
    {
        var s = $"protocol={Protocol}\nhost={Host}\n";
        if (Path is not null) s += $"path={Path}\n";
        if (includeUser && UserName.Length > 0) s += $"username={UserName}\n";
        return s + "\n";
    }
}

public sealed record GitTools(string GitVersion, string? CredentialManagerVersion);

/// <summary>
/// Identity and credentials. CentralGit never stores secrets itself: sign-in and sign-out go
/// through <c>git credential</c>, i.e. whatever helper the user configured (normally GCM).
/// </summary>
public sealed class GitAccounts(GitRunner git)
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public async Task<GitTools> GetToolsAsync(CancellationToken ct = default)
    {
        var gitVersion = await git.RunAsync(Home, ["--version"], ct).ConfigureAwait(false);
        var gcm = await git.RunAsync(Home, ["credential-manager", "--version"], ct).ConfigureAwait(false);
        return new GitTools(
            gitVersion.StandardOutput.Replace("git version", "", StringComparison.Ordinal).Trim(),
            gcm.Succeeded ? gcm.StandardOutput.Trim().Split(' ', '+')[0] : null);
    }

    public async Task<string> GetGlobalAsync(string key, CancellationToken ct = default)
    {
        var result = await git.RunAsync(Home, ["config", "--global", "--get", key], ct).ConfigureAwait(false);
        return result.Succeeded ? result.StandardOutput.Trim() : "";
    }

    public async Task SetGlobalAsync(string key, string value, CancellationToken ct = default)
    {
        string[] args = value.Length == 0
            ? ["config", "--global", "--unset", key]
            : ["config", "--global", key, value];
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
        var input = $"protocol={uri.Scheme}\nhost={uri.Authority}\n" +
                    (uri.AbsolutePath.Trim('/') is { Length: > 0 } p ? $"path={p}\n" : "") + "\n";

        var fill = await git.RunAsync(Home, ["credential", "fill"], ct, input).ConfigureAwait(false);
        if (!fill.Succeeded)
            throw new GitException(fill);

        await git.RunCheckedAsync(Home, ["credential", "approve"], ct, fill.StandardOutput).ConfigureAwait(false);
        return fill.StandardOutput.Split('\n').FirstOrDefault(l => l.StartsWith("username=", StringComparison.Ordinal))?[9..];
    }

    /// <summary>Tells the credential helper to forget a stored credential.</summary>
    public Task SignOutAsync(StoredCredential credential, CancellationToken ct = default) =>
        git.RunCheckedAsync(Home, ["credential", "reject"], ct, credential.ToCredentialInput());
}
