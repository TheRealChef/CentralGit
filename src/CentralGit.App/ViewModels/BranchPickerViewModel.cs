using CentralGit.App.Theming;
using CentralGit.Core.Git;
using CentralGit.Core.Operations;
using CentralGit.Core.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;
using Windows.UI;

namespace CentralGit.App.ViewModels;

public enum BranchPickKind { Create, Local, Remote }

public sealed class BranchPickItemViewModel
{
    public BranchPickItemViewModel(BranchPickKind kind, string name, string detail, string badge, Color badgeColor, bool isCurrent)
    {
        Kind = kind;
        Name = name;
        Detail = detail;
        Badge = badge;
        BadgeColor = badgeColor;
        IsCurrent = isCurrent;
        (Glyph, GlyphColor) = kind switch
        {
            BranchPickKind.Create => ("", Palette.Violet),                   // add
            BranchPickKind.Remote => ("", Palette.Info),                     // cloud
            _ => ("", isCurrent ? Palette.Violet : Palette.TextSecondary), // branch
        };
        NameColor = isCurrent ? Palette.VioletText : Palette.TextPrimary;
    }

    public BranchPickKind Kind { get; }
    public string Name { get; }
    public string Detail { get; }
    public string Badge { get; }
    public Color BadgeColor { get; }
    public bool IsCurrent { get; }
    public string Glyph { get; }
    public Color GlyphColor { get; }
    public Color NameColor { get; }
    public RemoteBranch? Remote { get; init; }
}

/// <summary>
/// Ctrl+B: switch branch. Local branches by recency, then remote branches nobody has checked
/// out yet; typing filters fuzzily and offers "Create branch …" when nothing matches exactly.
/// </summary>
public sealed partial class BranchPickerViewModel(ShellViewModel shell) : ObservableObject
{
    private const int MaxRemote = 300;
    private string _query = "";
    private IReadOnlyList<LocalBranch> _local = [];
    private IReadOnlyList<RemoteBranch> _remote = [];
    private IReadOnlyList<BranchPickItemViewModel> _results = [];
    private BranchPickItemViewModel? _selected;
    private string _hint = "";

    public string Query
    {
        get => _query;
        set { if (SetProperty(ref _query, value)) Refresh(); }
    }

    public IReadOnlyList<BranchPickItemViewModel> Results { get => _results; private set => SetProperty(ref _results, value); }
    public BranchPickItemViewModel? Selected { get => _selected; set => SetProperty(ref _selected, value); }
    public string Hint { get => _hint; private set => SetProperty(ref _hint, value); }

    public async Task OpenAsync()
    {
        _query = "";
        OnPropertyChanged(nameof(Query));
        (_local, _remote) = await shell.GetBranchesAsync();
        Refresh();
    }

    public void Move(int delta)
    {
        if (Results.Count == 0)
            return;
        var index = -1;
        for (var i = 0; i < Results.Count; i++)
            if (ReferenceEquals(Results[i], Selected))
                index = i;
        Selected = Results[Math.Clamp(index + delta, 0, Results.Count - 1)];
    }

    /// <summary>Runs the selected item. Returns false when nothing happened (e.g. invalid name), so the picker stays open.</summary>
    public async Task<bool> ActivateAsync(BranchPickItemViewModel? item)
    {
        item ??= Selected;
        switch (item?.Kind)
        {
            case BranchPickKind.Create:
                if (await shell.ValidateNewBranchNameAsync(item.Name) is { } problem)
                {
                    Hint = problem;
                    return false;
                }
                await shell.CreateBranchAsync(item.Name);
                return true;
            case BranchPickKind.Local:
                await shell.SwitchToAsync(item.Name);
                return true;
            case BranchPickKind.Remote when item.Remote is { } remote:
                await shell.CheckoutRemoteAsync(remote);
                return true;
            default:
                return false;
        }
    }

    private void Refresh()
    {
        var now = DateTimeOffset.Now;
        var query = _query.Trim();
        var current = shell.CurrentBranch;
        var tracked = _local.Select(b => b.Upstream).Where(u => u is not null).ToHashSet(StringComparer.Ordinal);
        var localNames = _local.Select(b => b.Name).ToHashSet(StringComparer.Ordinal);

        var local = _local.Select(b => (
            item: new BranchPickItemViewModel(BranchPickKind.Local, b.Name,
                Detail(b.LastCommit, b.Upstream is null ? "not published" : b.UpstreamGone ? $"{b.Upstream} is gone" : b.Upstream, now),
                b.IsHead ? "current" : b.Behind > 0 && b.Ahead > 0 ? $"↑{b.Ahead} ↓{b.Behind}" : b.Behind > 0 ? $"↓{b.Behind}" : b.Ahead > 0 ? $"↑{b.Ahead}" : "",
                b.IsHead ? Palette.Violet : b.Behind > 0 ? Palette.OrangeText : Palette.VioletText,
                b.IsHead),
            b.LastCommit,
            b.IsHead));

        // Remote branches already checked out locally are reached through their local branch.
        var remote = _remote.Where(r => !tracked.Contains(r.Name) && !localNames.Contains(r.ShortName))
            .Select(r => (item: new BranchPickItemViewModel(BranchPickKind.Remote, r.Name, Detail(r.LastCommit, "remote — check out to track it", now),
                "", Palette.TextMuted, false) { Remote = r }, r.LastCommit));

        List<BranchPickItemViewModel> results;
        if (query.Length == 0)
        {
            results =
            [
                .. local.OrderByDescending(x => x.IsHead).ThenByDescending(x => x.LastCommit).Select(x => x.item),
                .. remote.OrderByDescending(x => x.LastCommit).Take(MaxRemote).Select(x => x.item),
            ];
        }
        else
        {
            results =
            [
                .. local.Select(x => (x.item, Score: FuzzyMatcher.Score(query, x.item.Name)))
                    .Where(x => x.Score is not null).OrderByDescending(x => x.Score).Select(x => x.item),
                .. remote.Select(x => (x.item, Score: FuzzyMatcher.Score(query, x.item.Name)))
                    .Where(x => x.Score is not null).OrderByDescending(x => x.Score).Take(MaxRemote).Select(x => x.item),
            ];

            var name = BranchOperations.Suggest(query);
            if (name.Length > 0 && !localNames.Contains(name))
            {
                var create = new BranchPickItemViewModel(BranchPickKind.Create, name, $"Create branch from {current ?? "HEAD"} and switch to it", "new", Palette.Violet, false);
                // Exact matches stay on top; otherwise creating is the likely intent.
                if (results.Count > 0 && results[0].Name.Equals(query, StringComparison.OrdinalIgnoreCase))
                    results.Insert(1, create);
                else
                    results.Insert(0, create);
            }
        }

        Results = results;
        // Enter on an empty query should go to another branch, not "switch" to the current one.
        Selected = results.FirstOrDefault(r => !r.IsCurrent) ?? results.FirstOrDefault();
        Hint = results.Count == 0 ? "No branches match" : "↑↓ to choose · Enter to switch · type a new name to create";
    }

    private static string Detail(DateTimeOffset? when, string? extra, DateTimeOffset now) =>
        string.Join("  ·  ", new[] { when is { } w ? RelativeTime.Format(w, now) : null, extra }.Where(s => !string.IsNullOrEmpty(s)));
}
