using System.Linq;
using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The vessel's energy pool. Placed like every other card and drawn with no edges at all: energy is
/// a global pool, and running power lines to the facilities that draw from it would be a lie about
/// how it works.
/// </summary>
public sealed partial class PowerCard : NodeCard
{
    public const string NodeId = "power";

    private Label _capacity = null!;
    private Label _stability = null!;
    private Label _faults = null!;
    private CardMeter _draw = null!;

    public PowerCard(string badge)
        : base(new GraphSelection(GraphNodeKind.Power, NodeId), "Power", badge, "power")
    {
    }

    protected override void BuildBody(VBoxContainer column)
    {
        _capacity = Row(column, ShellPalette.TextDim);
        _draw = Meter(column, ShellPalette.StateWarn);

        // Above the faults line, because it is a reading and the line below it is a count of things
        // that went wrong; a card whose readings and its faults interleave has to be parsed rather
        // than glanced at.
        _stability = Row(column, ShellPalette.TextFaint);
        _faults = Row(column, ShellPalette.TextFaint);
    }

    public override void Refresh(WorldSnapshot snapshot)
    {
        var energy = snapshot.Energy;

        Status(
            "DRAW",
            $"{Units.Format(energy.Draw)} MW",
            energy.Reserve == 0 ? ShellPalette.StateFault : ShellPalette.StateWarn);

        _capacity.Text =
            $"CAP {Units.Format(energy.Capacity)} · RESERVE {Units.Format(energy.Reserve)}";
        _draw.Set(Fill(energy.Draw, energy.Capacity));

        // The largest standing claim on the pool above, read here rather than given a node of its
        // own: a stabilization field has no route and no material, so authoring it a cell would be
        // authoring a placement for something that can never have an edge. A second compact reading
        // on the card that already owns energy is where it belongs.
        //
        // Named by id rather than taken as "the only sink" or "the largest one": a scenario may seed
        // more than one, and this row is about this one. An absent sink says so rather than reading
        // zero — a field drawing nothing and a field that is not there look identical as a number
        // and mean entirely different things.
        var stability = snapshot.Sinks.FirstOrDefault(
            sink => sink.Id == DefaultVessel.StabilizationField.Value);

        _stability.Text = stability is null
            ? "STABILITY —"
            : $"{stability.Label.ToUpperInvariant()} {Units.Format(stability.PowerDraw)} MW";

        // Cap hits and starvation answer different questions and neither implies the other, so
        // both are shown even when they read zero.
        _faults.Text = $"CAP HITS {energy.CapHits} · STARVED {energy.StarvedTicks}";
    }
}
