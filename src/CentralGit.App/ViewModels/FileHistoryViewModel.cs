using System.Collections.ObjectModel;
using CentralGit.App.Diff;
using CentralGit.App.Theming;
using CentralGit.Application.Features.History;
using CentralGit.Application.Git;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Diff;
using CentralGit.Domain.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Windows.UI;

namespace CentralGit.App.ViewModels;

/// <summary>One commit in a file's history.</summary>
public sealed class FileRevisionRowViewModel
{
    public FileRevisionRowViewModel(FileRevision revision, string currentPath, DateTimeOffset now)
    {
        Revision = revision;
        Subject = revision.Subject;
        ShortHash = revision.ShortHash;
        Author = revision.Author;
        var known = revision.When != DateTimeOffset.MinValue;
        WhenText = known ? RelativeTime.Format(revision.When, now) : "";
        WhenTooltip = known ? revision.When.ToLocalTime().ToString("f") : "";
        (KindText, KindColor) = revision.Kind switch
        {
            ChangeKind.Added => ("added", Palette.GreenText),
            ChangeKind.Deleted => ("deleted", Palette.RedText),
            ChangeKind.Renamed => ($"renamed from {revision.OldPath}", Palette.Info),
            ChangeKind.Copied => ($"copied from {revision.OldPath}", Palette.Info),
            _ => ("", Palette.TextMuted),
        };
        // Before a rename the file had another name: say which, so the list isn't confusing.
        PathText = revision.Path != currentPath && revision.Kind != ChangeKind.Renamed ? $"as {revision.Path}" : "";
    }

    public FileRevision Revision { get; }
    public string Subject { get; }
    public string ShortHash { get; }
    public string Author { get; }
    public string WhenText { get; }
    public string WhenTooltip { get; }
    public string KindText { get; }
    public Color KindColor { get; }
    public bool HasKind => KindText.Length > 0;
    public string PathText { get; }
    public bool HasPathText => PathText.Length > 0;
}

/// <summary>The File History page: pick a file, see every commit that changed it, and each change.</summary>
public sealed partial class FileHistoryViewModel : ObservableObject
{
    private IHistoryService? _history;
    private IReadOnlyList<string>? _files;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _diffCts;
    private string _path = "";
    private FileRevisionRowViewModel? _selected;
    private IReadOnlyList<DiffRowViewModel> _diffRows = [];
    private string _diffMessage = "";
    private bool _isLoading;
    private string _errorText = "";

    public ObservableCollection<FileRevisionRowViewModel> Revisions { get; } = [];

    /// <summary>The file being shown (its current name), or empty before one is picked.</summary>
    public string Path
    {
        get => _path;
        private set
        {
            if (SetProperty(ref _path, value))
            {
                OnPropertyChanged(nameof(HasPath));
                OnPropertyChanged(nameof(HasNoPath));
                OnPropertyChanged(nameof(FileName));
                OnPropertyChanged(nameof(Folder));
            }
        }
    }

    public bool HasPath => _path.Length > 0;
    public bool HasNoPath => !HasPath;
    public string FileName => _path[(_path.LastIndexOf('/') + 1)..];
    public string Folder => _path.Contains('/') ? _path[.._path.LastIndexOf('/')] : "";

    public string CountText =>
        Revisions.Count switch
        {
            0 => "",
            1 => "1 commit",
            var n => $"{n} commits",
        };

    public FileRevisionRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(SelectedTitle));
                _ = LoadDiffAsync(value);
            }
        }
    }

    public string SelectedTitle => _selected is { } s ? $"{s.ShortHash}  {s.Subject}" : "";

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

    /// <summary>A newly opened repository: forget the old file and file list.</summary>
    public void Attach(IHistoryService history)
    {
        _history = history;
        _files = null;
        _loadCts?.Cancel();
        _diffCts?.Cancel();
        Path = "";
        Revisions.Clear();
        _selected = null;
        DiffRows = [];
        DiffMessage = "";
        ErrorText = "";
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Picker suggestions; the file list is read once per repository.</summary>
    public async Task<IReadOnlyList<string>> SuggestAsync(string query)
    {
        if (_history is not { } history)
            return [];
        try
        {
            _files ??= await Task.Run(() => history.ListFilesAsync());
        }
        catch (GitException)
        {
            return [];
        }
        var files = _files;
        return await Task.Run(() => history.MatchFiles(files, query));
    }

    public async Task ShowAsync(string path)
    {
        if (_history is not { } history || path.Trim().Length == 0)
            return;
        path = path.Trim().Replace('\\', '/');
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        Path = path;
        ErrorText = "";
        IsLoading = true;
        try
        {
            var revisions = await Task.Run(() => history.GetFileHistoryAsync(path, ct: cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
                return;
            var now = DateTimeOffset.Now;
            Revisions.Clear();
            foreach (var revision in revisions)
                Revisions.Add(new FileRevisionRowViewModel(revision, path, now));
            if (Revisions.Count == 0)
            {
                ErrorText =
                    $"No commits on this branch changed {path}. Check the spelling, or that the file is tracked.";
                DiffRows = [];
                DiffMessage = "";
            }
            OnPropertyChanged(nameof(CountText));
            Selected = Revisions.FirstOrDefault();
        }
        catch (OperationCanceledException) { }
        catch (GitException e)
        {
            Revisions.Clear();
            ErrorText = $"{e.Result.CommandLine} failed: {e.Result.StandardError.Trim()}";
        }
        finally
        {
            if (_loadCts == cts)
                IsLoading = false;
        }
    }

    /// <summary>A theme switch: rows hold colors, so rebuild them.</summary>
    public async Task RecolorAsync()
    {
        if (HasPath)
            await ShowAsync(_path);
    }

    private async Task LoadDiffAsync(FileRevisionRowViewModel? row)
    {
        _diffCts?.Cancel();
        if (row is null || _history is not { } history)
        {
            DiffRows = [];
            DiffMessage = "";
            return;
        }
        var cts = _diffCts = new CancellationTokenSource();
        try
        {
            var revision = row.Revision;
            var diff = await Task.Run(() => history.GetRevisionDiffAsync(revision, cts.Token), cts.Token);
            var language = SyntaxHighlighter.FromPath(revision.Path);
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
                        revision.Kind == ChangeKind.Renamed
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
}
