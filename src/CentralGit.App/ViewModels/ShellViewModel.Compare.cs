using CentralGit.Application.Git;

namespace CentralGit.App.ViewModels;

/// <summary>The Compare page: two refs side by side.</summary>
public sealed partial class ShellViewModel
{
    public CompareViewModel Compare { get; } = new();

    /// <summary>Refreshes the ref pickers (branches may have changed) and compares the chosen pair.</summary>
    public async Task LoadCompareAsync()
    {
        if (_session is not { } session)
            return;
        try
        {
            var branches = await Task.Run(() => session.Branches.GetBranchesAsync());
            if (_session != session)
                return;
            Compare.Offer(
                session.Compare.Choices(
                    branches.Local.Select(b => b.Name).ToList(),
                    branches.Remote.Select(b => b.Name).ToList(),
                    _branch?.Head,
                    _branch?.Upstream
                )
            );
        }
        catch (GitException) { } // the comparison below reports what's wrong
        await Compare.CompareAsync();
    }

    /// <summary>Opens Compare with <paramref name="head"/> against the default base (from a branch's context menu).</summary>
    public async Task CompareBranchAsync(string head)
    {
        Page = AppPage.Compare; // loads the pickers
        await LoadCompareAsync();
        if (Compare.Refs.Contains(head) && Compare.Head != head)
            Compare.Head = head;
    }
}
