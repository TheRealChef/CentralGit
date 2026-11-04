using System.Text.Json;

namespace CentralGit.App.Services;

/// <summary>Per-repository layout the user chose: splitter position and diff style.</summary>
public sealed record RepositoryLayout(double HistoryRatio = 0.55, bool SplitDiff = true);

/// <summary>
/// Remembers <see cref="RepositoryLayout"/> per repository in
/// %APPDATA%\CentralGit\layout.json. Failures are non-fatal: layout is a convenience.
/// </summary>
public static class LayoutStore
{
    private static readonly string FilePath = AppPaths.File("layout.json");

    private static Dictionary<string, RepositoryLayout>? _cache;

    public static RepositoryLayout Get(string repositoryPath) =>
        Load().TryGetValue(Key(repositoryPath), out var layout) ? layout : new RepositoryLayout();

    public static void Set(string repositoryPath, RepositoryLayout layout)
    {
        var all = Load();
        all[Key(repositoryPath)] = layout;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static Dictionary<string, RepositoryLayout> Load()
    {
        if (_cache is not null)
            return _cache;
        try
        {
            _cache = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, RepositoryLayout>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _cache = [];
        }
        return _cache;
    }

    private static string Key(string path) => Path.GetFullPath(path).TrimEnd('\\', '/').ToLowerInvariant();
}
