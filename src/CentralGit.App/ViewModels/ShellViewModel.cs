using System.Collections.ObjectModel;
using System.Diagnostics;
using CentralGit.App.Graph;
using CentralGit.App.Services;
using CentralGit.Core.Git;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentralGit.App.ViewModels;

/// <summary>Browse: history first. Investigate: a commit in depth with a band of history. Focus: the diff alone.</summary>
public enum WorkspaceMode { Browse, Investigate, Focus }

public sealed partial class ShellViewModel : ObservableObject
{
    private const int HistoryPageSize = 5000;

    private GitRepository? _repository;

    // Full history as loaded so far (pages appended), plus what the list currently shows.
    private ObservableCollection<CommitRowViewModel> _all = [];
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

    public ShellViewModel()
    {
        FetchCommand = new AsyncRelayCommand(FetchAsync, () => _repository is not null);
    }

    public IAsyncRelayCommand FetchCommand { get; }

    public ObservableCollection<BranchItemViewModel> Branches { get; } = [];

    public InspectorViewModel Inspector { get; } = new();

    public string? RepositoryPath => _repository?.Path;

    public WorkspaceMode Mode { get => _mode; set => SetProperty(ref _mode, value); }

    public string RepositoryName { get => _repositoryName; private set => SetProperty(ref _repositoryName, value); }
    public string BranchName { get => _branchName; private set => SetProperty(ref _branchName, value); }
    public string CommitCountText { get => _commitCountText; private set => SetProperty(ref _commitCountText, value); }
    public string RepositoryHubText { get => _repositoryHubText; private set => SetProperty(ref _repositoryHubText, value); }

    /// <summary>What the history list shows: the live, growing full history or a filtered snapshot.</summary>
    public IReadOnlyList<CommitRowViewModel> Commits { get => _commits; private set => SetProperty(ref _commits, value); }

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

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public bool StatusIsError { get => _statusIsError; private set => SetProperty(ref _statusIsError, value); }

    public int Ahead
    {
        get => _ahead;
        private set { if (SetProperty(ref _ahead, value)) { OnPropertyChanged(nameof(AheadText)); OnPropertyChanged(nameof(HasAhead)); } }
    }

    public int Behind
    {
        get => _behind;
        private set { if (SetProperty(ref _behind, value)) OnPropertyChanged(nameof(BehindText)); }
    }

    public int ChangedFiles
    {
        get => _changedFiles;
        private set { if (SetProperty(ref _changedFiles, value)) { OnPropertyChanged(nameof(ChangedFilesText)); OnPropertyChanged(nameof(HasChanges)); } }
    }

    public string AheadText => Ahead.ToString();
    public bool HasAhead => Ahead > 0;
    public string BehindText => Behind > 0 ? $"↓{Behind}" : "";
    public bool HasChanges => ChangedFiles > 0;
    public string ChangedFilesText => ChangedFiles.ToString();

    public string UpstreamSummary =>
        Ahead == 0 && Behind == 0 ? "Up to date with upstream"
        : Ahead > 0 && Behind > 0 ? $"{Ahead} ahead, {Behind} behind upstream"
        : Ahead > 0 ? $"{Ahead} ahead of upstream" : $"{Behind} behind upstream";

    public async Task InitializeAsync(string? requestedPath)
    {
        var located = await Task.Run(() => RepositoryLocator.Locate(requestedPath));
        RepositoryHubText = $"{located.Discovered.Count} repositories found";

        if (located.Selected is null)
        {
            SetStatus("No repository found. Start CentralGit with a path, e.g. CentralGit.exe D:\\Source\\Repos\\my-repo", error: true);
            return;
        }

        _repository = new GitRepository(located.Selected);
        Inspector.Attach(_repository);
        Inspector.IsSplit = LayoutStore.Get(_repository.Path).SplitDiff;
        RepositoryName = _repository.Name;
        FetchCommand.NotifyCanExecuteChanged();
        await ReloadAsync();
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
            try { SelectCommit(hash); }
            finally { _navigating = false; }
            return;
        }
    }

    // ─── History loading ───────────────────────────────────────────────────

    private async Task ReloadAsync()
    {
        if (_repository is not { } repo)
            return;

        var generation = ++_historyGeneration;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var statusTask = repo.GetStatusAsync();
            var branchesTask = repo.GetLocalBranchesAsync();
            var headTask = repo.GetHeadHashAsync();

            var status = await statusTask;
            var head = await headTask;
            var builder = new HistoryBuilder(head, status.Branch.Head);
            var (rows, more) = await Task.Run(async () =>
            {
                var page = await repo.GetHistoryAsync(HistoryPageSize);
                return (builder.Add(page), page.Count >= HistoryPageSize);
            });
            if (generation != _historyGeneration)
                return;

            BranchName = status.Branch.Head ?? $"detached at {status.Branch.Oid?[..7]}";
            Ahead = status.Branch.Ahead;
            Behind = status.Branch.Behind;
            OnPropertyChanged(nameof(UpstreamSummary));
            ChangedFiles = status.ChangedFileCount;

            Branches.Clear();
            foreach (var b in (await branchesTask).OrderByDescending(b => b.IsHead).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
                Branches.Add(new BranchItemViewModel(b));

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
            finally { _navigating = false; }

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
        if (_loadingMore || !_hasMoreHistory || IsFiltered || _repository is not { } repo || _builder is not { } builder)
            return;

        _loadingMore = true;
        var generation = _historyGeneration;
        try
        {
            SetStatus($"Loading more history after {builder.Count:N0} commits…", error: false);
            var skip = builder.Count;
            var (rows, more) = await Task.Run(async () =>
            {
                var page = await repo.GetHistoryAsync(HistoryPageSize, skip);
                return (builder.Add(page), page.Count >= HistoryPageSize);
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
            if (filter.Path is { } path && _repository is { } repo)
            {
                SetStatus($"Finding commits that touch {path}…", error: false);
                touching = await repo.GetCommitsTouchingAsync(path, cts.Token);
            }

            var snapshot = _all.ToList();
            var rows = await Task.Run(() => snapshot
                .Where(r => filter.Matches(r.Commit) && (touching is null || touching.Contains(r.Commit.Hash)))
                .Select(r => r.Flat())
                .ToList(), cts.Token);
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
            SetStatus(touching is not null && touching.Count > rows.Count && _hasMoreHistory
                ? $"{rows.Count:N0} loaded commits touch {filter.Path}; {touching.Count:N0} in all history"
                : $"{rows.Count:N0} matching commits{scope}", error: false);
        }
        catch (OperationCanceledException)
        {
        }
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

    private async Task FetchAsync()
    {
        if (_repository is not { } repo)
            return;

        SetStatus("Fetching all remotes…", error: false);
        var result = await repo.Git.RunAsync(repo.Path, ["fetch", "--all", "--prune"]);
        if (!result.Succeeded)
        {
            SetStatus($"Fetch failed — {FirstLine(result.StandardError)}", error: true);
            return;
        }

        await ReloadAsync();
        SetStatus($"Fetched all remotes in {result.Duration.TotalSeconds:0.0} s · {UpstreamSummary}", error: false);
    }

    private void SetStatus(string text, bool error)
    {
        StatusText = text;
        StatusIsError = error;
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "no output";
}
