namespace CentralGit.App.ViewModels;

public sealed partial class ShellViewModel
{
    /// <summary>
    /// After a theme switch: view models captured their colors when they were built, so rebuild
    /// what's on screen (history, branch tree, the open commit, Changes and Health).
    /// </summary>
    public async Task RefreshColorsAsync()
    {
        if (_session is null)
            return;
        await ReloadAsync();
        if (SelectedCommit is { } row)
            await Inspector.ShowCommitAsync(row.Commit.Hash);
        if (Page == AppPage.Changes)
            await Changes.RefreshAsync();
        if (Page == AppPage.Health)
            await LoadHealthAsync();
        else
            Health.Clear(); // rebuilt with the new colors when next shown
    }
}
