using CentralGit.Application.Settings;
using CentralGit.Domain.Accounts;
using CentralGit.Domain.Hosting;
using CentralGit.Domain.Repositories;

namespace CentralGit.Application.Ports;

/// <summary>Everything in Settings that CentralGit owns. Read once, written on every change.</summary>
public interface ISettingsStore
{
    AppSettings Current { get; }
    void Update(Func<AppSettings, AppSettings> change);
}

/// <summary>Per-repository layout. Failures are non-fatal: layout is a convenience.</summary>
public interface ILayoutStore
{
    RepositoryLayout Get(string repositoryPath);
    void Set(string repositoryPath, RepositoryLayout layout);
}

/// <summary>Finds repositories on disk and answers questions about folders.</summary>
public interface IRepositoryFileSystem
{
    IEnumerable<DiscoveredRepository> Scan(string root, ScanOptions options, CancellationToken ct = default);
    bool IsRepository(string path);

    bool FolderExists(string path);

    /// <summary>The repository containing <paramref name="start"/>, walking up; null if none.</summary>
    string? FindEnclosingRepository(string? start);

    /// <summary>When the repository was last worked in (its index file changed).</summary>
    DateTime LastActivity(string repositoryPath);

    string CurrentDirectory { get; }
}

/// <summary>Status of many repositories for the switcher, cached briefly.</summary>
public interface IRepositoryGlances
{
    RepositoryGlance? Cached(string path);
    Task<RepositoryGlance?> GetAsync(string path, CancellationToken ct);
    void Invalidate(string path);
}

/// <summary>Credentials stored by Git Credential Manager — names only, never secrets.</summary>
public interface ICredentialStore
{
    IReadOnlyList<StoredCredential> ListGitCredentials();
}

/// <summary>Git identity, tool versions and sign-in through the configured credential helper.</summary>
public interface IGitAccounts
{
    Task<GitTools> GetToolsAsync(CancellationToken ct = default);
    Task<string> GetGlobalAsync(string key, CancellationToken ct = default);

    /// <summary>An empty value unsets the key.</summary>
    Task SetGlobalAsync(string key, string value, CancellationToken ct = default);

    /// <summary>Runs the helper's sign-in for <paramref name="url"/> and keeps the result; returns the user name.</summary>
    Task<string?> SignInAsync(string url, CancellationToken ct = default);

    Task SignOutAsync(StoredCredential credential, CancellationToken ct = default);
}

/// <summary>A newer version published on the release feed.</summary>
public sealed record AvailableUpdate(string Version, string? Notes);

/// <summary>The app's own updates (Velopack, from the GitHub releases).</summary>
public interface IAppUpdater
{
    /// <summary>False when running from a build folder: only installed copies can update.</summary>
    bool IsInstalled { get; }

    string CurrentVersion { get; }

    Task<AvailableUpdate?> CheckAsync(CancellationToken ct = default);

    Task DownloadAsync(AvailableUpdate update, Action<int> percent, CancellationToken ct = default);

    /// <summary>Closes the app, installs the downloaded update and starts the new version.</summary>
    void RestartInto(AvailableUpdate update);
}

/// <summary>Programs outside CentralGit that a folder or file can be handed to.</summary>
public enum ExternalTool
{
    Explorer,
    Terminal,
    Editor,
}

/// <summary>Opens the repository (or one file in it) in Explorer, a terminal or a code editor.</summary>
public interface IExternalTools
{
    /// <summary>Starts the tool; returns why it couldn't, or null when it started.</summary>
    string? Open(ExternalTool tool, string folder, string? file = null);

    /// <summary>Opens a web page in the default browser; returns why it couldn't, or null.</summary>
    string? OpenUrl(string url);
}

/// <summary>A code host's API (GitHub): pull requests and CI checks. Failures read as "nothing known", never throw.</summary>
public interface IHostingProvider
{
    /// <summary>The open pull request whose head is <paramref name="branch"/> in this repository; null if none or unknown.</summary>
    Task<PullRequest?> FindOpenPullRequestAsync(
        HostedRepository repository,
        string branch,
        CancellationToken ct = default
    );

    /// <summary>The combined state of the check runs on <paramref name="commit"/>.</summary>
    Task<CheckState> GetChecksAsync(HostedRepository repository, string commit, CancellationToken ct = default);
}
