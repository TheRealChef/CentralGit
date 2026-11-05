using System.Text.Json;
using CentralGit.Application.Ports;
using CentralGit.Application.Settings;

namespace CentralGit.Infrastructure.Storage;

/// <summary>
/// Remembers <see cref="RepositoryLayout"/> per repository in
/// %APPDATA%\CentralGit\layout.json. Failures are non-fatal: layout is a convenience.
/// </summary>
public sealed class LayoutStore(string filePath) : ILayoutStore
{
    private Dictionary<string, RepositoryLayout>? _cache;

    public LayoutStore()
        : this(AppPaths.File("layout.json")) { }

    public RepositoryLayout Get(string repositoryPath) =>
        Load().TryGetValue(Key(repositoryPath), out var layout) ? layout : new RepositoryLayout();

    public void Set(string repositoryPath, RepositoryLayout layout)
    {
        var all = Load();
        all[Key(repositoryPath)] = layout;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(
                filePath,
                JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true })
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private Dictionary<string, RepositoryLayout> Load()
    {
        if (_cache is not null)
            return _cache;
        try
        {
            _cache = File.Exists(filePath)
                ? JsonSerializer.Deserialize<Dictionary<string, RepositoryLayout>>(File.ReadAllText(filePath)) ?? []
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
