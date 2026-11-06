using System.Collections.ObjectModel;
using CentralGit.App.Diff;
using CentralGit.Application.Features.Workflow;
using CentralGit.Application.Git;
using CentralGit.Domain.Diff;
using CentralGit.Domain.Text;
using CentralGit.Domain.Workflow;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CentralGit.App.ViewModels;

/// <summary>One stash entry: its message, where it was made and when.</summary>
public sealed class StashRowViewModel(Stash stash, DateTimeOffset now)
{
    public Stash Stash { get; } = stash;
    public string Title { get; } = stash.Message.Length > 0 ? stash.Message : "(no message)";
    public string Detail { get; } =
        (stash.Branch is { } branch ? $"on {branch} · " : "") + RelativeTime.Format(stash.When, now);
    public string Name { get; } = stash.Name;
    public string WhenTooltip { get; } = stash.When.ToLocalTime().ToString("f");
}

/// <summary>The Stash page: parked changes, what each holds, and the diff of one file.</summary>
public sealed partial class StashViewModel : ObservableObject
{
    private IStashService? _stashes;
    private CancellationTokenSource? _filesCts;
    private CancellationTokenSource? _diffCts;
    private readonly Dictionary<ChangedFileViewModel, StashFile> _files = [];
    private bool _loaded;
    private StashRowViewModel? _selected;
    private ChangedFileViewModel? _selectedFile;
    private IReadOnlyList<DiffRowViewModel> _diffRows = [];
    private string _diffMessage = "";
    private string _errorText = "";

    public ObservableCollection<StashRowViewModel> Rows { get; } = [];
    public ObservableCollection<ChangedFileViewModel> Files { get; } = [];

    public bool IsEmpty => _loaded && Rows.Count == 0;
    public bool HasStashes => Rows.Count > 0;
    public bool HasSelection => _selected is not null;

    /// <summary>Sidebar count next to Stash.</summary>
    public string SidebarText => Rows.Count > 0 ? Rows.Count.ToString() : "";

    public string FilesHeader => _selected is null ? "" : $"{Files.Count} FILE{(Files.Count == 1 ? "" : "S")}";

    public StashRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                _ = LoadFilesAsync(value);
            }
        }
    }

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

    public void Attach(IStashService stashes)
    {
        _stashes = stashes;
        _loaded = false;
        Rows.Clear();
        Selected = null;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Reloads the list, keeping the selection when that stash is still there.</summary>
    public async Task LoadAsync()
    {
        if (_stashes is not { } stashes)
            return;
        try
        {
            var list = await Task.Run(() => stashes.GetStashesAsync());
            if (_stashes != stashes)
                return;
            var keep = _selected?.Stash.Commit;
            var now = DateTimeOffset.Now;
            Rows.Clear();
            foreach (var stash in list)
                Rows.Add(new StashRowViewModel(stash, now));
            _loaded = true;
            ErrorText = "";
            Selected = Rows.FirstOrDefault(r => r.Stash.Commit == keep) ?? Rows.FirstOrDefault();
        }
        catch (GitException e)
        {
            ErrorText = e.Result.StandardError.Trim();
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasStashes));
        OnPropertyChanged(nameof(SidebarText));
    }

    private async Task LoadFilesAsync(StashRowViewModel? row)
    {
        _filesCts?.Cancel();
        Files.Clear();
        _files.Clear();
        SelectedFile = null;
        OnPropertyChanged(nameof(FilesHeader));
        if (row is null || _stashes is not { } stashes)
            return;
        var cts = _filesCts = new CancellationTokenSource();
        try
        {
            var files = await Task.Run(() => stashes.GetFilesAsync(row.Stash, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
                return;
            foreach (var file in files)
            {
                var vm = new ChangedFileViewModel(file.File);
                _files[vm] = file;
                Files.Add(vm);
            }
            OnPropertyChanged(nameof(FilesHeader));
            SelectedFile = Files.FirstOrDefault();
        }
        catch (OperationCanceledException) { }
        catch (GitException e)
        {
            DiffMessage = $"{e.Result.CommandLine} failed: {e.Result.StandardError.Trim()}";
        }
    }

    /// <summary>Rebuilds the diff rows after a layout setting changed (line wrapping).</summary>
    public void Relayout() => _ = LoadDiffAsync(_selectedFile);

    private async Task LoadDiffAsync(ChangedFileViewModel? file)
    {
        _diffCts?.Cancel();
        if (file is null || _stashes is not { } stashes || !_files.TryGetValue(file, out var stashFile))
        {
            DiffRows = [];
            DiffMessage = "";
            return;
        }
        var cts = _diffCts = new CancellationTokenSource();
        try
        {
            var diff = await Task.Run(() => stashes.GetFileDiffAsync(stashFile, cts.Token), cts.Token);
            var language = SyntaxHighlighter.FromPath(stashFile.File.Path);
            var rows = await Task.Run(
                () => DiffRowViewModel.Build(DiffLayout.Unified(diff), split: false, language),
                cts.Token
            );
            if (cts.IsCancellationRequested)
                return;
            DiffMessage =
                diff.IsBinary ? "Binary file, so there's no text diff to show."
                : diff.Hunks.Count == 0 ? "No text changes (mode or whitespace only)."
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
