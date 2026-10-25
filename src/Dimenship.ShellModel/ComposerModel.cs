using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;

namespace Dimenship.ShellModel;

/// <summary>
/// The Operations composer as the player left it: which mode and target, the draft with its
/// edits, its undo and redo, and whether a plan's detail was open instead. Written by
/// <see cref="ComposerModel.ToState"/> when the view unmounts and read back by
/// <see cref="ComposerModel.FromState"/> when it mounts, because the zone frees the panel on every
/// view change and a draft that lived only in the panel was thrown away by a glance at the vessel
/// (UX-01).
/// <para>
/// The build target is held by facility, not by list index: the list of unbuilt slots shrinks as
/// they commission, and an index kept across a trip could name a different slot on return. The
/// draft comes back as it was and is refreshed against the world on the first snapshot, through
/// the composer's ordinary world refresh, so a draft composed before the trip never shows stale
/// feasibility as current.
/// </para>
/// </summary>
public sealed record OperationsComposerState(
    bool BuildMode,
    ExecutorId? BuildFacility,
    int ProduceIndex,
    long QuantityUnits,
    PlanDraft? Draft,
    IReadOnlyList<PlanDraft> Undo,
    IReadOnlyList<PlanDraft> Redo,
    DraftStepId? SelectedStep,
    bool ApproveLocked,
    PlanId? SelectedPlan);

/// <summary>
/// The Operations composer's state and every rule about it, with no widget in sight: mode, target,
/// quantity, the draft, the revision stack, the approve lock, and the park-and-restore round trip.
/// <c>OperationsFocus</c> holds one, forwards each control's input to it, and renders what it
/// says. Split out of the widget (R5) because each of these rules has been broken and fixed at
/// least once (UX-01's lost draft, the second APPROVE, the target index left over from a longer
/// list), and the only way to exercise them was to boot Godot.
/// <para>
/// The kernel is reached through three delegates the view binds on mount: <see cref="Compose"/>
/// and <see cref="Adjust"/> (the driver's draft and adjust) and <see cref="Approver"/> (the
/// shell's approve-then-commit). Unbound, the model composes nothing and approves nothing, which
/// is what the view did before its context arrived.
/// </para>
/// <para>
/// The REPLANNED flash and the add-move row are the view's. The model tells it when a draft was
/// re-expanded (<see cref="Adjusted"/>) and when the revision stack was thrown away
/// (<see cref="RevisionsCleared"/>), and the view keeps its own clock.
/// </para>
/// </summary>
public sealed class ComposerModel
{
    /// <summary>Undo keeps this many drafts; the oldest falls off first.</summary>
    public const int UndoLimit = 64;

    private readonly IReadOnlyList<BuildTarget> _buildTargets;
    private readonly List<PlanDraft> _undo = new();
    private readonly List<PlanDraft> _redo = new();

    /// <summary>The still-unbuilt subset of the build targets, recomputed from each snapshot.</summary>
    private List<BuildTarget> _visibleBuildTargets;

    /// <summary>The ids behind <see cref="_visibleBuildTargets"/>, kept to tell whether the set changed.</summary>
    private List<ExecutorId> _visibleBuildIds;

    private ItemAmount? _trackedGoal;
    private StorageId? _trackedDestination;
    private ExecutorId? _trackedAssemblyTarget;

    /// <summary>
    /// The draft as it was when the player started typing a quantity. Each live change re-adjusts
    /// from this baseline without pushing an undo entry per keystroke; ending the edit pushes one.
    /// Without it, a snapshot rebuilding the step list under the focused spin box skipped the
    /// commit, and the typed value snapped back a tick later.
    /// </summary>
    private PlanDraft? _quantityEditBaseline;

    private DraftStepId? _quantityEditStepId;

    /// <summary>
    /// Starts optimistic: before the first snapshot nothing is known to be built, so every build
    /// target is provisionally visible. The first <see cref="Refresh"/> corrects it.
    /// </summary>
    public ComposerModel(IReadOnlyList<BuildTarget> buildTargets, IReadOnlyList<ProduceTarget> produceTargets)
    {
        _buildTargets = buildTargets;
        ProduceTargets = produceTargets;
        _visibleBuildTargets = buildTargets.ToList();
        _visibleBuildIds = buildTargets.Select(t => t.Facility).ToList();
    }

