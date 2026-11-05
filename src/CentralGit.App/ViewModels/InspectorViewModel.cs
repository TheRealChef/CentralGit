using CentralGit.App.Diff;
using CentralGit.Application.Features.History;
using CentralGit.Application.Git;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Diff;
using CentralGit.Domain.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CentralGit.App.ViewModels;

/// <summary>
/// The lower half of the workspace: details of the selected commit, its files, and the diff of
/// the selected file. Loads are cancelled when the selection moves on, so arrowing through
/// history never queues up git processes.
/// </summary>
public sealed partial class InspectorViewModel : ObservableObject
{
    private IHistoryService? _history;
    private CancellationTokenSource? _commitCts;
    private CancellationTokenSource? _diffCts;
    private FileDiff? _currentDiff;
    private string? _preferredPath;

    private CommitDetailsViewModel? _details;
    private ChangedFileViewModel? _selectedFile;
    private IReadOnlyList<DiffRowViewModel> _diffRows = [];
    private IReadOnlyList<int> _changeStarts = [];
    private int _currentChange = -1;
    private string _diffMessage = "";
    private bool _isSplit = true;
    private string _emptyText = "Select a commit to inspect it";

    /// <summary>Raised when the view should bring a diff row into view (change navigation).</summary>
    public event Action<int>? ScrollToRowRequested;

    public CommitDetailsViewModel? Details
    {
        get => _details;
        private set
        {
            if (SetProperty(ref _details, value))
            {
                OnPropertyChanged(nameof(HasDetails));
                OnPropertyChanged(nameof(HasNoDetails));
            }
        }
    }

    public bool HasDetails => _details is not null;
    public bool HasNoDetails => _details is null;

    public string EmptyText
    {
        get => _emptyText;
        private set => SetProperty(ref _emptyText, value);
    }

    public ChangedFileViewModel? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (!SetProperty(ref _selectedFile, value))
                return;
            OnPropertyChanged(nameof(DiffTitle));
            OnPropertyChanged(nameof(DiffDirectory));
            if (value is not null)
                _preferredPath = value.Model.Path;
            _ = LoadDiffAsync(value);
        }
    }

    public string DiffTitle => _selectedFile?.FileName ?? "";
    public string DiffDirectory => _selectedFile?.Model.Directory ?? "";

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

    public bool IsSplit
    {
        get => _isSplit;
        set
        {
            if (!SetProperty(ref _isSplit, value))
                return;
            OnPropertyChanged(nameof(IsUnified));
            if (_currentDiff is { } diff && _selectedFile is { } file)
                _ = ShowDiffAsync(diff, file, CancellationToken.None);
        }
    }

    public bool IsUnified
    {
        get => !_isSplit;
        set => IsSplit = !value;
    }

    public string ChangePosition =>
        _changeStarts.Count == 0 ? "no changes"
        : _currentChange < 0 ? (_changeStarts.Count == 1 ? "1 change" : $"{_changeStarts.Count} changes")
        : $"{_currentChange + 1} / {_changeStarts.Count}";

    public void Attach(IHistoryService history)
    {
        _history = history;
    }

    public async Task ShowCommitAsync(string? hash)
    {
        _commitCts?.Cancel();
        _diffCts?.Cancel();
        if (hash is null || _history is not { } history)
        {
            Details = null;
            return;
        }

        var cts = _commitCts = new CancellationTokenSource();
        try
        {
            // Debounce: holding an arrow key should not spawn a git process per row.
            await Task.Delay(40, cts.Token);
            var details = await Task.Run(
                async () => new CommitDetailsViewModel(await history.GetCommitAsync(hash, cts.Token)),
                cts.Token
            );
            if (cts.IsCancellationRequested)
                return;

            Details = details;
            SelectedFile =
                details.Files.FirstOrDefault(f => f.Model.Path == _preferredPath) ?? details.Files.FirstOrDefault();
            if (details.Files.Count == 0)
            {
                DiffRows = [];
                DiffMessage = "This commit changes no files.";
            }
        }
        catch (OperationCanceledException) { }
        catch (GitException e)
        {
            Details = null;
            EmptyText = $"{e.Result.CommandLine} failed: {e.Result.StandardError.Trim()}";
        }
    }

    private async Task LoadDiffAsync(ChangedFileViewModel? file)
    {
        _diffCts?.Cancel();
        _currentDiff = null;
        if (file is null || _details is not { } details || _history is not { } history)
        {
            DiffRows = [];
            DiffMessage = "";
            return;
        }

        var cts = _diffCts = new CancellationTokenSource();
        try
        {
            var diff = await Task.Run(() => history.GetFileDiffAsync(details.Model, file.Model, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
                return;
            _currentDiff = diff;
            await ShowDiffAsync(diff, file, cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (GitException e)
        {
            DiffRows = [];
            DiffMessage = $"{e.Result.CommandLine} failed: {e.Result.StandardError.Trim()}";
        }
    }

    private async Task ShowDiffAsync(FileDiff diff, ChangedFileViewModel file, CancellationToken ct)
    {
        // A side-by-side view of a file that only exists on one side is half empty: use one column.
        var split = _isSplit && file.Model.Kind is not (ChangeKind.Added or ChangeKind.Deleted);
        var language = SyntaxHighlighter.FromPath(file.Model.Path);
        var (rows, starts) = await Task.Run(
            () =>
            {
                var layout = split ? DiffLayout.Split(diff) : DiffLayout.Unified(diff);
                var starts = new List<int>();
                for (var i = 0; i < layout.Count; i++)
                    if (layout[i].ChangeBlock >= 0 && (i == 0 || layout[i - 1].ChangeBlock != layout[i].ChangeBlock))
                        starts.Add(i);
                return (DiffRowViewModel.Build(layout, split, language), (IReadOnlyList<int>)starts);
            },
            ct
        );

        if (ct.IsCancellationRequested)
            return;

        DiffMessage =
            diff.IsBinary ? "Binary file — no text diff to show."
            : diff.Hunks.Count == 0
                ? (
                    file.Model.Kind == ChangeKind.Renamed
                        ? "Renamed without content changes."
                        : "No text changes (mode or whitespace only)."
                )
            : "";
        _changeStarts = starts;
        _currentChange = -1;
        DiffRows = rows;
        OnPropertyChanged(nameof(ChangePosition));
    }

    /// <summary>Alt+Down / Alt+Up: jump between runs of changes.</summary>
    public void MoveChange(int direction)
    {
        if (_changeStarts.Count == 0)
            return;
        _currentChange = Math.Clamp(_currentChange + direction, 0, _changeStarts.Count - 1);
        OnPropertyChanged(nameof(ChangePosition));
        ScrollToRowRequested?.Invoke(_changeStarts[_currentChange]);
    }

    /// <summary>Moves the file selection (used by keyboard navigation in Focus mode).</summary>
    public void MoveFile(int direction)
    {
        if (_details is not { Files.Count: > 0 } d)
            return;
        var index = _selectedFile is null ? -1 : IndexOf(d.Files, _selectedFile);
        SelectedFile = d.Files[Math.Clamp(index + direction, 0, d.Files.Count - 1)];
    }

    private static int IndexOf(IReadOnlyList<ChangedFileViewModel> files, ChangedFileViewModel file)
    {
        for (var i = 0; i < files.Count; i++)
            if (ReferenceEquals(files[i], file))
                return i;
        return -1;
    }
}
