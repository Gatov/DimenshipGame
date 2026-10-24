using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Content;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Presentation;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Shell;
using Dimenship.ShellModel;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// Everything about whatever is selected in the focus view. It reads the selection from the shell
/// context and resolves the identifier against the current snapshot every time, so it holds no
/// reference to the graph and cannot go stale with it.
/// </summary>
public sealed partial class FacilityInspectorPanel : PanelBase
{
    private readonly List<Line> _lines = new();

    private ShellContext? _context;
    private IconSlot _icon = null!;
    private Label _title = null!;
    private Label _subtitle = null!;
    private VBoxContainer _rows = null!;

    /// <summary>
    /// The construction action, outside the recycled <see cref="DetailRow"/>s: nothing in that
    /// index-reconciled mechanism can be clicked, so the one row that needs to be is a real
    /// persistent child instead. Three states and no fourth — see <see cref="OnSnapshot"/> and
    /// <see cref="Executor"/> — and hidden by default so every selection kind that isn't an unbuilt
    /// executor with a construction unit leaves it alone.
    /// </summary>
    private Button _actionButton = null!;

    /// <summary>What pressing <see cref="_actionButton"/> does right now — set fresh on every
    /// snapshot alongside the button's text and visibility, never left over from a previous
    /// selection.</summary>
    private System.Action? _pendingActionCommand;

    /// <summary>
    /// BUILD commits a plan that takes material from the hold, so it takes a second press, as the
    /// plan controls' cancel does (U1). It is disarmed by any other selection, by the slot being
    /// ordered, and by its draft stopping being committable.
    /// </summary>
    private readonly ConfirmGate _buildGate = new();

    /// <summary>The selection the armed state and the refusal belong to; a different one resets both.</summary>
    private string? _buildFor;

    /// <summary>The planner's refusal of the last confirmed BUILD, shown until the selection changes.</summary>
    private string? _buildRefusal;

    private Label _actionFeedback = null!;

    public override PanelId Id => ShellRoot.FacilityInspectorId;

    public override string Title => "Facility Inspector";

    public override void OnMount(ShellContext context) => _context = context;

    public override void _Ready()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        AddChild(column);

        // The header is built like a node card's: the same icon at the same size beside the same
        // title, so the selected thing looks like the thing that was clicked on the graph.
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        column.AddChild(head);

        _icon = new IconSlot(IconSlot.CardSize, ShellPalette.TextDim);
        head.AddChild(_icon);

        var heading = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        heading.AddThemeConstantOverride("separation", 0);
        head.AddChild(heading);

        _title = new Label();
        _title.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
        _title.AddThemeFontSizeOverride("font_size", ShellPalette.FontHeading);
        heading.AddChild(_title);

        _subtitle = new Label();
        _subtitle.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
        _subtitle.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        heading.AddChild(_subtitle);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
        column.AddChild(_rows);

        // A third child beside head and _rows, not a row inside either — the precedent is
        // EventLogPanel's filter row above its output, built the same way.
        var actionArea = new HBoxContainer();
        actionArea.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        column.AddChild(actionArea);

        _actionButton = new Button { Visible = false, FocusMode = FocusModeEnum.None };
        ShellTheme.ApplyGlass(_actionButton);
        _actionButton.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        _actionButton.Pressed += () => _pendingActionCommand?.Invoke();
        actionArea.AddChild(_actionButton);