    /// <summary>Composes a fresh draft for a goal; bound to the driver's <c>Draft</c>.</summary>
    public Func<ItemAmount, StorageId?, ExecutorId?, PlanDraft>? Compose { get; set; }

    /// <summary>Re-expands one edit against the live world; bound to the driver's <c>Adjust</c>.</summary>
    public Func<PlanDraft, DraftEdit, PlanDraft>? Adjust { get; set; }

    /// <summary>Approves and commits a draft; bound to the shell's <c>PlanApproved</c>.</summary>
    public Func<PlanDraft, PlanApproval>? Approver { get; set; }

    /// <summary>A draft was re-expanded by an edit, a live quantity or a world refresh.</summary>
    public event Action<PlanDraft>? Adjusted;

    /// <summary>The revision stack was thrown away: a fresh compose or a discard.</summary>
    public event Action? RevisionsCleared;

    public bool BuildMode { get; private set; } = true;

    public int BuildIndex { get; private set; }

    public int ProduceIndex { get; private set; }

    public long QuantityUnits { get; private set; } = 1;

    public PlanDraft? Draft { get; private set; }

    /// <summary>
    /// Set the moment APPROVE commits a plan, and cleared only by a real composer change: a mode,
    /// target or quantity, an edit, an undo or redo, a discard. Never by the snapshot-driven
    /// refresh, which recomposes a fresh candidate on every tick after a commit; without the lock
    /// that candidate re-enabled APPROVE and a second press committed the same order again.
    /// </summary>
    public bool ApproveLocked { get; private set; }

    /// <summary>The committed plan whose detail is open, or null while the composer shows.</summary>
    public PlanId? SelectedPlan { get; set; }

    /// <summary>The step row the player has open; parked and restored with the draft.</summary>
    public DraftStepId? SelectedStep { get; set; }

    /// <summary>
    /// Whether UNDO has anything to step back to: an entry, or a quantity still being typed that
    /// changed the draft, which <see cref="Undo"/> commits and then reverts. Reading the stack
    /// alone disabled UNDO over the first typed change on a fresh draft.
    /// </summary>
    public bool CanUndo => _undo.Count > 0 || TypedChangePending;

    /// <summary>
    /// Whether REDO has anything to step forward to. A quantity still being typed that changed the
    /// draft is an edit, and committing it drops the redo stack, so there is nothing to redo while
    /// one is open.
    /// </summary>
    public bool CanRedo => _redo.Count > 0 && !TypedChangePending;

    public bool QuantityEditInProgress => _quantityEditBaseline is not null;

    /// <summary>An open quantity edit that has moved the draft off the baseline it started from.</summary>
    private bool TypedChangePending =>
        _quantityEditBaseline is { } baseline && Draft is not null && !ReferenceEquals(Draft, baseline);

    public IReadOnlyList<BuildTarget> VisibleBuildTargets => _visibleBuildTargets;

    public IReadOnlyList<ProduceTarget> ProduceTargets { get; }

    /// <summary>
    /// The selected build target, clamped against the visible list, so an index left over from a
    /// longer list (a pad just commissioned and dropped out) names the nearest slot rather than
    /// faulting.
    /// </summary>
    public BuildTarget? SelectedBuildTarget =>
        _visibleBuildTargets.Count == 0
            ? null
            : _visibleBuildTargets[Math.Clamp(BuildIndex, 0, _visibleBuildTargets.Count - 1)];

    /// <summary>The selected produce target, clamped the same way.</summary>
    public ProduceTarget? SelectedProduceTarget =>
        ProduceTargets.Count == 0
            ? null
            : ProduceTargets[Math.Clamp(ProduceIndex, 0, ProduceTargets.Count - 1)];

    /// <summary>What the composer is asking for: a build target's order, or a produce item at the quantity.</summary>
    public ItemAmount? Goal => BuildMode
        ? SelectedBuildTarget?.Order.Goal
        : SelectedProduceTarget is { } target
            ? new ItemAmount(target.Item, QuantityUnits * Units.MilliPerUnit)
            : null;

