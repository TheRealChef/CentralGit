using CentralGit.Application.Ports;
using Velopack;
using Velopack.Sources;

namespace CentralGit.Infrastructure.Updates;

/// <summary>
/// Updates from the GitHub releases of CentralGit. The release workflow attaches Velopack's feed
/// (releases.win.json and the .nupkg packages) next to the setup exe.
/// </summary>
public sealed class VelopackUpdater : IAppUpdater
{
    public const string RepositoryUrl = "https://github.com/TheRealChef/CentralGit";

    private readonly UpdateManager _manager = new(
        new GithubSource(RepositoryUrl, accessToken: null, prerelease: false)
    );
    private readonly Dictionary<string, UpdateInfo> _found = [];

    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersion =>
        _manager.CurrentVersion?.ToString() ?? typeof(VelopackUpdater).Assembly.GetName().Version?.ToString(3) ?? "dev";

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken ct = default)
    {
        var info = await _manager.CheckForUpdatesAsync().WaitAsync(ct);
        if (info is null)
            return null;
        var version = info.TargetFullRelease.Version.ToString();
        lock (_found)
            _found[version] = info;
        return new AvailableUpdate(version, info.TargetFullRelease.NotesMarkdown);
    }

    public Task DownloadAsync(AvailableUpdate update, Action<int> percent, CancellationToken ct = default) =>
        _manager.DownloadUpdatesAsync(Find(update), percent, ct);

    public void RestartInto(AvailableUpdate update) => _manager.ApplyUpdatesAndRestart(Find(update).TargetFullRelease);

    private UpdateInfo Find(AvailableUpdate update)
    {
        lock (_found)
            return _found.TryGetValue(update.Version, out var info)
                ? info
                : throw new InvalidOperationException($"Update {update.Version} was not found by a check.");
    }
}
