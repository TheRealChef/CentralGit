using System.Collections.ObjectModel;
using CentralGit.App.Theming;
using CentralGit.Application.Features.Accounts;
using CentralGit.Application.Features.Repositories;
using CentralGit.Application.Git;
using CentralGit.Application.Ports;
using CentralGit.Application.Settings;
using CentralGit.Domain.Accounts;
using CommunityToolkit.Mvvm.ComponentModel;
using Windows.UI;

namespace CentralGit.App.ViewModels;

public enum SettingsSection
{
    Repositories,
    Accounts,
    Appearance,
}

public sealed class ScanFolderViewModel(string path, int count, bool exists)
{
    public string Path { get; } = path;
    public string CountText { get; } = count == 1 ? "1 repository" : $"{count} repositories";
    public bool Exists { get; } = exists;
    public string MissingText => Exists ? "" : "folder not found";
}

public sealed partial class RepositoryToggleViewModel(string path, string name, bool isWorktree, bool shown)
    : ObservableObject
{
    private bool _isShown = shown;

    public string Path { get; } = path;
    public string Name { get; } = name;
    public string Location { get; } = System.IO.Path.GetDirectoryName(path) ?? "";
    public string Kind { get; } = isWorktree ? "worktree" : "";

    public event Action<RepositoryToggleViewModel>? Changed;

    public bool IsShown
    {
        get => _isShown;
        set
        {
            if (SetProperty(ref _isShown, value))
                Changed?.Invoke(this);
        }
    }
}

public sealed class AccountViewModel(StoredCredential credential)
{
    public StoredCredential Model { get; } = credential;
    public string Provider { get; } =
        credential.Host switch
        {
            "github.com" => "GitHub",
            "gitlab.com" => "GitLab",
            "bitbucket.org" => "Bitbucket",
            var h
                when h.EndsWith("dev.azure.com", StringComparison.OrdinalIgnoreCase)
                    || h.EndsWith("visualstudio.com", StringComparison.OrdinalIgnoreCase) => "Azure DevOps",
            _ => credential.Host,
        };
    public string Initial => Provider[..1].ToUpperInvariant();
    public string UserName { get; } = credential.UserName.Length > 0 ? credential.UserName : "(no user name)";
    public string Scope { get; } = credential.Url;
}

