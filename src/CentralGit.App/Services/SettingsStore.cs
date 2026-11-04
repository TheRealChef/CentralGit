using System.Text.Json;
using CentralGit.Core.Repositories;

namespace CentralGit.App.Services;

public sealed record RecentRepository(string Path, DateTimeOffset OpenedAt);

public sealed record PaneSizes(
    double Sidebar = PaneSizes.DefaultSidebar,
    double Details = PaneSizes.DefaultDetails,
    double ChangesFiles = PaneSizes.DefaultChangesFiles,
    double UnstagedShare = 0.5)
{
    public const double DefaultSidebar = 232;
    public const double DefaultDetails = 360;
    public const double DefaultChangesFiles = 380;
}

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

    public ScanOptions ToScanOptions() => new()
    {
        MaxDepth = ScanDepth,
        IncludeWorktrees = IncludeWorktrees,
        ExcludedFolderNames = new HashSet<string>(SkipFolderNames, StringComparer.OrdinalIgnoreCase),
    };

    public bool IsHidden(string path) => HiddenRepositories.Contains(path, StringComparer.OrdinalIgnoreCase);
}

/// <summary>%APPDATA%\CentralGit\settings.json. Read once, written on every change.</summary>
public static class SettingsStore
{
    private static readonly string FilePath = AppPaths.File("settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly Lock Gate = new();
    private static AppSettings? _current;

    public static AppSettings Current
    {
        get
        {
            lock (Gate)
                return _current ??= Load();
        }
    }

    public static void Update(Func<AppSettings, AppSettings> change)
    {
        lock (Gate)
        {
            _current = change(_current ?? Load());
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_current, Json));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Remembers a repository as just opened (most recent first, 20 kept).</summary>
    public static void MarkOpened(string path) => Update(s => s with
    {
        Recent = [new RecentRepository(path, DateTimeOffset.Now),
                  .. s.Recent.Where(r => !r.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).Take(19)],
    });

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) is { } loaded)
                return loaded;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        // First run: the usual places people keep repositories.
        string[] candidates =
        [
            @"D:\Source\Repos",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos"),
        ];
        return new AppSettings { ScanFolders = candidates.Where(Directory.Exists).ToList() };
    }
}
