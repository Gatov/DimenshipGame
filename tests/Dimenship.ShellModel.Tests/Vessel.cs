using Dimenship.Core.Content;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Simulation;

namespace Dimenship.ShellModel.Tests;

/// <summary>
/// A composer over a running engine on the shipped vessel, bound the way the shell binds it: the
/// driver's draft and adjust, and the shell's approve-then-commit. The tests drive the model the
/// way <c>OperationsFocus</c> does, one control at a time followed by a refresh against the
/// latest snapshot, so what passes here is what the view sees.
/// </summary>
internal sealed class Vessel
{
    private static readonly ContentLoadResult Content =
        new JsonContentSource(new DirectoryContentFileSystem(Path.Combine(AppContext.BaseDirectory, "content"))).Load();

    public Vessel()
    {
        var catalog = Content.Catalog ?? throw new InvalidOperationException(string.Join("\n", Content.Errors));
        var scenario = Content.Scenarios.Single(s => s.Id == "default_vessel");
        Engine = SimulationEngine.NewGame(catalog, scenario);
        Model = Composer(catalog, scenario);
    }

    public SimulationEngine Engine { get; }

    public ComposerModel Model { get; }

    /// <summary>A fresh composer over the same engine, as a remount builds one.</summary>
    public ComposerModel Remount() => Composer(Content.Catalog!, Content.Scenarios.Single(s => s.Id == "default_vessel"));

    public int PlanCount => Engine.Snapshot.Plans.Count;

    /// <summary>
    /// Produce mode on Components: a three-step order on a new campaign. The first two produce
    /// targets, Hydrogen and Basic Metals, compose to no steps at all on a new campaign, so they
    /// cannot carry an edit.
    /// </summary>
    public void OrderComponents(ComposerModel model, long units = 1)
    {
        model.SetMode(build: false);
        model.SelectProduceTarget(model.ProduceTargets.ToList().FindIndex(t => t.Item.Value == "component"));
        model.SetQuantity(units);
        model.Refresh(Engine.Snapshot, freshCompose: true);
    }

    /// <summary>One simulated tick, then the refresh a snapshot delivery makes.</summary>
    public bool Tick(ComposerModel? model = null)
    {
        Engine.Advance(1);
        return (model ?? Model).Refresh(Engine.Snapshot);
    }

    /// <summary>A slot commissions: it is built from the next snapshot on.</summary>
    public void Commission(ExecutorId slot) =>
        Engine.State.Vessel.Facilities.Single(f => f.Id == slot).Built = true;

    private ComposerModel Composer(ContentCatalog catalog, Scenario scenario) =>
        new(ComposerTargets.Build(catalog, scenario), ComposerTargets.Produce(catalog, scenario, item => item.Value))
        {
            Compose = (goal, destination, assembly) => PlanDraftEditor.Create(goal, Engine, destination, assembly),
            Adjust = (draft, edit) => PlanDraftEditor.Adjust(draft, Engine, edit),
            Approver = draft =>
            {
                var approval = PlanDraftEditor.Approve(draft, Engine);
                return approval is PlanApprovalCommitted { Plan: var plan }
                       && Engine.Execute(new CommitPlan(plan)) is CommandRefused refused
                    ? new PlanApprovalRefused(refused.Issues)
                    : approval;
            },
        };
}