/// <summary>Settings › Repositories and Settings › Accounts.</summary>
public sealed partial class SettingsViewModel(
    ShellViewModel shell,
    IAccountService accounts,
    IRepositoryService repositories,
    ISettingsStore settings
) : ObservableObject
{
    private SettingsSection _section = SettingsSection.Repositories;
    private int _scanDepth = 3;
    private bool _includeWorktrees = true;
    private string _skipNames = "";
    private string _discoveredSummary = "";
    private string _toolsText = "";
    private bool _credentialManagerMissing;
    private string _userName = "";
    private string _userEmail = "";
    private string _identityMessage = "";
    private string _accountsMessage = "";
    private bool _accountsMessageIsError;
    private string _otherHost = "";
    private bool _busy;

    public ObservableCollection<ScanFolderViewModel> ScanFolders { get; } = [];
    public ObservableCollection<RepositoryToggleViewModel> Repositories { get; } = [];
    public ObservableCollection<AccountViewModel> Accounts { get; } = [];

    public SettingsSection Section
    {
        get => _section;
        set
        {
            if (!SetProperty(ref _section, value))
                return;
            OnPropertyChanged(nameof(IsRepositories));
            OnPropertyChanged(nameof(IsAccounts));
            OnPropertyChanged(nameof(IsAppearance));
            if (value == SettingsSection.Accounts)
                _ = LoadAccountsAsync();
        }
    }

    public bool IsRepositories => _section == SettingsSection.Repositories;
    public bool IsAccounts => _section == SettingsSection.Accounts;
    public bool IsAppearance => _section == SettingsSection.Appearance;

    public string BackText =>
        shell.RepositoryName is { Length: > 0 } n && shell.RepositoryPath is not null ? $"Back to {n}" : "Back";

    // ─── Repositories ──────────────────────────────────────────────────────

    public int ScanDepth
    {
        get => _scanDepth;
        private set
        {
            if (SetProperty(ref _scanDepth, value))
                OnPropertyChanged(nameof(ScanDepthText));
        }
    }

    public string ScanDepthText => _scanDepth.ToString();

    public bool IncludeWorktrees
    {
        get => _includeWorktrees;
        set
        {
            if (!SetProperty(ref _includeWorktrees, value))
                return;
            settings.Update(s => s with { IncludeWorktrees = value });
            _ = RescanAsync();
        }
    }

    public string SkipNames
    {
        get => _skipNames;
        set => SetProperty(ref _skipNames, value);
    }

    private static readonly int[] FetchIntervals = [5, 15, 30, 60];

    public IReadOnlyList<string> FetchIntervalLabels { get; } =
    ["Every 5 minutes", "Every 15 minutes", "Every 30 minutes", "Every hour"];

    public bool BackgroundFetch
    {
        get => settings.Current.BackgroundFetchMinutes > 0;
        set
        {
            settings.Update(s =>
                s with
                {
                    BackgroundFetchMinutes = value ? FetchIntervals[Math.Max(0, _fetchIntervalIndex)] : 0,
                }
            );
            OnPropertyChanged();
        }
    }

    private int _fetchIntervalIndex = 1;

    public int FetchIntervalIndex
    {
        get => _fetchIntervalIndex;
        set
        {
            if (value < 0 || !SetProperty(ref _fetchIntervalIndex, value))
                return;
            if (BackgroundFetch)
                settings.Update(s => s with { BackgroundFetchMinutes = FetchIntervals[value] });
        }
    }

    public string DiscoveredSummary
    {
        get => _discoveredSummary;
        private set => SetProperty(ref _discoveredSummary, value);
    }

    public bool Busy
    {
        get => _busy;
        private set
        {
            if (SetProperty(ref _busy, value))
                OnPropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsIdle => !_busy;

    public void Open(SettingsSection section)
    {
        var s = settings.Current;
        _scanDepth = s.ScanDepth;
        _includeWorktrees = s.IncludeWorktrees;
        _skipNames = string.Join(", ", s.SkipFolderNames);
        _fetchIntervalIndex = Math.Max(
            0,
            Array.IndexOf(FetchIntervals, s.BackgroundFetchMinutes is > 0 and var m ? m : 15)
        );
        OnPropertyChanged(string.Empty);
        RefreshRepositoryLists();
        RefreshThemeChoices();
        Section = section;
        if (section == SettingsSection.Accounts)
            _ = LoadAccountsAsync();
    }

    public void ChangeDepth(int delta)
    {
        var depth = Math.Clamp(_scanDepth + delta, 1, 8);
        if (depth == _scanDepth)
            return;
        ScanDepth = depth;
        settings.Update(s => s with { ScanDepth = depth });
        _ = RescanAsync();
    }

    public void CommitSkipNames()
    {
        var names = _skipNames
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.SequenceEqual(settings.Current.SkipFolderNames, StringComparer.OrdinalIgnoreCase))
            return;
        settings.Update(s => s with { SkipFolderNames = names });
        SkipNames = string.Join(", ", names);
        _ = RescanAsync();
    }

    public async Task AddScanFolderAsync(string path)
    {
        path = Path.GetFullPath(path).TrimEnd('\\');
        if (settings.Current.ScanFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
            return;
        settings.Update(s => s with { ScanFolders = [.. s.ScanFolders, path] });
        await RescanAsync();
    }

    public async Task RemoveScanFolderAsync(string path)
    {
        settings.Update(s =>
            s with
            {
                ScanFolders = s.ScanFolders.Where(f => !f.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList(),
            }
        );
        await RescanAsync();
    }

    public async Task RescanAsync()
    {
        Busy = true;
        try
        {
            await shell.RescanAsync();
            RefreshRepositoryLists();
        }
        finally
        {
            Busy = false;
        }
    }

    private void RefreshRepositoryLists()
    {
        var current = settings.Current;
        var found = shell.Discovered;

        ScanFolders.Clear();
        foreach (var folder in current.ScanFolders)
            ScanFolders.Add(
                new ScanFolderViewModel(
                    folder,
                    found.Count(r =>
                        r.Path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
                    ),
                    repositories.FolderExists(folder)
                )
            );

        foreach (var r in Repositories)
            r.Changed -= OnRepositoryToggled;
        Repositories.Clear();
        foreach (var r in found)
        {
            var row = new RepositoryToggleViewModel(r.Path, r.Name, r.IsWorktree, !current.IsHidden(r.Path));
            row.Changed += OnRepositoryToggled;
            Repositories.Add(row);
        }
        UpdateSummary();
    }

    private void OnRepositoryToggled(RepositoryToggleViewModel row)
    {
        settings.Update(s =>
            s with
            {
                HiddenRepositories = row.IsShown
                    ? s.HiddenRepositories.Where(p => !p.Equals(row.Path, StringComparison.OrdinalIgnoreCase)).ToList()
                    : [.. s.HiddenRepositories, row.Path],
            }
        );
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var shown = Repositories.Count(r => r.IsShown);
        DiscoveredSummary = $"{Repositories.Count} found · {shown} shown in switcher";
    }

    // ─── Accounts ──────────────────────────────────────────────────────────

    public string ToolsText
    {
        get => _toolsText;
        private set => SetProperty(ref _toolsText, value);
    }
    public bool CredentialManagerMissing
    {
        get => _credentialManagerMissing;
        private set => SetProperty(ref _credentialManagerMissing, value);
    }
    public string UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value);
    }
    public string UserEmail
    {
        get => _userEmail;
        set => SetProperty(ref _userEmail, value);
    }
    public string IdentityMessage
    {
        get => _identityMessage;
        private set => SetProperty(ref _identityMessage, value);
    }
    public string OtherHost
    {
        get => _otherHost;
        set => SetProperty(ref _otherHost, value);
    }

    public string AccountsMessage
    {
        get => _accountsMessage;
        private set
        {
            if (SetProperty(ref _accountsMessage, value))
                OnPropertyChanged(nameof(HasAccountsMessage));
        }
    }

    public bool HasAccountsMessage => _accountsMessage.Length > 0;

    public bool AccountsMessageIsError
    {
        get => _accountsMessageIsError;
        private set
        {
            if (SetProperty(ref _accountsMessageIsError, value))
                OnPropertyChanged(nameof(AccountsMessageColor));
        }
    }

    public Color AccountsMessageColor => _accountsMessageIsError ? Palette.RedText : Palette.GreenText;

    public bool HasNoAccounts => Accounts.Count == 0;

    private async Task LoadAccountsAsync()
    {
        var tools = await accounts.GetToolsAsync();
        CredentialManagerMissing = tools.CredentialManagerVersion is null;
        ToolsText = tools.CredentialManagerVersion is { } gcm
            ? $"git {tools.GitVersion} · Git Credential Manager {gcm}"
            : $"git {tools.GitVersion} · Git Credential Manager not found";

        (UserName, UserEmail) = await accounts.GetIdentityAsync();
        IdentityMessage = "";
        await RefreshAccountsAsync();
    }

    private async Task RefreshAccountsAsync()
    {
        var list = await Task.Run(accounts.ListAccounts);
        Accounts.Clear();
        foreach (var c in list)
            Accounts.Add(new AccountViewModel(c));
        OnPropertyChanged(nameof(HasNoAccounts));
    }

    public async Task SaveIdentityAsync()
    {
        try
        {
            await accounts.SaveIdentityAsync(new GitIdentity(UserName, UserEmail));
            IdentityMessage = "Saved to your global git config";
        }
        catch (GitException e)
        {
            IdentityMessage = $"Could not save: {e.Result.StandardError.Trim()}";
        }
    }

    public async Task SignInAsync(string url)
    {
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "https://" + url.Trim().TrimEnd('/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            ShowAccountsMessage($"\"{url}\" is not a valid host", error: true);
            return;
        }

        ShowAccountsMessage($"Signing in to {url} — continue in the Git Credential Manager window…", error: false);
        try
        {
            var user = await accounts.SignInAsync(url);
            ShowAccountsMessage(
                user is { Length: > 0 } ? $"Signed in to {url} as {user}" : $"Signed in to {url}",
                error: false
            );
        }
        catch (GitException e)
        {
            ShowAccountsMessage($"Sign-in to {url} did not complete: {FirstLine(e.Result.StandardError)}", error: true);
        }
        await RefreshAccountsAsync();
    }

    public async Task SignOutAsync(AccountViewModel account)
    {
        try
        {
            await accounts.SignOutAsync(account.Model);
            ShowAccountsMessage($"Signed out of {account.Scope}", error: false);
        }
        catch (GitException e)
        {
            ShowAccountsMessage($"Could not sign out: {FirstLine(e.Result.StandardError)}", error: true);
        }
        await RefreshAccountsAsync();
    }

    private void ShowAccountsMessage(string text, bool error)
    {
        AccountsMessage = text;
        AccountsMessageIsError = error;
    }

    private static string FirstLine(string s) =>
        s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
        ?? "no details from git";
}
