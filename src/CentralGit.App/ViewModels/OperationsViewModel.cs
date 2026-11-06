using System.Collections.ObjectModel;
using CentralGit.App.Theming;
using CentralGit.Application.Operations;
using CentralGit.Domain.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Windows.UI;

namespace CentralGit.App.ViewModels;

public enum OperationState
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>A button offered after a failure ("Pull", "Rebase", "Publish branch", …).</summary>
public sealed record OperationAction(string Label, string Kind, bool IsDestructive = false)
{
    public Color Background => IsDestructive ? Palette.WithAlpha(Palette.Chrome.Red, 0.16) : Palette.Chrome.Raised;
    public Color Foreground => IsDestructive ? Palette.Chrome.RedText : Palette.Chrome.TextPrimary;
}

/// <summary>One fetch / pull / push, from start to finish, with everything needed to understand it.</summary>
public sealed partial class OperationViewModel : ObservableObject
{
    private OperationState _state = OperationState.Running;
    private string _phase = "Starting…";
    private double _percent;
    private bool _isIndeterminate = true;
    private string _duration = "";
    private string _explanationTitle = "";
    private string _explanationDetail = "";
    private string _output = "";
    private IReadOnlyList<OperationAction> _actions = [];
    internal readonly CancellationTokenSource Cancellation = new();

    public OperationViewModel(string kind, string title, string commandLine, bool isBackground)
    {
        Kind = kind;
        Title = title;
        CommandLine = commandLine;
        IsBackground = isBackground;
    }

    /// <summary>fetch, pull or push — what "Try again" re-runs.</summary>
    public string Kind { get; }
    public string Title { get; }
    public string CommandLine { get; }
    public bool IsBackground { get; }

    /// <summary>What the operation was aimed at (a branch name), for follow-up actions.</summary>
    public string? Target { get; set; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

    public OperationState State
    {
        get => _state;
        set
        {
            if (!SetProperty(ref _state, value))
                return;
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(Glyph));
            OnPropertyChanged(nameof(StateColor));
        }
    }

    public bool IsRunning => _state == OperationState.Running;
    public bool IsFailed => _state == OperationState.Failed;

    public string Glyph =>
        _state switch
        {
            OperationState.Running => "", // sync
            OperationState.Succeeded => "", // check
            OperationState.Failed => "", // error
            _ => "", // cancel
        };

    public Color StateColor =>
        _state switch
        {
            OperationState.Running => Palette.Chrome.Teal,
            OperationState.Succeeded => Palette.Chrome.Green,
            OperationState.Failed => Palette.Chrome.Red,
            _ => Palette.Chrome.TextMuted,
        };

    public string Phase
    {
        get => _phase;
        set => SetProperty(ref _phase, value);
    }
    public double Percent
    {
        get => _percent;
        set => SetProperty(ref _percent, value);
    }
    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set => SetProperty(ref _isIndeterminate, value);
    }
    public string Duration
    {
        get => _duration;
        set
        {
            if (SetProperty(ref _duration, value))
                OnPropertyChanged(nameof(DurationSuffix));
        }
    }

    public string DurationSuffix => _duration.Length > 0 ? " · " + _duration : "";
    public string StartedText => StartedAt.ToString("HH:mm:ss");
    public string BackgroundSuffix => IsBackground ? "  background" : "";
    public string ExplanationTitle
    {
        get => _explanationTitle;
        set => SetProperty(ref _explanationTitle, value);
    }
    public string ExplanationDetail
    {
        get => _explanationDetail;
        set => SetProperty(ref _explanationDetail, value);
    }

    public string Output
    {
        get => _output;
        set
        {
            if (SetProperty(ref _output, value))
                OnPropertyChanged(nameof(HasOutput));
        }
    }

    public bool HasOutput => _output.Length > 0;

    public IReadOnlyList<OperationAction> Actions
    {
        get => _actions;
        set
        {
            if (SetProperty(ref _actions, value))
                OnPropertyChanged(nameof(HasActions));
        }
    }

    public bool HasActions => _actions.Count > 0;
}

/// <summary>
/// The Operations Center: runs remote operations one at a time per window, streams their
/// progress, keeps the last few with full diagnostics, and explains failures.
/// </summary>
public sealed partial class OperationsViewModel : ObservableObject
{
    private const int Keep = 20;
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private OperationViewModel? _current;
    private OperationViewModel? _latest;

