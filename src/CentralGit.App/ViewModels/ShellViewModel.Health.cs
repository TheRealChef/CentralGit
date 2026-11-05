using CentralGit.Application.Features.Repositories;
using CentralGit.Application.Git;
using CentralGit.Domain.Health;

namespace CentralGit.App.ViewModels;

/// <summary>The Health page: diagnostics and branch cleanup.</summary>
public sealed partial class ShellViewModel
{
    public HealthViewModel Health { get; } = new();

    /// <summary>Branch name → assessment, for the sidebar's staleness hints. Refreshed with every reload.</summary>
    private IReadOnlyDictionary<string, BranchAssessment> _branchAssessments =
        new Dictionary<string, BranchAssessment>();

    private int _healthGeneration;

    /// <summary>Sidebar count next to Health: local branches that could go.</summary>
    public string StaleBranchText =>
        _branchAssessments.Values.Count(a => a.IsCleanupCandidate) is var n and > 0 ? n.ToString() : "";

    public async Task LoadHealthAsync()
    {
        if (_session is not { } session)
            return;
        var generation = ++_healthGeneration;
        Health.IsLoading = true;
        try
        {
            var snapshot = await Task.Run(() => session.Health.LoadReportAsync());
            if (generation == _healthGeneration && _session == session)
                Health.Apply(snapshot.Report, snapshot.At);
        }
        catch (Exception ex)
        {
            if (generation == _healthGeneration)
                Health.ErrorText = ex is GitException g ? $"{g.Message}" : ex.Message;
        }
        finally
        {
            if (generation == _healthGeneration)
                Health.IsLoading = false;
        }
    }

    /// <summary>Sidebar staleness, computed alongside the branch list (a few cheap ref queries).</summary>
    private static async Task<IReadOnlyDictionary<string, BranchAssessment>> AssessLocalBranchesAsync(
        RepositorySession session
    )
    {
        try
        {
            var review = await session.Health.ReviewBranchesAsync();
            return review.Local.ToDictionary(a => a.Name, StringComparer.Ordinal);
        }
        catch (GitException)
        {
            return new Dictionary<string, BranchAssessment>();
        }
    }

    /// <summary>Deletes the selected cleanup rows: local branches in one command, remote ones per remote after a clear warning.</summary>
    public async Task DeleteSelectedBranchesAsync()
    {
        if (_session is not { } session || Operations.IsBusy)
            return;
        var rows = Health.Selected;
        if (rows.Count == 0)
            return;

        var unmerged = rows.Where(r => !r.Assessment.IsSafeToDelete).ToList();
        var remote = rows.Where(r => r.IsRemote).ToList();

        if (remote.Count > 0)
        {
            var list =
                string.Join("\n", remote.Take(8).Select(r => "  • " + r.Name))
                + (remote.Count > 8 ? $"\n  … and {remote.Count - 8} more" : "");
            if (
                ConfirmDestructive is null
                || !await ConfirmDestructive(
                    $"Delete {remote.Count} branch{(remote.Count == 1 ? "" : "es")} on the server?",
                    $"This removes them from the remote for everyone on the team, not just from this PC:\n\n{list}"
                        + (unmerged.Count > 0 ? $"\n\n{unmerged.Count} of them aren't merged into a main branch." : "")
                        + "\n\nAnyone who still has them locally keeps their copy.",
                    "Delete on server"
                )
            )
                return;
        }
        else if (unmerged.Count > 0)
        {
            var list =
                string.Join("\n", unmerged.Take(8).Select(r => $"  • {r.Name} ({r.Reason})"))
                + (unmerged.Count > 8 ? $"\n  … and {unmerged.Count - 8} more" : "");
            if (
                ConfirmDestructive is null
                || !await ConfirmDestructive(
                    $"Delete {unmerged.Count} unmerged branch{(unmerged.Count == 1 ? "" : "es")}?",
                    $"These have commits that aren't in a main branch:\n\n{list}\n\nAfter deleting, their commits can only be found through Recovery (reflog). Merged branches in the selection are safe.",
                    "Delete branches"
                )
            )
                return;
        }

        var plan = session.Branches.PlanCleanup(rows.Select(r => r.Assessment), _branch?.Head);
        if (plan.Local is { Count: > 0 } local)
        {
            var (result, op) = await Operations.RunAsync(
                "delete",
                $"Delete {local.Count} branch{(local.Count == 1 ? "" : "es")}",
                session.Branches.DeleteLocal(local)
            );
            op.Target = local.Count == 1 ? local[0] : $"{local.Count} branches";
            await FinishAsync(
                result,
                op,
                () =>
                    $"Deleted {op.Target} — the Operations panel lists the commit each one pointed at, if you need one back."
            );
            if (!result.Succeeded)
            {
                await LoadHealthAsync();
                return;
            }
        }

        foreach (var group in plan.Remote)
        {
            var count = group.ShortNames.Count;
            var (result, op) = await Operations.RunAsync(
                "push",
                $"Delete {count} branch{(count == 1 ? "" : "es")} on {group.Remote}",
                session.Branches.DeleteOnRemote(group)
            );
            op.Target = group.Remote;
            await FinishAsync(result, op, () => $"Deleted {count} branch{(count == 1 ? "" : "es")} on {group.Remote}");
            if (!result.Succeeded)
                break;
        }

        await LoadHealthAsync();
    }
}
