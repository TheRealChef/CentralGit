namespace CentralGit.App.ViewModels;

/// <summary>The File History page: one file's commits across renames.</summary>
public sealed partial class ShellViewModel
{
    public FileHistoryViewModel FileHistory { get; } = new();

    /// <summary>Opens File History on <paramref name="path"/> (from the inspector's file list or the picker).</summary>
    public async Task ShowFileHistoryAsync(string path)
    {
        Page = AppPage.FileHistory;
        await FileHistory.ShowAsync(path);
    }
}
