using CentralGit.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace CentralGit.App;

public sealed partial class MainWindow : Window
{
    private readonly string? _requestedPath;
    private bool _initialized;

    public ShellViewModel ViewModel { get; } = new();

    public MainWindow(string? requestedPath)
    {
        _requestedPath = requestedPath;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var titleBar = AppWindow.TitleBar;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 0xAD, 0xB1, 0xB9);
        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 0x2A, 0x2E, 0x35);
        titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 0xED, 0xEB, 0xE8);

        FitToWorkArea();

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.StatusIsError))
                StatusLine.Foreground = (Brush)Application.Current.Resources[ViewModel.StatusIsError ? "RedTextBrush" : "TextSecondaryBrush"];
            if (e.PropertyName is nameof(ShellViewModel.RepositoryName) or nameof(ShellViewModel.BranchName))
                DispatcherQueue.TryEnqueue(UpdateTitleBarRegions);
        };

        AppTitleBar.SizeChanged += (_, _) => UpdateTitleBarRegions();
        Activated += OnActivated;
    }

    /// <summary>Designed at 1600×1000, but never larger than 92% of the monitor's work area; centered.</summary>
    private void FitToWorkArea()
    {
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min(1600, (int)(work.Width * 0.92));
        var height = Math.Min(1000, (int)(work.Height * 0.92));
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_initialized)
            return;
        _initialized = true;
        UpdateTitleBarRegions();
        await ViewModel.InitializeAsync(_requestedPath);
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

        var interactive = new FrameworkElement[] { RepoButton, BranchButton, CommandBox, SyncPanel };
        var rects = interactive
            .Where(e => e.ActualWidth > 0)
            .Select(e =>
            {
                var bounds = e.TransformToVisual(null).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
                return new RectInt32(
                    (int)Math.Round(bounds.X * scale), (int)Math.Round(bounds.Y * scale),
                    (int)Math.Round(bounds.Width * scale), (int)Math.Round(bounds.Height * scale));
            })
            .ToArray();

        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, rects);
    }

    private void OnCommandPaletteAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        CommandBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void OnFilterAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        FilterBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }
}
