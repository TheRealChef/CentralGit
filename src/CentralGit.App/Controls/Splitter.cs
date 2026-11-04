using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CentralGit.App.Controls;

/// <summary>
/// A thin horizontal drag handle (WinUI has no GridSplitter). It only reports movement;
/// the owner decides what to resize, which keeps mode transitions in one place.
/// </summary>
public sealed partial class Splitter : Grid
{
    private double? _startY;
    private readonly Border _line = new() { Height = 1, VerticalAlignment = VerticalAlignment.Center };

    public event Action<double>? DragDelta;
    public event Action? DragCompleted;

    public Splitter()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); // hit-testable
        _line.Background = (Brush)Application.Current.Resources["SeamBrush"];
        Children.Add(_line);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);

        PointerEntered += (_, _) => _line.Background = (Brush)Application.Current.Resources["VioletBrush"];
        PointerExited += (_, _) => { if (_startY is null) _line.Background = (Brush)Application.Current.Resources["SeamBrush"]; };
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerCaptureLost += (_, _) => End();
    }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        _startY = e.GetCurrentPoint(null).Position.Y;
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_startY is not { } start)
            return;
        var y = e.GetCurrentPoint(null).Position.Y;
        DragDelta?.Invoke(y - start);
        _startY = y;
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
        if (_startY is null)
            return;
        _startY = null;
        _line.Background = (Brush)Application.Current.Resources["SeamBrush"];
        DragCompleted?.Invoke();
    }
}
