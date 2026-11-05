using CentralGit.Application.Ports;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Diff;

namespace CentralGit.Application.Features.Compare;

/// <summary>The refs to offer in the pickers, and which pair to start with.</summary>
public sealed record CompareChoices(IReadOnlyList<string> Refs, string? Base, string? Head);

public interface ICompareService
{
    /// <summary>
    /// Branches to pick from, local first. Starts with the current branch against its trunk
    /// (main, master, develop… or their remote copies), or against its upstream when it is the trunk.
    /// </summary>
    CompareChoices Choices(
        IReadOnlyList<string> local,
        IReadOnlyList<string> remote,
        string? current,
        string? upstream
    );

    Task<Comparison> CompareAsync(string baseRef, string headRef, CancellationToken ct = default);

    /// <summary>One file as head changes it, from the merge base.</summary>
    Task<FileDiff> GetFileDiffAsync(Comparison comparison, ChangedFile file, CancellationToken ct = default);
}

public sealed class CompareService(IHistorySource repository) : ICompareService
{
    /// <summary>Commit lists stop here; the counts stay exact.</summary>
    public const int CommitLimit = 300;

    private static readonly string[] Trunks = ["main", "master", "develop", "development", "trunk"];

    public CompareChoices Choices(
        IReadOnlyList<string> local,
        IReadOnlyList<string> remote,
        string? current,
        string? upstream
    )
    {
        var refs = local.Concat(remote).Distinct(StringComparer.Ordinal).ToList();
        var head = current ?? local.FirstOrDefault();

        // On the trunk itself, the interesting question is how it differs from the server.
        var onTrunk = head is not null && Trunks.Contains(head);
        var localTrunk = Trunks.FirstOrDefault(t => t != head && local.Contains(t));
        var remoteTrunk = Trunks
            .SelectMany(t => remote.Where(r => r.EndsWith("/" + t, StringComparison.Ordinal)))
            .FirstOrDefault();
        var baseRef = onTrunk ? upstream ?? localTrunk ?? remoteTrunk : localTrunk ?? remoteTrunk ?? upstream;

        // On a trunk with nothing to compare it to, look at another branch against it instead.
        if (onTrunk && baseRef is null && local.FirstOrDefault(b => b != head) is { } other)
            return new CompareChoices(refs, head, other);

        if (baseRef is null || baseRef == head)
            baseRef = refs.FirstOrDefault(r => r != head);
        return new CompareChoices(refs, baseRef, head);
    }

    public Task<Comparison> CompareAsync(string baseRef, string headRef, CancellationToken ct = default) =>
        repository.CompareAsync(baseRef, headRef, CommitLimit, ct);

    public Task<FileDiff> GetFileDiffAsync(Comparison comparison, ChangedFile file, CancellationToken ct = default) =>
        repository.GetFileDiffAsync(comparison.DiffBase, comparison.HeadHash, file, ct);
}
