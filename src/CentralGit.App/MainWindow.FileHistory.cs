using CentralGit.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CentralGit.App;

/// <summary>The File History page: the file picker and jumping to a commit.</summary>
public sealed partial class MainWindow
{
    private FileHistoryViewModel FileHistory => ViewModel.FileHistory;

    private void OnShowFileHistory(object sender, RoutedEventArgs e)
    {
        ShowPage(AppPage.FileHistory);
        if (!FileHistory.HasPath)
            DispatcherQueue.TryEnqueue(() => FileHistoryPicker.Focus(FocusState.Programmatic));
    }

    private async void OnFileHistoryFromInspector(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            FileHistoryPicker.Text = "";
            await ViewModel.ShowFileHistoryAsync(path);
        }
    }

    private async void OnFileHistoryPickerTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;
        var query = sender.Text;
        var matches = await FileHistory.SuggestAsync(query);
        if (sender.Text == query) // typing moved on: a newer request will fill the list
            sender.ItemsSource = matches;
    }

    private async void OnFileHistoryPickerSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var path = args.ChosenSuggestion as string;
        if (path is null)
        {
            // Enter without choosing: take the best match, or the text as typed (a deleted file's old path).
            var matches = await FileHistory.SuggestAsync(args.QueryText);
            path =
                matches.FirstOrDefault(m => m.Equals(args.QueryText.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? matches.FirstOrDefault()
                ?? args.QueryText;
        }
        sender.Text = "";
        sender.ItemsSource = null;
        await FileHistory.ShowAsync(path);
        FileRevisionList.Focus(FocusState.Programmatic);
    }

    private void OnFileHistoryShowCommit(object sender, RoutedEventArgs e)
    {
        if (FileHistory.Selected is not { } row)
            return;
        ViewModel.Page = AppPage.History;
        if (ViewModel.SelectCommit(row.Revision.Hash))
            DispatcherQueue.TryEnqueue(FocusSelectedCommit);
        else
            ViewModel.ReportNotLoaded(row.ShortHash);
    }
}
