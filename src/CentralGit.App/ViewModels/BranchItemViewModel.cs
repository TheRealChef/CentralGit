using CentralGit.App.Theming;
using CentralGit.Application.Git;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Health;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace CentralGit.App.ViewModels;

/// <summary>
/// A row of the sidebar's branch tree: either a folder ("feature/") or a branch. Branch names
/// with slashes are grouped into folders, the way most teams name them.
/// </summary>
public sealed class BranchItemViewModel
{
    private const double IndentStep = 14;

    /// <summary>A branch row.</summary>
    public BranchItemViewModel(LocalBranch branch, int depth, BranchAssessment? assessment = null)
    {
        Branch = branch;
        Depth = depth;
        FullName = branch.Name;
        Name = branch.Name.Contains('/') ? branch.Name[(branch.Name.LastIndexOf('/') + 1)..] : branch.Name;
        IsHead = branch.IsHead;
        var verdict = IsHead ? BranchVerdict.Active : assessment?.Verdict ?? BranchVerdict.Active;
        IsStale = verdict is not BranchVerdict.Active;

        Badge =
            branch.Behind > 0 && branch.Ahead > 0 ? $"↑{branch.Ahead} ↓{branch.Behind}"
            : branch.Behind > 0 ? $"↓{branch.Behind}"
            : branch.Ahead > 0 ? $"↑{branch.Ahead}"
            : verdict == BranchVerdict.Merged ? "merged"
            : branch.UpstreamGone ? "gone"
            : branch.Upstream is null ? "local"
            : "";
        BadgeColor =
            branch.Behind > 0 ? Palette.Chrome.OrangeText
            : branch.Ahead > 0 ? Palette.Chrome.VioletText
            : verdict == BranchVerdict.Merged ? Palette.Chrome.GreenText
            : Palette.Chrome.TextMuted;

        // Age only when it's worth noticing: quiet for weeks (muted) or abandoned (orange).
        if (
            verdict is BranchVerdict.Aging or BranchVerdict.Abandoned or BranchVerdict.Merged or BranchVerdict.Gone
            && assessment?.Age is { } age
            && age >= RepositoryHealth.AgingAfter
        )
        {
            AgeText = RepositoryHealth.ShortAge(age);
            AgeColor = verdict == BranchVerdict.Abandoned ? Palette.Chrome.OrangeText : Palette.Chrome.TextMuted;
        }

        DotColor =
            IsHead ? Palette.Chrome.Violet
            : branch.Name is "main" or "master" or "develop" ? Palette.Chrome.Teal
            : IsStale ? Palette.WithAlpha(Palette.Chrome.Slate, 0.45)
            : Palette.Chrome.Slate;
        NameColor =
            IsHead ? Palette.Chrome.TextPrimary
            : IsStale ? Palette.Chrome.TextMuted
            : Palette.Chrome.TextBranch;

        var staleness = verdict switch
        {
            BranchVerdict.Merged =>
                $"\nMerged into {assessment!.MergedInto} — safe to delete (Health → Branch cleanup).",
            BranchVerdict.Gone =>
                "\nDeleted on the remote — probably merged through a pull request. Consider deleting it.",
            BranchVerdict.Abandoned =>
                $"\nNo commits for {RepositoryHealth.DescribeAge(assessment!.Age!.Value)} and not merged. Consider deleting it.",
            BranchVerdict.Aging => $"\nQuiet for {RepositoryHealth.DescribeAge(assessment!.Age!.Value)}.",
            _ => "",
        };
        var last =
            assessment?.Age is { } a2 && assessment.Branch.LastAuthor is { } who
                ? $"\nLast commit {(a2.TotalDays < 1 ? "today" : RepositoryHealth.DescribeAge(a2) + " ago")} by {who}"
                : "";
        Tooltip =
            (branch.Upstream is { } u ? $"{branch.Name} → {u}" : $"{branch.Name} (not published)")
            + last
            + staleness
            + "\nClick to show it in History · double-click to switch · right-click for more";
    }

