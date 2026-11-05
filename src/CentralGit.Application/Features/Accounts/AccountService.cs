using CentralGit.Application.Ports;
using CentralGit.Domain.Accounts;

namespace CentralGit.Application.Features.Accounts;

public sealed record GitIdentity(string Name, string Email);

/// <summary>
/// Settings › Accounts. CentralGit never stores secrets itself: sign-in and sign-out go through
/// the credential helper the user configured (normally Git Credential Manager).
/// </summary>
public interface IAccountService
{
    Task<GitTools> GetToolsAsync(CancellationToken ct = default);
    Task<GitIdentity> GetIdentityAsync(CancellationToken ct = default);

    /// <summary>Writes user.name and user.email to the global git config; empty values unset them.</summary>
    Task SaveIdentityAsync(GitIdentity identity, CancellationToken ct = default);

    IReadOnlyList<StoredCredential> ListAccounts();

    /// <summary>Returns the user name the helper signed in as.</summary>
    Task<string?> SignInAsync(string url, CancellationToken ct = default);

    Task SignOutAsync(StoredCredential credential, CancellationToken ct = default);
}

public sealed class AccountService(IGitAccounts git, ICredentialStore credentials) : IAccountService
{
    public Task<GitTools> GetToolsAsync(CancellationToken ct = default) => git.GetToolsAsync(ct);

    public async Task<GitIdentity> GetIdentityAsync(CancellationToken ct = default) =>
        new(
            await git.GetGlobalAsync("user.name", ct).ConfigureAwait(false),
            await git.GetGlobalAsync("user.email", ct).ConfigureAwait(false)
        );

    public async Task SaveIdentityAsync(GitIdentity identity, CancellationToken ct = default)
    {
        await git.SetGlobalAsync("user.name", identity.Name.Trim(), ct).ConfigureAwait(false);
        await git.SetGlobalAsync("user.email", identity.Email.Trim(), ct).ConfigureAwait(false);
    }

    public IReadOnlyList<StoredCredential> ListAccounts() => credentials.ListGitCredentials();

    public Task<string?> SignInAsync(string url, CancellationToken ct = default) => git.SignInAsync(url, ct);

    public Task SignOutAsync(StoredCredential credential, CancellationToken ct = default) =>
        git.SignOutAsync(credential, ct);
}
