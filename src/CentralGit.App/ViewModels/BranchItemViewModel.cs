using CentralGit.App.Theming;
using CentralGit.Core.Git;
using Windows.UI;

namespace CentralGit.App.ViewModels;

public sealed class BranchItemViewModel(LocalBranch branch)
{
    public string Prefix { get; } = branch.Name.Contains('/') ? branch.Name[..(branch.Name.LastIndexOf('/') + 1)] : "";
    public string Name { get; } = branch.Name.Contains('/') ? branch.Name[(branch.Name.LastIndexOf('/') + 1)..] : branch.Name;
    public bool IsHead { get; } = branch.IsHead;

    public string Badge { get; } =
        branch.UpstreamGone ? "gone"
        : branch.Behind > 0 ? $"↓{branch.Behind}"
        : branch.Ahead > 0 ? $"↑{branch.Ahead}"
        : branch.Upstream is null ? "local" : "";

    public Color BadgeColor =>
        branch.Behind > 0 ? Palette.OrangeText : branch.Ahead > 0 ? Palette.VioletText : Palette.TextMuted;

    public Color DotColor => IsHead ? Palette.Violet : branch.Name is "main" or "master" or "develop" ? Palette.Teal : Palette.Slate;
    public Color NameColor => IsHead ? Palette.TextPrimary : Color.FromArgb(255, 0xD5, 0xD7, 0xDB);
    public Color RowBackground => IsHead ? Palette.WithAlpha(Palette.Violet, 0.10) : Palette.Transparent;
}
