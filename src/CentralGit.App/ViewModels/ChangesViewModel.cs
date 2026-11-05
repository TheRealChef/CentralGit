using System.Collections.ObjectModel;
using CentralGit.App.Diff;
using CentralGit.App.Theming;
using CentralGit.Application.Features.Changes;
using CentralGit.Application.Git;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Diff;
using CentralGit.Domain.Operations;
using CentralGit.Domain.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Windows.UI;

namespace CentralGit.App.ViewModels;

/// <summary>One file on one side of the index.</summary>
public sealed partial class WorkingFileViewModel : ObservableObject
{
    private string _addedText = "";
    private string _deletedText = "";

    /// <summary>Line counts arrive after the list (a second, cheaper git call).</summary>
    public void SetCounts(LineCounts? counts)
    {
        Counts = counts;
        AddedText =
            counts is null ? ""
            : counts.IsBinary ? "binary"
            : counts.Added > 0 ? $"+{counts.Added}"
            : "";
        DeletedText = counts is { IsBinary: false, Deleted: > 0 } ? $"−{counts.Deleted}" : "";
    }

    public LineCounts? Counts { get; private set; }
    public string AddedText
    {
        get => _addedText;
        private set => SetProperty(ref _addedText, value);
    }
    public string DeletedText
    {
        get => _deletedText;
        private set => SetProperty(ref _deletedText, value);
    }

    /// <summary>The status letter sits on a faint tint of its own color.</summary>
    public Color LetterBackground => Palette.WithAlpha(LetterColor, 0.14);

    public WorkingFileViewModel(FileChange change, ChangeSide side)
    {
        Change = change;
        Side = side;
        var slash = change.Path.LastIndexOf('/');
        FileName = slash < 0 ? change.Path : change.Path[(slash + 1)..];
        Directory = slash < 0 ? "" : change.Path[..slash];
        if (change.OriginalPath is { } original && side == ChangeSide.Staged)
            Directory = Directory.Length > 0 ? $"{Directory}  ← {original}" : $"← {original}";

        var kind = side == ChangeSide.Staged ? change.Staged : change.Unstaged;
        (Letter, LetterColor, KindText) =
            change.IsConflicted ? ("!", Palette.OrangeText, "Conflicted — resolve, then stage to mark it resolved")
            : change.Staged == ChangeKind.Untracked
                ? ("U", Palette.GreenText, "Untracked — new file git doesn't know yet")
            : kind switch
            {
                ChangeKind.Added => ("A", Palette.GreenText, "Added"),
                ChangeKind.Deleted => ("D", Palette.RedText, "Deleted"),
                ChangeKind.Renamed => ("R", Palette.Info, "Renamed"),
                ChangeKind.Copied => ("C", Palette.Info, "Copied"),
                ChangeKind.TypeChanged => ("T", Palette.OrangeText, "Type changed"),
                _ => ("M", Palette.TextSecondary, "Modified"),
            };
    }

    public FileChange Change { get; }
    public ChangeSide Side { get; }
    public string Path => Change.Path;
    public string FileName { get; }
    public string Directory { get; }
    public string Letter { get; }
    public Color LetterColor { get; }
    public string KindText { get; }
    public bool IsStaged => Side == ChangeSide.Staged;
    public string ToggleGlyph => IsStaged ? "" : ""; // minus / plus
    public string ToggleTip => IsStaged ? "Unstage (Space)" : "Stage (Space)";

    internal string Key => (IsStaged ? "S:" : "U:") + Path;
}

/// <summary>
/// The Changes page: what's modified, what's staged, the diff of the selected file with
/// per-hunk staging, and the commit composer.
/// </summary>
public sealed partial class ChangesViewModel : ObservableObject
{
    public const int SummarySoftLimit = 50;
    public const int SummaryHardLimit = 72;

    private readonly Func<Task<WorkingTreeStatus>> _readStatus;
    private readonly Func<Task> _reloadRepository;
    private readonly Action<WorkingTreeStatus> _statusChanged;
    private IChangesService? _tree;
    private bool _hasHead;
    private string? _signature; // null until the first status, so a clean first status still fills the header
    private CancellationTokenSource? _diffCts;
    private FileDiff? _diff;
    private WorkingFileViewModel? _selected;
    private IReadOnlyList<DiffRowViewModel> _diffRows = [];
    private string _diffMessage = "";
    private bool _isSplit;
    private string _summary = "";
    private string _body = "";
    private bool _isAmend;
    private bool _busy;
    private bool _rebuilding;
    private string _errorTitle = "";
    private string _errorDetail = "";
    private string _branchName = "";
    private string? _amendBackupSummary;
    private string? _amendBackupBody;

