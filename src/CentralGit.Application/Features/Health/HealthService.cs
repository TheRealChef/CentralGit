using CentralGit.Application.Ports;
using CentralGit.Domain.Health;

namespace CentralGit.Application.Features.Health;

/// <summary>A report and the moment it describes (ages are relative to it).</summary>
public sealed record HealthSnapshot(HealthReport Report, DateTimeOffset At);

public interface IHealthService
{
    /// <summary>Branch facts and merge state only — cheap enough to run on every reload.</summary>
    Task<BranchOverview> ReviewBranchesAsync(CancellationToken ct = default);

    /// <summary>Everything the Health page shows.</summary>
    Task<HealthSnapshot> LoadReportAsync(CancellationToken ct = default);
}

public sealed class HealthService(IHealthSource repository, TimeProvider time) : IHealthService
{
    public Task<BranchOverview> ReviewBranchesAsync(CancellationToken ct = default) =>
        ReviewAsync(time.GetUtcNow(), ct);

    public async Task<HealthSnapshot> LoadReportAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var branches = ReviewAsync(now, ct);
        var facts = repository.ReadFactsAsync(now, ct);
        return new HealthSnapshot(
            RepositoryHealth.Report(await branches.ConfigureAwait(false), await facts.ConfigureAwait(false), now),
            now
        );
    }

    private async Task<BranchOverview> ReviewAsync(DateTimeOffset now, CancellationToken ct)
    {
        var refs = await repository.ReadBranchRefsAsync(ct).ConfigureAwait(false);
        var (_, targets) = RepositoryHealth.PickIntegrationBranches(refs);
        var merged = await Task.WhenAll(targets.Select(t => repository.GetRefsMergedIntoAsync(t, ct)))
            .ConfigureAwait(false);
        var byTarget = targets
            .Select((t, i) => (t, merged[i]))
            .ToDictionary(p => p.t, p => p.Item2, StringComparer.Ordinal);
        return RepositoryHealth.Review(refs, byTarget, now);
    }
}
