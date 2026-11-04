using System.Collections.ObjectModel;
using System.Diagnostics;
using CentralGit.App.Graph;
using CentralGit.App.Services;
using CentralGit.Core.Git;
using CentralGit.Core.Graph;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentralGit.App.ViewModels;

/// <summary>Browse: history first. Investigate: a commit in depth with a band of history. Focus: the diff alone.</summary>
public enum WorkspaceMode { Browse, Investigate, Focus }

public sealed partial class ShellViewModel : ObservableObject
{
    private const int HistoryPageSize = 5000;

    private Dictionary<string, CommitRowViewModel> _byHash = [];
    private WorkspaceMode _mode;

    private GitRepository? _repository;

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

    public string RepositoryName { get => _repositoryName; private set => SetProperty(ref _repositoryName, value); }
    public string BranchName { get => _branchName; private set => SetProperty(ref _branchName, value); }
    public string CommitCountText { get => _commitCountText; private set => SetProperty(ref _commitCountText, value); }
    public string RepositoryHubText { get => _repositoryHubText; private set => SetProperty(ref _repositoryHubText, value); }
    public IReadOnlyList<CommitRowViewModel> Commits { get => _commits; private set => SetProperty(ref _commits, value); }
    public CommitRowViewModel? SelectedCommit
    {
        get => _selectedCommit;
        set
        {
            if (SetProperty(ref _selectedCommit, value))
                _ = Inspector.ShowCommitAsync(value?.Commit.Hash);
        }
    }

    public InspectorViewModel Inspector { get; } = new();

    public string? RepositoryPath => _repository?.Path;

    public WorkspaceMode Mode { get => _mode; set => SetProperty(ref _mode, value); }

    /// <summary>Selects the commit with this hash if it is loaded (parent links in the inspector).</summary>
    public bool SelectCommit(string hash)
    {
        if (!_byHash.TryGetValue(hash, out var row))
            return false;
        SelectedCommit = row;
        return true;
    }

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

    private async Task ReloadAsync()
    {
        if (_repository is not { } repo)
            return;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var statusTask = repo.GetStatusAsync();
            var branchesTask = repo.GetLocalBranchesAsync();
            var headTask = repo.GetHeadHashAsync();

            var status = await statusTask;
            var head = await headTask;
            var rows = await Task.Run(async () => BuildRows(await repo.GetHistoryAsync(HistoryPageSize), head, status.Branch.Head));

            BranchName = status.Branch.Head ?? $"detached at {status.Branch.Oid?[..7]}";
            Ahead = status.Branch.Ahead;
            Behind = status.Branch.Behind;
            OnPropertyChanged(nameof(UpstreamSummary));
            ChangedFiles = status.ChangedFileCount;

            Branches.Clear();
            foreach (var b in (await branchesTask).OrderByDescending(b => b.IsHead).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
                Branches.Add(new BranchItemViewModel(b));

            var previous = SelectedCommit?.Commit.Hash;
            _byHash = rows.ToDictionary(r => r.Commit.Hash);
            Commits = rows;
            SelectedCommit = (previous is not null ? _byHash.GetValueOrDefault(previous) : null)
                             ?? rows.FirstOrDefault(r => r.IsHead) ?? rows.FirstOrDefault();
            CommitCountText = rows.Count >= HistoryPageSize ? $"latest {rows.Count:N0} commits" : $"{rows.Count:N0} commits";
            SetStatus($"Loaded {rows.Count:N0} commits in {stopwatch.ElapsedMilliseconds:N0} ms", error: false);
        }
        catch (GitException e)
        {
            SetStatus($"{e.Result.CommandLine} failed: {FirstLine(e.Result.StandardError)}", error: true);
        }
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

    private static IReadOnlyList<CommitRowViewModel> BuildRows(IReadOnlyList<CommitInfo> commits, string? head, string? headBranch)
    {
        // The current branch's first-parent chain is what gets the violet lane.
        var firstParent = new Dictionary<string, string?>(commits.Count);
        foreach (var c in commits)
            firstParent[c.Hash] = c.Parents.Count > 0 ? c.Parents[0] : null;

        var onCurrentBranch = new HashSet<string>();
        for (var h = head; h is not null && onCurrentBranch.Add(h); )
            h = firstParent.GetValueOrDefault(h);

        var layout = new GraphLayout();
        var colorizer = new GraphColorizer();
        var rows = new List<CommitRowViewModel>(commits.Count);

        foreach (var commit in commits)
        {
            var graphRow = layout.Add(commit);
            var (strokes, dot) = colorizer.Colorize(graphRow, onCurrentBranch.Contains(commit.Hash));
            rows.Add(new CommitRowViewModel(
                commit, graphRow.Lane, strokes, dot,
                isHead: commit.Hash == head,
                refs: CommitRowViewModel.BuildRefs(commit, headBranch, dot)));
        }

        return rows;
    }

    private void SetStatus(string text, bool error)
    {
        StatusText = text;
        StatusIsError = error;
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "no output";
}
