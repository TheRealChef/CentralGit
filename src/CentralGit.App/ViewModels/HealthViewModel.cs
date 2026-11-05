using System.Collections.ObjectModel;
using CentralGit.App.Theming;
using CentralGit.Domain.Health;
using CommunityToolkit.Mvvm.ComponentModel;
using Windows.UI;

namespace CentralGit.App.ViewModels;

/// <summary>A number on the Health page: "1,284 commits".</summary>
public sealed class HealthTileViewModel(string value, string label, string detail, Color accent)
{
    public string Value { get; } = value;
    public string Label { get; } = label;
    public string Detail { get; } = detail;
    public Color Accent { get; } = accent;
}

public sealed class HealthFindingViewModel(HealthFinding finding)
{
    public string Title { get; } = finding.Title;
    public string Detail { get; } = finding.Detail;
    public string Glyph { get; } =
        finding.Level switch
        {
            FindingLevel.Good => "", // check
            FindingLevel.Attention => "", // warning
            _ => "", // info
        };
    public Color Color { get; } =
        finding.Level switch
        {
            FindingLevel.Good => Palette.Green,
            FindingLevel.Attention => Palette.Orange,
            _ => Palette.Info,
        };
}

/// <summary>A branch in the cleanup list, with why it's there and whether it's selected.</summary>
public sealed partial class CleanupRowViewModel : ObservableObject
{
    public CleanupRowViewModel(BranchAssessment assessment, DateTimeOffset now)
    {
        Assessment = assessment;
        var b = assessment.Branch;
        Name = b.Name;
        IsRemote = b.IsRemote;
        if (b.IsRemote && b.Name.IndexOf('/') is var slash and > 0)
        {
            Remote = b.Name[..slash];
            ShortName = b.Name[(slash + 1)..];
        }
        else
            ShortName = b.Name;

        (Reason, ReasonColor, ReasonBackground) = assessment.Verdict switch
        {
            BranchVerdict.Merged => (
                $"merged into {assessment.MergedInto}",
                Palette.GreenText,
                Palette.WithAlpha(Palette.Green, 0.14)
            ),
            BranchVerdict.Gone => ("deleted on remote", Palette.Info, Palette.WithAlpha(Palette.Info, 0.14)),
            _ => ("not merged", Palette.OrangeText, Palette.WithAlpha(Palette.Orange, 0.14)),
        };
        AgeText = assessment.Age switch
        {
            null => "no commits",
            { TotalDays: < 1 } => "last commit today",
            var age => $"last commit {RepositoryHealth.DescribeAge(age.Value)} ago",
        };
        Author = b.LastAuthor is { } a ? $"by {a}" : "";
        Tooltip = assessment.Verdict switch
        {
            BranchVerdict.Merged =>
                $"Every commit on {Name} is already in {assessment.MergedInto}. Deleting it loses nothing.",
            BranchVerdict.Gone =>
                $"{Name} tracked {b.Upstream}, which no longer exists on the server — usually because its pull request was merged (squash or rebase) and the branch removed.\nIts commits aren't in a main branch as-is; delete if the PR was merged.",
            _ =>
                $"{Name} has commits that aren't in any main branch and hasn't changed in {RepositoryHealth.DescribeAge(assessment.Age ?? TimeSpan.Zero)}.\nAfter deleting it, the commits can only be found through Recovery (reflog).",
        };
        // Pre-select only what can't lose work, and not if it was touched in the last week
        // (a branch just created from main also counts as "merged").
        _isSelected = assessment.IsSafeToDelete && assessment.Age is { TotalDays: >= 7 };
    }

    public BranchAssessment Assessment { get; }
    public string Name { get; }
    public string ShortName { get; }
    public string? Remote { get; }
    public bool IsRemote { get; }
    public string Reason { get; }
    public Color ReasonColor { get; }
    public Color ReasonBackground { get; }
    public string AgeText { get; }
    public string Author { get; }
    public string Tooltip { get; }