        _actionFeedback = new Label
        {
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ThemeTypeVariation = ShellTheme.Prose,
        };
        _actionFeedback.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        _actionFeedback.AddThemeColorOverride("font_color", ShellPalette.StateFault);
        column.AddChild(_actionFeedback);
    }

    public override void OnSnapshot(WorldSnapshot snapshot)
    {
        _lines.Clear();

        // Reset here, once, rather than in every branch below: this is what makes "hidden
        // otherwise" true for every selection kind that isn't an unbuilt executor with a
        // construction unit, without repeating the reset at each of those other call sites.
        _actionButton.Visible = false;
        _pendingActionCommand = null;
        _actionButton.Disabled = false;

        var selectedId = _context?.CurrentSelection?.Id;
        if (selectedId != _buildFor)
        {
            _buildFor = selectedId;
            _buildGate.Disarm();
            _buildRefusal = null;
        }

        _actionFeedback.Text = _buildRefusal ?? string.Empty;
        _actionFeedback.Visible = _buildRefusal is not null;

        var selection = _context?.CurrentSelection;
        if (selection is not { } selected)
        {
            Head("NO SELECTION", string.Empty, null);
            Sync();
            return;
        }

        switch (selected.Kind)
        {
            case GraphNodeKind.Executor:
                Executor(snapshot, selected.Id);
                break;
            case GraphNodeKind.Transport:
                Transport(snapshot, selected.Id);
                break;
            case GraphNodeKind.Storage:
                Storage(snapshot, selected.Id);
                break;
            case GraphNodeKind.Power:
                Power(snapshot);
                break;
        }

        Sync();
    }

    private void Executor(WorldSnapshot snapshot, string id)
    {
        var executor = snapshot.Executors.FirstOrDefault(e => e.Id.Value == id);
        if (executor is null)
        {
            Gone(id);
            return;
        }

        // The icon name is the facility kind lowercased, exactly as ExecutorCard derives it: the
        // inspector and the card it was opened from must not name the same kind two ways.
        Head(
            executor.Label,
            $"FACILITY · {executor.Type.ToString().ToUpperInvariant()}",
            new IconRef("facility", executor.Type.ToString().ToLowerInvariant()));

        // An unbuilt facility has no run history, no queue and no configured schematic, and every row
        // below this branch assumes a working one. What it needs (PURPOSE, REQUIRES) is read from
        // content; what it would take now is quoted from the composer's own draft, and BUILD commits
        // it from here (U4), which reverses construction-interface Decision 3.
        if (!executor.Built)
        {
            Row("STATUS", "UNBUILT", ShellPalette.StateWarn, null, new IconRef("status", "idle"));

            // ExecutorState carries no FacilityArchetypeId of its own — the same scenario lookup
            // OperationsFocus.ResolveBuildTargets() already uses to get from a placed facility back
            // to the archetype that defines it.
            var archetypeId = ShellContent.DefaultVessel.Facilities
                .FirstOrDefault(f => f.Id == executor.Id)?.Archetype;
            var archetype = archetypeId is { } resolvedId ? ShellContent.Catalog.Facility(resolvedId) : null;
            if (archetype is null)
            {
                return;
            }

            // Shown in its authored sentence case, not upper-cased like every other row's value in
            // this panel — Purpose is the one piece of actual prose here, and a full sentence in
            // all caps reads as a shouted wall of text rather than a label.
            Row("PURPOSE", archetype.Purpose, prose: true);

            if (ConstructionOrder.For(archetype, executor.Id, executor.LocalStorage) is not { } order)
            {
                // No construction unit named at all: never commissioned this way (already built,
                // or not a buildable slot — see FacilityArchetype.ConstructionUnit). Nothing to
                // plan and nothing to show, so REQUIRES and the action button both stay off.
                return;
            }

            // The first schematic in declaration order that produces this unit — the same
            // first-in-declaration-order rule ProductionPlanner.ChooseFacility's neighbour Require
            // already uses to pick among producers of one output. Commissioning itself withdraws
            // exactly one whole unit (SimulationEngine.WholeConstructionUnit), never a fraction and
            // never more, which is the quantity the order asks for.
            var unit = order.Goal.Item;
            var schematic = ShellContent.Catalog.Schematics.ForOutput(unit).FirstOrDefault();
            Row(
                "REQUIRES",
                schematic is not null
                    ? $"{Units.Format(order.Goal.Quantity)} {Labels.Item(unit)} · {schematic.Id.Value.ToUpperInvariant()}"
                    : $"{Labels.Item(unit)} · NO SCHEMATIC PRODUCES THIS",
                ShellPalette.TextPrimary,
                null,
                new IconRef("item", unit.Value));

            // Safe unconditionally here: executor is confirmed present in snapshot.Executors (the
            // method returned via Gone(id) above otherwise), which is the only case ConstructionProgress.For
            // throws for. The active plan only: a cancelled build still attributes to the slot, and
            // opening its detail would leave the player no way to order the slot again.
            if (ConstructionProgress.ActivePlan(snapshot, executor.Id) is { } planId)
            {
                _buildGate.Disarm();
                _buildRefusal = null;
                _actionFeedback.Visible = false;
                _actionButton.Text = "VIEW PLAN";
                _actionButton.Visible = true;
                _pendingActionCommand = () =>
                    _context?.Actions.OperationsRequested?.Invoke(new PendingOperationsTarget(Plan: planId));
                return;
            }

            // The composer's own door, so the quote is of the draft BUILD would commit (U4). It is
            // recomposed per snapshot while the slot is selected and unordered: one small planner
            // pass, and a quote that never shows the world as it was a minute ago.
            if (_context?.ComposeDraft is not { } compose)
            {
                return;
            }

            var draft = compose(order.Goal, order.Destination, order.Slot);

            // After a kernel fault the driver hands back an empty draft that assembles nothing,
            // and a quote of it would throw on every delivery. There is nothing to build then.
            if (draft.AssemblyTarget is null)
            {
                return;
            }

            var quote = BuildQuote.For(draft, ShellContent.Catalog.Schematics, ShellContent.DefaultVessel.Hold);
            Quote(snapshot, quote);

            if (!quote.Committable)
            {
                _buildGate.Disarm();
            }

            _actionButton.Text = _buildGate.Armed ? "CONFIRM BUILD" : "BUILD";
            _actionButton.Disabled = !quote.Committable;
            _actionButton.Visible = true;
            _pendingActionCommand = () => OnBuildPressed(order);
            return;
        }

        // The card's reading, from the same projection (U3), so the two never disagree about
        // whether a facility is waiting or blocked.
        var condition = ExecutorCondition.For(snapshot, executor.Id);
        var (status, color, glyph) = Conditions.Standing(condition);
        Row("STATUS", status, color, null, new IconRef("status", glyph));
        Row("SETUP", Conditions.Setup(condition));
        Row(
            "POWER",
            $"{Units.Format(executor.PowerDraw)} MW",
            ShellPalette.TextPrimary,
            null,
            new IconRef("status", "energy"));

        if (condition.Standing == ExecutorStanding.ChangingOver)
        {
            var elapsed = condition.ChangeoverTicksTotal - condition.ChangeoverTicksRemaining;
            Row(
                "CHANGEOVER",
                $"{elapsed} / {condition.ChangeoverTicksTotal} ticks",
                ShellPalette.StateWarn,
                Fill(elapsed, condition.ChangeoverTicksTotal),
                new IconRef("status", "time"));
        }

        // Zero total means no run in progress, so the bar renders empty rather than dividing.
        var done = executor.RunTicksTotal - executor.RunTicksRemaining;
        Row(
            "RUN",
            executor.RunTicksTotal == 0 ? "—" : $"{done} / {executor.RunTicksTotal} ticks",
            ShellPalette.TextPrimary,
            Fill(done, executor.RunTicksTotal),
            new IconRef("status", "time"));

        // What the run will deposit (U2): its inputs are already consumed, so until it lands this
        // is the one place that material is anywhere at all.
        if (executor.RunOutput is { } output)
        {
            Row(
                "OUTPUT",
                $"{output.Item.Value.ToUpperInvariant()} {Units.Format(output.Quantity)}",
                ShellPalette.TextPrimary,
                null,
                new IconRef("item", output.Item.Value));
        }

        Heading("QUEUE", new IconRef("status", "queue"));
        var queue = FacilityQueue.For(snapshot, executor.Id);
        if (queue.Count == 0)
        {
            Row("—", "NOTHING QUEUED", ShellPalette.TextFaint);
        }

        foreach (var row in queue.Take(MaxQueueRows))
        {
            QueueRow(row);
        }

        // A side panel, not a campaign's ledger: past this many, Operations is where to read on.
        if (queue.Count > MaxQueueRows)
        {
            Row($"+{queue.Count - MaxQueueRows} MORE", "SEE OPERATIONS", ShellPalette.TextFaint);
        }

        Heading(
            $"LOCAL STORAGE · {executor.LocalStorage.Value.ToUpperInvariant()}",
            new IconRef("facility", "storage"));
        Contents(snapshot, executor.LocalStorage);
    }

    private void Transport(WorldSnapshot snapshot, string id)
    {
        var line = snapshot.Transports.FirstOrDefault(t => t.Id.Value == id);
        if (line is null)
        {
            Gone(id);
            return;
        }

        Head(line.Label, "TRANSPORT LINE", new IconRef("control", "chevron_right"));

        var (status, color) = Status(line.Status, line.BlockReason);
        Row("STATUS", status, color, null, new IconRef("status", Glyph(line.Status)));
        Row(
            "ROUTE",
            $"{line.From} → {line.To} · {line.LengthTicks}T",
            ShellPalette.TextPrimary,
            null,
            new IconRef("control", "chevron_right"));

        // What is actually on the belt, which is not the same as what the current haul is for: a
        // line takes the next transfer on as soon as the last one is aboard, so two items can be
        // travelling at once. An empty belt still says CARRYING NOTHING and gets no item glyph —
        // an icon there would name a cargo the line is not carrying.
        var carrying = line.Cargo.Count switch
        {
            0 => "NOTHING",
            1 => $"{line.Cargo[0].Id.Value.ToUpperInvariant()} {Units.Format(line.Cargo[0].Amount)}",
            _ => $"{line.Cargo.Count} ITEMS {Units.Format(line.Cargo.Sum(c => c.Amount))}",
        };

        Row(
            "CARRYING",
            carrying,
            ShellPalette.TextPrimary,
            null,
            line.Cargo.Count == 1 ? new IconRef("item", line.Cargo[0].Id.Value) : null);

        // The reading the issue asks for, and the one that stays put when a line freezes: how much
        // of the belt is in flight. Capacity is derived — one tick of throughput per tick of
        // length — so it is shown rather than left for the player to multiply out.
        Row(
            "IN FLIGHT",
            $"{Units.Format(line.Cargo.Sum(c => c.Amount))} / {Units.Format(line.Capacity)}",
            ShellPalette.TextPrimary,
            line.CargoFillPermille / 1000f,
            new IconRef("status", "queue"));

        Row(
            "POWER",
            $"{Units.Format(line.PowerDraw)} MW",
            ShellPalette.TextPrimary,
            null,
            new IconRef("status", "energy"));

        // Two readings, because a belt makes them differ: a line whose source has run dry is still
        // delivering, and one that has just started is loading without having arrived.
        Row(
            "LOADED",
            $"{Units.Format(line.LoadedLastTick)} / {Units.Format(line.ThroughputPerTick)}",
            ShellPalette.TextPrimary,
            Fill(line.LoadedLastTick, line.ThroughputPerTick),
            new IconRef("status", "rate"));
        Row(
            "DELIVERED",
            $"{Units.Format(line.DeliveredLastTick)} / {Units.Format(line.ThroughputPerTick)}",
            ShellPalette.TextPrimary,
            Fill(line.DeliveredLastTick, line.ThroughputPerTick),
            new IconRef("status", "rate"));

        Heading("QUEUE", new IconRef("status", "queue"));
        var queued = snapshot.Tasks.Where(t => t.Action is Transfer).Where(t => t.Executor == line.Id).ToList();
        if (queued.Count == 0)
        {
            Row("—", "NOTHING QUEUED", ShellPalette.TextFaint);
        }

        foreach (var task in queued)
        {
            var (state, stateColor) = TaskState(task.State, task.LastReason);
            var transfer = (Transfer)task.Action;

            // Arrived over picked up. A haul entirely aboard has moved nothing yet, and one
            // figure alone would either hide the progress or overstate it.
            var moved = transfer.Quantity is { } requested
                ? $"{Units.Format(task.MovedQuantity)}+{Units.Format(task.LoadedQuantity - task.MovedQuantity)}/{Units.Format(requested)}"
                : $"{Units.Format(task.MovedQuantity)} · STANDING";

            Row($"{transfer.Item} {moved}", state, stateColor, null, new IconRef("item", transfer.Item.Value));
        }
    }

    private void Storage(WorldSnapshot snapshot, string id)
    {
        var storage = snapshot.Storages.FirstOrDefault(s => s.Id.Value == id);
        if (storage is null)
        {
            Gone(id);
            return;
        }

        Head(storage.Label, "STORAGE", new IconRef("facility", "storage"));
        Row(
            "FILL",
            $"{Units.FormatPermille(storage.FillPermille)} OF ONE HOLD",
            ShellPalette.TextPrimary,
            Fill(storage.FillPermille, StorageArchetype.FullHold),
            new IconRef("status", "capacity"));

        Heading("CONTENTS", new IconRef("status", "queue"));
        Contents(snapshot, storage.Id);
        Elsewhere(snapshot, storage);
    }

    /// <summary>
    /// For each item this storage lists, how much of it is anywhere else aboard (U2, over
    /// <see cref="StockLocations.For"/>): in other storages, on belts and in runs. Summed by kind
    /// of place rather than listed place by place, which on the main hold would be a row per item
    /// per buffer; each storage it names can be selected for its own breakdown.
    /// </summary>
    private void Elsewhere(WorldSnapshot snapshot, StorageState storage)
    {
        var rows = new List<(ItemId Item, string Text)>();
        foreach (var item in storage.Items)
        {
            var places = StockLocations.For(snapshot, item.Id).Where(p => p.Storage != storage.Id).ToList();
            if (places.Count == 0)
            {
                continue;
            }

            var parts = new List<string>();
            foreach (var (place, word) in new[]
                     {
                         (StockPlace.Storage, "STORED"), (StockPlace.Belt, "ON BELTS"), (StockPlace.Run, "IN RUNS"),
                     })
            {
                var amount = places.Where(p => p.Place == place).Sum(p => p.Amount);
                if (amount > 0)
                {
                    parts.Add($"{word} {Units.Format(amount)}");
                }
            }

            rows.Add((item.Id, string.Join(" · ", parts)));
        }

        if (rows.Count == 0)
        {
            return;
        }

        Heading("ELSEWHERE ABOARD", new IconRef("facility", "storage"));
        foreach (var (item, text) in rows)
        {
            Row(item.Value.ToUpperInvariant(), text, ShellPalette.TextDim, null, new IconRef("item", item.Value));
        }
    }

    private void Power(WorldSnapshot snapshot)
    {
        // Deliberately a subset of the Energy Budget panel, which keeps the per-consumer
        // breakdown. Two panels showing the same list would be one panel too many.
        var energy = snapshot.Energy;

        Head("Power", "ENERGY POOL", new IconRef("facility", "power"));
        Row(
            "CAPACITY",
            $"{Units.Format(energy.Capacity)} MW",
            ShellPalette.TextPrimary,
            null,
            new IconRef("status", "capacity"));
        Row(
            "DRAW",
            $"{Units.Format(energy.Draw)} MW",
            ShellPalette.StateWarn,
            Fill(energy.Draw, energy.Capacity),
            new IconRef("status", "energy"));
        Row(
            "RESERVE",
            $"{Units.Format(energy.Reserve)} MW",
            ShellPalette.StateWarn,
            null,
            new IconRef("status", "durability"));
        Row(
            "CAP HITS",
            energy.CapHits.ToString(),
            energy.CapHits > 0 ? ShellPalette.StateFault : ShellPalette.TextFaint,
            null,
            new IconRef("status", "alert"));
        Row(
            "STARVED",
            energy.StarvedTicks.ToString(),
            energy.StarvedTicks > 0 ? ShellPalette.StateFault : ShellPalette.TextFaint,
            null,
            new IconRef("status", "alert"));
    }

    private void Contents(WorldSnapshot snapshot, StorageId id)
    {
        var storage = snapshot.Storages.FirstOrDefault(s => s.Id == id);
        if (storage is null)
        {
            Row("—", "NO SUCH STORAGE", ShellPalette.StateFault);
            return;
        }

        foreach (var item in storage.Items)
        {
            // The held part is said beside the amount (U2), because it is the part a plan-less
            // task or another plan cannot take: without it, a full-looking buffer whose work
            // postpones with MATERIAL_CLAIMED reads as a contradiction.
            var held = item.Held > 0 ? $" · {Units.Format(item.Held)} HELD" : string.Empty;
            Row(
                item.Id.Value.ToUpperInvariant(),
                $"{Units.Format(item.Amount)} / {Units.Format(item.Capacity)}{held}",
                item.Amount > 0 ? ShellPalette.TextPrimary : ShellPalette.TextFaint,
                Fill(item.Amount, item.Capacity),
                new IconRef("item", item.Id.Value));
        }

        Claims(snapshot, id);
        Incoming(snapshot, id);
    }

    /// <summary>
    /// Who holds what here (U2, K6b): one row per plan and item, in the snapshot's (plan, storage,
    /// item) order. A plan put on hold still holds its stock, which is exactly when the player
    /// needs to see whose it is, so the row says so.
    /// </summary>
    private void Claims(WorldSnapshot snapshot, StorageId id)
    {
        var claims = snapshot.Claims.Where(c => c.Storage == id && c.Held > 0).ToList();
        if (claims.Count == 0)
        {
            return;
        }

        Heading("HELD FOR PLANS", new IconRef("status", "durability"));
        foreach (var claim in claims)
        {
            var onHold = snapshot.Plans.FirstOrDefault(p => p.Id == claim.Plan)?.Held == true;
            Row(
                $"#{claim.Plan.Value} · {claim.Item.Value}",
                onHold ? $"{Units.Format(claim.Held)} · PLAN ON HOLD" : Units.Format(claim.Held),
                onHold ? ShellPalette.StateWarn : ShellPalette.TextPrimary,
                null,
                new IconRef("item", claim.Item.Value));
        }
    }

    /// <summary>
    /// What is on its way here (U2): belt cargo bound for this storage, then runs that will deposit
    /// into it, read off <see cref="StockLocations.BoundFor"/> so the inspector and anything else
    /// asking cannot disagree about what counts as coming.
    /// </summary>
    private void Incoming(WorldSnapshot snapshot, StorageId id)
    {
        var incoming = StockLocations.BoundFor(snapshot, id);
        if (incoming.Count == 0)
        {
            return;
        }

        Heading("INCOMING", new IconRef("control", "chevron_right"));
        foreach (var arriving in incoming)
        {
            var from = arriving.Place == StockPlace.Belt
                ? snapshot.Transports.FirstOrDefault(t => t.Id == arriving.From)?.Label
                : snapshot.Executors.FirstOrDefault(e => e.Id == arriving.From)?.Label;
            var how = arriving.Place == StockPlace.Belt ? "ON BELT" : "IN RUN";
            Row(
                from ?? arriving.From.Value,
                $"{arriving.Item.Value.ToUpperInvariant()} {Units.Format(arriving.Amount)} · {how}",
                ShellPalette.TextPrimary,
                null,
                new IconRef("item", arriving.Item.Value));
        }
    }

    private const int MaxQueueRows = 8;

    /// <summary>
    /// One task in up to three lines: what it runs and how far along; which plan it is for, at what
    /// priority (that line opens the plan); and who it waits behind, when K8 names someone. A task
    /// queued by hand says so and opens nothing. A standing order shows a running total rather than
    /// a ratio with an invented denominator.
    /// </summary>
    private void QueueRow(FacilityQueueRow row)
    {
        var progress = row.Runs is { } requested
            ? $"{row.CompletedRuns}/{requested}"
            : $"{row.CompletedRuns} · STANDING";
        var (state, color, glyph) = row.Reason is { } reason
            ? ($"WAITING — {Conditions.Describe(reason)}", Conditions.Colour(reason), "blocked")
            : row.Current
                ? ("RUNNING", ShellPalette.StateOk, "active")
                : ("QUEUED", ShellPalette.TextFaint, "idle");
        Row($"{(row.Current ? "▶ " : string.Empty)}{row.Schematic.Value} {progress}", state, color, null,
            new IconRef("status", glyph), row.Plan);

        var standing = row.Held
            ? " · HELD"
            : row.Priority != Priority.Normal ? $" · {row.Priority.ToString().ToUpperInvariant()}" : string.Empty;
        if (row.Plan is { } plan && row.PlanGoal is { } goal)
        {
            Row($"  FOR PLAN {plan}", $"→ {Units.Format(goal.Quantity)} {Labels.Item(goal.Item)}{standing}",
                ShellPalette.TextDim, null, null, plan);
        }
        else
        {
            Row("  BY HAND", standing.Length == 0 ? "NORMAL" : standing.TrimStart(' ', '·'), ShellPalette.TextFaint);
        }

        if (row.Waiting is { } cause)
        {
            Row("  BEHIND", cause.Text, ShellPalette.StateWarn, null, null, row.Plan);
        }
    }

    /// <summary>
    /// The quote's rows. Cost is what leaves the hold. A shortage reads as a wait, because it never
    /// blocks approval, and a warning as a note, because the draft would commit and might never
    /// deliver. What blocks is named in the fault colour.
    /// </summary>
    private void Quote(WorldSnapshot snapshot, BuildQuote quote)
    {
        if (quote.MadeAt is { } at)
        {
            var label = snapshot.Executors.FirstOrDefault(e => e.Id == at)?.Label ?? at.Value;
            Row("MADE AT", label.ToUpperInvariant(), ShellPalette.TextPrimary, null, new IconRef("status", "active"));
        }

        for (var i = 0; i < quote.FromHold.Count; i++)
        {
            var cost = quote.FromHold[i];
            Row(i == 0 ? "COST" : string.Empty, $"{Units.Format(cost.Quantity)} {Labels.Item(cost.Item)}",
                ShellPalette.TextPrimary, null, new IconRef("item", cost.Item.Value));
        }

        foreach (var shortage in quote.Short)
        {
            Row("SHORT", $"WILL WAIT FOR {Units.Format(shortage.Quantity)} {Labels.Item(shortage.Item)}",
                ShellPalette.StateWarn, null, new IconRef("item", shortage.Item.Value));
        }

        foreach (var warning in quote.Warnings)
        {
            Row("NOTE", $"{Labels.Issue(warning.Kind)} · {Labels.Item(warning.Item)}", ShellPalette.StateWarn);
        }

        foreach (var blocking in quote.Blocking)
        {
            Row("BLOCKED", $"{Labels.Issue(blocking.Kind)} · {Labels.Item(blocking.Item)}",
                ShellPalette.StateFault, null, new IconRef("status", "blocked"));
        }

        Row("EST", Units.FormatSimTime(quote.EstimatedTicks), ShellPalette.TextPrimary, null,
            new IconRef("status", "time"));
    }

    /// <summary>
    /// The first press arms; the second composes a fresh draft and approves it, so anything
    /// committed or built since the preview is accounted for. A refusal is shown as the planner's
    /// issues and disarms. Acceptance needs no words: the engine rebuilt its snapshot inside the
    /// commit, so the next frame shows the slot QUEUED and the button VIEW PLAN, paused or not.
    /// </summary>
    private void OnBuildPressed(ConstructionOrder order)
    {
        if (!_buildGate.Press())
        {
            _actionButton.Text = "CONFIRM BUILD";
            return;
        }

        _actionButton.Text = "BUILD";
        if (_context?.ComposeDraft is not { } compose || _context.Actions.PlanApproved is not { } approve)
        {
            return;
        }

        _buildRefusal = approve(compose(order.Goal, order.Destination, order.Slot)) is PlanApprovalRefused refused
            ? "REFUSED: " + string.Join(", ", refused.Issues.Select(i => Labels.Issue(i.Kind)).Distinct())
            : null;
        _actionFeedback.Text = _buildRefusal ?? string.Empty;
        _actionFeedback.Visible = _buildRefusal is not null;
    }

    /// <summary>
    /// The selected thing is not in this snapshot. Said plainly rather than falling back to an
    /// empty state, which would read as "nothing is selected" and be a different, wrong answer.
    /// </summary>
    private void Gone(string id)
    {
        Head(id.ToUpperInvariant(), string.Empty, new IconRef("status", "blocked"));
        Row(
            "STATUS",
            "NO LONGER PRESENT",
            ShellPalette.StateFault,
            null,
            new IconRef("status", "blocked"));
    }

    private static float Fill(long amount, long capacity) =>
        capacity <= 0 ? 0f : Mathf.Clamp((float)((double)amount / capacity), 0f, 1f);

    /// <summary>
    /// A transport line's status. A production facility reads <see cref="ExecutorCondition"/>
    /// instead (U3); a line needs no such projection, because the engine already sets its blocked
    /// status only for cargo it cannot put down.
    /// </summary>
    private static (string Text, Color Color) Status(ExecutorStatus status, PostponeReason? reason) =>
        status switch
        {
            ExecutorStatus.RunningTask => ("Production", ShellPalette.StateOk),
            ExecutorStatus.SwitchingOver => ("Reconfiguration", ShellPalette.StateWarn),
            ExecutorStatus.AllQueuedTasksBlocked =>
                ($"Blocked — {Conditions.Describe(reason)}", ShellPalette.StateFault),

            // Not a fault. A line with an empty belt is stopping nothing, whatever its queued
            // transfers are waiting on — and each of those says so on its own row below.
            ExecutorStatus.NothingToCarry => ("Nothing to carry", ShellPalette.TextDim),
            _ => ("Idle", ShellPalette.TextDim),
        };

    /// <summary>
    /// The status domain's name for a facility state. It sits beside <see cref="Status"/> rather
    /// than inside it because the two are read at different rates: the word and its colour change
    /// on the tick, the glyph only when the state itself does.
    /// </summary>
    private static string Glyph(ExecutorStatus status) => status switch
    {
        ExecutorStatus.RunningTask => "active",
        ExecutorStatus.SwitchingOver => "time",
        ExecutorStatus.AllQueuedTasksBlocked => "blocked",
        ExecutorStatus.NothingToCarry => "idle",
        _ => "idle",
    };

    private static (string Text, Color Color) TaskState(TaskState state, PostponeReason? reason) =>
        state switch
        {
            Core.Simulation.TaskState.Running => ("RUNNING", ShellPalette.StateOk),
            Core.Simulation.TaskState.Complete => ("COMPLETE", ShellPalette.TextDim),
            Core.Simulation.TaskState.Postponed =>
                ($"POSTPONED — {Conditions.Describe(reason)}", Conditions.Colour(reason)),
            _ => ("NOT STARTED", ShellPalette.TextFaint),
        };

    private void Head(string title, string subtitle, IconRef? icon)
    {
        _title.Text = title.ToUpperInvariant();
        _subtitle.Text = subtitle;

        if (icon is { } reference)
        {
            _icon.SetIcon(reference.Domain, reference.Name);
        }
        else
        {
            _icon.Clear();
        }
    }

    private void Row(string name, string value, bool prose = false) =>
        Row(name, value, ShellPalette.TextPrimary, prose: prose);

    private void Sync()
    {
        while (_rows.GetChildCount() < _lines.Count)
        {
            _rows.AddChild(new DetailRow { Open = OpenPlan });
        }

        while (_rows.GetChildCount() > _lines.Count)
        {
            var extra = _rows.GetChild(_rows.GetChildCount() - 1);
            _rows.RemoveChild(extra);
            extra.QueueFree();
        }

        for (var i = 0; i < _lines.Count; i++)
        {
            ((DetailRow)_rows.GetChild(i)).Update(_lines[i]);
        }
    }

    /// <summary>
    /// One rendered line, flattened from whichever kind of thing produced it. A line's value is a
    /// reading, drawn in <see cref="ShellTheme.Value"/>'s fixed-width face, unless
    /// <paramref name="IsProse"/> says it is a sentence.
    /// </summary>
    private readonly record struct Line(
        string Name, string Value, Color Color, float? Fill, bool IsHeading, IconRef? Icon, PlanId? Opens,
        bool IsProse = false);

    private void Heading(string text, IconRef? icon = null) =>
        _lines.Add(
            new Line(text.ToUpperInvariant(), string.Empty, ShellPalette.TextFaint, null, true, icon, null));

    private void Row(
        string name, string value, Color color, float? fill = null, IconRef? icon = null, PlanId? opens = null,
        bool prose = false) =>
        _lines.Add(new Line(name.ToUpperInvariant(), value, color, fill, false, icon, opens, prose));

    /// <summary>A queue row's press: Operations opened on the plan, the same door VIEW PLAN uses.</summary>
    private void OpenPlan(PlanId plan) =>
        _context?.Actions.OperationsRequested?.Invoke(new PendingOperationsTarget(Plan: plan));

    /// <summary>
    /// Which icon a line carries, if any. A pair rather than one <c>domain/name</c> string because
    /// that is what <see cref="IconSlot.SetIcon"/> takes, and splitting one back apart on every
    /// row of every snapshot would be work done purely to undo the joining.
    /// </summary>
    private readonly record struct IconRef(string Domain, string Name);

    /// <summary>
    /// A name, a value, and optionally a bar. One row type rather than three keeps the list sync
    /// above a single loop no matter what the selection is.
    /// </summary>
    private sealed partial class DetailRow : VBoxContainer
    {
        /// <summary>These bars are 4px, which is below the height a rounded fill is legible at.</summary>
        private const int BarHeight = 4;

        /// <summary>
        /// What a press on a row naming a plan does. A row is recycled between lines, so the plan
        /// is read from the line it holds now, never captured when the row was made. U4 made the
        /// queue's rows open their plan, and a separate pool of buttons beside these rows would
        /// have split the panel's order in two.
        /// </summary>
        public System.Action<PlanId>? Open { get; init; }

        private PlanId? _opens;

        public override void _GuiInput(InputEvent @event)
        {
            if (_opens is { } plan && @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                Open?.Invoke(plan);
                AcceptEvent();
            }
        }

        private IconSlot _icon = null!;
        private Label _name = null!;
        private Label _value = null!;
        private ProgressBar _bar = null!;
        private Color? _lastColor;
        private bool? _lastHeading;
        private bool? _lastProse;

        public override void _Ready()
        {
            AddThemeConstantOverride("separation", 0);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
            AddChild(row);

            // Always present, empty when the line has no icon: rows are recycled between
            // selections, and a slot that came and went would step every name in the list
            // sideways as the player clicked from one node to the next.
            _icon = new IconSlot(IconSlot.RowSize, ShellPalette.TextDim);
            row.AddChild(_icon);

            _name = new Label();
            _name.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            row.AddChild(_name);

            // The spacer and the bar ignore the mouse. A plain Control stops it by default, and
            // these two cover most of a row, so a queue row opened its plan only when the press
            // landed on its text.
            row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });

            _value = new Label();
            _value.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            row.AddChild(_value);

            _bar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 1,
                ShowPercentage = false,
                CustomMinimumSize = new Vector2(0, BarHeight),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _bar.AddThemeStyleboxOverride("background", ShellTheme.MeterTrough(BarHeight));
            AddChild(_bar);
        }

        public void Update(Line line)
        {
            _opens = line.Opens;
            MouseFilter = line.Opens is null ? MouseFilterEnum.Pass : MouseFilterEnum.Stop;
            MouseDefaultCursorShape = line.Opens is null ? CursorShape.Arrow : CursorShape.PointingHand;
            _name.Text = line.Name;
            _value.Text = line.Value;

            // Rows are recycled between selections, so the face is set from the line each time,
            // and only when it changes, like the colour below.
            if (_lastProse != line.IsProse)
            {
                _lastProse = line.IsProse;
                _value.ThemeTypeVariation = line.IsProse ? ShellTheme.Prose : ShellTheme.Value;
            }

            if (line.Icon is { } reference)
            {
                _icon.SetIcon(reference.Domain, reference.Name);
            }
            else
            {
                _icon.Clear();
            }

            _bar.Visible = line.Fill.HasValue;
            if (line.Fill is { } fill)
            {
                _bar.Value = fill;
            }

            // Text dedupes internally; the theme overrides do not, so they are skipped whenever
            // nothing about the row's colour or kind has actually changed.
            if (_lastColor != line.Color || _lastHeading != line.IsHeading)
            {
                _lastColor = line.Color;
                _lastHeading = line.IsHeading;
                _name.AddThemeColorOverride(
                    "font_color", line.IsHeading ? ShellPalette.TextFaint : ShellPalette.TextDim);
                _value.AddThemeColorOverride("font_color", line.Color);
                _bar.AddThemeStyleboxOverride("fill", ShellTheme.MeterFill(line.Color, BarHeight));

                // The glyph takes the row's own colour, so a blocked line reads as blocked from
                // its icon as well as from the word and the bar beside it.
                _icon.SetTint(line.IsHeading ? ShellPalette.TextFaint : line.Color);
            }
        }
    }
}
