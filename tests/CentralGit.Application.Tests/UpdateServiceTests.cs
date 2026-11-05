using CentralGit.Application.Features.Updates;
using CentralGit.Application.Ports;

namespace CentralGit.Application.Tests;

public class UpdateServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    private sealed class FakeUpdater : IAppUpdater
    {
        public bool IsInstalled { get; init; } = true;
        public string CurrentVersion => "0.1.5";
        public AvailableUpdate? Next { get; init; }
        public Exception? Failure { get; init; }
        public int Checks { get; private set; }
        public AvailableUpdate? Restarted { get; private set; }

        public Task<AvailableUpdate?> CheckAsync(CancellationToken ct = default)
        {
            Checks++;
            return Failure is { } f ? Task.FromException<AvailableUpdate?>(f) : Task.FromResult(Next);
        }

        public Task DownloadAsync(AvailableUpdate update, Action<int> percent, CancellationToken ct = default)
        {
            percent(50);
            percent(100);
            return Task.CompletedTask;
        }

        public void RestartInto(AvailableUpdate update) => Restarted = update;
    }

    private sealed class Collect : IProgress<UpdateStatus>
    {
        public List<UpdateStatus> Seen { get; } = [];

        public void Report(UpdateStatus value) => Seen.Add(value);
    }

    [Fact]
    public async Task A_development_build_never_checks()
    {
        var updater = new FakeUpdater { IsInstalled = false };
        var status = await new UpdateService(updater, new FixedClock(Now)).CheckAsync();
        Assert.Equal(UpdateState.NotInstalled, status.State);
        Assert.Equal(0, updater.Checks);
    }

    [Fact]
    public async Task No_newer_version_is_up_to_date()
    {
        var status = await new UpdateService(new FakeUpdater(), new FixedClock(Now)).CheckAsync();
        Assert.Equal(UpdateState.UpToDate, status.State);
        Assert.Equal(Now, status.CheckedAt);
    }

    [Fact]
    public async Task A_newer_version_is_downloaded_then_waits_for_restart()
    {
        var updater = new FakeUpdater { Next = new AvailableUpdate("0.1.6", null) };
        var service = new UpdateService(updater, new FixedClock(Now));
        var progress = new Collect();

        var status = await service.CheckAsync(progress);

        Assert.Equal(UpdateState.Ready, status.State);
        Assert.Equal("0.1.6", status.NewVersion);
        Assert.Equal(
            [
                UpdateState.Checking,
                UpdateState.Downloading,
                UpdateState.Downloading,
                UpdateState.Downloading,
                UpdateState.Ready,
            ],
            progress.Seen.Select(s => s.State)
        );
        Assert.Contains(progress.Seen, s => s.Percent == 50);

        service.RestartToUpdate();
        Assert.Equal("0.1.6", updater.Restarted?.Version);
    }

    [Fact]
    public async Task Once_ready_it_stops_checking()
    {
        var updater = new FakeUpdater { Next = new AvailableUpdate("0.1.6", null) };
        var service = new UpdateService(updater, new FixedClock(Now));
        await service.CheckAsync();
        await service.CheckAsync();
        Assert.Equal(1, updater.Checks);
    }

    [Fact]
    public async Task A_failed_check_reports_why_and_can_be_retried()
    {
        var service = new UpdateService(
            new FakeUpdater { Failure = new HttpRequestException("No such host is known.") },
            new FixedClock(Now)
        );
        var status = await service.CheckAsync();
        Assert.Equal(UpdateState.Failed, status.State);
        Assert.Equal("No such host is known.", status.Error);
    }

    [Fact]
    public void Restart_without_a_download_does_nothing()
    {
        var updater = new FakeUpdater();
        new UpdateService(updater, new FixedClock(Now)).RestartToUpdate();
        Assert.Null(updater.Restarted);
    }
}