    public string SelectLabel => $"Select {Name}";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed partial class HealthViewModel : ObservableObject
{
    private HealthReport? _report;

    public ObservableCollection<HealthTileViewModel> Tiles { get; } = [];
    public ObservableCollection<HealthFindingViewModel> Findings { get; } = [];
    public ObservableCollection<CleanupRowViewModel> Cleanup { get; } = [];

    private readonly List<CleanupRowViewModel> _local = [];
    private readonly List<CleanupRowViewModel> _remote = [];

    private bool _isLoading;
    private string _loadedText = "";
    private string _errorText = "";
    private bool _showRemote;

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
                OnPropertyChanged(nameof(HasNoCleanup));
        }
    }
    public string LoadedText
    {
        get => _loadedText;
        set => SetProperty(ref _loadedText, value);
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

    /// <summary>The cleanup list shows remote branches instead of local ones.</summary>
    public bool ShowRemote
    {
        get => _showRemote;
        set
        {
            if (!SetProperty(ref _showRemote, value))
                return;
            OnPropertyChanged(nameof(ShowLocal));
            Fill();
        }
    }

    public bool ShowLocal => !ShowRemote;
    public bool HasError => ErrorText.Length > 0;

    public string LocalTabText => $"Local · {_local.Count}";
    public string RemoteTabText => $"On remote · {_remote.Count}";
    public bool HasCleanup => Cleanup.Count > 0;
    public bool HasNoCleanup => !IsLoading && Cleanup.Count == 0;
    public string EmptyCleanupText =>
        ShowRemote
            ? "No remote branches to clean up. Fetch first if it's been a while — deleted branches are pruned on fetch."
            : "No local branches to clean up. Nice and tidy.";

    public string CleanupSubtitle =>
        _report is null
            ? ""
            : $"Merged into {(_report.IntegrationBranches.Count == 0 ? "a main branch" : string.Join(", ", _report.IntegrationBranches.Take(3)))}, deleted on the remote, or untouched for {RepositoryHealth.DescribeAge(RepositoryHealth.AbandonedAfter)}. "
                + $"Main and release branches are never listed.{(_agingCount > 0 ? $" {_agingCount} more {(_agingCount == 1 ? "is" : "are")} quiet for a few weeks." : "")}";

    private int _agingCount;

    public int SelectedCount => Cleanup.Count(r => r.IsSelected);
    public bool HasSelection => SelectedCount > 0;
    public string DeleteButtonText =>
        SelectedCount switch
        {
            0 => "Delete selected",
            1 => "Delete 1 branch",
            var n => $"Delete {n} branches",
        } + (ShowRemote ? " on remote" : "");

    public string IdentityText =>
        _report is null ? "" : $"{_report.UserName ?? "name not set"} · {_report.UserEmail ?? "email not set"}";
    public string GitVersionText => _report?.GitVersion ?? "";
    public string TrunkText => _report?.Trunk ?? "none found";
    public string RemotesText =>
        _report is null ? ""
        : _report.Remotes.Count == 0 ? "none"
        : string.Join("\n", _report.Remotes.Select(r => $"{r.Name}  {r.Url}"));
    public string WorktreesText => _report is null ? "" : _report.Stats.Worktrees.ToString();

    public void Clear()
    {
        _report = null;
        _local.Clear();
        _remote.Clear();
        Tiles.Clear();
        Findings.Clear();
        Fill();
    }

    public void Apply(HealthReport report, DateTimeOffset now)
    {
        _report = report;
        ErrorText = "";
        var s = report.Stats;

        Tiles.Clear();
        Tiles.Add(
            new(
                $"{s.Commits:N0}",
                "commits on this branch",
                s.LastCommit is { } lc ? $"latest {Ago(now - lc)}" : "no commits yet",
                Palette.Violet
            )
        );
        Tiles.Add(
            new(
                $"{s.CommitsLast30Days:N0}",
                "commits in 30 days",
                $"{s.ContributorsLast90Days} {(s.ContributorsLast90Days == 1 ? "person" : "people")} active in 90 days",
                Palette.Teal
            )
        );
        var localCount = report.Local.Count;
        var candidates = report.Local.Count(b => b.IsCleanupCandidate) + report.Remote.Count(b => b.IsCleanupCandidate);
        Tiles.Add(
            new(
                $"{localCount} / {report.Remote.Count}",
                "local / remote branches",
                candidates == 0 ? "none stale" : $"{candidates} could be cleaned up",
                candidates == 0 ? Palette.Green : Palette.Orange
            )
        );
        Tiles.Add(
            new(
                RepositoryHealth.FormatSize(s.SizeKiB),
                "on disk",
                $"{s.Stashes} stash{(s.Stashes == 1 ? "" : "es")} · {s.LooseObjects:N0} loose objects",
                Palette.Slate
            )
        );
        Tiles.Add(
            new(
                s.LastFetch is { } f ? Ago(now - f) : "never",
                "last fetch",
                report.Remotes.Count == 0 ? "no remote" : string.Join(", ", report.Remotes.Select(r => r.Name)),
                s.LastFetch is { } f2 && now - f2 < TimeSpan.FromDays(7) ? Palette.Teal : Palette.Orange
            )
        );

        Findings.Clear();
        foreach (
            var finding in report
                .Findings.OrderByDescending(f => f.Level == FindingLevel.Attention)
                .ThenBy(f => f.Level == FindingLevel.Good)
        )
            Findings.Add(new HealthFindingViewModel(finding));

        _local.Clear();
        _remote.Clear();
        _local.AddRange(Order(report.Local).Select(a => new CleanupRowViewModel(a, now)));
        _remote.AddRange(Order(report.Remote).Select(a => new CleanupRowViewModel(a, now)));
        _agingCount =
            report.Local.Count(b => b.Verdict == BranchVerdict.Aging)
            + report.Remote.Count(b => b.Verdict == BranchVerdict.Aging);
        Fill();

        LoadedText = $"Checked {now.ToLocalTime():HH:mm}";
        foreach (
            var name in new[]
            {
                nameof(IdentityText),
                nameof(GitVersionText),
                nameof(TrunkText),
                nameof(RemotesText),
                nameof(WorktreesText),
                nameof(CleanupSubtitle),
            }
        )
            OnPropertyChanged(name);

        static IEnumerable<BranchAssessment> Order(IEnumerable<BranchAssessment> list) =>
            list.Where(a => a.IsCleanupCandidate)
                .OrderBy(a =>
                    a.Verdict switch
                    {
                        BranchVerdict.Merged => 0,
                        BranchVerdict.Gone => 1,
                        _ => 2,
                    }
                )
                .ThenByDescending(a => a.Age ?? TimeSpan.Zero);
    }

    private void Fill()
    {
        foreach (var row in Cleanup)
            row.PropertyChanged -= OnRowChanged;
        Cleanup.Clear();
        foreach (var row in ShowRemote ? _remote : _local)
        {
            row.PropertyChanged += OnRowChanged;
            Cleanup.Add(row);
        }
        OnPropertyChanged(nameof(LocalTabText));
        OnPropertyChanged(nameof(RemoteTabText));
        OnPropertyChanged(nameof(HasCleanup));
        OnPropertyChanged(nameof(HasNoCleanup));
        OnPropertyChanged(nameof(EmptyCleanupText));
        NotifySelection();
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => NotifySelection();

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(DeleteButtonText));
    }

    /// <summary>Selects everything that can't lose work (merged), or clears the selection.</summary>
    public void SelectMerged()
    {
        foreach (var row in Cleanup)
            row.IsSelected = row.Assessment.IsSafeToDelete;
    }

    public void SelectAll(bool selected)
    {
        foreach (var row in Cleanup)
            row.IsSelected = selected;
    }

    public IReadOnlyList<CleanupRowViewModel> Selected => Cleanup.Where(r => r.IsSelected).ToList();

    private static string Ago(TimeSpan span) =>
        span.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)span.TotalMinutes} min ago",
            < 60 * 48 => $"{(int)span.TotalHours} h ago",
            _ => $"{RepositoryHealth.DescribeAge(span)} ago",
        };
}