    public ChangesViewModel(
        Func<Task<WorkingTreeStatus>> readStatus,
        Func<Task> reloadRepository,
        Action<WorkingTreeStatus> statusChanged
    )
    {
        _readStatus = readStatus;
        _reloadRepository = reloadRepository;
        _statusChanged = statusChanged;
    }

    /// <summary>Asks before destroying work; the view shows the dialog.</summary>
    public Func<string, string, Task<bool>>? Confirm { get; set; }

    /// <summary>Raised after a commit so the view can switch back to History, say.</summary>
    public event Action<string>? Committed;

    public ObservableCollection<WorkingFileViewModel> Unstaged { get; } = [];
    public ObservableCollection<WorkingFileViewModel> Staged { get; } = [];

    public string UnstagedHeader => $"UNSTAGED · {Unstaged.Count}";
    public string StagedHeader => $"STAGED · {Staged.Count}";

    private string _unstagedTotals = "";
    private string _stagedTotals = "";
    private string _subtitle = "";

    /// <summary>"+12 −4" over all files in a list (shown beside its header).</summary>
    public string UnstagedTotals
    {
        get => _unstagedTotals;
        private set => SetProperty(ref _unstagedTotals, value);
    }
    public string StagedTotals
    {
        get => _stagedTotals;
        private set => SetProperty(ref _stagedTotals, value);
    }

