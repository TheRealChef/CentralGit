using System.Collections.ObjectModel;
using CentralGit.App.Theming;
using CentralGit.Application.Features.Recovery;
using CentralGit.Domain.Recovery;
using CommunityToolkit.Mvvm.ComponentModel;
using Windows.UI;

namespace CentralGit.App.ViewModels;

/// <summary>One reflog row: when, what moved HEAD, and the commit it pointed at.</summary>
public sealed class RecoveryRowViewModel
{
    public RecoveryRowViewModel(RecoveryPoint point, DateTimeOffset now)
    {
        Point = point;
        var entry = point.Entry;
        var known = entry.When != DateTimeOffset.MinValue;
        WhenText = known ? RelativeTime.Format(entry.When, now) : "";
        WhenTooltip = known ? entry.When.ToLocalTime().ToString("f") : "";
        Description = entry.Description;
        Subject = entry.Subject;
        // A commit entry already says its message; repeat it only when the line says something else.
        Detail = (entry.Subject == Description ? "" : "  " + entry.Subject) + "  ·  " + entry.Author;
        ShortHash = entry.ShortHash;
        Author = entry.Author;
        IsLost = !point.IsOnBranch;
        (Glyph, GlyphColor) = entry.Action switch
        {
            ReflogAction.Commit or ReflogAction.Amend => ("", Palette.Violet),
            ReflogAction.Merge or ReflogAction.Pull => ("", Palette.Teal),
            ReflogAction.Rebase or ReflogAction.CherryPick or ReflogAction.Revert => ("", Palette.Teal),
            ReflogAction.Reset => ("", Palette.Orange),
            _ => ("", Palette.TextMuted),
        };
        BadgeColor = Palette.OrangeText;
        BadgeBackground = Palette.WithAlpha(Palette.Orange, 0.14);
    }

    public RecoveryPoint Point { get; }
    public string WhenText { get; }
    public string WhenTooltip { get; }
    public string Description { get; }
    public string Subject { get; }

    /// <summary>After the hash: the commit message (unless the description already is it) and the author.</summary>
    public string Detail { get; }
    public string ShortHash { get; }
    public string Author { get; }
    public string Glyph { get; }
    public Color GlyphColor { get; }

    /// <summary>No branch, tag or stash holds this commit: only the reflog remembers it.</summary>
    public bool IsLost { get; }
    public bool IsOnBranch => !IsLost;
    public Color BadgeColor { get; }
    public Color BadgeBackground { get; }
}

/// <summary>The Recovery page: the HEAD reflog, lost commits first.</summary>
public sealed partial class RecoveryViewModel : ObservableObject
{
    private RecoveryList? _list;
    private DateTimeOffset _at;
    private bool _showLostOnly = true;
    private bool _isLoading;
    private string _errorText = "";

    public ObservableCollection<RecoveryRowViewModel> Rows { get; } = [];

    public bool ShowLostOnly
    {
        get => _showLostOnly;
        set
        {
            if (SetProperty(ref _showLostOnly, value))
                Fill();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string ErrorText
    {
        get => _errorText;
        set
        {
            if (SetProperty(ref _errorText, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => _errorText.Length > 0;

    public int LostCount => _list?.Lost.Count ?? 0;
    public string LostTabText => LostCount > 0 ? $"Not on any branch · {LostCount}" : "Not on any branch";
    public string AllTabText => _list is { } l ? $"Everything · {l.All.Count}" : "Everything";

    /// <summary>Sidebar count next to Recovery.</summary>
    public string SidebarText => LostCount > 0 ? LostCount.ToString() : "";

    public bool IsEmpty => _list is not null && Rows.Count == 0;

    public string EmptyText =>
        _showLostOnly
            ? "Nothing is lost: every commit HEAD has pointed at is still on a branch, tag or stash."
            : "The reflog is empty, so this repository has no history to recover yet.";

    public string LoadedText => _list is null ? "" : $"Checked {_at.ToLocalTime():HH:mm}";

    public void Apply(RecoveryList list, DateTimeOffset at)
    {
        var first = _list is null;
        _list = list;
        _at = at;
        ErrorText = "";
        // Open on lost commits when there are any; otherwise the whole history is the useful view.
        if (first || list.Lost.Count == 0)
            _showLostOnly = list.Lost.Count > 0;
        Fill();
    }

    /// <summary>A theme switch or another repository: rebuild (or drop) the rows.</summary>
    public void Recolor() => Fill();

    public void Clear()
    {
        _list = null;
        ErrorText = "";
        Fill();
    }

    private void Fill()
    {
        Rows.Clear();
        if (_list is { } list)
            foreach (var point in _showLostOnly ? list.Lost : list.All)
                Rows.Add(new RecoveryRowViewModel(point, _at));
        OnPropertyChanged(string.Empty);
    }
}