    private StorageId? Destination => BuildMode ? SelectedBuildTarget?.Destination : null;

    private ExecutorId? AssemblyTarget => BuildMode ? SelectedBuildTarget?.Facility : null;

    // ---- Choosing what to compose ---------------------------------------------------------------

    // Each choice unlocks APPROVE, because it is a real change of what is being composed. None of
    // them composes: the view follows each with Refresh(snapshot, freshCompose: true), which also
    // tells it whether its build picker needs rebuilding.

    /// <summary>Switches mode. False, and nothing changes, when already in it.</summary>
    public bool SetMode(bool build)
    {
        if (BuildMode == build)
        {
            return false;
        }

        BuildMode = build;
        ApproveLocked = false;
        return true;
    }

    public void SelectBuildTarget(int index)
    {
        BuildIndex = index;
        ApproveLocked = false;
    }

    public void SelectProduceTarget(int index)
    {
        ProduceIndex = index;
        ApproveLocked = false;
    }

    /// <summary>A produce quantity in whole units, never below one.</summary>
    public void SetQuantity(long units)
    {
        QuantityUnits = Math.Max(1, units);
        ApproveLocked = false;
    }

    /// <summary>
    /// The inspector's request to build one slot: Build mode, on that slot if it is still listed.
    /// A kept draft for the same slot is resumed, edits and all; another slot is another goal,
    /// and the next <see cref="Refresh"/> recomposes it. Unlocks, because the player asked for an
    /// order. Returns whether the slot was found.
    /// </summary>
    public bool OpenBuild(ExecutorId facility)
    {
        BuildMode = true;
        var index = _visibleBuildTargets.FindIndex(t => t.Facility == facility);
        if (index >= 0)
        {
            BuildIndex = index;
        }

        ApproveLocked = false;
        return index >= 0;
    }

    /// <summary>
    /// Shows the composer instead of a plan's detail. An empty composer is unlocked, because there
    /// is nothing on it a second press could commit twice.
    /// </summary>
    public void ShowComposer()
    {
        SelectedPlan = null;
        if (Draft is null)
        {
            ApproveLocked = false;
        }
    }

    // ---- Keeping the draft current --------------------------------------------------------------

    /// <summary>
    /// Brings the composer up to date with <paramref name="snapshot"/>. The visible build targets
    /// are recomputed first. Then the draft is composed afresh when asked to, when there is none,
    /// or when the goal it was composed for has changed under it (its target commissioned and the
    /// selection moved); otherwise it is refreshed against the world, keeping the player's edits.
    /// Returns whether the visible build targets changed, so the view rebuilds its picker only
    /// then and never resets a dropdown the player is looking at.
    /// </summary>
    public bool Refresh(WorldSnapshot? snapshot, bool freshCompose = false)
    {
        var targetsChanged = snapshot is not null && RefreshVisibleBuildTargets(snapshot);

        if (freshCompose || Draft is null || GoalChanged())
        {
            ClearRevisions();
            Draft = Compose is { } compose && Goal is { } goal ? compose(goal, Destination, AssemblyTarget) : null;
            TrackGoal();
        }
        else if (Draft is { } draft && Adjust is { } adjust)
        {
            // An open quantity edit is kept: the refresh runs against the live-edited draft, so
            // availability updates and the typed contribution survives through its locks.
            Draft = adjust(draft, new WorldRefresh());
            Adjusted?.Invoke(Draft);
        }

        return targetsChanged;
    }

    /// <summary>
    /// Recomputes which build targets are still unbuilt. When the set changed, the selected
    /// facility stays selected if it is still listed, and the index falls back to the first
    /// otherwise.
    /// <para>
    /// That fallback is a target change made here rather than by the player, so it unlocks
    /// APPROVE: left locked, a plan approved for a slot that has since commissioned would leave the
    /// player looking at a whole preview for the slot that took its place, with APPROVE disabled and
    /// nothing saying why. Only in Build mode, because this runs on every snapshot in either mode,
    /// and a slot commissioning elsewhere must not unlock a Produce order just approved; that was
    /// the double commit the lock exists to stop, reopened by the fix for the first case.
    /// </para>
    /// </summary>
    private bool RefreshVisibleBuildTargets(WorldSnapshot snapshot)
    {
        var visible = _buildTargets.Where(t => StillUnbuilt(t.Facility, snapshot)).ToList();
        var ids = visible.Select(t => t.Facility).ToList();
        if (ids.SequenceEqual(_visibleBuildIds))
        {
            return false;
        }

        var selected = SelectedBuildTarget?.Facility;
        _visibleBuildTargets = visible;
        _visibleBuildIds = ids;
        BuildIndex = selected is { } id ? Math.Max(0, visible.FindIndex(t => t.Facility == id)) : 0;

        if (BuildMode && SelectedBuildTarget?.Facility != selected)
        {
            ApproveLocked = false;
        }

        return true;
    }