    /// <summary>"3 files on main" under the page title.</summary>
    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }
    public string EmptyStagedText =>
        Staged.Count == 0 && Unstaged.Count > 0
            ? "Nothing staged yet. Click + on a file, or press Space, to stage it — or just commit everything."
            : "";
    public string EmptyUnstagedText =>
        Unstaged.Count == 0 && Staged.Count > 0 ? "Everything is staged and ready to commit." : "";
    public bool HasUnstaged => Unstaged.Count > 0;
    public bool HasStaged => Staged.Count > 0;
    public bool IsClean => Unstaged.Count == 0 && Staged.Count == 0;
    public bool HasFiles => !IsClean;
    public string CleanText => $"Nothing to commit on {_branchName} — the working tree matches HEAD.";

    /// <summary>The file whose diff is shown (from either list).</summary>
    public WorkingFileViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
                return;
            NotifySelection();
            _ = LoadDiffAsync(keepScroll: false);
        }
    }

    // Two lists, one selection: picking in one list clears the other.
    public WorkingFileViewModel? SelectedUnstaged
    {
        get => _selected is { IsStaged: false } f ? f : null;
        set
        {
            if (!_rebuilding && (value is not null || _selected is { IsStaged: false }))
                Selected = value;
        }
    }

    public WorkingFileViewModel? SelectedStaged
    {
        get => _selected is { IsStaged: true } f ? f : null;
        set
        {
            if (!_rebuilding && (value is not null || _selected is { IsStaged: true }))
                Selected = value;
        }
    }

    public bool HasSelection => _selected is not null;
    public string DiffTitle => _selected?.FileName ?? "";
    public string DiffDirectory => _selected?.Directory ?? "";
    public string SideText =>
        _selected is null ? ""
        : _selected.IsStaged ? "Staged"
        : _selected.Change.Staged == ChangeKind.Untracked ? "Untracked"
        : "Not staged";
    public Color SideColor => _selected is { IsStaged: true } ? Palette.GreenText : Palette.OrangeText;
    public Color SideBackground => Palette.WithAlpha(SideColor, 0.14);

    /// <summary>"src / " before the file name in the diff header.</summary>
    public string DiffBreadcrumb =>
        _selected is { } f && f.Change.Path.Contains('/')
            ? string.Join(" / ", f.Change.Path.Split('/')[..^1]) + " / "
            : "";
    public bool CanDiscardFile => _selected is { IsStaged: false, Change.IsConflicted: false };
    public string ToggleFileLabel => _selected is { IsStaged: true } ? "Unstage file" : "Stage file";

    /// <summary>True while a refresh replaces the rows of the same file (the view keeps its scroll position).</summary>
    public bool KeepScroll { get; private set; }

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
            _ = ShowDiffAsync(keepScroll: true, CancellationToken.None);
        }
    }

    public bool IsUnified
    {
        get => !_isSplit;
        set => IsSplit = !value;
    }

    // ─── Composer ─────────────────────────────────────────────────────────

    public string Summary
    {
        get => _summary;
        set
        {
            if (!SetProperty(ref _summary, value))
                return;
            OnPropertyChanged(nameof(SummaryCounter));
            OnPropertyChanged(nameof(SummaryCounterColor));
            OnPropertyChanged(nameof(CanCommit));
        }
    }

    public string Body
    {
        get => _body;
        set => SetProperty(ref _body, value);
    }

    public string SummaryCounter => _summary.Trim().Length == 0 ? "" : $"{_summary.Trim().Length}";

    /// <summary>Muted up to 50 characters, orange up to 72, red beyond (git log and most hosts truncate there).</summary>
    public Color SummaryCounterColor =>
        _summary.Trim().Length switch
        {
            > SummaryHardLimit => Palette.RedText,
            > SummarySoftLimit => Palette.OrangeText,
            _ => Palette.TextMuted,
        };

    public bool IsAmend
    {
        get => _isAmend;
        set
        {
            if (!SetProperty(ref _isAmend, value))
                return;
            _ = OnAmendChangedAsync(value);
            NotifyCommitState();
        }
    }

    public bool CanAmend => _hasHead;

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (SetProperty(ref _busy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                NotifyCommitState();
            }
        }
    }

    public bool IsIdle => !_busy;

    public bool CanCommit =>
        !_busy
        && _tree is not null
        && _summary.Trim().Length > 0
        && (_isAmend || Staged.Count > 0 || Unstaged.Count > 0);

    public string CommitButtonText =>
        _isAmend ? "Amend last commit"
        : Staged.Count > 0 ? $"Commit {Plural(Staged.Count, "file")} to {_branchName}"
        : Unstaged.Count == 1 ? $"Commit 1 file to {_branchName}"
        : Unstaged.Count > 0 ? $"Commit all {Unstaged.Count} files to {_branchName}"
        : "Commit";

    public string CommitHint =>
        _isAmend ? "Replaces the last commit with the staged changes and this message"
        : Staged.Count == 0 && Unstaged.Count > 0 ? "Nothing staged yet: every change will be staged and committed"
        : Unstaged.Count > 0
            ? $"{Plural(Unstaged.Count, "unstaged file")} {(Unstaged.Count == 1 ? "stays" : "stay")} out of this commit"
        : "Ctrl+Enter to commit";

    public string ErrorTitle
    {
        get => _errorTitle;
        private set
        {
            if (SetProperty(ref _errorTitle, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public string ErrorDetail
    {
        get => _errorDetail;
        private set => SetProperty(ref _errorDetail, value);
    }
    public bool HasError => _errorTitle.Length > 0;

    public void ClearError() => (ErrorTitle, ErrorDetail) = ("", "");

    // ─── Loading ──────────────────────────────────────────────────────────

    public void Attach(IChangesService changes)
    {
        _tree = changes;
        _signature = null;
        Unstaged.Clear();
        Staged.Clear();
        Selected = null;
        Summary = "";
        Body = "";
        _isAmend = false;
        OnPropertyChanged(nameof(IsAmend));
        ClearError();
        NotifyLists();
    }

    private int _countsGeneration;

    /// <summary>
    /// Line counts for every listed file: two numstat calls, plus a line count for untracked
    /// files (git has no diff for them). Runs after the lists show, so they never wait on it.
    /// </summary>
    private async Task LoadCountsAsync()
    {
        if (_tree is not { } tree)
            return;
        var generation = ++_countsGeneration;
        var unstagedFiles = Unstaged.ToList();
        var stagedFiles = Staged.ToList();
        try
        {
            var (unstaged, staged, untracked) = await Task.Run(() =>
                tree.ReadCountsAsync(unstagedFiles.Select(f => f.Change))
            );
            if (generation != _countsGeneration)
                return;

            foreach (var f in unstagedFiles)
                f.SetCounts(untracked.TryGetValue(f.Path, out var n) ? n : unstaged.GetValueOrDefault(f.Path));
            foreach (var f in stagedFiles)
                f.SetCounts(staged.GetValueOrDefault(f.Path));
            UnstagedTotals = Totals(unstagedFiles);
            StagedTotals = Totals(stagedFiles);
        }
        catch (GitException)
        {
            // Counts are decoration; the lists and diffs still work without them.
        }
    }

    private static string Totals(IEnumerable<WorkingFileViewModel> files)
    {
        int added = 0,
            deleted = 0;
        foreach (var f in files)
        {
            added += f.Counts?.Added ?? 0;
            deleted += f.Counts?.Deleted ?? 0;
        }
        return added == 0 && deleted == 0 ? "" : $"+{added}  −{deleted}";
    }

    /// <summary>Takes a fresh status (from the shell's reload or our own refresh) and updates the lists.</summary>
    public void Apply(WorkingTreeStatus status)
    {
        _hasHead = status.Branch.Oid is not null;
        _branchName = status.Branch.Head ?? "detached HEAD";
        OnPropertyChanged(nameof(CanAmend));
        OnPropertyChanged(nameof(CleanText));

        var unstaged = status
            .Changes.Where(c => c.IsUnstaged || c.IsConflicted)
            .OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .Select(c => new WorkingFileViewModel(c, ChangeSide.Unstaged))
            .ToList();
        var staged = status
            .Changes.Where(c => c.IsStaged && !c.IsConflicted)
            .OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .Select(c => new WorkingFileViewModel(c, ChangeSide.Staged))
            .ToList();

        // Same files, same states: leave the lists (and the selection and diff) alone.
        var signature = string.Join(
            '\n',
            unstaged.Concat(staged).Select(f => $"{f.Key}|{f.Change.Staged}|{f.Change.Unstaged}")
        );
        var unchanged = signature == _signature;
        _signature = signature;
        if (unchanged)
        {
            _ = LoadDiffAsync(keepScroll: true); // contents may have changed on disk
            _ = LoadCountsAsync();
            return;
        }

        var previous = _selected;
        var previousIndex = previous is null ? -1 : (previous.IsStaged ? Staged : Unstaged).IndexOf(previous);

        // Clearing a list makes its ListView write a null selection back; that is not the user deselecting.
        _rebuilding = true;
        try
        {
            Replace(Unstaged, unstaged);
            Replace(Staged, staged);
        }
        finally
        {
            _rebuilding = false;
        }
        NotifyLists();
        _ = LoadCountsAsync(); // for the rows just created

        // Keep the same file selected; if it left its list, take its neighbour in the same list.
        WorkingFileViewModel? next = null;
        if (previous is not null)
        {
            var list = previous.IsStaged ? Staged : Unstaged;
            next = list.FirstOrDefault(f => f.Key == previous.Key);
            if (next is null && list.Count > 0)
                next = list[Math.Clamp(previousIndex, 0, list.Count - 1)];
            next ??= (previous.IsStaged ? Unstaged : Staged).FirstOrDefault(f => f.Path == previous.Path);
        }
        next ??= Unstaged.FirstOrDefault() ?? Staged.FirstOrDefault();

        if (next is not null && previous is not null && next.Key == previous.Key)
        {
            _selected = next; // same file: refresh its diff in place
            NotifySelection();
            _ = LoadDiffAsync(keepScroll: true);
        }
        else
        {
            Selected = next;
        }
    }

    public async Task RefreshAsync()
    {
        if (_tree is null)
            return;
        try
        {
            var status = await Task.Run(_readStatus);
            Apply(status);
            _statusChanged(status);
        }
        catch (GitException e)
        {
            ShowError("Couldn't read the working tree", e.Result);
        }
    }

    private async Task LoadDiffAsync(bool keepScroll)
    {
        _diffCts?.Cancel();
        if (_selected is not { } file || _tree is not { } tree)
        {
            _diff = null;
            DiffRows = [];
            DiffMessage = IsClean ? "" : "Select a file to see its changes";
            return;
        }

        var cts = _diffCts = new CancellationTokenSource();
        try
        {
            var diff = await Task.Run(() => tree.DiffAsync(file.Change, file.Side, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
                return;
            _diff = diff;
            await ShowDiffAsync(keepScroll, cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (GitException e)
        {
            _diff = null;
            DiffRows = [];
            DiffMessage = $"{e.Result.CommandLine} failed: {e.Result.StandardError.Trim()}";
        }
    }

    private async Task ShowDiffAsync(bool keepScroll, CancellationToken ct)
    {
        if (_diff is not { } diff || _selected is not { } file)
            return;

        var newFile =
            file.Change.Staged == ChangeKind.Untracked
            || (file.IsStaged ? file.Change.Staged : file.Change.Unstaged) is ChangeKind.Added or ChangeKind.Deleted;
        var split = _isSplit && !newFile;
        var actions =
            file.Change.IsConflicted ? HunkActions.None
            : file.IsStaged ? HunkActions.Unstage
            : HunkActions.Stage;
        var language = SyntaxHighlighter.FromPath(file.Path);
        var rows = await Task.Run(
            () =>
                DiffRowViewModel.Build(
                    split ? DiffLayout.Split(diff) : DiffLayout.Unified(diff),
                    split,
                    language,
                    actions
                ),
            ct
        );
        if (ct.IsCancellationRequested)
            return;

        DiffMessage =
            diff.IsBinary ? "Binary file — stage or discard it as a whole."
            : diff.Hunks.Count == 0
                ? (
                    file.Change.IsConflicted
                        ? "Conflicted — open it in your editor, resolve the markers, then stage it."
                        : "No text changes (mode or whitespace only)."
                )
            : "";
        KeepScroll = keepScroll;
        DiffRows = rows;
        KeepScroll = false;
    }

    // ─── Staging ──────────────────────────────────────────────────────────

    public Task ToggleAsync(WorkingFileViewModel file) =>
        RunAsync(
            file.IsStaged ? "Unstage failed" : "Stage failed",
            t => file.IsStaged ? t.UnstageAsync([file.Path], _hasHead) : t.StageAsync([file.Path])
        );

    public Task ToggleSelectedAsync() => _selected is { } f ? ToggleAsync(f) : Task.CompletedTask;

    public Task StageAllAsync() => RunAsync("Stage all failed", t => t.StageAllAsync());

    public Task UnstageAllAsync() => RunAsync("Unstage all failed", t => t.UnstageAllAsync(_hasHead));

    public Task ApplyHunkAsync(int hunk)
    {
        if (_diff is not { } diff || _selected is not { } file || hunk < 0 || hunk >= diff.Hunks.Count)
            return Task.CompletedTask;
        return RunAsync(
            file.IsStaged ? "Unstage hunk failed" : "Stage hunk failed",
            t => t.ApplyHunkToIndexAsync(diff, hunk, file.Side)
        );
    }

    public async Task DiscardHunkAsync(int hunk)
    {
        if (_diff is not { } diff || _selected is not { IsStaged: false } file || hunk < 0 || hunk >= diff.Hunks.Count)
            return;
        var h = diff.Hunks[hunk];
        if (
            Confirm is null
            || !await Confirm(
                "Discard this change?",
                $"Lines {h.NewStart}–{h.NewStart + Math.Max(h.NewCount - 1, 0)} of {file.Path} go back to their staged version. This can't be undone."
            )
        )
            return;
        await RunAsync("Discard failed", t => t.DiscardHunkAsync(diff, hunk));
    }

    public async Task DiscardSelectedAsync()
    {
        if (_selected is not { IsStaged: false } file)
            return;
        var untracked = file.Change.Staged == ChangeKind.Untracked;
        if (
            Confirm is null
            || !await Confirm(
                untracked ? "Delete this new file?" : "Discard changes?",
                untracked
                    ? $"{file.Path} isn't tracked by git, so it is deleted from disk. This can't be undone."
                    : $"Unstaged changes to {file.Path} are thrown away; staged changes stay. This can't be undone."
            )
        )
            return;
        await RunAsync("Discard failed", t => t.DiscardAsync(file.Change));
    }

    private async Task RunAsync(string failure, Func<IChangesService, Task<GitResult>> action)
    {
        if (_tree is not { } tree || _busy)
            return;
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => action(tree));
            if (result.Succeeded)
                ClearError();
            else
                ShowError(failure, result);
        }
        finally
        {
            IsBusy = false;
        }
        await RefreshAsync();
    }

    // ─── Committing ───────────────────────────────────────────────────────

    public async Task CommitAsync()
    {
        if (!CanCommit || _tree is not { } tree)
            return;

        var (summaryText, body, amend) = (_summary, _body, _isAmend);
        var stageAll = !amend && Staged.Count == 0;
        IsBusy = true;
        CommitOutcome outcome;
        try
        {
            outcome = await Task.Run(() => tree.CommitAsync(summaryText, body, amend, stageAll));
        }
        finally
        {
            IsBusy = false;
        }

        var result = outcome.Result;
        if (outcome.StagingFailed)
        {
            ShowError("Couldn't stage the changes", result);
            return;
        }
        if (!result.Succeeded)
        {
            ShowError(_isAmend ? "Amend failed" : "Commit failed", result);
            await RefreshAsync();
            return;
        }

        var summary = _summary.Trim();
        var amended = _isAmend;
        ClearError();
        Summary = "";
        Body = "";
        _amendBackupSummary = _amendBackupBody = null;
        _isAmend = false;
        OnPropertyChanged(nameof(IsAmend));
        await _reloadRepository();
        Committed?.Invoke(amended ? $"Amended: {summary}" : $"Committed: {summary}");
    }

    private async Task OnAmendChangedAsync(bool amend)
    {
        if (_tree is not { } tree)
            return;
        if (amend)
        {
            // Start from the last commit's message, but give back what was typed if amend is turned off.
            _amendBackupSummary = _summary;
            _amendBackupBody = _body;
            if (await tree.LastMessageAsync() is { } last && _isAmend)
                (Summary, Body) = last;
        }
        else if (_amendBackupSummary is not null)
        {
            (Summary, Body) = (_amendBackupSummary, _amendBackupBody ?? "");
            _amendBackupSummary = _amendBackupBody = null;
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private void ShowError(string title, GitResult result)
    {
        var detail = (result.StandardError + "\n" + result.StandardOutput).Trim();
        ErrorTitle = title + Explain(detail);
        ErrorDetail = $"> {result.CommandLine}\n{detail}";
    }

    /// <summary>Plain-language hints for the failures people actually hit when committing.</summary>
    private static string Explain(string output) =>
        output.Contains("Please tell me who you are", StringComparison.Ordinal)
        || output.Contains("empty ident", StringComparison.Ordinal)
            ? " — git doesn't know your name and email yet. Set them in Settings › Accounts."
        : output.Contains("gpg failed", StringComparison.OrdinalIgnoreCase)
        || output.Contains("failed to sign", StringComparison.OrdinalIgnoreCase)
            ? " — signing the commit failed. Check your GPG/SSH signing setup."
        : output.Contains("hook", StringComparison.OrdinalIgnoreCase)
            ? " — a git hook rejected it; its output is below."
        : output.Contains("index.lock", StringComparison.Ordinal)
            ? " — another git process is using this repository. Try again in a moment."
        : output.Contains("patch does not apply", StringComparison.Ordinal)
            ? " — the file changed since the diff was shown. It has been refreshed; try again."
        : "";

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(SelectedUnstaged));
        OnPropertyChanged(nameof(SelectedStaged));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(DiffTitle));
        OnPropertyChanged(nameof(DiffDirectory));
        OnPropertyChanged(nameof(SideText));
        OnPropertyChanged(nameof(SideColor));
        OnPropertyChanged(nameof(SideBackground));
        OnPropertyChanged(nameof(DiffBreadcrumb));
        OnPropertyChanged(nameof(CanDiscardFile));
        OnPropertyChanged(nameof(ToggleFileLabel));
    }

    private void NotifyLists()
    {
        OnPropertyChanged(nameof(UnstagedHeader));
        OnPropertyChanged(nameof(StagedHeader));
        OnPropertyChanged(nameof(HasUnstaged));
        OnPropertyChanged(nameof(HasStaged));
        OnPropertyChanged(nameof(IsClean));
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(EmptyStagedText));
        OnPropertyChanged(nameof(EmptyUnstagedText));
        var total = Unstaged.Select(f => f.Path).Union(Staged.Select(f => f.Path)).Count();
        Subtitle = total == 0 ? $"on {_branchName}" : $"{Plural(total, "file")} changed on {_branchName}";
        NotifyCommitState();
    }

    private void NotifyCommitState()
    {
        OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(CommitButtonText));
        OnPropertyChanged(nameof(CommitHint));
        OnPropertyChanged(nameof(ToggleFileLabel));
    }

    private static void Replace(ObservableCollection<WorkingFileViewModel> target, List<WorkingFileViewModel> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    private static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
