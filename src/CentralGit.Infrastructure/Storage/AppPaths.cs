namespace CentralGit.Infrastructure.Storage;

/// <summary>
/// Where CentralGit keeps its own files. The installer owns %LOCALAPPDATA%\CentralGit (program
/// files, replaced on update, removed on uninstall), so user data lives in %APPDATA%\CentralGit.
/// </summary>
public static class AppPaths
{
    private static readonly Lazy<string> Data = new(Prepare);

    public static string DataDirectory => Data.Value;

    public static string File(string name) => Path.Combine(DataDirectory, name);

    private static string Prepare()
    {
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CentralGit");
        try
        {
            Directory.CreateDirectory(data);

            // Builds before the installer kept data in %LOCALAPPDATA%\CentralGit: bring it along once.
            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CentralGit"
            );
            foreach (var name in new[] { "settings.json", "layout.json" })
            {
                var from = Path.Combine(legacy, name);
                var to = Path.Combine(data, name);
                if (System.IO.File.Exists(from) && !System.IO.File.Exists(to))
                    System.IO.File.Copy(from, to);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return data;
    }
}
