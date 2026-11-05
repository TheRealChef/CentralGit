using CentralGit.Application.Operations;
using CentralGit.Application.Ports;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Recovery;

namespace CentralGit.Application.Features.Recovery;

/// <summary>The reflog, judged: <see cref="Lost"/> are commits nothing holds any more.</summary>
public sealed record RecoveryList(IReadOnlyList<RecoveryPoint> All, IReadOnlyList<RecoveryPoint> Lost);

public interface IRecoveryService
{
    /// <summary>Reads the most recent <paramref name="limit"/> reflog entries and checks which commits are lost.</summary>
    Task<RecoveryList> LoadAsync(int limit = 500, CancellationToken ct = default);

    /// <summary>A branch name to offer for bringing back <paramref name="point"/>.</summary>
    string SuggestBranchName(RecoveryPoint point);

    /// <summary>Puts a branch on the commit, without switching to it.</summary>
    IPreparedOperation RestoreAsBranch(string name, RecoveryPoint point);
}

public sealed class RecoveryService(IRecoverySource repository, IOperationSource operations) : IRecoveryService
{
    public async Task<RecoveryList> LoadAsync(int limit = 500, CancellationToken ct = default)
    {
        var entries = await repository.ReadReflogAsync(limit, ct).ConfigureAwait(false);
        var lost = await repository.FindUnreachableAsync(entries.Select(e => e.Hash), ct).ConfigureAwait(false);
        return new RecoveryList(
            Reflog.Points(entries, lost, lostOnly: false),
            Reflog.Points(entries, lost, lostOnly: true)
        );
    }

    public string SuggestBranchName(RecoveryPoint point)
    {
        var words = BranchNames.Suggest(point.Entry.Subject).Trim('-');
        if (words.Length > 40)
            words = words[..40].TrimEnd('-', '.', '/');
        return words.Length > 0 ? $"recovered/{words}" : $"recovered/{point.Entry.ShortHash}";
    }

    public IPreparedOperation RestoreAsBranch(string name, RecoveryPoint point) =>
        operations.Prepare(new CreateBranchAt(name, point.Entry.Hash));
}
