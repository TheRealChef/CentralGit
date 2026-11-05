using System.Text.Json;
using System.Text.Json.Serialization;
using CentralGit.Application.Ports;
using CentralGit.Application.Settings;

namespace CentralGit.Infrastructure.Storage;

/// <summary>%APPDATA%\CentralGit\settings.json. Read once, written on every change.</summary>
public sealed class SettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly Lock _gate = new();
    private AppSettings? _current;

    public SettingsStore()
        : this(AppPaths.File("settings.json")) { }

    public SettingsStore(string filePath) => _filePath = filePath;

    public AppSettings Current
    {
        get
        {
            lock (_gate)
                return _current ??= Load();
        }
    }

    public void Update(Func<AppSettings, AppSettings> change)
    {
        lock (_gate)
        {
            _current = change(_current ?? Load());
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                File.WriteAllText(_filePath, JsonSerializer.Serialize(_current, Json));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (
                File.Exists(_filePath)
                && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), Json) is { } loaded
            )
                return loaded;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }

        // First run: the usual places people keep repositories.
        string[] candidates =
        [
            @"D:\Source\Repos",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos"),
        ];
        return new AppSettings { ScanFolders = candidates.Where(Directory.Exists).ToList() };
    }
}
