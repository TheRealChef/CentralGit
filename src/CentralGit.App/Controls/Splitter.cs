using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CentralGit.App.Controls;

/// <summary>
/// A thin drag handle (WinUI has no GridSplitter). Horizontal handles resize rows, vertical
/// ones resize columns. It only reports movement; the owner decides what to resize. The
/// seam lights up violet on hover; double-click asks the owner to reset to the default size.
/// </summary>
public sealed partial class Splitter : Grid
{
    private double? _start;
    private Orientation _orientation = Orientation.Horizontal;
    private readonly Border _line = new();

    public event Action<double>? DragDelta;
    public event Action? DragCompleted;
    public event Action? ResetRequested;

    public Splitter()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); // hit-testable
        Children.Add(_line);
        ApplyOrientation();
        Idle();

        PointerEntered += (_, _) => _line.Background = (Brush)Application.Current.Resources["VioletBrush"];
        PointerExited += (_, _) => { if (_start is null) Idle(); };
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerCaptureLost += (_, _) => End();
        DoubleTapped += (_, e) => { ResetRequested?.Invoke(); e.Handled = true; };
    }

    /// <summary>Horizontal: a line across, dragged up and down. Vertical: a line down, dragged sideways.</summary>
    public Orientation Orientation
    {
        get => _orientation;
        set { _orientation = value; ApplyOrientation(); }
    }

    /// <summary>When false the handle is invisible until hovered (for edges that already draw a seam).</summary>
    public bool ShowLine { get => _showLine; set { _showLine = value; Idle(); } }

    private bool _showLine = true;

    private void ApplyOrientation()
    {
        var vertical = _orientation == Orientation.Vertical;
        _line.Width = vertical ? 1 : double.NaN;
        _line.Height = vertical ? double.NaN : 1;
        _line.HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        _line.VerticalAlignment = vertical ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        ProtectedCursor = InputSystemCursor.Create(vertical ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeNorthSouth);
    }

    private void Idle() =>
        _line.Background = _showLine ? (Brush)Application.Current.Resources["SeamBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    private double Position(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(null).Position;
        return _orientation == Orientation.Vertical ? p.X : p.Y;
    }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        _start = Position(e);
        CapturePointer(e.Pointer);
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
        Idle();
        DragCompleted?.Invoke();
    }
}
