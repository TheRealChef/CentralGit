using CentralGit.Application.Ports;

namespace CentralGit.Application.Features.Updates;

public enum UpdateState
{
    /// <summary>Running from a build folder; updates come with the installer.</summary>
    NotInstalled,
    Idle,
    Checking,
    UpToDate,
    Downloading,

    /// <summary>Downloaded; applies on restart.</summary>
    Ready,
    Failed,
}

public sealed record UpdateStatus(
    UpdateState State,
    string CurrentVersion,
    string? NewVersion = null,
    int Percent = 0,
    string? Error = null,
    DateTimeOffset? CheckedAt = null
);

public interface IUpdateService
{
    UpdateStatus Status { get; }

    /// <summary>Checks the release feed and downloads a newer version, reporting each step.</summary>
    Task<UpdateStatus> CheckAsync(IProgress<UpdateStatus>? progress = null, CancellationToken ct = default);

    /// <summary>Installs the downloaded update and restarts. Does nothing unless an update is ready.</summary>
    void RestartToUpdate();
}

public sealed class UpdateService(IAppUpdater updater, TimeProvider time) : IUpdateService
{
    private AvailableUpdate? _ready;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UpdateStatus Status { get; private set; } =
        new(updater.IsInstalled ? UpdateState.Idle : UpdateState.NotInstalled, updater.CurrentVersion);

    public async Task<UpdateStatus> CheckAsync(IProgress<UpdateStatus>? progress = null, CancellationToken ct = default)
    {
        if (!updater.IsInstalled || Status.State == UpdateState.Ready)
            return Status;
        if (!await _gate.WaitAsync(0, ct))
            return Status; // a check is already running

        try
        {
            Report(Status with { State = UpdateState.Checking, Error = null }, progress);
            var update = await updater.CheckAsync(ct);
            var now = time.GetLocalNow();
            if (update is null)
                return Report(new UpdateStatus(UpdateState.UpToDate, updater.CurrentVersion, CheckedAt: now), progress);

            var downloading = new UpdateStatus(
                UpdateState.Downloading,
                updater.CurrentVersion,
                update.Version,
                CheckedAt: now
            );
            Report(downloading, progress);
            await updater.DownloadAsync(update, p => Report(downloading with { Percent = p }, progress), ct);

            _ready = update;
            return Report(downloading with { State = UpdateState.Ready, Percent = 100 }, progress);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Report(Status with { State = UpdateState.Idle }, progress);
        }
        catch (Exception ex)
        {
            return Report(
                Status with
                {
                    State = UpdateState.Failed,
                    Error = ex.Message,
                    CheckedAt = time.GetLocalNow(),
                },
                progress
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    public void RestartToUpdate()
    {
        if (_ready is { } update)
            updater.RestartInto(update);
    }

    private UpdateStatus Report(UpdateStatus status, IProgress<UpdateStatus>? progress)
    {
        Status = status;
        progress?.Report(status);
        return status;
    }
}
