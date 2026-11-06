using CentralGit.App.Theming;
using CentralGit.Domain.Hosting;
using Windows.UI;

namespace CentralGit.App.ViewModels;

/// <summary>GitHub: the current branch's pull request, CI checks, and opening a new pull request.</summary>
public sealed partial class ShellViewModel
{
    private static readonly TimeSpan HostingRefresh = TimeSpan.FromSeconds(60);

    private PullRequest? _pullRequest;
    private CheckState _headChecks;
    private CheckState _selectedChecks;
    private string? _hostingKey;
    private DateTimeOffset _hostingAt;
    private readonly Dictionary<string, CheckState> _checksCache = [];

    public bool HasPullRequest => _pullRequest is not null;

    public string PullRequestText => _pullRequest is { } pr ? $"#{pr.Number}{(pr.IsDraft ? " draft" : "")}" : "";

    public string PullRequestTooltip =>
        _pullRequest is { } pr
            ? $"Pull request #{pr.Number}: {pr.Title}\ninto {pr.BaseBranch} · by {pr.Author}\n{ChecksDescription(_headChecks)}\nClick to open it on GitHub."
            : "";

    public bool HasHeadChecks => _headChecks != CheckState.None;
    public Color HeadChecksColor => ChecksColor(_headChecks, Palette.Chrome);
    public string HeadChecksGlyph => ChecksGlyph(_headChecks);

    public bool HasSelectedChecks => _selectedChecks != CheckState.None;
    public string SelectedChecksText => ChecksDescription(_selectedChecks);
    public Color SelectedChecksColor => ChecksColor(_selectedChecks, Palette.Content);

    /// <summary>Asks GitHub again when the branch or HEAD moved, or a minute has passed.</summary>
    private async Task RefreshHostingAsync(bool force = false)
    {
        if (_session is not { } session)
            return;
        var branch = CurrentBranch;
        var head = LoadedCommits.FirstOrDefault(r => r.IsHead)?.Commit.Hash;
        var key = $"{session.Path}|{branch}|{head}";
        var now = DateTimeOffset.Now;
        if (!force && key == _hostingKey && now - _hostingAt < HostingRefresh)
            return;
        _hostingKey = key;
        _hostingAt = now;
        try
        {
            var pr = branch is null ? null : await Task.Run(() => session.Hosting.FindPullRequestAsync(branch));
            var checks = head is null ? CheckState.None : await Task.Run(() => session.Hosting.GetChecksAsync(head));
            if (_session != session)
                return;
            _pullRequest = pr;
            _headChecks = checks;
            if (head is not null && checks is CheckState.Success or CheckState.Failure)
                _checksCache[head] = checks;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        OnPropertyChanged(nameof(HasPullRequest));
        OnPropertyChanged(nameof(PullRequestText));
        OnPropertyChanged(nameof(PullRequestTooltip));
        OnPropertyChanged(nameof(HasHeadChecks));
        OnPropertyChanged(nameof(HeadChecksColor));
        OnPropertyChanged(nameof(HeadChecksGlyph));
    }

    private async Task LoadSelectedChecksAsync(string? hash)
    {
        SetSelectedChecks(hash is not null && _checksCache.TryGetValue(hash, out var known) ? known : CheckState.None);
        if (hash is null || _session is not { } session || _checksCache.ContainsKey(hash))
            return;
        var state = await Task.Run(() => session.Hosting.GetChecksAsync(hash));
        if (state is CheckState.Success or CheckState.Failure)
            _checksCache[hash] = state;
        if (_selectedCommit?.Commit.Hash == hash)
            SetSelectedChecks(state);
    }

    private void SetSelectedChecks(CheckState state)
    {
        _selectedChecks = state;
        OnPropertyChanged(nameof(HasSelectedChecks));
        OnPropertyChanged(nameof(SelectedChecksText));
        OnPropertyChanged(nameof(SelectedChecksColor));
    }

    public void OpenPullRequest()
    {
        if (_pullRequest is { } pr)
            OpenUrl(pr.Url);
    }

    /// <summary>GitHub's new-pull-request page for <paramref name="head"/> into <paramref name="base"/>.</summary>
    public async Task CreatePullRequestAsync(string head, string @base)
    {
        if (_session is not { } session)
            return;
        if (await session.Hosting.GetHostedAsync() is not { } hosted)
        {
            SetStatus(
                "This repository's remote isn't on GitHub, so there's no pull request page to open.",
                error: true
            );
            return;
        }
        OpenUrl(hosted.CompareUrl(@base, head));
    }

    private void OpenUrl(string url)
    {
        if (_external.OpenUrl(url) is { } problem)
            SetStatus(problem, error: true);
    }

    private static string ChecksDescription(CheckState state) =>
        state switch
        {
            CheckState.Success => "Checks passed",
            CheckState.Failure => "Checks failed",
            CheckState.Pending => "Checks running",
            _ => "No checks",
        };

    private static string ChecksGlyph(CheckState state) =>
        state switch
        {
            CheckState.Success => "",
            CheckState.Failure => "",
            _ => "",
        };

    private static Color ChecksColor(CheckState state, ThemeColors colors) =>
        state switch
        {
            CheckState.Success => colors.Green,
            CheckState.Failure => colors.Red,
            CheckState.Pending => colors.Orange,
            _ => colors.TextMuted,
        };
}
