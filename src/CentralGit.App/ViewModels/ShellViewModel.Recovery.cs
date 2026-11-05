using CentralGit.Application.Git;

namespace CentralGit.App.ViewModels;

/// <summary>The Recovery page: the reflog, and bringing lost commits back as branches.</summary>
public sealed partial class ShellViewModel
{
    public RecoveryViewModel Recovery { get; } = new();

    private int _recoveryGeneration;

    /// <summary>Sidebar count next to Recovery: commits only the reflog still knows.</summary>
    public string RecoverySidebarText => Recovery.SidebarText;

    public async Task LoadRecoveryAsync()
    {
        if (_session is not { } session)
            return;
        var generation = ++_recoveryGeneration;
        Recovery.IsLoading = true;
        try
        {
            var list = await Task.Run(() => session.Recovery.LoadAsync());
            if (generation == _recoveryGeneration && _session == session)
                Recovery.Apply(list, DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            if (generation == _recoveryGeneration)
                Recovery.ErrorText = ex is GitException g ? g.Message : ex.Message;
        }
        finally
        {
            if (generation == _recoveryGeneration)
                Recovery.IsLoading = false;
            OnPropertyChanged(nameof(RecoverySidebarText));
        }
    }

    /// <summary>Asks for a name and puts a branch on the row's commit, staying on the current branch.</summary>
    public async Task RestoreAsBranchAsync(RecoveryRowViewModel row)
    {
        if (_session is not { } session || PromptText is null || Operations.IsBusy)
            return;
        var name = await PromptText(
            "Restore as branch",
            $"A new branch at {row.ShortHash} ({row.Subject}). You stay on the current branch.",
            session.Recovery.SuggestBranchName(row.Point),
            "Create branch",
            ValidateNewBranchNameAsync
        );
        if (name is null)
            return;

        var (result, op) = await Operations.RunAsync(
            "branch",
            $"Restore {row.ShortHash} as {name}",
            session.Recovery.RestoreAsBranch(name, row.Point)
        );
        op.Target = name;
        await FinishAsync(result, op, () => $"Restored {row.ShortHash} as {name}. It's in the sidebar under Branches.");
    }

    /// <summary>History loads in pages; an old commit may not be loaded yet.</summary>
    public void ReportNotLoaded(string shortHash) =>
        SetStatus(
            $"{shortHash} is further back than the loaded history. Type it in the History filter to find it.",
            error: false
        );

    /// <summary>A commit still on a branch: show it in History.</summary>
    public bool ShowInHistory(RecoveryRowViewModel row)
    {
        Page = AppPage.History;
        return SelectCommit(row.Point.Entry.Hash);
    }
}
