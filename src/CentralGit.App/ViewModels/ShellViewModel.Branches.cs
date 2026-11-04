using CentralGit.Core.Git;
using CentralGit.Core.Operations;

namespace CentralGit.App.ViewModels;

/// <summary>Branch switching, creation, renaming and deletion — all through the Operations Center.</summary>
public sealed partial class ShellViewModel
{
    /// <summary>Asks before something destructive: (title, message, button) → confirmed.</summary>
    public Func<string, string, string, Task<bool>>? ConfirmDestructive { get; set; }

    /// <summary>Asks for a line of text: (title, message, initial, button, validate) → text or null if cancelled.</summary>
    public Func<string, string, string, string, Func<string, Task<string?>>, Task<string?>>? PromptText { get; set; }

    public string? CurrentBranch => _branch?.Head;

    private IReadOnlyList<LocalBranch> _localBranches = [];
    private readonly HashSet<string> _collapsedFolders = new(StringComparer.Ordinal);

    /// <summary>Sidebar rows from the last branch list, honouring collapsed folders.</summary>
    private void RebuildBranchTree()
    {
        Branches.Clear();
        foreach (var row in BranchItemViewModel.BuildTree(_localBranches, _collapsedFolders, _branchAssessments))
            Branches.Add(row);
    }

    public void ToggleFolder(string folderPath)
    {
        if (!_collapsedFolders.Remove(folderPath))
            _collapsedFolders.Add(folderPath);
        RebuildBranchTree();
    }

    /// <summary>Single click on a branch: show its tip in History (switching page if needed).</summary>
    public void RevealBranch(BranchItemViewModel row)
    {
        if (row.Branch?.Tip is not { } tip)
            return;
        ShowChanges = false;
        if (!SelectCommit(tip))
            SetStatus($"{row.FullName} points at {tip[..7]}, which isn't in the loaded history yet — scroll down to load more.", error: false);
    }

    public async Task<(IReadOnlyList<LocalBranch> Local, IReadOnlyList<RemoteBranch> Remote)> GetBranchesAsync()
    {
        if (_repository is not { } repo)
            return ([], []);
        var local = repo.GetLocalBranchesAsync();
        var remote = repo.GetRemoteBranchesAsync();
        return (await local, await remote);
    }

    public Task SwitchToAsync(string branch) =>
        branch == _branch?.Head ? Task.CompletedTask
        : RunBranchOperationAsync("switch", $"Switch to {branch}", BranchOperations.Switch(branch), branch, () => $"Switched to {branch}");

    /// <summary>A remote branch: switch to the local branch tracking it, or create one.</summary>
    public async Task CheckoutRemoteAsync(RemoteBranch remote)
    {
        var (local, _) = await GetBranchesAsync();
        if (local.FirstOrDefault(b => b.Upstream == remote.Name) is { } tracking)
        {
            await SwitchToAsync(tracking.Name);
            return;
        }
        if (local.Any(b => b.Name == remote.ShortName))
        {
            // A local branch with that name exists but tracks something else: don't guess.
            SetStatus($"A local branch {remote.ShortName} already exists and doesn't track {remote.Name}.", error: true);
            return;
        }
        await RunBranchOperationAsync("switch", $"Check out {remote.Name}", BranchOperations.Track(remote.Name), remote.ShortName,
            () => $"Created {remote.ShortName} tracking {remote.Name}");
    }

    /// <summary>Validates through git; returns an error sentence or null when the name is usable.</summary>
    public async Task<string?> ValidateNewBranchNameAsync(string name)
    {
        if (_repository is not { } repo)
            return "No repository is open.";
        if (await BranchOperations.ValidateNameAsync(repo.Git, repo.Path, name) is { } problem)
            return problem;
        var (local, _) = await GetBranchesAsync();
        return local.Any(b => b.Name == name) ? $"A branch called {name} already exists." : null;
    }

    public Task CreateBranchAsync(string name, string? startPoint = null, string? startLabel = null) =>
        RunBranchOperationAsync("switch", $"Create {name}" + (startLabel is null ? "" : $" from {startLabel}"),
            BranchOperations.Create(name, startPoint), name, () => $"Created and switched to {name}");

    public async Task RenameBranchAsync(string branch)
    {
        if (PromptText is null)
            return;
        var name = await PromptText("Rename branch", $"New name for {branch}", branch, "Rename",
            async n => n == branch ? "That's the current name." : await ValidateNewBranchNameAsync(n));
        if (name is null)
            return;
        await RunBranchOperationAsync("branch", $"Rename {branch} to {name}", BranchOperations.Rename(branch, name), name, () => $"Renamed {branch} to {name}");
    }

    /// <summary>Asks for a name, then creates a branch at <paramref name="startPoint"/> and switches to it.</summary>
    public async Task NewBranchAsync(string? startPoint = null, string? startLabel = null)
    {
        if (PromptText is null)
            return;
        var from = startLabel ?? _branch?.Head ?? "HEAD";
        var name = await PromptText("New branch", $"Created from {from} and checked out. Uncommitted changes come along.", "", "Create branch",
            ValidateNewBranchNameAsync);
        if (name is not null)
            await CreateBranchAsync(name, startPoint, startLabel);
    }

    public async Task DeleteBranchAsync(string branch, bool force)
    {
        if (branch == _branch?.Head)
        {
            SetStatus("Can't delete the branch you're on — switch to another branch first.", error: true);
            return;
        }
        if (force && (ConfirmDestructive is null || !await ConfirmDestructive("Delete unmerged branch?",
                $"{branch} has commits that aren't on any other branch. After deleting it they can only be found through the reflog (Recovery).",
                "Delete branch")))
            return;
        await RunBranchOperationAsync("delete", $"Delete {branch}", BranchOperations.Delete(branch, force), branch, () => $"Deleted {branch}");
    }

    /// <summary>"Stash changes and switch": park the changes (labelled), then switch.</summary>
    private async Task StashAndSwitchAsync(string target)
    {
        if (_repository is not { } repo || Operations.IsBusy)
            return;
        var (stash, op) = await Operations.RunAsync(repo.Git, repo.Path, "stash", "Stash changes", BranchOperations.StashBeforeSwitch(target));
        if (!stash.Succeeded)
        {
            await FinishAsync(stash, op, () => "");
            return;
        }
        await SwitchToAsync(target);
        if (_branch?.Head == target)
            SetStatus($"Switched to {target} — your changes are in the stash (“changes left behind when switching to {target}”)", error: false);
    }

    private async Task RunBranchOperationAsync(string kind, string title, IReadOnlyList<string> args, string target, Func<string> success)
    {
        if (_repository is not { } repo || Operations.IsBusy)
            return;
        var (result, op) = await Operations.RunAsync(repo.Git, repo.Path, kind, title, args);
        op.Target = target;
        await FinishAsync(result, op, success);
        OnPropertyChanged(nameof(CurrentBranch));
    }
}
