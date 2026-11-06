using System.Collections.ObjectModel;
using CentralGit.Application.Features.Workflow;
using CentralGit.Application.Git;
using CentralGit.Domain.Workflow;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CentralGit.App.ViewModels;

/// <summary>One piece of a conflicted file: unchanged text between conflicts, or a conflict to decide.</summary>
public sealed partial class ConflictBlockViewModel : ObservableObject
{
    private const int ContextLines = 3;
    private ConflictChoice _choice;

    public ConflictBlockViewModel(CommonSegment common, bool first, bool last)
    {
        IsConflict = false;
        CommonText = Trim(common.Text, first, last);
    }

    public ConflictBlockViewModel(ConflictHunk hunk, int count)
    {
        IsConflict = true;
        Hunk = hunk;
        Title = $"Conflict {hunk.Index + 1} of {count}";
        Ours = Show(hunk.Ours);
        Theirs = Show(hunk.Theirs);
        OursLabel = hunk.OursLabel.Length > 0 ? $"Yours · {hunk.OursLabel}" : "Yours";
        TheirsLabel = hunk.TheirsLabel.Length > 0 ? $"Incoming · {hunk.TheirsLabel}" : "Incoming";
        BaseTooltip = hunk.Base is { } b ? $"Before either change:\n{Show(b)}" : null;
    }

    public bool IsConflict { get; }
    public bool IsCommon => !IsConflict;
    public ConflictHunk? Hunk { get; }
    public string CommonText { get; } = "";
    public string Title { get; } = "";
    public string Ours { get; } = "";
    public string Theirs { get; } = "";
    public string OursLabel { get; } = "";
    public string TheirsLabel { get; } = "";
    public string? BaseTooltip { get; }

    public ConflictChoice Choice
    {
        get => _choice;
        set
        {
            if (!SetProperty(ref _choice, value))
                return;
            OnPropertyChanged(nameof(OursOpacity));
            OnPropertyChanged(nameof(TheirsOpacity));
            OnPropertyChanged(nameof(ChoiceText));
            OnPropertyChanged(nameof(IsResolved));
        }
    }

    public bool IsResolved => _choice != ConflictChoice.Unresolved;

    /// <summary>The side that won't be kept fades out.</summary>
    public double OursOpacity => _choice == ConflictChoice.Theirs ? 0.4 : 1;
    public double TheirsOpacity => _choice == ConflictChoice.Ours ? 0.4 : 1;

    public string ChoiceText =>
        _choice switch
        {
            ConflictChoice.Ours => "✓ Keeping yours",
            ConflictChoice.Theirs => "✓ Taking incoming",
            ConflictChoice.OursThenTheirs => "✓ Both, yours first",
            ConflictChoice.TheirsThenOurs => "✓ Both, incoming first",
            _ => "Choose what to keep",
        };

    /// <summary>Empty sides read as "(nothing)" rather than an empty box.</summary>
    private static string Show(string text) =>
        text.Length == 0 ? "(nothing — this side removed these lines)" : Clean(text);

    private static string Clean(string text) => text.Replace("\r\n", "\n").TrimEnd('\n');

    /// <summary>Only a few lines around each conflict; the rest is summarised.</summary>
    private static string Trim(string text, bool first, bool last)
    {
        var lines = Clean(text).Split('\n');
        var keepTop = first ? 0 : ContextLines;
        var keepBottom = last ? 0 : ContextLines;
        if (lines.Length <= keepTop + keepBottom + 1)
            return string.Join('\n', lines);
        var hidden = lines.Length - keepTop - keepBottom;
        var parts = new List<string>();
        parts.AddRange(lines.Take(keepTop));
        parts.Add($"⋯ {hidden} unchanged line{(hidden == 1 ? "" : "s")}");
        parts.AddRange(lines.Skip(lines.Length - keepBottom));
        return string.Join('\n', parts);
    }
}

/// <summary>
/// The conflict editor on the Changes page: each conflict of the selected file side by side, a
/// choice per conflict, and Save once every one is decided.
/// </summary>
public sealed partial class ConflictEditorViewModel : ObservableObject
{
    private ICommitActionService? _actions;
    private ConflictText? _text;
    private string? _path;
    private int _loadVersion;
    private bool _isActive;
    private string _message = "";

    public ObservableCollection<ConflictBlockViewModel> Blocks { get; } = [];

    /// <summary>Shown instead of the diff while a conflicted file with readable markers is selected.</summary>
    public bool IsActive
    {
        get => _isActive;
        private set => SetProperty(ref _isActive, value);
    }

    public string Path => _path ?? "";

    public string Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
                OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => _message.Length > 0;

    private IEnumerable<ConflictBlockViewModel> Conflicts => Blocks.Where(b => b.IsConflict);

    public string ProgressText
    {
        get
        {
            var total = Conflicts.Count();
            var done = Conflicts.Count(b => b.IsResolved);
            return done == total
                ? $"All {total} conflict{(total == 1 ? "" : "s")} decided"
                : $"{done} of {total} conflict{(total == 1 ? "" : "s")} decided";
        }
    }

    public bool CanSave => _text is not null && Conflicts.All(b => b.IsResolved);

    public void Attach(ICommitActionService actions)
    {
        _actions = actions;
        Clear();
    }

    public void Clear()
    {
        _loadVersion++;
        _path = null;
        _text = null;
        Blocks.Clear();
        IsActive = false;
        Message = "";
        Changed();
    }

    /// <summary>Reads the file and splits it; stays inactive (the diff shows) when there are no markers to work with.</summary>
    public async Task LoadAsync(string path)
    {
        if (_actions is not { } actions)
            return;
        var version = ++_loadVersion;
        try
        {
            var text = await Task.Run(() => actions.ReadConflictAsync(path));
            if (version != _loadVersion)
                return;
            _path = path;
            _text = text;
            Blocks.Clear();
            if (text is null || text.Conflicts.Count == 0)
            {
                IsActive = false;
                Changed();
                return;
            }
            var count = text.Conflicts.Count;
            for (var i = 0; i < text.Segments.Count; i++)
            {
                var block = text.Segments[i] switch
                {
                    ConflictHunk h => new ConflictBlockViewModel(h, count),
                    CommonSegment c => new ConflictBlockViewModel(c, first: i == 0, last: i == text.Segments.Count - 1),
                    _ => null,
                };
                if (block is null)
                    continue;
                block.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ConflictBlockViewModel.Choice))
                        Changed();
                };
                Blocks.Add(block);
            }
            Message = "";
            IsActive = true;
            Changed();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or GitException)
        {
            if (version == _loadVersion)
                IsActive = false;
        }
    }

    /// <summary>Writes the decided file and stages it; the caller refreshes Changes.</summary>
    public async Task<GitResult?> SaveAsync()
    {
        if (_actions is not { } actions || _text is not { } text || _path is not { } path || !CanSave)
            return null;
        var choices = Conflicts.OrderBy(b => b.Hunk!.Index).Select(b => b.Choice).ToList();
        var result = await Task.Run(() => actions.SaveResolutionAsync(path, text, choices));
        Message = result.Succeeded ? "" : $"{result.CommandLine} failed: {result.StandardError.Trim()}";
        return result;
    }

    /// <summary>The same choice for every conflict (the "all mine" / "all theirs" shortcut).</summary>
    public void ChooseAll(ConflictChoice choice)
    {
        foreach (var block in Conflicts)
            block.Choice = choice;
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(Path));
    }
}
