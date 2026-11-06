using System.Collections.ObjectModel;
using System.Diagnostics;
using CentralGit.App.Graph;
using CentralGit.Application.Features.Repositories;
using CentralGit.Application.Features.Sync;
using CentralGit.Application.Git;
using CentralGit.Application.Operations;
using CentralGit.Application.Ports;
using CentralGit.Application.Settings;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Health;
using CentralGit.Domain.Operations;
using CentralGit.Domain.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentralGit.App.ViewModels;

/// <summary>Browse: history first. Investigate: a commit in depth with a band of history. Focus: the diff alone.</summary>
public enum WorkspaceMode
{
    Browse,
    Investigate,
    Focus,
}

public enum AppPage
{
    History,
    Changes,
    Health,
    Recovery,
    FileHistory,
    Compare,
    Stashes,
}

public sealed partial class ShellViewModel : ObservableObject
{
    private const int HistoryPageSize = 5000;

    private readonly IRepositoryService _repositories;
    private readonly ISettingsStore _settings;
    private readonly ILayoutStore _layouts;
    private readonly IExternalTools _external;
    private RepositorySession? _session;

    // Full history as loaded so far (pages appended), plus what the list currently shows.
    private ObservableCollection<CommitRowViewModel> _all = [];

    /// <summary>History rows loaded so far, unfiltered.</summary>
    public IReadOnlyList<CommitRowViewModel> LoadedCommits => _all;
    private readonly Dictionary<string, CommitRowViewModel> _byHash = new(StringComparer.Ordinal);
    private HistoryBuilder? _builder;
    private bool _hasMoreHistory;
    private bool _loadingMore;
    private int _historyGeneration;

    private CommitFilter _filter = CommitFilter.Parse(null);
    private string _filterText = "";
    private CancellationTokenSource? _filterCts;

    // Alt+Left / Alt+Right: commits the user looked at, like a browser history.
    private readonly List<string> _back = [];
    private readonly List<string> _forward = [];
    private bool _navigating;

    private WorkspaceMode _mode;
    private string _repositoryName = "No repository";
    private string _branchName = "";
    private int _ahead;
    private int _behind;
    private int _changedFiles;
    private string _statusText = "Starting…";
    private bool _statusIsError;
    private string _commitCountText = "";
    private string _repositoryHubText = "";
    private IReadOnlyList<CommitRowViewModel> _commits = [];
    private CommitRowViewModel? _selectedCommit;