    /// <summary>Merged, gone, or quiet for weeks — drawn dimmed.</summary>
    public bool IsStale { get; }
    public string AgeText { get; } = "";
    public Color AgeColor { get; } = Palette.Chrome.TextMuted;
    public bool HasAge => AgeText.Length > 0;

    /// <summary>A folder row.</summary>
    public BranchItemViewModel(
        string folderPath,
        string folderName,
        int depth,
        int count,
        bool isExpanded,
        bool containsHead
    )
    {
        IsFolder = true;
        Depth = depth;
        FullName = folderPath;
        Name = folderName;
        Badge = count.ToString();
        BadgeColor = Palette.Chrome.TextMuted;
        IsExpanded = isExpanded;
        DotColor = Palette.Transparent;
        NameColor = containsHead ? Palette.Chrome.TextPrimary : Palette.Chrome.TextSecondary;
        Tooltip = $"{folderPath}/ — {count} branch{(count == 1 ? "" : "es")}";
    }

    public LocalBranch? Branch { get; }
    public bool IsFolder { get; }
    public bool IsBranch => !IsFolder;
    public int Depth { get; }
    public Thickness Indent => new(12 + Depth * IndentStep, 0, 10, 0);

    /// <summary>Branch: full name (feature/login). Folder: its path (feature).</summary>
    public string FullName { get; }
    public string Name { get; }
    public bool IsHead { get; }
    public bool IsNotHead => !IsHead;
    public bool IsExpanded { get; }
    public string Chevron => IsExpanded ? "" : ""; // down / right
    public string Badge { get; }
    public Color BadgeColor { get; }
    public Color DotColor { get; }
    public Color NameColor { get; }
    public Color RowBackground => IsHead ? Palette.WithAlpha(Palette.Chrome.Violet, 0.10) : Palette.Transparent;
    public string Tooltip { get; }

    /// <summary>The ListView item's accessible name.</summary>
    public override string ToString() => IsFolder ? FullName + "/" : FullName;

    /// <summary>
    /// Flattens branches into tree rows: folders first (alphabetical), then branches. Folders in
    /// <paramref name="collapsed"/> hide their contents; the current branch sorts to the top of its level.
    /// </summary>
    public static List<BranchItemViewModel> BuildTree(
        IReadOnlyList<LocalBranch> branches,
        IReadOnlySet<string> collapsed,
        IReadOnlyDictionary<string, BranchAssessment>? assessments = null
    )
    {
        var rows = new List<BranchItemViewModel>();
        Add(branches.Select(b => (Remainder: b.Name, Branch: b)).ToList(), "", 0);
        return rows;

        void Add(List<(string Remainder, LocalBranch Branch)> items, string prefix, int depth)
        {
            var folders = items
                .Where(i => i.Remainder.Contains('/'))
                .GroupBy(i => i.Remainder[..i.Remainder.IndexOf('/')], StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
            var leaves = items
                .Where(i => !i.Remainder.Contains('/'))
                .OrderByDescending(i => i.Branch.IsHead)
                .ThenBy(i => i.Remainder, StringComparer.OrdinalIgnoreCase);

            // Top level: the current branch and main-line branches before folders.
            if (depth == 0)
                foreach (var leaf in leaves)
                    rows.Add(
                        new BranchItemViewModel(leaf.Branch, depth, assessments?.GetValueOrDefault(leaf.Branch.Name))
                    );

            foreach (var folder in folders)
            {
                var path = prefix + folder.Key;
                var expanded = !collapsed.Contains(path);
                var children = folder
                    .Select(i => (Remainder: i.Remainder[(folder.Key.Length + 1)..], i.Branch))
                    .ToList();
                rows.Add(
                    new BranchItemViewModel(
                        path,
                        folder.Key,
                        depth,
                        children.Count,
                        expanded,
                        children.Any(c => c.Branch.IsHead)
                    )
                );
                if (expanded)
                    Add(children, path + "/", depth + 1);
            }

            if (depth > 0)
                foreach (var leaf in leaves)
                    rows.Add(
                        new BranchItemViewModel(leaf.Branch, depth, assessments?.GetValueOrDefault(leaf.Branch.Name))
                    );
        }
    }
}
