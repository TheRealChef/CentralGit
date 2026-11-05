using CentralGit.Application.Features.Updates;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CentralGit.App.ViewModels;

/// <summary>
/// App updates: checked shortly after launch and then every few hours, downloaded in the background,
/// applied when the user restarts from the status bar or Settings › About.
/// </summary>
public sealed partial class UpdatesViewModel(IUpdateService updates) : ObservableObject
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private UpdateStatus _status = updates.Status;
    private bool _started;

    public string VersionText => $"CentralGit {_status.CurrentVersion}";

    /// <summary>Status bar: an update is downloaded and waits for a restart.</summary>
    public bool IsReady => _status.State == UpdateState.Ready;

    public string RestartText => $"Restart to update to {_status.NewVersion}";

    public bool CanCheck => _status.State is UpdateState.Idle or UpdateState.UpToDate or UpdateState.Failed;

    public bool IsWorking => _status.State is UpdateState.Checking or UpdateState.Downloading;

    public bool IsFailed => _status.State == UpdateState.Failed;

    public string StatusText =>
        _status.State switch
        {
            UpdateState.NotInstalled => "Development build: updates come with the installer.",
            UpdateState.Idle => "Updates are checked automatically.",
            UpdateState.Checking => "Checking for updates…",
            UpdateState.UpToDate => $"Up to date{Checked}.",
            UpdateState.Downloading => $"Downloading {_status.NewVersion}… {_status.Percent}%",
            UpdateState.Ready => $"Version {_status.NewVersion} is downloaded. Restart to finish updating.",
            UpdateState.Failed => $"Couldn't check for updates: {_status.Error}",
            _ => "",
        };

    private string Checked => _status.CheckedAt is { } at ? $" · checked {at:HH:mm}" : "";

    /// <summary>Starts the background schedule once; call from the UI thread.</summary>
    public async void Start()
    {
        if (_started || _status.State == UpdateState.NotInstalled)
            return;
        _started = true;
        await Task.Delay(FirstCheck);
        while (_status.State != UpdateState.Ready)
        {
            await CheckAsync();
            await Task.Delay(Interval);
        }
    }

    public Task CheckAsync() => updates.CheckAsync(new Progress<UpdateStatus>(Apply));

    public void Restart() => updates.RestartToUpdate();

    private void Apply(UpdateStatus status)
    {
        _status = status;
        OnPropertyChanged(string.Empty);
    }
}
