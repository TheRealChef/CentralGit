using CentralGit.App.Theming;
using CentralGit.App.ViewModels;
using CentralGit.Application.Ports;
using CentralGit.Application.Settings;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;

namespace CentralGit.App;

public sealed partial class MainWindow : Window
{
    private const double SplitterHeight = 6;
    private const double InvestigateHistoryHeight = 44 + 24 + 5 * 26; // header, column header, five rows
    private const double MinPaneHeight = 120;
    private static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(180);

    private readonly string? _requestedPath;
    private bool _initialized;
    private double _historyRatio = 0.55;
    private DispatcherQueueTimer? _transition;

    private readonly ISettingsStore _settings;
    private readonly ILayoutStore _layouts;

    public ShellViewModel ViewModel { get; }
    public SwitcherViewModel Switcher { get; }
    public SettingsViewModel Settings { get; }
    public UpdatesViewModel Updates { get; }

    public MainWindow(
        StartupOptions startup,
        ShellViewModel viewModel,
        SwitcherViewModel switcher,
        SettingsViewModel settingsPage,
        BranchPickerViewModel branchPicker,
        UpdatesViewModel updates,
        ISettingsStore settings,
        ILayoutStore layouts
    )
    {
        _requestedPath = startup.RequestedPath;
        _settings = settings;
        _layouts = layouts;
        _panes = settings.Current.Panes;
        ViewModel = viewModel;
        Switcher = switcher;
        Settings = settingsPage;
        BranchPicker = branchPicker;
        Updates = updates;
        InitializeComponent();

        // Ctrl+P (switcher) and Ctrl+, (settings). Comma has no named VirtualKey, so these are added here.
        Root.KeyboardAccelerators.Add(
            Accelerator(
                Windows.System.VirtualKey.P,
                (_, a) =>
                {
                    OpenSwitcher();
                    a.Handled = true;
                }
            )
        );
        Root.KeyboardAccelerators.Add(
            Accelerator(
                (Windows.System.VirtualKey)188,
                (_, a) =>
                {
                    OpenSettings(SettingsSection.Repositories);
                    a.Handled = true;
                }
            )
        );
        ViewModel.RepositoryOpened += () =>
        {
            if (ViewModel.RepositoryPath is { } p)
                _historyRatio = _layouts.Get(p).HistoryRatio;
            ApplyMode(animate: false);
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, FocusSelectedCommit);
        };
        InitializeOperations();
        InitializeChanges();
        InitializeBranches();
        InitializeHealth();
        InitializeRecovery();
        InitializeCompare();
        InitializeWorkflow();
        InitializePanes();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "CentralGit.ico")); // taskbar, Alt+Tab

        var titleBar = AppWindow.TitleBar;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        InitializeTheme();

        FitToWorkArea();

        ViewModel.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(ShellViewModel.StatusIsError):
                    StatusLine.Foreground = ThemeService.ChromeBrush(
                        ViewModel.StatusIsError ? "RedTextBrush" : "TextSecondaryBrush"
                    );
                    break;
                case nameof(ShellViewModel.RepositoryName) or nameof(ShellViewModel.BranchName):
                    DispatcherQueue.TryEnqueue(UpdateTitleBarRegions);
                    break;
                case nameof(ShellViewModel.SelectedCommit) when ViewModel.SelectedCommit is { } row:
                    HistoryList.ScrollIntoView(row);
                    break;
                case nameof(ShellViewModel.Mode):
                    ApplyMode(animate: true);
                    break;
            }
        };

        ViewModel.Inspector.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InspectorViewModel.IsSplit))
                UpdateDiffToggle();
            if (e.PropertyName == nameof(InspectorViewModel.Details))
                UpdateFocusHeader();
            if (e.PropertyName == nameof(InspectorViewModel.DiffRows) && ViewModel.Inspector.DiffRows.Count > 0)
                DiffList.ScrollIntoView(ViewModel.Inspector.DiffRows[0], ScrollIntoViewAlignment.Leading);
        };
        ViewModel.Inspector.ScrollToRowRequested += index =>
        {
            var rows = ViewModel.Inspector.DiffRows;
            if (rows.Count > 0)
                DiffList.ScrollIntoView(rows[Math.Max(0, index - 3)], ScrollIntoViewAlignment.Leading);
        };

        HistorySplitter.DragDelta += OnSplitterDrag;
        HistorySplitter.DragCompleted += SaveLayout;
        Workspace.SizeChanged += (_, _) =>
        {
            if (_transition is null)
                ApplyMode(animate: false);
        };

        AppTitleBar.SizeChanged += (_, _) => UpdateTitleBarRegions();
        BranchButton.SizeChanged += (_, _) => UpdateTitleBarRegions();
        ChangesBadge.SizeChanged += (_, _) => UpdateTitleBarRegions();
        Activated += OnActivated;
        HistoryList.Loaded += (_, _) => HookHistoryScrolling();
        UpdateDiffToggle();
    }

    /// <summary>Loads the next page of history when the list is scrolled near its end.</summary>
    private void HookHistoryScrolling()
    {
        if (FindDescendant<ScrollViewer>(HistoryList) is not { } scroller)
            return;
        scroller.ViewChanged += async (_, _) =>
        {
            if (scroller.ScrollableHeight - scroller.VerticalOffset < 40 * 26)
                await ViewModel.LoadMoreHistoryAsync();
        };
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;
            if (FindDescendant<T>(child) is { } deeper)
                return deeper;
        }
        return null;
    }

    private void OnBackAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.GoBack();
        args.Handled = true;
    }

    private void OnForwardAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.GoForward();
        args.Handled = true;
    }

    private void OnFilterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Escape when ViewModel.FilterText.Length > 0:
                ViewModel.FilterText = "";
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Down:
                FocusSelectedCommit(); // from the filter straight into the results
                e.Handled = true;
                break;
        }
    }

    /// <summary>Designed at 1600x1000, but never larger than 92% of the monitor's work area; centered.</summary>
    private void FitToWorkArea()
    {
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min(1600, (int)(work.Width * 0.92));
        var height = Math.Min(1000, (int)(work.Height * 0.92));
        AppWindow.MoveAndResize(
            new RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height)
        );
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_initialized)
            return;
        _initialized = true;
        UpdateTitleBarRegions();
        Updates.Start();
        await ViewModel.InitializeAsync(_requestedPath);

        if (ViewModel.RepositoryPath is { } path)
        {
            _historyRatio = _layouts.Get(path).HistoryRatio;
            ApplyMode(animate: false);
        }

        // Keyboard-first: arrows move through history right away. Queued at low priority so the
        // row containers exist (and window activation has settled) before focus is placed.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, FocusSelectedCommit);
    }

    private void FocusSelectedCommit()
    {
        if (ViewModel.SelectedCommit is { } selected && HistoryList.ContainerFromItem(selected) is ListViewItem item)
            item.Focus(FocusState.Keyboard);
        else
            HistoryList.Focus(FocusState.Keyboard);
    }

    // --- Modes ---------------------------------------------------------------

    private double AvailableHeight => Math.Max(0, Workspace.ActualHeight - SplitterHeight);

    private (double History, double Sidebar, double Details) Targets() =>
        ViewModel.Mode switch
        {
            WorkspaceMode.Investigate => (
                Math.Min(InvestigateHistoryHeight, AvailableHeight - MinPaneHeight),
                _panes.Sidebar,
                _panes.Details
            ),
            WorkspaceMode.Focus => (0, 0, 0),
            _ => (
                Math.Clamp(
                    _historyRatio * AvailableHeight,
                    MinPaneHeight,
                    Math.Max(MinPaneHeight, AvailableHeight - MinPaneHeight)
                ),
                _panes.Sidebar,
                _panes.Details
            ),
        };

    /// <summary>
    /// Moves the history/inspector split, sidebar and details column to the current mode.
    /// GridLength can't be animated by the framework, so a short timer eases it (180 ms).
    /// </summary>
    private void ApplyMode(bool animate)
    {
        if (Workspace.ActualHeight <= 0)
            return;

        var focus = ViewModel.Mode == WorkspaceMode.Focus;
        SplitterRow.Height = new GridLength(focus ? 0 : SplitterHeight);
        HistorySplitter.Visibility = focus ? Visibility.Collapsed : Visibility.Visible;
        UpdatePaneHandles(focus);
        FocusGlyph.Glyph = focus ? "" : ""; // BackToWindow / FullScreen
        UpdateFocusHeader();

        var (toHistory, toSidebar, toDetails) = Targets();
        _transition?.Stop();
        _transition = null;

        if (!animate || !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            SetSizes(toHistory, toSidebar, toDetails);
            return;
        }

        var (fromHistory, fromSidebar, fromDetails) = (
            HistoryRow.ActualHeight,
            SidebarColumn.ActualWidth,
            DetailsColumn.ActualWidth
        );
        var started = DateTime.UtcNow;
        var timer = _transition = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(8);
        timer.Tick += (t, _) =>
        {
            var p = Math.Min(1, (DateTime.UtcNow - started) / TransitionDuration);
            var e = 1 - Math.Pow(1 - p, 3); // ease-out cubic
            SetSizes(
                fromHistory + (toHistory - fromHistory) * e,
                fromSidebar + (toSidebar - fromSidebar) * e,
                fromDetails + (toDetails - fromDetails) * e
            );
            if (p >= 1)
            {
                t.Stop();
                if (ReferenceEquals(_transition, t))
                    _transition = null;
            }
        };
        timer.Start();
    }

    private void SetSizes(double history, double sidebar, double details)
    {
        HistoryRow.Height = new GridLength(Math.Max(0, history));
        SidebarColumn.Width = new GridLength(Math.Max(0, sidebar));
        DetailsColumn.Width = new GridLength(Math.Max(0, details));
    }

    private void SetMode(WorkspaceMode mode)
    {
        if (mode != WorkspaceMode.Browse && !ViewModel.Inspector.HasDetails)
            return;
        ViewModel.Mode = mode;
    }

    private void OnSplitterDrag(double delta)
    {
        if (AvailableHeight <= 0)
            return;
        var height = Math.Clamp(HistoryRow.ActualHeight + delta, MinPaneHeight, AvailableHeight - MinPaneHeight);
        HistoryRow.Height = new GridLength(height);
        _historyRatio = height / AvailableHeight;
        if (ViewModel.Mode != WorkspaceMode.Browse)
            ViewModel.Mode = WorkspaceMode.Browse; // dragging the split is a browse gesture
    }

    private void SaveLayout()
    {
        if (ViewModel.RepositoryPath is { } path)
            _layouts.Set(path, new RepositoryLayout(_historyRatio, ViewModel.Inspector.IsSplit));
    }

    private void UpdateFocusHeader()
    {
        FocusCommitRun.Text =
            ViewModel.Mode == WorkspaceMode.Focus && ViewModel.Inspector.Details is { } d
                ? $"{d.ShortHash[..7]} · {d.Subject}   "
                : "";
    }

    // --- Input ---------------------------------------------------------------

    private void OnHistoryDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        SetMode(WorkspaceMode.Investigate);

    private void OnHistoryPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
            return;
        SetMode(ViewModel.Mode == WorkspaceMode.Browse ? WorkspaceMode.Investigate : WorkspaceMode.Focus);
        e.Handled = true;
    }

    private void OnEscapeAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Esc closes the innermost thing first: palette, switcher, then settings, then steps back a mode.
        if (ClosePalette())
        {
            FocusSelectedCommit();
            args.Handled = true;
            return;
        }
        if (SwitcherOverlay.Visibility == Visibility.Visible)
        {
            CloseSwitcher();
            args.Handled = true;
            return;
        }
        if (SettingsPage.Visibility == Visibility.Visible)
        {
            CloseSettings();
            args.Handled = true;
            return;
        }
        if (ViewModel.Mode == WorkspaceMode.Browse)
            return;
        ViewModel.Mode = ViewModel.Mode == WorkspaceMode.Focus ? WorkspaceMode.Investigate : WorkspaceMode.Browse;
        if (ViewModel.Mode == WorkspaceMode.Browse)
            FocusSelectedCommit();
        args.Handled = true;
    }

    private void OnFocusClick(object sender, RoutedEventArgs e) =>
        SetMode(ViewModel.Mode == WorkspaceMode.Focus ? WorkspaceMode.Investigate : WorkspaceMode.Focus);

    private void OnNextChangeAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.Inspector.MoveChange(+1);
        args.Handled = true;
    }

    private void OnPreviousChangeAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.Inspector.MoveChange(-1);
        args.Handled = true;
    }

    private void OnNextChange(object sender, RoutedEventArgs e) => ViewModel.Inspector.MoveChange(+1);

    private void OnPreviousChange(object sender, RoutedEventArgs e) => ViewModel.Inspector.MoveChange(-1);

    private void OnSplitClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Inspector.IsSplit = true;
        SaveLayout();
    }

    private void OnUnifiedClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Inspector.IsSplit = false;
        SaveLayout();
    }

    private void UpdateDiffToggle()
    {
        var split = ViewModel.Inspector.IsSplit;
        var on = ThemeService.ContentBrush("ElevatedBrush");
        var off = new SolidColorBrush(Colors.Transparent);
        SplitButton.Background = split ? on : off;
        UnifiedButton.Background = split ? off : on;
    }

    private void OnParentClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hash } && !ViewModel.SelectCommit(hash))
            ViewModel.Inspector.ShowCommitAsync(hash).ConfigureAwait(true); // outside the loaded page: inspect anyway
    }

    private void OnCopyHash(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Inspector.Details is not { } d)
            return;
        var package = new DataPackage();
        package.SetText(d.Hash);
        Clipboard.SetContent(package);
    }

    /// <summary>
    /// With a custom title bar the whole strip is a drag region; the controls in it must be
    /// carved out as pass-through, or clicks on them would drag the window instead.
    /// </summary>
    private void UpdateTitleBarRegions()
    {
        if (AppTitleBar.XamlRoot is not { } root)
            return;

        var scale = root.RasterizationScale;
        CaptionColumn.Width = new GridLength(AppWindow.TitleBar.RightInset / scale);

        var interactive = new FrameworkElement[] { RepoButton, BranchButton, ChangesBadge, CommandBox, SyncPanel };
        var rects = interactive
            .Where(e => e.ActualWidth > 0)
            .Select(e =>
            {
                var bounds = e.TransformToVisual(null).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
                return new RectInt32(
                    (int)Math.Round(bounds.X * scale),
                    (int)Math.Round(bounds.Y * scale),
                    (int)Math.Round(bounds.Width * scale),
                    (int)Math.Round(bounds.Height * scale)
                );
            })
            .ToArray();

        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, rects);
    }

    private void OnCommandPaletteAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        OpenPalette();
        args.Handled = true;
    }

    private void OnFilterAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        FilterBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }
}
