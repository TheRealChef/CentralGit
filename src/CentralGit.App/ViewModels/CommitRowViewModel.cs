using CentralGit.App.Graph;
using CentralGit.App.Theming;
using CentralGit.Core.Git;
using Windows.UI;

namespace CentralGit.App.ViewModels;

public sealed record RefChipViewModel(string Name, Color Background, Color Border, Color Foreground, bool IsBold);

/// <summary>One history row. Immutable and built off the UI thread; only plain Colors, no brushes.</summary>
public sealed class CommitRowViewModel(
    CommitInfo commit,
    int lane,
    IReadOnlyList<GraphStroke> strokes,
    Color dotColor,
    bool isHead,
    IReadOnlyList<RefChipViewModel> refs,
    CommitSync sync = CommitSync.None)
{
    public CommitInfo Commit { get; } = commit;
    public int Lane { get; } = lane;
    public IReadOnlyList<GraphStroke> Strokes { get; } = strokes;
    public Color DotColor { get; } = dotColor;
    public bool IsHead { get; } = isHead;
    public IReadOnlyList<RefChipViewModel> Refs { get; } = refs;

    /// <summary>Incoming: fetched but not in your branch yet. Outgoing: yours, not on the remote yet.</summary>
    public CommitSync Sync { get; } = sync;
    public bool HasSync => Sync != CommitSync.None;
    public string SyncText => Sync switch { CommitSync.Incoming => "↓ incoming", CommitSync.Outgoing => "↑ not pushed", _ => "" };
    public Color SyncForeground => Sync == CommitSync.Incoming ? Palette.OrangeText : Palette.VioletText;
    public Color SyncBackground => Sync == CommitSync.Incoming ? Palette.WithAlpha(Palette.Orange, 0.16) : Palette.WithAlpha(Palette.Violet, 0.18);
    public Color RowTint => Sync switch { CommitSync.Incoming => Palette.WithAlpha(Palette.Orange, 0.06), CommitSync.Outgoing => Palette.WithAlpha(Palette.Violet, 0.07), _ => Palette.Transparent };
    public Color EdgeColor => Sync switch { CommitSync.Incoming => Palette.Orange, CommitSync.Outgoing => Palette.Violet, _ => Palette.Transparent };
    public string SyncTooltip => Sync switch
    {
        CommitSync.Incoming => "Fetched from the remote but not in your branch yet. Pull to bring it in.",
        CommitSync.Outgoing => "Only on this PC. Push to send it to the remote.",
        _ => "",
    };

    private CommitRowViewModel? _flat;

    /// <summary>The same row without graph lines, for filtered views where lanes would have gaps.</summary>
    public CommitRowViewModel Flat() => _flat ??= new CommitRowViewModel(Commit, 0, [], DotColor, IsHead, Refs, Sync);

    public bool IsMerge => Commit.IsMerge;
    public string Subject => Commit.Subject;
    public string Author => Commit.Author.Name;
    public string ShortHash => Commit.ShortHash;
    public string Date => RelativeTime.Format(Commit.Author.When, DateTimeOffset.Now);

    /// <summary>Merges are de-emphasized so real work stands out.</summary>
    public Color SubjectColor => IsMerge || Sync == CommitSync.Incoming ? Palette.TextSecondary : Palette.TextPrimary;

    public static IReadOnlyList<RefChipViewModel> BuildRefs(CommitInfo commit, string? headBranch, Color laneColor)
    {
        var chips = new List<RefChipViewModel>();
        foreach (var r in commit.Refs)
        {
            if (r is "HEAD" or "refs/stash")
                continue;

            if (r.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                var name = r["refs/heads/".Length..];
                var isHead = name == headBranch;
                chips.Add(isHead
                    ? new RefChipViewModel(name, Palette.WithAlpha(Palette.Violet, 0.26), Palette.WithAlpha(Palette.Violet, 0.6), Color.FromArgb(255, 0xE2, 0xD8, 0xF5), true)
                    : new RefChipViewModel(name, Palette.WithAlpha(laneColor, 0.14), Palette.WithAlpha(laneColor, 0.32), Color.FromArgb(255, 0xE0, 0xE2, 0xE6), false));
            }
            else if (r.StartsWith("refs/remotes/", StringComparison.Ordinal))
            {
                var name = r["refs/remotes/".Length..];
                if (name.EndsWith("/HEAD", StringComparison.Ordinal))
                    continue;
                chips.Add(new RefChipViewModel(name, Palette.Transparent, Palette.WithAlpha(laneColor, 0.4), Palette.TextSecondary, false));
            }
            else if (r.StartsWith("refs/tags/", StringComparison.Ordinal))
            {
                chips.Add(new RefChipViewModel(r["refs/tags/".Length..], Color.FromArgb(10, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), Color.FromArgb(255, 0xD2, 0xC8, 0xB3), false));
            }
        }
        return chips;
    }
}

public static class RelativeTime
{
    public static string Format(DateTimeOffset when, DateTimeOffset now)
    {
        var local = when.ToLocalTime();
        var delta = now - when;
        if (delta < TimeSpan.FromMinutes(1)) return "just now";
        if (delta < TimeSpan.FromHours(1)) return $"{(int)delta.TotalMinutes} min ago";
        if (delta < TimeSpan.FromHours(12)) return $"{(int)delta.TotalHours} h ago";
        if (local.Date == now.LocalDateTime.Date.AddDays(-1)) return $"Yesterday {local:HH:mm}";
        if (delta < TimeSpan.FromDays(6)) return local.ToString("ddd HH:mm");
        return local.Year == now.Year ? local.ToString("MMM d") : local.ToString("MMM d, yyyy");
    }
}
