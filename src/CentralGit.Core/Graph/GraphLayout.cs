using CentralGit.Core.Git;

namespace CentralGit.Core.Graph;

/// <summary>A line inside one half of a row: from a lane at the half's top edge to a lane at its bottom edge.</summary>
public readonly record struct LaneSegment(int From, int To);

/// <summary>
/// Everything needed to draw one row of the commit graph without looking at other rows.
/// <see cref="Upper"/> runs from the row's top edge to its center, <see cref="Lower"/> from center to bottom edge.
/// Lanes keep their column for their whole life, so pass-through lines are always straight.
/// </summary>
public sealed record GraphRow(
    int Lane,
    int Width,
    IReadOnlyList<LaneSegment> Upper,
    IReadOnlyList<LaneSegment> Lower,
    IReadOnlyList<ParentLink> Parents);

/// <summary>Where a parent continues: <see cref="Opened"/> is false when it joins a lane that already waits for it.</summary>
public readonly record struct ParentLink(int Lane, bool Opened, bool IsFirstParent);

/// <summary>
/// Assigns commits (in topological order, newest first) to lanes. Runs in O(commits × lanes) and is
/// incremental: feed it pages as they stream out of git log.
/// </summary>
public sealed class GraphLayout
{
    private readonly List<string?> _lanes = [];

    public GraphRow Add(CommitInfo commit) => Add(commit.Hash, commit.Parents);

    public GraphRow Add(string hash, IReadOnlyList<string> parents)
    {
        var before = _lanes.ToArray();
        var upper = new List<LaneSegment>();
        var lower = new List<LaneSegment>();

        // Every lane waiting for this commit converges into it; the first one becomes its lane.
        var incoming = new List<int>();
        for (var i = 0; i < before.Length; i++)
            if (before[i] == hash)
                incoming.Add(i);

        var lane = incoming.Count > 0 ? incoming[0] : FreeLane();

        foreach (var i in incoming)
        {
            upper.Add(new LaneSegment(i, lane));
            _lanes[i] = null;
        }

        for (var i = 0; i < before.Length; i++)
        {
            if (before[i] is not null && before[i] != hash)
            {
                upper.Add(new LaneSegment(i, i));
                lower.Add(new LaneSegment(i, i));
            }
        }

        var links = new List<ParentLink>(parents.Count);
        for (var p = 0; p < parents.Count; p++)
        {
            var parent = parents[p];
            var existing = _lanes.IndexOf(parent);
            if (existing >= 0)
            {
                // Someone already waits for this parent: join their lane instead of opening a new one.
                lower.Add(new LaneSegment(lane, existing));
                links.Add(new ParentLink(existing, Opened: false, IsFirstParent: p == 0));
                continue;
            }

            var target = p == 0 && _lanes[lane] is null ? lane : FreeLane(exclude: lane);
            _lanes[target] = parent;
            lower.Add(new LaneSegment(lane, target));
            links.Add(new ParentLink(target, Opened: true, IsFirstParent: p == 0));
        }

        while (_lanes.Count > 0 && _lanes[^1] is null)
            _lanes.RemoveAt(_lanes.Count - 1);

        var width = Math.Max(Math.Max(before.Length, _lanes.Count), lane + 1);
        return new GraphRow(lane, width, upper, lower, links);
    }

    private int FreeLane(int exclude = -1)
    {
        for (var i = 0; i < _lanes.Count; i++)
            if (i != exclude && _lanes[i] is null)
                return i;

        _lanes.Add(null);
        return _lanes.Count - 1;
    }
}
