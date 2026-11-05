using CentralGit.Domain.Commits;
using CentralGit.Domain.Graph;

namespace CentralGit.Domain.Tests;

public class GraphLayoutTests
{
    [Fact]
    public void Linear_history_stays_in_lane_zero()
    {
        var layout = new GraphLayout();
        var rows = new[] { layout.Add("c", ["b"]), layout.Add("b", ["a"]), layout.Add("a", []) };

        Assert.All(rows, r => Assert.Equal(0, r.Lane));
        Assert.Equal([new LaneSegment(0, 0)], rows[0].Lower);
        Assert.Equal([new LaneSegment(0, 0)], rows[1].Upper);
        Assert.Empty(rows[2].Lower);
    }

    [Fact]
    public void Merge_opens_a_side_lane_that_converges_at_the_fork_point()
    {
        //  M        merge of X into main
        //  |\
        //  B X
        //  |/
        //  A
        var layout = new GraphLayout();
        var m = layout.Add("M", ["B", "X"]);
        var b = layout.Add("B", ["A"]);
        var x = layout.Add("X", ["A"]);
        var a = layout.Add("A", []);

        Assert.Equal(0, m.Lane);
        Assert.Contains(new LaneSegment(0, 1), m.Lower);

        Assert.Equal(0, b.Lane);
        Assert.Contains(new LaneSegment(1, 1), b.Lower); // X's lane passes through B's row

        Assert.Equal(1, x.Lane);
        Assert.Contains(new LaneSegment(1, 0), x.Lower); // X joins main's lane, which already waits for A

        Assert.Equal(0, a.Lane);
        Assert.Equal([new LaneSegment(0, 0)], a.Upper); // both lines arrived in lane 0 before A
        Assert.Equal(1, a.Width);
    }

    [Fact]
    public void Two_branch_tips_get_separate_lanes_and_a_freed_lane_is_reused()
    {
        var layout = new GraphLayout();
        var tip1 = layout.Add("T1", ["P1"]);
        var tip2 = layout.Add("T2", ["Q1"]);
        var p1 = layout.Add("P1", []); // lane 0 ends here
        var tip3 = layout.Add("T3", ["R1"]);

        Assert.Equal(0, tip1.Lane);
        Assert.Equal(1, tip2.Lane);
        Assert.Equal(0, p1.Lane);
        Assert.Equal(0, tip3.Lane); // reuses the freed column instead of growing the graph
    }
}
