using CentralGit.App.Services;
using CentralGit.App.Theming;
using CentralGit.Core.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;
using Windows.UI;

namespace CentralGit.App.ViewModels;

public sealed partial class SwitcherItemViewModel(string path, string name, string location, string badge, Color badgeColor, bool isCurrent) : ObservableObject
{
    private string _changesText = "";
    private string _syncText = "";

    public string Path { get; } = path;
    public string Name { get; } = name;
    public string Location { get; } = location;
    public string Badge { get; } = badge;
    public Color BadgeColor { get; } = badgeColor;
    public bool IsCurrent { get; } = isCurrent;
    public Color NameColor { get; } = isCurrent ? Palette.VioletText : Palette.TextPrimary;

    /// <summary>"3 changed" (orange) when the repository has uncommitted work.</summary>
    public string ChangesText { get => _changesText; private set { if (SetProperty(ref _changesText, value)) OnPropertyChanged(nameof(HasChanges)); } }
    public bool HasChanges => _changesText.Length > 0;

    /// <summary>"↑2 ↓1" against its upstream.</summary>
    public string SyncText { get => _syncText; private set => SetProperty(ref _syncText, value); }

    public void Show(RepositoryGlance? glance)
    {
        if (glance is null)
            return;
        ChangesText = glance.ChangedFiles > 0 ? $"{glance.ChangedFiles} changed" : "";
        SyncText = string.Join(" ", new[] { glance.Ahead > 0 ? $"↑{glance.Ahead}" : "", glance.Behind > 0 ? $"↓{glance.Behind}" : "" }.Where(s => s.Length > 0));
    }
}

/// <summary>
/// Ctrl+P: jump to any repository. Empty query lists recent repositories first, then the rest
/// alphabetically; typing ranks everything by fuzzy match on name, then on path.
/// </summary>
public sealed partial class SwitcherViewModel(ShellViewModel shell) : ObservableObject
{
    private string _query = "";
    private IReadOnlyList<SwitcherItemViewModel> _results = [];
    private SwitcherItemViewModel? _selected;

    public string Query
    {
        get => _query;
        set { if (SetProperty(ref _query, value)) Refresh(); }
    }

    public IReadOnlyList<SwitcherItemViewModel> Results { get => _results; private set => SetProperty(ref _results, value); }

    public SwitcherItemViewModel? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    public string Hint => Results.Count == 0
        ? (shell.Discovered.Count == 0 ? "No repositories found — add a scan folder in Settings (Ctrl+,)" : "No repository matches")
        : "↑↓ to choose · Enter to open · Esc to close";

    public void Reset()
    {
        _query = "";
        OnPropertyChanged(nameof(Query));
        Refresh();
    }

    public void Move(int delta)
    {
        if (Results.Count == 0)
            return;
        var index = Selected is null ? -1 : IndexOf(Selected);
        Selected = Results[Math.Clamp(index + delta, 0, Results.Count - 1)];
    }

    private void Refresh()
    {
        var settings = SettingsStore.Current;
        var recent = settings.Recent
            .Select((r, i) => (r.Path, Rank: i))
            .ToDictionary(r => r.Path, r => r.Rank, StringComparer.OrdinalIgnoreCase);
        var current = shell.RepositoryPath;

        var candidates = shell.Discovered
            .Where(r => !settings.IsHidden(r.Path))
            .Select(r => (r.Path, r.Name, r.IsWorktree))
            // Recently opened repositories outside the scan folders still belong here.
            .Concat(settings.Recent
                .Where(r => !shell.Discovered.Any(d => d.Path.Equals(r.Path, StringComparison.OrdinalIgnoreCase)) && RepositoryLocator.IsRepository(r.Path))
                .Select(r => (r.Path, Name: System.IO.Path.GetFileName(r.Path.TrimEnd('\\')), IsWorktree: false)))
            .ToList();

        IEnumerable<(string Path, string Name, bool IsWorktree)> ordered;
        if (_query.Trim().Length == 0)
        {
            ordered = candidates
                .OrderBy(c => recent.TryGetValue(c.Path, out var rank) ? rank : int.MaxValue)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            ordered = candidates
                .Select(c => (c, Score: FuzzyMatcher.Score(_query, c.Name) ?? (FuzzyMatcher.Score(_query, c.Path) - 10_000)))
                .Where(x => x.Score is not null)
                .OrderByDescending(x => x.Score + (recent.ContainsKey(x.c.Path) ? 50 : 0))
                .Select(x => x.c);
        }

        Results = ordered.Take(200).Select(c =>
        {
            var isCurrent = string.Equals(c.Path, current, StringComparison.OrdinalIgnoreCase);
            var (badge, color) =
                isCurrent ? ("current", Palette.Violet)
                : c.IsWorktree ? ("worktree", Palette.Info)
                : recent.ContainsKey(c.Path) ? ("recent", Palette.TextMuted)
                : ("", Palette.TextMuted);
            return new SwitcherItemViewModel(c.Path, c.Name, System.IO.Path.GetDirectoryName(c.Path) ?? "", badge, color, isCurrent);
        }).ToList();

        // Enter on an empty query should switch to the previous repository, not reopen this one.
        Selected = Results.FirstOrDefault(r => !r.IsCurrent) ?? Results.FirstOrDefault();
        OnPropertyChanged(nameof(Hint));
        _ = LoadGlancesAsync(Results);
    }

    private CancellationTokenSource? _glanceCts;

    /// <summary>Fills in changed-file counts for the top results; typing again cancels the rest.</summary>
    private async Task LoadGlancesAsync(IReadOnlyList<SwitcherItemViewModel> items)
    {
        _glanceCts?.Cancel();
        var cts = _glanceCts = new CancellationTokenSource();
        var top = items.Take(40).ToList();
        foreach (var item in top)
            item.Show(RepositoryStatusCache.Cached(item.Path)); // instant from earlier opens

        try
        {
            await Task.WhenAll(top.Select(async item =>
            {
                var glance = await Task.Run(() => RepositoryStatusCache.GetAsync(item.Path, cts.Token), cts.Token);
                if (!cts.IsCancellationRequested)
                    item.Show(glance);
            }));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private int IndexOf(SwitcherItemViewModel item)
    {
        for (var i = 0; i < Results.Count; i++)
            if (ReferenceEquals(Results[i], item))
                return i;
        return -1;
    }
}
