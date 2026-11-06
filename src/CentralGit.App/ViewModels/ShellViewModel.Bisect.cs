using CentralGit.Domain.Workflow;

namespace CentralGit.App.ViewModels;

/// <summary>
/// Bisect from History: mark a bad and a good commit, then answer good/bad for each commit git
/// checks out until the first bad one is found.
/// </summary>
public sealed partial class ShellViewModel
{
    private BisectState _bisect = BisectState.Inactive;

    public bool HasBisect => _bisect.IsActive;
    public bool IsBisectFound => _bisect.FirstBad is not null;
    public bool IsBisectTesting => _bisect.IsActive && _bisect.HasBothEnds && _bisect.FirstBad is null;
    public bool IsBisectWaiting => _bisect.IsActive && !_bisect.HasBothEnds;

    public string BisectTitle =>
        !_bisect.IsActive ? ""
        : _bisect.FirstBad is { } culprit ? $"Found it: {Describe(culprit)} is the first bad commit"
        : !_bisect.HasBothEnds
            ? _bisect.Bad is null ? "Bisecting: right-click a commit that has the problem and mark it bad"
                : "Bisecting: right-click a commit from before the problem and mark it good"
        : $"Bisecting: test {Describe(_bisect.Current)}";

    public string BisectDetail =>
        !_bisect.IsActive || _bisect.FirstBad is not null ? ""
        : !_bisect.HasBothEnds ? "Git then checks out commits between them for you to try."
        : $"Check it out (it's in your working tree now) and say whether it has the problem. {_bisect.Candidates} commit{(_bisect.Candidates == 1 ? "" : "s")} left, about {_bisect.StepsLeft} more step{(_bisect.StepsLeft == 1 ? "" : "s")}.";

    private string Describe(string? hash)
    {
        if (hash is null)
            return "HEAD";
        var row = LoadedCommits.FirstOrDefault(r => r.Commit.Hash == hash);
        return row is null ? hash[..7] : $"{row.ShortHash} “{row.Subject}”";
    }

    private async Task RefreshBisectAsync()
    {
        if (_session is not { } session)
            return;
        try
        {
            var state = await Task.Run(() => session.Actions.GetBisectAsync());
            if (_session != session)
                return;
            _bisect = state;
        }
        catch (Application.Git.GitException)
        {
            _bisect = BisectState.Inactive;
        }
        OnPropertyChanged(nameof(HasBisect));
        OnPropertyChanged(nameof(IsBisectFound));
        OnPropertyChanged(nameof(IsBisectTesting));
        OnPropertyChanged(nameof(IsBisectWaiting));
        OnPropertyChanged(nameof(BisectTitle));
        OnPropertyChanged(nameof(BisectDetail));
    }

    /// <summary>Marks a commit (HEAD when null: the one being tested); the first mark starts the bisect.</summary>
    public async Task MarkBisectAsync(CommitRowViewModel? row, BisectMark mark)
    {
        if (_session is not { } session || Operations.IsBusy)
            return;
        if (!_bisect.IsActive)
        {
            if (Changes.HasStaged || Changes.HasUnstaged)
            {
                SetStatus("Commit or stash your changes first: bisect checks out other commits.", error: true);
                return;
            }
            var (started, startOp) = await Operations.RunAsync("bisect", "Start bisect", session.Actions.BisectStart());
            await FinishAsync(started, startOp, () => "Bisect started");
            if (!started.Succeeded)
                return;
        }
        var what = row is null ? "the tested commit" : row.ShortHash;
        var label = mark switch
        {
            BisectMark.Good => "good",
            BisectMark.Bad => "bad",
            _ => "skipped",
        };
        var (result, op) = await Operations.RunAsync(
            "bisect",
            $"Mark {what} {label}",
            session.Actions.BisectMark(row?.Commit.Hash, mark)
        );
        await FinishAsync(result, op, () => $"Marked {what} {label}");
        await RefreshBisectAsync();
        if (_bisect.FirstBad is { } culprit)
            SelectCommit(culprit);
        else if (_bisect.HasBothEnds && _bisect.Current is { } current)
            SelectCommit(current);
    }

    public async Task EndBisectAsync()
    {
        if (_session is not { } session || Operations.IsBusy || !_bisect.IsActive)
            return;
        var culprit = _bisect.FirstBad;
        var (result, op) = await Operations.RunAsync("bisect", "End bisect", session.Actions.BisectReset());
        await FinishAsync(result, op, () => "Bisect ended; back where you started");
        await RefreshBisectAsync();
        if (culprit is not null)
            SelectCommit(culprit);
    }

    public void ShowBisectCulprit()
    {
        if (_bisect.FirstBad is { } culprit && !SelectCommit(culprit))
            ReportNotLoaded(culprit[..7]);
    }
}