    public ObservableCollection<OperationViewModel> Recent { get; } = [];

    /// <summary>The running operation, if any.</summary>
    public OperationViewModel? Current
    {
        get => _current;
        private set
        {
            if (!SetProperty(ref _current, value))
                return;
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsIdle));
        }
    }

    /// <summary>What the status bar shows: the running operation, else the latest one.</summary>
    public OperationViewModel? Latest
    {
        get => _latest;
        private set => SetProperty(ref _latest, value);
    }

    public bool IsBusy => _current is not null;
    public bool IsIdle => _current is null;

    public bool HasRecent => Recent.Count > 0;
    public bool HasNoRecent => Recent.Count == 0;

    public async Task<(OperationOutcome Result, OperationViewModel Operation)> RunAsync(
        string kind,
        string title,
        IPreparedOperation operation,
        bool background = false
    )
    {
        var op = new OperationViewModel(kind, title, operation.CommandLine, background);
        Recent.Insert(0, op);
        while (Recent.Count > Keep)
            Recent.RemoveAt(Recent.Count - 1);
        OnPropertyChanged(nameof(HasRecent));
        OnPropertyChanged(nameof(HasNoRecent));
        Current = op;
        Latest = op;

        // Progress arrives on a background thread many times a second; forward at most ~15/s.
        var lastUpdate = DateTime.MinValue;
        void OnProgress(GitProgress p)
        {
            var now = DateTime.UtcNow;
            if ((now - lastUpdate).TotalMilliseconds < 66)
                return;
            lastUpdate = now;
            _ui.TryEnqueue(() =>
            {
                if (!op.IsRunning)
                    return; // a late progress line must not overwrite the final state
                op.Phase = p.Percent is { } pct ? $"{p.Phase} {pct}%" : p.Phase;
                op.IsIndeterminate = p.Percent is null;
                if (p.Percent is { } value)
                    op.Percent = value;
            });
        }

        var outcome = await Task.Run(() => operation.RunAsync(OnProgress, op.Cancellation.Token));
        var result = outcome.Result;
        if (outcome.Cancelled)
            op.State = OperationState.Cancelled;

        op.Duration =
            result.Duration.TotalSeconds < 10
                ? $"{result.Duration.TotalSeconds:0.0} s"
                : $"{result.Duration.TotalSeconds:0} s";
        op.Output = (result.StandardError + "\n" + result.StandardOutput).Trim();
        op.IsIndeterminate = false;
        op.Percent = 100;

        if (op.State != OperationState.Cancelled)
        {
            if (result.Succeeded)
            {
                op.State = OperationState.Succeeded;
                op.Phase = "Done";
            }
            else
            {
                var explanation =
                    outcome.Failure
                    ?? new OperationExplanation(
                        "Git reported an error",
                        $"git exited with code {result.ExitCode}.",
                        SuggestedFix.None
                    );
                op.ExplanationTitle = explanation.Title;
                op.ExplanationDetail = explanation.Detail;
                op.Phase = explanation.Title;
                op.Actions = ActionsFor(explanation.Fix, kind);
                op.State = OperationState.Failed;
            }
        }

        Current = null;
        return (outcome, op);
    }

    public void Cancel() => _current?.Cancellation.Cancel();

    private static IReadOnlyList<OperationAction> ActionsFor(SuggestedFix fix, string kind) =>
        fix switch
        {
            SuggestedFix.CommitOrStash when kind == "switch" => [new("Stash changes and switch", "stash-switch")],
            SuggestedFix.CommitOrStash => [new("Stash my changes", "stash")],
            SuggestedFix.ForceDelete => [new("Delete anyway…", "force-delete", IsDestructive: true)],
            SuggestedFix.PullFirst when kind == "push" =>
            [
                new("Pull", "pull"),
                new("Force push (with lease)…", "force-push", IsDestructive: true),
            ],
            SuggestedFix.PullFirst => [new("Fetch", "fetch")],
            SuggestedFix.ChooseReconcile => [new("Rebase my commits", "pull-rebase"), new("Merge", "pull-merge")],
            SuggestedFix.PublishBranch => [new("Publish branch", "publish")],
            SuggestedFix.SignIn => [new("Open Accounts", "accounts")],
            SuggestedFix.CheckConnection => [new("Try again", "retry")],
            SuggestedFix.ResolveConflicts => [new("Resolve conflicts", "resolve")],
            _ => [],
        };
}
