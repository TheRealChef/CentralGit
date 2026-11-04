using CentralGit.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace CentralGit.App.Graph;

/// <summary>
/// Draws one row of the commit graph. Rows are self-contained (see GraphRow), so this works
/// inside a virtualized list. A Win2D renderer for the whole viewport can replace it later
/// without touching the layout code.
/// </summary>
public sealed partial class GraphCell : Canvas
{
    public const double RowHeight = 26;
    public const double LaneWidth = 14;
    public const double Inset = 12;

    private static readonly Dictionary<Color, SolidColorBrush> Brushes = [];

    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(
        nameof(Row), typeof(CommitRowViewModel), typeof(GraphCell),
        new PropertyMetadata(null, (d, _) => ((GraphCell)d).Rebuild()));

    public CommitRowViewModel? Row
    {
        get => (CommitRowViewModel?)GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    public GraphCell()
    {
        Height = RowHeight;
        IsHitTestVisible = false;
    }

    private static double X(int lane) => Inset + lane * LaneWidth;

    private void Rebuild()
    {
        Children.Clear();
        if (Row is not { } row)
            return;

        foreach (var stroke in row.Strokes)
            Children.Add(CreateStroke(stroke));

        var cx = X(row.Lane);
        const double cy = RowHeight / 2;

        if (row.IsHead)
            Children.Add(Circle(cx, cy, 7.5, fill: null, stroke: row.DotColor, thickness: 1.5));

        var radius = row.IsMerge ? 3.5 : 4.5;
        Children.Add(Circle(cx, cy, radius, fill: row.DotColor, stroke: Theming.Palette.Surface, thickness: 2));
    }

    private static Microsoft.UI.Xaml.Shapes.Path CreateStroke(GraphStroke s)
    {
        var (y0, y1) = s.Upper ? (0d, RowHeight / 2) : (RowHeight / 2, RowHeight);
        var (x0, x1) = (X(s.From), X(s.To));

        var figure = new PathFigure { StartPoint = new Point(x0, y0) };
        if (s.From == s.To)
        {
            figure.Segments.Add(new LineSegment { Point = new Point(x1, y1) });
        }
        else
        {
            var mid = (y0 + y1) / 2;
            figure.Segments.Add(new BezierSegment
            {
                Point1 = new Point(x0, mid),
                Point2 = new Point(x1, mid),
                Point3 = new Point(x1, y1),
            });
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = Brush(s.Color), StrokeThickness = 2 };
    }

    private static Ellipse Circle(double cx, double cy, double r, Color? fill, Color stroke, double thickness)
    {
        var e = new Ellipse
        {
            Width = r * 2,
            Height = r * 2,
            Fill = fill is { } f ? Brush(f) : null,
            Stroke = Brush(stroke),
            StrokeThickness = thickness,
        };
        SetLeft(e, cx - r);
        SetTop(e, cy - r);
        return e;
    }

    private static SolidColorBrush Brush(Color c)
    {
        if (!Brushes.TryGetValue(c, out var brush))
            Brushes[c] = brush = new SolidColorBrush(c);
        return brush;
    }
}
