using CentralGit.App.ViewModels;
using CentralGit.Application.Git;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Graph;

namespace CentralGit.App.Graph;

/// <summary>
/// Turns pages of <c>git log --topo-order</c> into history rows. Lane layout, lane colors and
/// the walk along the current branch's first-parent line all carry over from page to page, so
/// the graph continues seamlessly when more history is loaded.
/// </summary>
public sealed class HistoryBuilder(string? headHash, string? headBranch, SyncSets? sync = null)
{
    private readonly GraphLayout _layout = new();
    private readonly GraphColorizer _colorizer = new();

    // Topo order lists children before parents, so the first-parent chain from HEAD can be
    // followed while streaming: the next commit on it is always still ahead of us.
    private readonly string? _head = headHash;
    private string? _nextOnCurrentBranch = headHash;

    public int Count { get; private set; }

    public IReadOnlyList<CommitRowViewModel> Add(IReadOnlyList<CommitInfo> commits)
    {
        var rows = new List<CommitRowViewModel>(commits.Count);
        foreach (var commit in commits)
        {
            var onCurrentBranch = commit.Hash == _nextOnCurrentBranch;
            if (onCurrentBranch)
                _nextOnCurrentBranch = commit.Parents.Count > 0 ? commit.Parents[0] : null;

            var graphRow = _layout.Add(commit);
            var (strokes, dot) = _colorizer.Colorize(graphRow, onCurrentBranch);
            rows.Add(
                new CommitRowViewModel(
                    commit,
                    graphRow.Lane,
                    strokes,
                    dot,
                    isHead: commit.Hash == _head,
                    refs: CommitRowViewModel.BuildRefs(commit, headBranch, dot),
                    sync: (sync ?? SyncSets.Empty).Of(commit.Hash)
                )
            );
        }
        Count += rows.Count;
        return rows;
    }
}
