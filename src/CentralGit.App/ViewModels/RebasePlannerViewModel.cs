using System.Collections.ObjectModel;
using CentralGit.App.Theming;
using CentralGit.Domain.Workflow;
using CommunityToolkit.Mvvm.ComponentModel;
using Windows.UI;

namespace CentralGit.App.ViewModels;

/// <summary>One commit in the interactive-rebase planner.</summary>
public sealed partial class RebaseStepViewModel : ObservableObject
{
    private RebaseAction _action;
    private string _message;

    public RebaseStepViewModel(RebaseStep step)
    {
        Step = step;
        _message = step.Subject;
    }

    public RebaseStep Step { get; }
    public string ShortHash => Step.Hash[..7];
    public string Subject => Step.Subject;

    /// <summary>Order matches the action picker: Pick, Reword, Squash, Fixup, Drop.</summary>
    public static IReadOnlyList<string> ActionNames { get; } =
    ["Keep", "Reword", "Squash into above", "Fixup into above (drop message)", "Drop"];

    public int ActionIndex
    {
        get => (int)_action;
        set
        {
            var action = (RebaseAction)Math.Clamp(value, 0, 4);
            if (!SetProperty(ref _action, action, nameof(Action)))
                return;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsReword));
            OnPropertyChanged(nameof(RowOpacity));
            OnPropertyChanged(nameof(AccentColor));
        }
    }

    public RebaseAction Action => _action;

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public bool IsReword => _action == RebaseAction.Reword;
    public double RowOpacity => _action == RebaseAction.Drop ? 0.45 : 1;

    /// <summary>Left edge colour: violet for rewritten, red for dropped, muted for kept.</summary>
    public Color AccentColor =>
        _action switch
        {
            RebaseAction.Drop => Palette.Red,
            RebaseAction.Pick => Palette.Transparent,
            _ => Palette.Violet,
        };

    public RebaseStep ToStep() => Step with { Action = _action, Message = IsReword ? _message : null };
}

/// <summary>The planner dialog: commits oldest first, drag to reorder, an action per commit.</summary>
public sealed partial class RebasePlannerViewModel : ObservableObject
{
    private readonly IReadOnlyList<string> _originalOrder;
    private string _problem = "";

    public RebasePlannerViewModel(RebaseRange range, string branch)
    {
        Range = range;
        Branch = branch;
        _originalOrder = range.Steps.Select(s => s.Hash).ToList();
        foreach (var step in range.Steps)
            Add(new RebaseStepViewModel(step));
        Steps.CollectionChanged += (_, _) => Validate();
        Validate();
    }

    public RebaseRange Range { get; }
    public string Branch { get; }
    public ObservableCollection<RebaseStepViewModel> Steps { get; } = [];

    public string Intro =>
        $"{Steps.Count} commit{(Steps.Count == 1 ? "" : "s")} on {Branch}, oldest at the top. Drag to reorder, choose what happens to each, then rewrite. They all get new hashes, so a branch that's already pushed will need a force push.";

    public string Problem
    {
        get => _problem;
        private set
        {
            if (SetProperty(ref _problem, value))
            {
                OnPropertyChanged(nameof(HasProblem));
                OnPropertyChanged(nameof(CanRun));
            }
        }
    }

    public bool HasProblem => _problem.Length > 0;
    public bool CanRun => _problem.Length == 0;

    public IReadOnlyList<RebaseStep> Plan => Steps.Select(s => s.ToStep()).ToList();

    public void Validate() => Problem = RebasePlan.Check(Plan, _originalOrder) ?? "";

    private void Add(RebaseStepViewModel step)
    {
        step.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RebaseStepViewModel.ActionIndex) or nameof(RebaseStepViewModel.Message))
                Validate();
        };
        Steps.Add(step);
    }
}
