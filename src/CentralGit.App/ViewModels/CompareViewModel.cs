using System.Collections.ObjectModel;
using CentralGit.App.Diff;
using CentralGit.App.Theming;
using CentralGit.Application.Features.Compare;
using CentralGit.Application.Git;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Diff;
using CentralGit.Domain.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Windows.UI;

namespace CentralGit.App.ViewModels;

/// <summary>A commit on one side of a comparison.</summary>
public sealed class CompareCommitRowViewModel(CommitInfo commit, bool isAhead, DateTimeOffset now)
{
    public CommitInfo Commit { get; } = commit;
    public string Subject { get; } = commit.Subject;
    public string ShortHash { get; } = commit.Hash[..Math.Min(9, commit.Hash.Length)];
    public string Author { get; } = commit.Author.Name;
    public string WhenText { get; } = RelativeTime.Format(commit.Author.When, now);

    /// <summary>Violet: commits the head side brings. Orange: commits it doesn't have yet.</summary>
    public Color MarkerColor { get; } = isAhead ? Palette.Violet : Palette.Orange;
}

/// <summary>The Compare page: two refs, the commits on each side, and the files head changes.</summary>
public sealed partial class CompareViewModel : ObservableObject
{
    private ICompareService? _compare;
    private Comparison? _comparison;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _diffCts;
    private IReadOnlyList<string> _refs = [];
    private string? _base;
    private string? _head;
    private bool _showFiles = true;
    private ChangedFileViewModel? _selectedFile;
    private IReadOnlyList<DiffRowViewModel> _diffRows = [];
    private string _diffMessage = "";
    private bool _isLoading;
    private string _errorText = "";

    public ObservableCollection<ChangedFileViewModel> Files { get; } = [];
    public ObservableCollection<CompareCommitRowViewModel> Ahead { get; } = [];
    public ObservableCollection<CompareCommitRowViewModel> Behind { get; } = [];

    public IReadOnlyList<string> Refs
    {
        get => _refs;
        private set => SetProperty(ref _refs, value);
    }

    /// <summary>What head is compared against (usually the trunk).</summary>
    public string? Base
    {
        get => _base;
        set
        {
            if (SetProperty(ref _base, value))
                _ = CompareAsync();
        }
    }

    public string? Head
    {
        get => _head;
        set
        {
            if (SetProperty(ref _head, value))
                _ = CompareAsync();
        }
    }

    public bool ShowFiles
    {
        get => _showFiles;
        set
        {
            if (SetProperty(ref _showFiles, value))
                OnPropertyChanged(nameof(ShowCommits));
        }
    }

    public bool ShowCommits => !_showFiles;

    public bool HasComparison => _comparison is not null;
    public string SummaryText => _comparison?.Summary ?? "";
    public string FilesTabText => _comparison is { } c ? $"Files · {c.Files.Count}" : "Files";
    public string CommitsTabText => _comparison is { } c ? $"Commits · {c.AheadCount + c.BehindCount}" : "Commits";
    public string TotalsText =>
        _comparison is { Files.Count: > 0 } c ? $"+{c.Added}  −{c.Deleted} in {Plural(c.Files.Count, "file")}" : "";

    public string AheadTitle => _comparison is { } c ? $"Only on {c.Head} · {c.AheadCount}" : "";
    public string BehindTitle => _comparison is { } c ? $"Only on {c.Base} · {c.BehindCount}" : "";
    public bool HasAhead => Ahead.Count > 0;
    public bool HasBehind => Behind.Count > 0;

    /// <summary>Commit lists stop at a few hundred; say so rather than pretend they're complete.</summary>
    public string TruncatedText =>
        _comparison is { } c && (c.AheadCount > c.Ahead.Count || c.BehindCount > c.Behind.Count)
            ? $"Showing the newest {CompareService.CommitLimit} on each side."
            : "";
    public bool IsTruncated => TruncatedText.Length > 0;

    public bool HasNoFiles => _comparison is not null && Files.Count == 0;