    /// <summary>A slot not yet reporting built, or one the snapshot has no row for, which counts as unbuilt.</summary>
    private static bool StillUnbuilt(ExecutorId facility, WorldSnapshot snapshot) =>
        snapshot.Executors.FirstOrDefault(e => e.Id == facility) is not { Built: true };

    private bool GoalChanged() =>
        Goal != _trackedGoal || Destination != _trackedDestination || AssemblyTarget != _trackedAssemblyTarget;

    private void TrackGoal()
    {
        _trackedGoal = Goal;
        _trackedDestination = Destination;
        _trackedAssemblyTarget = AssemblyTarget;
    }

    // ---- Approve and discard --------------------------------------------------------------------

    /// <summary>
    /// Commits the draft exactly once. Locked, empty, or with no task to queue, nothing is sent.
    /// Committed, the draft is cleared and the lock set. Refused, the draft carries the refusal's
    /// issues so the preview can show them. Returns what the approver answered, or null when
    /// nothing was sent.
    /// </summary>
    public PlanApproval? Approve()
    {
        if (ApproveLocked)
        {
            return null;
        }

        CommitQuantityEdit();
        if (Draft is not { } draft || draft.Flatten().Tasks.Count == 0 || Approver is not { } approve)
        {
            return null;
        }

        var result = approve(draft);
        if (result is PlanApprovalCommitted)
        {
            Draft = null;
            ApproveLocked = true;
        }
        else if (result is PlanApprovalRefused refused)
        {
            Draft = draft with { Issues = refused.Issues };
        }

        return result;
    }

    /// <summary>
    /// Clears the draft and puts the composer back to its defaults, both modes at once, because
    /// discard resets the composer and not only the mode showing. It does not lock: a discard
    /// starts a composition, it does not commit one. The view recomposes after it.
    /// </summary>
    public void Discard()
    {
        Draft = null;
        ApproveLocked = false;
        ClearRevisions();
        BuildIndex = 0;
        ProduceIndex = 0;
        QuantityUnits = 1;
    }

    // ---- Editing the draft ----------------------------------------------------------------------

    /// <summary>
    /// One player edit, pushed onto undo. Every edit but RE-ADJUST unlocks APPROVE: re-adjust is a
    /// validation pass and must not reopen APPROVE after a commit, while UNLOCK ALL changes locks
    /// and re-expands, which is a change of substance.
    /// </summary>
    public void ApplyEdit(DraftEdit edit)
    {
        if (Draft is not { } previous || Adjust is not { } adjust)
        {
            return;
        }

        CommitQuantityEdit();
        PushUndo(previous);
        _redo.Clear();
        Draft = adjust(previous, edit);
        if (edit is not ReAdjust)
        {
            ApproveLocked = false;
        }

        Adjusted?.Invoke(Draft);
    }

    /// <summary>
    /// A quantity typed into a step's spin box, applied live from the baseline the edit started
    /// with, without an undo entry per change. A different step ends the open edit first. The lock
    /// is left alone here: <see cref="CommitQuantityEdit"/> clears it when the edit really changed
    /// the draft, and clearing it per keystroke would reopen APPROVE after a commit with no
    /// finished edit.
    /// </summary>
    public void LiveQuantityEdit(DraftStepId step, long quantityMilli)
    {
        if (Draft is null || Adjust is not { } adjust)
        {
            return;
        }

        if (_quantityEditBaseline is null || _quantityEditStepId != step)
        {
            if (_quantityEditBaseline is not null)
            {
                CommitQuantityEdit();
            }

            _quantityEditBaseline = Draft;
            _quantityEditStepId = step;
        }

        Draft = adjust(_quantityEditBaseline!, new SetQuantity(step, quantityMilli));
        Adjusted?.Invoke(Draft);
    }

