using CentralGit.App.Theming;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CentralGit.App.Controls;

/// <summary>
/// A thin drag handle (WinUI has no GridSplitter). Horizontal handles resize rows, vertical
/// ones resize columns. It only reports movement; the owner decides what to resize.
/// Idle it is a seam (or nothing); on hover a violet line and a grip fade in, and the line goes
/// solid while dragging. Double-click asks the owner to reset to the default size.
/// </summary>
public sealed partial class Splitter : Grid
{
    private static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(120);

    private double? _start;
    private bool _hover;
    private Orientation _orientation = Orientation.Horizontal;
    private bool _showLine = true;
    private bool _showGrip;
    private readonly Border _line = new();
    private readonly Border _accent = new() { Opacity = 0 };
    private readonly Border _grip = new() { Opacity = 0, CornerRadius = new CornerRadius(2) };

    public event Action<double>? DragDelta;
    public event Action? DragCompleted;
    public event Action? ResetRequested;

    public Splitter()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); // hit-testable
        _accent.OpacityTransition = new ScalarTransition { Duration = Fade };
        _grip.OpacityTransition = new ScalarTransition { Duration = Fade };
        Children.Add(_line);
        Children.Add(_accent);
        Children.Add(_grip);
        ApplyOrientation();
        Refresh();
        ActualThemeChanged += (_, _) => Refresh();

        PointerEntered += (_, _) =>
        {
            _hover = true;
            Refresh();
        };
        PointerExited += (_, _) =>
        {
            _hover = false;
            Refresh();
        };
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerCaptureLost += (_, _) => End();
        DoubleTapped += (_, e) =>
        {
            ResetRequested?.Invoke();
            e.Handled = true;
        };
    }

    /// <summary>Horizontal: a line across, dragged up and down. Vertical: a line down, dragged sideways.</summary>
    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            _orientation = value;
            ApplyOrientation();
        }
    }

    /// <summary>When false the handle is invisible until hovered (for edges that already draw a seam).</summary>
    public bool ShowLine
    {
        get => _showLine;
        set
        {
            _showLine = value;
            Refresh();
        }
    }

    /// <summary>Keep a faint grip visible at rest (for a handle that is a gap of its own, not an edge).</summary>
    public bool ShowGrip
    {
        get => _showGrip;
        set
        {
            _showGrip = value;
            Refresh();
        }
    }

    private void ApplyOrientation()
    {
        var vertical = _orientation == Orientation.Vertical;
        foreach (var line in new[] { _line, _accent })
        {
            line.Width = vertical ? (line == _accent ? 2 : 1) : double.NaN;
            line.Height = vertical ? double.NaN : (line == _accent ? 2 : 1);
            line.HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            line.VerticalAlignment = vertical ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        }
        _grip.Width = vertical ? 4 : 32;
        _grip.Height = vertical ? 32 : 4;
        _grip.HorizontalAlignment = HorizontalAlignment.Center;
        _grip.VerticalAlignment = VerticalAlignment.Center;
        ProtectedCursor = InputSystemCursor.Create(
            vertical ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeNorthSouth
        );
    }

    /// <summary>Rest, hover and drag, from the theme of wherever the handle sits.</summary>
    private void Refresh()
    {
        var dragging = _start is not null;
        var active = dragging || _hover;
        _line.Background = _showLine
            ? ThemeService.Brush("SeamBrush", this)
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _accent.Background = ThemeService.Brush(dragging ? "VioletBrush" : "SplitterHoverBrush", this);
        _accent.Opacity = active ? 1 : 0;
        _grip.Background = ThemeService.Brush(active ? "VioletBrush" : "SplitterGripBrush", this);
        _grip.Opacity = active || _showGrip ? 1 : 0;
    }

    private double Position(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(null).Position;
        return _orientation == Orientation.Vertical ? p.X : p.Y;
    }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        _start = Position(e);
        CapturePointer(e.Pointer);
        Refresh();
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_start is not { } start)
            return;
        var position = Position(e);
        DragDelta?.Invoke(position - start);
        _start = position;
        e.Handled = true;
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        ReleasePointerCapture(e.Pointer);
        End();
        e.Handled = true;
    }

    private void End()
    {
        if (_start is null)
            return;
        _start = null;
        Refresh();
        DragCompleted?.Invoke();
    }
}
