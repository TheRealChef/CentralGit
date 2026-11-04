using CentralGit.App.Services;
using CentralGit.App.ViewModels;
using Microsoft.UI.Xaml;

namespace CentralGit.App;

/// <summary>
/// Resizable panes: sidebar, inspector details column, the Changes file column and the
/// unstaged/staged split. Sizes are remembered globally; double-click a handle to reset it.
/// </summary>
public sealed partial class MainWindow
{
    private const double MinSidebar = 170, MaxSidebar = 420;
    private const double MinDetails = 240, MinChangesFiles = 280, MinDiff = 320;
    private const double MinListShare = 0.15;

    private PaneSizes _panes = SettingsStore.Current.Panes;

    private void InitializePanes()
    {
        ChangesFilesColumn.Width = new GridLength(_panes.ChangesFiles);
        ApplyListShare();

        SidebarSplitter.DragDelta += d =>
        {
            if (ViewModel.Mode == WorkspaceMode.Focus)
                return;
            _transition?.Stop();
            _panes = _panes with { Sidebar = Math.Clamp(SidebarColumn.ActualWidth + d, MinSidebar, MaxSidebar) };
            SidebarColumn.Width = new GridLength(_panes.Sidebar);
        };
        SidebarSplitter.ResetRequested += () => Reset(_panes with { Sidebar = PaneSizes.DefaultSidebar });

        DetailsSplitter.DragDelta += d =>
        {
            if (ViewModel.Mode == WorkspaceMode.Focus)
                return;
            _transition?.Stop();
            var max = Math.Max(MinDetails, InspectorPane.ActualWidth - MinDiff);
            _panes = _panes with { Details = Math.Clamp(DetailsColumn.ActualWidth + d, MinDetails, max) };
            DetailsColumn.Width = new GridLength(_panes.Details);
        };
        DetailsSplitter.ResetRequested += () => Reset(_panes with { Details = PaneSizes.DefaultDetails });

        ChangesFilesSplitter.DragDelta += d =>
        {
            var max = Math.Max(MinChangesFiles, ChangesPage.ActualWidth - MinDiff);
            _panes = _panes with { ChangesFiles = Math.Clamp(ChangesFilesColumn.ActualWidth + d, MinChangesFiles, max) };
            ChangesFilesColumn.Width = new GridLength(_panes.ChangesFiles);
        };
        ChangesFilesSplitter.ResetRequested += () => Reset(_panes with { ChangesFiles = PaneSizes.DefaultChangesFiles });

        StagedSplitter.DragDelta += d =>
        {
            var total = UnstagedRow.ActualHeight + StagedRow.ActualHeight;
            if (total <= 0)
                return;
            var share = (UnstagedRow.ActualHeight + d) / total;
            _panes = _panes with { UnstagedShare = Math.Clamp(share, MinListShare, 1 - MinListShare) };
            ApplyListShare();
        };
        StagedSplitter.ResetRequested += () => Reset(_panes with { UnstagedShare = 0.5 });

        foreach (var splitter in new[] { SidebarSplitter, DetailsSplitter, ChangesFilesSplitter, StagedSplitter })
            splitter.DragCompleted += SavePanes;
    }

    private void ApplyListShare()
    {
        UnstagedRow.Height = new GridLength(_panes.UnstagedShare, GridUnitType.Star);
        StagedRow.Height = new GridLength(1 - _panes.UnstagedShare, GridUnitType.Star);
    }

    private void Reset(PaneSizes panes)
    {
        _panes = panes;
        ChangesFilesColumn.Width = new GridLength(_panes.ChangesFiles);
        ApplyListShare();
        ApplyMode(animate: true); // sidebar and details follow the mode's targets
        SavePanes();
    }

    private void SavePanes()
    {
        var panes = _panes;
        SettingsStore.Update(s => s with { Panes = panes });
    }

    /// <summary>Focus mode hides the sidebar and details, and their handles with them.</summary>
    private void UpdatePaneHandles(bool focus)
    {
        var visibility = focus ? Visibility.Collapsed : Visibility.Visible;
        SidebarSplitter.Visibility = visibility;
        DetailsSplitter.Visibility = visibility;
    }
}
