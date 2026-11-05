using CentralGit.Domain.Repositories;

namespace CentralGit.Application.Settings;

/// <summary>What the user picked in Settings › Appearance.</summary>
public enum AppTheme
{
    Dark,
    Light,
    System,
}

public sealed record RecentRepository(string Path, DateTimeOffset OpenedAt);

public sealed record PaneSizes(
    double Sidebar = PaneSizes.DefaultSidebar,
    double Details = PaneSizes.DefaultDetails,
    double ChangesFiles = PaneSizes.DefaultChangesFiles,
    double UnstagedShare = 0.5
)
{
    public const double DefaultSidebar = 232;
    public const double DefaultDetails = 360;
    public const double DefaultChangesFiles = 380;
}

/// <summary>Per-repository layout the user chose: splitter position and diff style.</summary>
public sealed record RepositoryLayout(double HistoryRatio = 0.55, bool SplitDiff = true);

/// <summary>Everything in Settings that CentralGit owns (git identity lives in git config).</summary>
public sealed record AppSettings
{
    public List<string> ScanFolders { get; init; } = [];
    public int ScanDepth { get; init; } = 3;
    public bool IncludeWorktrees { get; init; } = true;
    public List<string> SkipFolderNames { get; init; } = ["node_modules", "bin", "obj", ".vs", "dist", "packages"];

    /// <summary>Repositories unticked in Settings › Repositories: found, but not offered in the switcher.</summary>
    public List<string> HiddenRepositories { get; init; } = [];

    public List<RecentRepository> Recent { get; init; } = [];

    /// <summary>Background fetch of the open repository, in minutes; 0 = off. Fetch only: never pulls, merges or rebases.</summary>
    public int BackgroundFetchMinutes { get; init; }

    /// <summary>Pane sizes the user dragged to. The same in every repository.</summary>
    public PaneSizes Panes { get; init; } = new();

    /// <summary>Settings › Appearance. In the light theme the title bar, sidebar and status bar stay dark unless turned off.</summary>
    public AppTheme Theme { get; init; } = AppTheme.Dark;
    public bool DarkChromeInLight { get; init; } = true;

    public ScanOptions ToScanOptions() =>
        new()
        {
            MaxDepth = ScanDepth,
            IncludeWorktrees = IncludeWorktrees,
            ExcludedFolderNames = new HashSet<string>(SkipFolderNames, StringComparer.OrdinalIgnoreCase),
        };

    public bool IsHidden(string path) => HiddenRepositories.Contains(path, StringComparer.OrdinalIgnoreCase);

    /// <summary>Remembers a repository as just opened (most recent first, 20 kept).</summary>
    public AppSettings WithOpened(string path, DateTimeOffset at) =>
        this with
        {
            Recent =
            [
                new RecentRepository(path, at),
                .. Recent.Where(r => !r.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).Take(19),
            ],
        };
}
