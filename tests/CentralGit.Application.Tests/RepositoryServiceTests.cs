using CentralGit.Application.Features.Hosting;
using CentralGit.Application.Features.Repositories;
using CentralGit.Application.Ports;
using CentralGit.Application.Settings;
using CentralGit.Domain.Repositories;

namespace CentralGit.Application.Tests;

public class RepositoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    private readonly FakeFileSystem _files = new();
    private readonly MemorySettings _settings = new(new AppSettings { ScanFolders = [@"D:\Repos"] });

    public RepositoryServiceTests()
    {
        _files.Repositories[@"D:\Repos\old"] = new DateTime(2025, 1, 1);
        _files.Repositories[@"D:\Repos\busy"] = new DateTime(2026, 11, 4);
        _files.Repositories[@"D:\Repos\recent"] = new DateTime(2026, 6, 1);
        _files.Repositories[@"E:\Elsewhere\app"] = new DateTime(2026, 1, 1);
    }

    private RepositoryService Service =>
        new(new NoGit(), _files, new NoGlances(), _settings, new FixedClock(Now), new NoHostingProvider());

    [Fact]
    public void Launch_opens_the_requested_path_first()
    {
        _files.CurrentDirectory = @"D:\Repos\recent\src";
        Assert.Equal(@"E:\Elsewhere\app", Service.Locate(@"E:\Elsewhere\app\docs").Selected);
    }

    [Fact]
    public void Then_the_repository_around_the_current_directory_then_the_last_opened_then_the_busiest()
    {
        _files.CurrentDirectory = @"D:\Repos\recent\src";
        Assert.Equal(@"D:\Repos\recent", Service.Locate(null).Selected);

        _files.CurrentDirectory = @"C:\";
        _settings.Update(s => s.WithOpened(@"D:\Repos\old", Now));
        Assert.Equal(@"D:\Repos\old", Service.Locate(null).Selected);

        _settings.Update(s => s with { Recent = [] });
        Assert.Equal(@"D:\Repos\busy", Service.Locate(null).Selected);
    }

    [Fact]
    public void Discovery_lists_each_repository_once_sorted_by_name()
    {
        var found = Service.Discover(_settings.Current with { ScanFolders = [@"D:\Repos", @"D:\Repos\busy"] });
        Assert.Equal(["busy", "old", "recent"], found.Select(r => r.Name));
    }

    [Fact]
    public void Opening_remembers_the_repository_most_recent_first_and_keeps_twenty()
    {
        _settings.Update(s =>
            s with
            {
                Recent = Enumerable.Range(0, 25).Select(i => new RecentRepository($@"D:\Repos\r{i}", Now)).ToList(),
            }
        );
        _settings.Update(s => s.WithOpened(@"D:\Repos\r3", Now));

        var session = Service.Open(@"D:\Repos\busy");

        Assert.Equal(@"D:\Repos\busy", session.Path);
        Assert.Equal(20, _settings.Current.Recent.Count);
        Assert.Equal([@"D:\Repos\busy", @"D:\Repos\r3"], _settings.Current.Recent.Take(2).Select(r => r.Path));
    }

    private sealed class NoGit : IGitRepositoryFactory
    {
        public IGitRepository Open(string path) => new StubRepository(path);
    }

    private sealed class NoGlances : IRepositoryGlances
    {
        public RepositoryGlance? Cached(string path) => null;

        public Task<RepositoryGlance?> GetAsync(string path, CancellationToken ct) =>
            Task.FromResult<RepositoryGlance?>(null);

        public void Invalidate(string path) { }
    }
}
