using CentralGit.Application.Git;
using CentralGit.Domain.Branches;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;

namespace CentralGit.App.ViewModels;

/// <summary>The History header's sync banner: what's incoming, what's only on this PC, or "in sync".</summary>
public sealed partial class ShellViewModel
{
    private string _incomingBannerText = "";
    private string _outgoingBannerText = "";
    private string _syncBannerText = "";
    private bool _isPublished = true;

    public string IncomingBannerText
    {
        get => _incomingBannerText;
        private set
        {
            if (SetProperty(ref _incomingBannerText, value))
                OnPropertyChanged(nameof(HasIncomingBanner));
        }
    }
    public string OutgoingBannerText
    {
        get => _outgoingBannerText;
        private set
        {
            if (SetProperty(ref _outgoingBannerText, value))
                OnPropertyChanged(nameof(HasOutgoingBanner));
        }
    }

    /// <summary>"In sync with origin/main" or "Not published"; empty while there's something to show above.</summary>
    public string SyncBannerText
    {
        get => _syncBannerText;
        private set
        {
            if (SetProperty(ref _syncBannerText, value))
                OnPropertyChanged(nameof(HasSyncBanner));
        }
    }

    public bool HasIncomingBanner => IncomingBannerText.Length > 0;
    public bool HasOutgoingBanner => OutgoingBannerText.Length > 0;
    public bool HasSyncBanner => SyncBannerText.Length > 0;
    public bool IsPublished
    {
        get => _isPublished;
        private set
        {
            if (SetProperty(ref _isPublished, value))
            {
                OnPropertyChanged(nameof(IsUnpublished));
                OnPropertyChanged(nameof(SyncBannerColor));
            }
        }
    }
    public bool IsUnpublished => !IsPublished;
    public Windows.UI.Color SyncBannerColor => IsPublished ? Theming.Palette.GreenText : Theming.Palette.TextMuted;

    private void UpdateSyncBanner(BranchStatus branch)
    {
        IsPublished = branch.Upstream is not null || branch.IsDetached;
        if (branch.IsDetached)
        {
            IncomingBannerText = OutgoingBannerText = "";
            SyncBannerText = "";
            return;
        }
        if (branch.Upstream is not { } upstream)
        {
            IncomingBannerText = OutgoingBannerText = "";
            SyncBannerText = $"{branch.Head} is only on this PC — push to publish it";
            return;
        }

        IncomingBannerText =
            branch.Behind > 0 ? $"↓ {Commits(branch.Behind)} incoming from {upstream} · not in your branch yet" : "";
        OutgoingBannerText =
            branch.Ahead > 0 ? $"↑ {Commits(branch.Ahead)} only on this PC · not on {upstream} yet" : "";
        SyncBannerText =
            branch.Ahead == 0 && branch.Behind == 0
                ? $"✓ In sync with {upstream} — everything you committed is on the remote"
                : "";

        static string Commits(int n) => n == 1 ? "1 commit" : $"{n} commits";
    }
}