    public ChangedFileViewModel? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetProperty(ref _selectedFile, value))
                _ = LoadDiffAsync(value);
        }
    }

    public IReadOnlyList<DiffRowViewModel> DiffRows
    {
        get => _diffRows;
        private set => SetProperty(ref _diffRows, value);
    }

    public string DiffMessage
    {
        get => _diffMessage;
        private set
        {
            if (SetProperty(ref _diffMessage, value))
                OnPropertyChanged(nameof(HasDiffMessage));
        }
    }

    public bool HasDiffMessage => _diffMessage.Length > 0;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => _errorText.Length > 0;

    /// <summary>A newly opened repository: start over, choices come with the next <see cref="Offer"/>.</summary>
    public void Attach(ICompareService compare)
    {
        _compare = compare;
        _loadCts?.Cancel();
        _diffCts?.Cancel();
        _comparison = null;
        _base = _head = null;
        Refs = [];
        Clear();
    }

    /// <summary>Fills the pickers; keeps the user's pair when both refs still exist.</summary>
    public void Offer(CompareChoices choices)
    {
        Refs = choices.Refs;
        var keep =
            _base is not null && _head is not null && choices.Refs.Contains(_base) && choices.Refs.Contains(_head);
        _base = keep ? _base : choices.Base;
        _head = keep ? _head : choices.Head;
        OnPropertyChanged(nameof(Base));
        OnPropertyChanged(nameof(Head));
    }

    public void Swap()
    {
        (_base, _head) = (_head, _base);
        OnPropertyChanged(nameof(Base));
        OnPropertyChanged(nameof(Head));
        _ = CompareAsync();
    }

    public async Task CompareAsync()
    {
        if (_compare is not { } compare || _base is not { } baseRef || _head is not { } headRef)
            return;
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        IsLoading = true;
        ErrorText = "";
        try
        {
            var comparison = await Task.Run(() => compare.CompareAsync(baseRef, headRef, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
                return;
            Apply(comparison);
        }
        catch (OperationCanceledException) { }
        catch (GitException e)
        {
            if (!cts.IsCancellationRequested)
            {
                _comparison = null;
                Clear();
                ErrorText = e.Result.StandardError.Trim() is { Length: > 0 } why ? why : e.Message;
            }
        }
        finally
        {
            if (_loadCts == cts)
                IsLoading = false;
        }
    }

    private void Apply(Comparison comparison)
    {
        var previousPath = _selectedFile?.Model.Path;
        _comparison = comparison;
        var now = DateTimeOffset.Now;
        Files.Clear();
        foreach (var file in comparison.Files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
            Files.Add(new ChangedFileViewModel(file));
        Ahead.Clear();
        foreach (var commit in comparison.Ahead)
            Ahead.Add(new CompareCommitRowViewModel(commit, isAhead: true, now));
        Behind.Clear();
        foreach (var commit in comparison.Behind)
            Behind.Add(new CompareCommitRowViewModel(commit, isAhead: false, now));
        OnPropertyChanged(string.Empty);
        SelectedFile = Files.FirstOrDefault(f => f.Model.Path == previousPath) ?? Files.FirstOrDefault();
    }

    private void Clear()
    {
        Files.Clear();
        Ahead.Clear();
        Behind.Clear();
        _selectedFile = null;
        DiffRows = [];
        DiffMessage = "";
        ErrorText = "";
        OnPropertyChanged(string.Empty);
    }

    private async Task LoadDiffAsync(ChangedFileViewModel? file)
    {
        _diffCts?.Cancel();
        if (file is null || _compare is not { } compare || _comparison is not { } comparison)
        {
            DiffRows = [];
            DiffMessage = "";
            return;
        }
        var cts = _diffCts = new CancellationTokenSource();
        try
        {
            var diff = await Task.Run(() => compare.GetFileDiffAsync(comparison, file.Model, cts.Token), cts.Token);
            var language = SyntaxHighlighter.FromPath(file.Model.Path);
            var rows = await Task.Run(
                () => DiffRowViewModel.Build(DiffLayout.Unified(diff), split: false, language),
                cts.Token
            );
            if (cts.IsCancellationRequested)
                return;
            DiffMessage =
                diff.IsBinary ? "Binary file, so there's no text diff to show."
                : diff.Hunks.Count == 0
                    ? (
                        file.Model.Kind == ChangeKind.Renamed
                            ? "Renamed without content changes."
                            : "No text changes (mode or whitespace only)."
                    )
                : "";
            DiffRows = rows;
        }
        catch (OperationCanceledException) { }
        catch (GitException e)
        {
            DiffRows = [];
            DiffMessage = $"{e.Result.CommandLine} failed: {e.Result.StandardError.Trim()}";
        }
    }

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";
}
