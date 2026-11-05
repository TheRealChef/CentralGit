using CentralGit.Application.Git;
using CentralGit.Application.Operations;
using CentralGit.Domain.Operations;

namespace CentralGit.Infrastructure.Git;

/// <summary>Maps an operation's intent to the git command line that carries it out.</summary>
public static class OperationArguments
{
    public static IReadOnlyList<string> For(GitOperation operation) =>
        operation switch
        {
            FetchAll => RemoteOperations.Fetch(),
            Pull { Mode: { } mode } => RemoteOperations.Pull(mode),
            Pull => ["pull", "--progress"], // respect the user's own pull configuration
            Push p => RemoteOperations.Push(p.Branch, p.HasUpstream, p.Remote, p.ForceWithLease),
            SwitchBranch s => BranchOperations.Switch(s.Branch),
            TrackRemoteBranch t => BranchOperations.Track(t.RemoteBranch),
            CreateBranch c => BranchOperations.Create(c.Name, c.StartPoint),
            CreateBranchAt c => BranchOperations.CreateAt(c.Name, c.Commit),
            RenameBranch r => BranchOperations.Rename(r.From, r.To),
            DeleteBranch d => BranchOperations.Delete(d.Name, d.Force),
            DeleteBranches d => BranchOperations.DeleteMany(d.Names),
            DeleteRemoteBranches d => BranchOperations.DeleteRemote(d.Remote, d.ShortNames),
            StashBeforeSwitch s => BranchOperations.StashBeforeSwitch(s.Target),
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation),
                operation,
                "No command line for this operation."
            ),
        };
}

/// <summary>A git command ready to run with streamed progress; failures come back explained.</summary>
internal sealed class PreparedOperation(GitRunner git, string repositoryPath, IReadOnlyList<string> arguments)
    : IPreparedOperation
{
    public string CommandLine { get; } = "git " + string.Join(' ', arguments);

    public async Task<OperationOutcome> RunAsync(Action<GitProgress> onProgress, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.Now;
        try
        {
            var result = await git.RunWithProgressAsync(
                    repositoryPath,
                    arguments,
                    line => onProgress(RemoteOperations.ParseProgress(line)),
                    cancellationToken
                )
                .ConfigureAwait(false);
            return new OperationOutcome(
                result,
                Cancelled: false,
                result.Succeeded ? null : RemoteOperations.Explain(result)
            );
        }
        catch (OperationCanceledException)
        {
            return new OperationOutcome(
                new GitResult([.. arguments], -1, "", "Cancelled.", DateTimeOffset.Now - started),
                Cancelled: true,
                null
            );
        }
    }
}