    public ShellViewModel(
        IRepositoryService repositories,
        ISettingsStore settings,
        ILayoutStore layouts,
        IExternalTools external
    )
    {
        _repositories = repositories;
        _settings = settings;
        Diff.DiffRowViewModel.WrapLines = settings.Current.WrapDiffLines;
        _layouts = layouts;
        _external = external;
        FetchCommand = new AsyncRelayCommand(FetchAsync, CanRunRemote);
        InitializeOperations();
        Changes = new ChangesViewModel(
            () => _session!.Changes.ReadStatusAsync(),
            ReloadAsync,
            status =>
            {
                ChangedFiles = status.ChangedFileCount;
                _ = RefreshInProgressAsync(status);
                _ = RefreshBisectAsync();
            }
        );
        Changes.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ChangesViewModel.IsConflictSelected) or nameof(ChangesViewModel.SelectedPath))
                SyncConflictEditor();
        };
        Changes.Committed += message => SetStatus(message, error: false);
    }

    public IAsyncRelayCommand FetchCommand { get; }

    public ObservableCollection<BranchItemViewModel> Branches { get; } = [];

    public InspectorViewModel Inspector { get; } = new();

    public ChangesViewModel Changes { get; }

    private AppPage _page = AppPage.History;

    /// <summary>Which workspace page is showing.</summary>
    public AppPage Page
    {
        get => _page;
        set
        {
            if (!SetProperty(ref _page, value))
                return;
            OnPropertyChanged(nameof(ShowHistory));
            OnPropertyChanged(nameof(ShowChanges));
            OnPropertyChanged(nameof(ShowHealth));
            OnPropertyChanged(nameof(ShowRecovery));
            OnPropertyChanged(nameof(ShowFileHistory));
            OnPropertyChanged(nameof(ShowCompare));
            OnPropertyChanged(nameof(ShowStashes));
            if (value == AppPage.Changes)
                _ = Changes.RefreshAsync();
            else if (value == AppPage.Compare)
                _ = LoadCompareAsync();
            else if (value == AppPage.Recovery)
                _ = LoadRecoveryAsync();
            else if (value == AppPage.Stashes)
                _ = LoadStashesAsync();
            else if (value == AppPage.Health)
                _ = LoadHealthAsync();
        }
    }

    /// <summary>The Changes page (working tree + composer). Setting false goes back to History.</summary>
    public bool ShowChanges
    {
        get => _page == AppPage.Changes;
        set => Page = value ? AppPage.Changes : AppPage.History;
    }

    public bool ShowHistory => _page == AppPage.History;
    public bool ShowHealth => _page == AppPage.Health;
    public bool ShowRecovery => _page == AppPage.Recovery;
    public bool ShowFileHistory => _page == AppPage.FileHistory;
    public bool ShowCompare => _page == AppPage.Compare;
    public bool ShowStashes => _page == AppPage.Stashes;

    public string? RepositoryPath => _session?.Path;

    public WorkspaceMode Mode
    {
        get => _mode;
        set => SetProperty(ref _mode, value);
    }

    public string RepositoryName
    {
        get => _repositoryName;
        private set => SetProperty(ref _repositoryName, value);
    }
    public string BranchName
    {
        get => _branchName;
        private set => SetProperty(ref _branchName, value);
    }
    public string CommitCountText
    {
        get => _commitCountText;
        private set => SetProperty(ref _commitCountText, value);
    }
    public string RepositoryHubText
    {
        get => _repositoryHubText;
        private set => SetProperty(ref _repositoryHubText, value);
    }

    /// <summary>What the history list shows: the live, growing full history or a filtered snapshot.</summary>
    public IReadOnlyList<CommitRowViewModel> Commits
    {
        get => _commits;
        private set => SetProperty(ref _commits, value);
    }

    public CommitRowViewModel? SelectedCommit
    {
        get => _selectedCommit;
        set
        {
            var previous = _selectedCommit;
            if (!SetProperty(ref _selectedCommit, value))
                return;

            // Swapping between the graph row and its flat (filtered) twin is not a new selection.
            if (previous?.Commit.Hash == value?.Commit.Hash)
                return;

            if (!_navigating && previous is not null)
            {
                _back.Add(previous.Commit.Hash);
                _forward.Clear();
            }
            _ = Inspector.ShowCommitAsync(value?.Commit.Hash);
            _ = LoadSelectedChecksAsync(value?.Commit.Hash);
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
                _ = ApplyFilterAsync(debounce: true);
        }
    }

    public bool IsFiltered => !_filter.IsEmpty;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }
    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public int Ahead
    {
        get => _ahead;
        private set
        {
            if (SetProperty(ref _ahead, value))
            {
                OnPropertyChanged(nameof(AheadText));
                OnPropertyChanged(nameof(HasAhead));
            }
        }
    }

    public int Behind
    {
        get => _behind;
        private set
        {
            if (SetProperty(ref _behind, value))
            {
                OnPropertyChanged(nameof(BehindText));
                OnPropertyChanged(nameof(HasBehind));
                OnPropertyChanged(nameof(BehindCountText));
            }
        }
    }

    public int ChangedFiles
    {
        get => _changedFiles;
        private set
        {
            if (SetProperty(ref _changedFiles, value))
            {
                OnPropertyChanged(nameof(ChangedFilesText));
                OnPropertyChanged(nameof(HasChanges));
            }
        }
    }

    public string AheadText => Ahead.ToString();
    public bool HasAhead => Ahead > 0;
    public string BehindText => Behind > 0 ? $"↓{Behind}" : "";
    public bool HasBehind => Behind > 0;
    public string BehindCountText => Behind.ToString();
    public bool HasChanges => ChangedFiles > 0;
    public string ChangedFilesText => ChangedFiles.ToString();

    public string UpstreamSummary =>
        Ahead == 0 && Behind == 0 ? "Up to date with upstream"
        : Ahead > 0 && Behind > 0 ? $"{Ahead} ahead, {Behind} behind upstream"
        : Ahead > 0 ? $"{Ahead} ahead of upstream"
        : $"{Behind} behind upstream";

    public async Task InitializeAsync(string? requestedPath)
    {
        var located = await Task.Run(() => _repositories.Locate(requestedPath));
        SetDiscovered(located.Discovered);

        if (located.Selected is null)
        {
            SetStatus(
                "No repository found. Add a scan folder in Settings (Ctrl+,) or start CentralGit with a path.",
                error: true
            );
            return;
        }

        await OpenRepositoryAsync(located.Selected);
    }

    // ─── Repositories ──────────────────────────────────────────────────────

    private IReadOnlyList<DiscoveredRepository> _discovered = [];

    /// <summary>Repositories under the scan folders (Settings › Repositories), hidden ones included.</summary>
    public IReadOnlyList<DiscoveredRepository> Discovered
    {
        get => _discovered;
        private set => SetProperty(ref _discovered, value);
    }

    /// <summary>Raised after another repository was opened, so the view can restore its layout.</summary>
    public event Action? RepositoryOpened;

    public async Task RescanAsync()
    {
        var settings = _settings.Current;
        SetDiscovered(await Task.Run(() => _repositories.Discover(settings)));
    }

    private void SetDiscovered(IReadOnlyList<DiscoveredRepository> found)
    {
        Discovered = found;
        var hidden = found.Count(r => _settings.Current.IsHidden(r.Path));
        RepositoryHubText = hidden > 0 ? $"{found.Count} found · {hidden} hidden" : $"{found.Count} repositories found";
    }

    public async Task OpenRepositoryAsync(string path)
    {
        if (!_repositories.IsRepository(path))
        {
            SetStatus($"{path} is not a Git repository (no .git found).", error: true);
            return;
        }
        if (_session is { } current && string.Equals(current.Path, path, StringComparison.OrdinalIgnoreCase))
            return;

        // Start clean: nothing from the previous repository may leak into this one.
        _historyGeneration++;
        _filterCts?.Cancel();
        _filterText = "";
        _filter = CommitFilter.Parse(null);
        OnPropertyChanged(nameof(FilterText));
        OnPropertyChanged(nameof(IsFiltered));
        _back.Clear();
        _forward.Clear();
        _navigating = true;
        try
        {
            SelectedCommit = null;
        }
        finally
        {
            _navigating = false;
        }
        _all = [];
        _byHash.Clear();
        Commits = _all;
        Branches.Clear();
        _localBranches = [];
        _collapsedFolders.Clear();
        _branchAssessments = new Dictionary<string, BranchAssessment>();
        Health.Clear();
        Mode = WorkspaceMode.Browse;

        _session = _repositories.Open(path);
        Inspector.Attach(_session.History);
        Changes.Attach(_session.Changes);
        FileHistory.Attach(_session.History, _session.Blame);
        Compare.Attach(_session.Compare);
        Stashes.Attach(_session.Stashes);
        ConflictEditor.Attach(_session.Actions);
        Tags.Clear();
        Inspector.IsSplit = _layouts.Get(path).SplitDiff;
        RepositoryName = _session.Name;
        OnPropertyChanged(nameof(RepositoryPath));
        _branch = null;
        FetchCommand.NotifyCanExecuteChanged();
        PullCommand.NotifyCanExecuteChanged();
        PushCommand.NotifyCanExecuteChanged();
        SetStatus($"Opening {_session.Name}…", error: false);

        await ReloadAsync();
        RepositoryOpened?.Invoke();
        // Recovery is cheap (one reflog read): load it now so the sidebar can show lost commits.
        Recovery.Clear();
        _ = LoadRecoveryAsync();
        _ = LoadStashesAsync();
        // Big histories page in ~0.1 s with git's commit-graph instead of ~1 s or more without: keep it fresh.
        var opened = _session;
        _ = Task.Run(async () =>
        {
            try
            {
                await opened.RefreshCommitGraphAsync();
            }
            catch (Exception e) when (e is GitException or IOException or InvalidOperationException) { }
        });
        if (Page == AppPage.Health)
            _ = LoadHealthAsync();
    }

    // ─── Selection & navigation ────────────────────────────────────────────

    /// <summary>Selects the commit with this hash if it is loaded (parent links, navigation).</summary>
    public bool SelectCommit(string hash)
    {
        if (!_byHash.TryGetValue(hash, out var row))
            return false;

        if (IsFiltered)
        {
            // Jumping to a commit the filter hides: drop the filter rather than select something invisible.
            var visible = _commits.FirstOrDefault(r => r.Commit.Hash == hash);
            if (visible is null)
            {
                FilterText = "";
                _filter = CommitFilter.Parse(null);
                ShowAll();
            }
            else
            {
                row = visible;
            }
        }

        SelectedCommit = row;
        return true;
    }

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    public void GoBack() => Navigate(_back, _forward);

    public void GoForward() => Navigate(_forward, _back);

    private void Navigate(List<string> from, List<string> to)
    {
        while (from.Count > 0)
        {
            var hash = from[^1];
            from.RemoveAt(from.Count - 1);
            if (!_byHash.ContainsKey(hash))
                continue; // history was reloaded and the commit is gone (e.g. rewritten)

            if (_selectedCommit is { } current)
                to.Add(current.Commit.Hash);
            _navigating = true;
            try
            {
                SelectCommit(hash);
            }
            finally
            {
                _navigating = false;
            }
            return;
        }
    }

    // ─── History loading ───────────────────────────────────────────────────

    private async Task ReloadAsync()
    {
        if (_session is not { } session)
            return;

        var generation = ++_historyGeneration;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var stateTask = session.History.ReadStateAsync();
            var branchesTask = session.Branches.GetLocalBranchesAsync();
            var assessTask = AssessLocalBranchesAsync(session);

            var (status, head, sync) = await stateTask;
            var builder = new HistoryBuilder(head, status.Branch.Head, sync);
            var (rows, more) = await Task.Run(async () =>
            {
                var page = await session.History.ReadPageAsync(HistoryPageSize);
                return (builder.Add(page.Commits), page.HasMore);
            });
            if (generation != _historyGeneration)
                return;

            _branch = status.Branch;
            BranchName = status.Branch.Head ?? $"detached at {status.Branch.Oid?[..7]}";
            Ahead = status.Branch.Ahead;
            Behind = status.Branch.Behind;
            OnPropertyChanged(nameof(UpstreamSummary));
            UpdateSyncBanner(status.Branch);
            ChangedFiles = status.ChangedFileCount;
            Changes.Apply(status);
            _ = RefreshInProgressAsync(status);
            _ = RefreshBisectAsync();
            _ = RefreshHostingAsync();
            _ = LoadTagsAsync(session);

            _localBranches = await branchesTask;
            _branchAssessments = await assessTask;
            OnPropertyChanged(nameof(StaleBranchText));
            RebuildBranchTree();

            var previous = SelectedCommit?.Commit.Hash;
            _builder = builder;
            _hasMoreHistory = more;
            _byHash.Clear();
            foreach (var r in rows)
                _byHash[r.Commit.Hash] = r;
            _all = new ObservableCollection<CommitRowViewModel>(rows);

            if (IsFiltered)
                await ApplyFilterAsync(debounce: false);
            else
                ShowAll();

            _navigating = true; // a reload is not a navigation step
            try
            {
                if (previous is null || !SelectCommit(previous))
                    SelectedCommit = rows.FirstOrDefault(r => r.IsHead) ?? rows.FirstOrDefault();
            }
            finally
            {
                _navigating = false;
            }

            SetStatus($"Loaded {rows.Count:N0} commits in {stopwatch.ElapsedMilliseconds:N0} ms", error: false);
        }
        catch (GitException e)
        {
            SetStatus($"{e.Result.CommandLine} failed: {FirstLine(e.Result.StandardError)}", error: true);
        }
    }

    /// <summary>Called by the view when the list nears its end: appends the next page of history.</summary>
    public async Task LoadMoreHistoryAsync()
    {
        if (
            _loadingMore
            || !_hasMoreHistory
            || IsFiltered
            || _session is not { } session
            || _builder is not { } builder
        )
            return;

        _loadingMore = true;
        var generation = _historyGeneration;
        try
        {
            SetStatus($"Loading more history after {builder.Count:N0} commits…", error: false);
            var skip = builder.Count;
            var (rows, more) = await Task.Run(async () =>
            {
                var page = await session.History.ReadPageAsync(HistoryPageSize, skip);
                return (builder.Add(page.Commits), page.HasMore);
            });
            if (generation != _historyGeneration)
                return;

            _hasMoreHistory = more;
            // Appended in slices so the UI thread keeps breathing on big pages.
            for (var i = 0; i < rows.Count; i++)
            {
                _byHash[rows[i].Commit.Hash] = rows[i];
                _all.Add(rows[i]);
                if (i % 500 == 499)
                    await Task.Yield();
            }
            UpdateCount();
            SetStatus($"Loaded {_all.Count:N0} commits", error: false);
        }
        catch (GitException e)
        {
            SetStatus($"{e.Result.CommandLine} failed: {FirstLine(e.Result.StandardError)}", error: true);
        }
        finally
        {
            _loadingMore = false;
        }
    }

    private void ShowAll()
    {
        Commits = _all;
        if (_selectedCommit is { } s && _byHash.TryGetValue(s.Commit.Hash, out var full))
            SelectedCommit = full;
        UpdateCount();
    }

    private async Task ApplyFilterAsync(bool debounce)
    {
        _filterCts?.Cancel();
        var cts = _filterCts = new CancellationTokenSource();
        try
        {
            if (debounce)
                await Task.Delay(150, cts.Token);

            var filter = CommitFilter.Parse(_filterText);
            if (filter.IsEmpty)
            {
                _filter = filter;
                OnPropertyChanged(nameof(IsFiltered));
                ShowAll();
                return;
            }

            IReadOnlySet<string>? touching = null;
            if (filter.Path is { } path && _session is { } session)
            {
                SetStatus($"Finding commits that touch {path}…", error: false);
                touching = await session.History.FindCommitsTouchingAsync(path, cts.Token);
            }

            var snapshot = _all.ToList();
            var rows = await Task.Run(
                () =>
                    snapshot
                        .Where(r => filter.Matches(r.Commit) && (touching is null || touching.Contains(r.Commit.Hash)))
                        .Select(r => r.Flat())
                        .ToList(),
                cts.Token
            );
            if (cts.IsCancellationRequested)
                return;

            _filter = filter;
            OnPropertyChanged(nameof(IsFiltered));
            Commits = rows;
            if (_selectedCommit is { } s)
            {
                var match = rows.FirstOrDefault(r => r.Commit.Hash == s.Commit.Hash);
                if (match is not null)
                    SelectedCommit = match;
            }
            UpdateCount();

            var scope = _hasMoreHistory ? $" (searched the latest {_all.Count:N0})" : "";
            SetStatus(
                touching is not null && touching.Count > rows.Count && _hasMoreHistory
                    ? $"{rows.Count:N0} loaded commits touch {filter.Path}; {touching.Count:N0} in all history"
                    : $"{rows.Count:N0} matching commits{scope}",
                error: false
            );
        }
        catch (OperationCanceledException) { }
        catch (GitException e)
        {
            SetStatus($"{e.Result.CommandLine} failed: {FirstLine(e.Result.StandardError)}", error: true);
        }
    }

    private void UpdateCount()
    {
        var total = _hasMoreHistory ? $"{_all.Count:N0}+" : $"{_all.Count:N0}";
        CommitCountText = IsFiltered ? $"{_commits.Count:N0} of {total} commits" : $"{total} commits";
    }

    // ─── Remote operations (Operations Center) ─────────────────────────────

    private BranchStatus? _branch;
    private DateTimeOffset _lastFetch = DateTimeOffset.Now;

    public OperationsViewModel Operations { get; } = new();

    /// <summary>Raised when a fix needs Settings › Accounts.</summary>
    public event Action? AccountsRequested;

    /// <summary>Asks the user to confirm a force push; the view shows the dialog.</summary>
    public Func<string, Task<bool>>? ConfirmForcePush { get; set; }

    public IAsyncRelayCommand PullCommand { get; private set; } = null!;
    public IAsyncRelayCommand PushCommand { get; private set; } = null!;

    private void InitializeOperations()
    {
        PullCommand = new AsyncRelayCommand(() => PullAsync(null), CanRunRemote);
        PushCommand = new AsyncRelayCommand(() => PushAsync(force: false), CanRunRemote);
        Operations.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(OperationsViewModel.IsBusy))
                return;
            FetchCommand.NotifyCanExecuteChanged();
            PullCommand.NotifyCanExecuteChanged();
            PushCommand.NotifyCanExecuteChanged();
        };
    }

    private bool CanRunRemote() => _session is not null && Operations.IsIdle;

    private Task FetchAsync() => FetchAsync(background: false);

    public async Task FetchAsync(bool background)
    {
        if (_session is not { } session || Operations.IsBusy)
            return;

        var (result, op) = await Operations.RunAsync("fetch", "Fetch all remotes", session.Sync.Fetch(), background);
        _lastFetch = DateTimeOffset.Now;
        await FinishAsync(result, op, success: () => $"Fetched in {op.Duration} · {UpstreamSummary}");
    }

    /// <summary>Pull; the sync service picks the strategy (see <see cref="ISyncService.PreparePullAsync"/>).</summary>
    public async Task PullAsync(PullMode? mode)
    {
        if (_session is not { } session || Operations.IsBusy)
            return;

        var pull = await session.Sync.PreparePullAsync(mode);
        var target = _branch?.Upstream ?? "upstream";
        var title = mode switch
        {
            PullMode.Rebase => $"Pull {target} (rebase)",
            PullMode.Merge => $"Pull {target} (merge)",
            _ => $"Pull {target}",
        };
        var (result, op) = await Operations.RunAsync("pull", title, pull);
        await FinishAsync(result, op, success: () => $"Pulled in {op.Duration} · {UpstreamSummary}");
    }

    public async Task PushAsync(bool force)
    {
        if (_session is not { } session || Operations.IsBusy)
            return;

        var plan = await session.Sync.PlanPushAsync(_branch);
        if (plan.Problem is { } problem)
        {
            SetStatus(problem, error: true);
            return;
        }
        var (branch, target) = (plan.Branch, plan.Target);

        if (force && (ConfirmForcePush is null || !await ConfirmForcePush(target)))
            return;

        var title =
            !plan.HasUpstream ? $"Publish {branch} → {target}"
            : force ? $"Force push {branch} → {target}"
            : $"Push {branch} → {target}";
        var (result, op) = await Operations.RunAsync("push", title, session.Sync.Push(plan, force));
        await FinishAsync(result, op, success: () => $"Pushed {branch} to {target} in {op.Duration}");
    }

    /// <summary>A button from a failed operation's explanation.</summary>
    public async Task RunActionAsync(OperationViewModel failed, OperationAction action)
    {
        switch (action.Kind)
        {
            case "pull":
                await PullAsync(null);
                break;
            case "pull-rebase":
                await PullAsync(PullMode.Rebase);
                break;
            case "pull-merge":
                await PullAsync(PullMode.Merge);
                break;
            case "fetch":
                await FetchAsync(background: false);
                break;
            case "publish":
                await PushAsync(force: false);
                break;
            case "force-push":
                await PushAsync(force: true);
                break;
            case "accounts":
                AccountsRequested?.Invoke();
                break;
            case "resolve":
                Page = AppPage.Changes;
                break;
            case "stash":
                await StashChangesAsync(null);
                break;
            case "stash-switch" when failed.Target is { } target:
                await StashAndSwitchAsync(target);
                break;
            case "force-delete" when failed.Target is { } branch:
                await DeleteBranchAsync(branch, force: true);
                break;
            case "retry":
                await (
                    failed.Kind switch
                    {
                        "push" => PushAsync(force: false),
                        "pull" => PullAsync(null),
                        _ => FetchAsync(background: false),
                    }
                );
                break;
        }
    }

    private async Task FinishAsync(OperationOutcome result, OperationViewModel op, Func<string> success)
    {
        // Operations change refs (and a failed pull may have changed some): always refresh, including
        // Recovery, since deleting or resetting a branch can leave commits only the reflog knows.
        await ReloadAsync();
        _ = LoadRecoveryAsync();
        _ = LoadStashesAsync();
        if (op.State == OperationState.Succeeded)
            SetStatus(success(), error: false);
        else if (op.State == OperationState.Failed && !op.IsBackground)
            SetStatus($"{op.Title} failed — {op.ExplanationTitle}. Click here for details.", error: true);
        else if (op.State == OperationState.Cancelled)
            SetStatus($"{op.Title} cancelled", error: false);
    }

    /// <summary>Called every minute by the view; fetches when background sync is due.</summary>
    public async Task BackgroundTickAsync()
    {
        var minutes = _settings.Current.BackgroundFetchMinutes;
        if (minutes <= 0 || _session is null || Operations.IsBusy)
            return;
        if (DateTimeOffset.Now - _lastFetch >= TimeSpan.FromMinutes(minutes))
            await FetchAsync(background: true);
    }

    private void SetStatus(string text, bool error)
    {
        StatusText = text;
        StatusIsError = error;
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
        ?? "no output";
}