    /// <summary>Ends an open quantity edit, pushing one undo entry if it changed anything.</summary>
    public void CommitQuantityEdit()
    {
        if (_quantityEditBaseline is not { } baseline)
        {
            return;
        }

        if (Draft is not null && !ReferenceEquals(Draft, baseline))
        {
            PushUndo(baseline);
            _redo.Clear();
            ApproveLocked = false;
        }

        _quantityEditBaseline = null;
        _quantityEditStepId = null;
    }

    /// <summary>
    /// Steps back one draft. False when there is nothing to undo. A quantity still being typed is
    /// committed first, so it is the step undone: the press reverts what the player can see, and
    /// it does so whether or not the stack had entries before the edit opened.
    /// </summary>
    public bool Undo()
    {
        if (Draft is null)
        {
            return false;
        }

        CommitQuantityEdit();
        if (_undo.Count == 0)
        {
            return false;
        }

        _redo.Add(Draft);
        Draft = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        ApproveLocked = false;
        return true;
    }

    /// <summary>
    /// Steps forward one draft. False when there is nothing to redo. A quantity still being typed
    /// is committed first, and a commit that changed the draft drops the redo stack, so the stack
    /// is read only after it. Reading it before once indexed an emptied stack and threw, when
    /// REDO, which takes no focus, was pressed with a spin box still being typed into.
    /// </summary>
    public bool Redo()
    {
        CommitQuantityEdit();
        if (_redo.Count == 0)
        {
            return false;
        }

        if (Draft is not null)
        {
            PushUndo(Draft);
        }

        Draft = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        ApproveLocked = false;
        return true;
    }

    private void PushUndo(PlanDraft draft)
    {
        _undo.Add(draft);
        if (_undo.Count > UndoLimit)
        {
            _undo.RemoveAt(0);
        }
    }

    private void ClearRevisions()
    {
        CommitQuantityEdit();
        _undo.Clear();
        _redo.Clear();
        SelectedStep = null;
        RevisionsCleared?.Invoke();
    }

    // ---- Park and restore (UX-01) ---------------------------------------------------------------

    /// <summary>
    /// The composer as it stands, for the next mount. An open quantity edit is committed first, as
    /// leaving the step list commits it, so the number the player typed is the number that comes
    /// back.
    /// </summary>
    public OperationsComposerState ToState()
    {
        CommitQuantityEdit();
        return new OperationsComposerState(
            BuildMode,
            SelectedBuildTarget?.Facility,
            ProduceIndex,
            QuantityUnits,
            Draft,
            _undo.ToList(),
            _redo.ToList(),
            SelectedStep,
            ApproveLocked,
            SelectedPlan);
    }

    /// <summary>
    /// Puts back what <see cref="ToState"/> kept. The build target is found again by facility, and
    /// left where it is when the slot is no longer listed. The goal is tracked last, so the first
    /// snapshot sees an unchanged goal and refreshes the draft against the world instead of
    /// composing over it.
    /// </summary>
    public void FromState(OperationsComposerState kept)
    {
        BuildMode = kept.BuildMode;
        if (kept.BuildFacility is { } facility
            && _visibleBuildTargets.FindIndex(t => t.Facility == facility) is var index and >= 0)
        {
            BuildIndex = index;
        }

        if (ProduceTargets.Count > 0)
        {
            ProduceIndex = Math.Clamp(kept.ProduceIndex, 0, ProduceTargets.Count - 1);
        }

        QuantityUnits = Math.Max(1, kept.QuantityUnits);
        Draft = kept.Draft;
        _undo.Clear();
        _undo.AddRange(kept.Undo);
        _redo.Clear();
        _redo.AddRange(kept.Redo);
        SelectedStep = kept.SelectedStep;
        ApproveLocked = kept.ApproveLocked;
        SelectedPlan = kept.SelectedPlan;
        TrackGoal();
    }
}
