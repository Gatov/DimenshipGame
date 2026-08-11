using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The vessel's energy pool. Pinned, and drawn with no edges at all: energy is a global pool, and
/// running power lines to the facilities that draw from it would be a lie about how it works.
/// </summary>
public sealed partial class PowerCard : NodeCard
{
    public const string NodeId = "power";

    /// <summary>
    /// Power's badge is a constant here rather than authored content, because power has no
    /// placement to author it beside: it is a global pool the view pins, not a node of the world.
    /// </summary>
    private const string BadgeId = "P";

    private Label _capacity = null!;
    private Label _faults = null!;
    private CardMeter _draw = null!;

    public PowerCard()
        : base(new GraphSelection(GraphNodeKind.Power, NodeId), "Power", BadgeId, "power")
    {
    }

    protected override void BuildBody(VBoxContainer column)
    {
        _capacity = Row(column, ShellPalette.TextDim);
        _draw = Meter(column, ShellPalette.StateWarn);
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

        // Cap hits and starvation answer different questions and neither implies the other, so
        // both are shown even when they read zero.
        _faults.Text = $"CAP HITS {energy.CapHits} · STARVED {energy.StarvedTicks}";
    }
}
