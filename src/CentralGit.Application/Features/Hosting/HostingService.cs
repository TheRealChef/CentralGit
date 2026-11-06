using CentralGit.Application.Ports;
using CentralGit.Domain.Hosting;

namespace CentralGit.Application.Features.Hosting;

/// <summary>Pull requests and CI checks for the open repository, when its remote is on GitHub.</summary>
public interface IHostingService
{
    /// <summary>The GitHub repository behind <c>origin</c> (or the only remote); null when it isn't on GitHub.</summary>
    Task<HostedRepository?> GetHostedAsync(CancellationToken ct = default);

    Task<PullRequest?> FindPullRequestAsync(string branch, CancellationToken ct = default);

    Task<CheckState> GetChecksAsync(string commit, CancellationToken ct = default);
}

public sealed class HostingService(IBranchSource branches, IHostingProvider provider) : IHostingService
{
    private HostedRepository? _hosted;
    private bool _resolved;

    public async Task<HostedRepository?> GetHostedAsync(CancellationToken ct = default)
    {
        if (_resolved)
            return _hosted;
        var remotes = await branches.GetRemotesAsync(ct).ConfigureAwait(false);
        var remote =
            remotes.Contains("origin") ? "origin"
            : remotes.Count == 1 ? remotes[0]
            : null;
        if (remote is not null)
            _hosted = HostedRepository.Parse(
                await branches.GetUserConfigAsync($"remote.{remote}.url", ct).ConfigureAwait(false)
            );
        _resolved = true;
        return _hosted;
    }

    public async Task<PullRequest?> FindPullRequestAsync(string branch, CancellationToken ct = default) =>
        await GetHostedAsync(ct).ConfigureAwait(false) is { } hosted
            ? await provider.FindOpenPullRequestAsync(hosted, branch, ct).ConfigureAwait(false)
            : null;

    public async Task<CheckState> GetChecksAsync(string commit, CancellationToken ct = default) =>
        await GetHostedAsync(ct).ConfigureAwait(false) is { } hosted
            ? await provider.GetChecksAsync(hosted, commit, ct).ConfigureAwait(false)
            : CheckState.None;
}

/// <summary>For sessions without a host provider (tests): nothing is ever found.</summary>
public sealed class NoHostingProvider : IHostingProvider
{
    public Task<PullRequest?> FindOpenPullRequestAsync(
        HostedRepository repository,
        string branch,
        CancellationToken ct = default
    ) => Task.FromResult<PullRequest?>(null);

    public Task<CheckState> GetChecksAsync(
        HostedRepository repository,
        string commit,
        CancellationToken ct = default
    ) => Task.FromResult(CheckState.None);
}
