using CentralGit.App.Theming;
using CentralGit.Core.Graph;
using Windows.UI;

namespace CentralGit.App.Graph;

public readonly record struct GraphStroke(int From, int To, bool Upper, Color Color);

/// <summary>
/// Colors graph lanes by meaning: the current branch's first-parent line is violet,
/// everything else keeps the neutral color of the lane it was opened in.
/// Feed rows in order; state carries lane colors from one row to the next.
/// </summary>
public sealed class GraphColorizer
{
    private readonly List<Color?> _laneColors = [];

    public (IReadOnlyList<GraphStroke> Strokes, Color Dot) Colorize(GraphRow row, bool onCurrentBranch)
    {
        var strokes = new List<GraphStroke>(row.Upper.Count + row.Lower.Count);

        foreach (var s in row.Upper)
            strokes.Add(new GraphStroke(s.From, s.To, Upper: true, Get(s.From)));

        var dot = onCurrentBranch ? Palette.Violet : Get(row.Lane);

        foreach (var s in row.Lower)
            if (s.From != row.Lane)
                strokes.Add(new GraphStroke(s.From, s.To, Upper: false, Get(s.From)));

        if (row.Parents.Count == 0 || !row.Parents[0].Opened || row.Parents[0].Lane != row.Lane)
            Set(row.Lane, null);

        foreach (var parent in row.Parents)
        {
            var color = parent.IsFirstParent ? dot : parent.Opened ? Palette.LaneDefault(parent.Lane) : Get(parent.Lane);
            strokes.Add(new GraphStroke(row.Lane, parent.Lane, Upper: false, color));
            if (parent.Opened)
                Set(parent.Lane, color);
        }

        return (strokes, dot);
    }

    private Color Get(int lane) =>
        lane < _laneColors.Count && _laneColors[lane] is { } c ? c : Palette.LaneDefault(lane);

    private void Set(int lane, Color? color)
    {
        while (_laneColors.Count <= lane)
            _laneColors.Add(null);
        _laneColors[lane] = color;
    }
}
