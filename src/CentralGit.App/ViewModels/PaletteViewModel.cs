using CentralGit.Domain.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CentralGit.App.ViewModels;

/// <summary>One thing the command palette can do: a command, a branch, a tag or a commit.</summary>
public sealed class PaletteItemViewModel(
    string group,
    string title,
    string detail,
    string glyph,
    Func<Task> run,
    string shortcut = "",
    string? searchText = null
)
{
    public string Group { get; } = group;
    public string Title { get; } = title;
    public string Detail { get; } = detail;
    public bool HasDetail => Detail.Length > 0;
    public string Glyph { get; } = glyph;
    public string Shortcut { get; } = shortcut;
    public bool HasShortcut => Shortcut.Length > 0;
    public Func<Task> Run { get; } = run;

    /// <summary>What the query is matched against (the title unless a hash or path should match too).</summary>
    public string SearchText { get; } = searchText ?? title;

    /// <summary>Commands are listed when nothing is typed; branches, tags and commits only once you type.</summary>
    public bool IsCommand => Group == PaletteGroups.Command;
}

public static class PaletteGroups
{
    public const string Command = "Command";
    public const string Branch = "Branch";
    public const string Tag = "Tag";
    public const string Commit = "Commit";
}

/// <summary>The command palette (Ctrl+K): fuzzy search over everything the window can do right now.</summary>
public sealed partial class PaletteViewModel : ObservableObject
{
    private const int MaxResults = 60;

    private IReadOnlyList<PaletteItemViewModel> _items = [];
    private string _query = "";
    private IReadOnlyList<PaletteItemViewModel> _results = [];
    private PaletteItemViewModel? _selected;

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value))
                Filter();
        }
    }

    public IReadOnlyList<PaletteItemViewModel> Results
    {
        get => _results;
        private set
        {
            if (SetProperty(ref _results, value))
                OnPropertyChanged(nameof(Hint));
        }
    }

    public PaletteItemViewModel? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public string Hint =>
        _results.Count == 0 ? "Nothing matches. Try a branch, tag, commit hash or command name."
        : _query.Trim().Length == 0 ? "Type to search branches, tags and loaded commits too · Enter runs · Esc closes"
        : "↑↓ to choose · Enter runs · Esc closes";

    /// <summary>Starts over with what's available right now.</summary>
    public void Reset(IReadOnlyList<PaletteItemViewModel> items)
    {
        _items = items;
        _query = "";
        OnPropertyChanged(nameof(Query));
        Filter();
    }

    public void Move(int delta)
    {
        if (_results.Count == 0)
            return;
        var index = _selected is null ? -1 : IndexOf(_selected);
        Selected = _results[Math.Clamp(index + delta, 0, _results.Count - 1)];
    }

    private int IndexOf(PaletteItemViewModel item)
    {
        for (var i = 0; i < _results.Count; i++)
            if (ReferenceEquals(_results[i], item))
                return i;
        return -1;
    }

    private void Filter()
    {
        var query = _query.Trim();
        if (query.Length == 0)
        {
            Results = _items.Where(i => i.IsCommand).ToList();
        }
        else
        {
            // Commands win ties: typing "push" should run Push before showing a branch called "push-fix".
            Results = _items
                .Select((item, order) => (item, order, score: FuzzyMatcher.Score(query, item.SearchText)))
                .Where(x => x.score is not null)
                .OrderByDescending(x => x.score!.Value + (x.item.IsCommand ? 3 : 0))
                .ThenBy(x => x.order)
                .Take(MaxResults)
                .Select(x => x.item)
                .ToList();
        }
        Selected = _results.FirstOrDefault();
    }
}
