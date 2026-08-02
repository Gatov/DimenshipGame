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

    private Label _capacity = null!;
    private Label _faults = null!;
    private ProgressBar _draw = null!;

    public PowerCard()
        : base(new GraphSelection(GraphNodeKind.Power, NodeId), "Power")
    {
    }

    protected override void BuildBody(VBoxContainer column)
    {
        _capacity = Row(column, ShellPalette.TextDim);
        _draw = Bar(column, ShellPalette.StateWarn);
        _faults = Row(column, ShellPalette.TextFaint);
    }

    public override void Refresh(WorldSnapshot snapshot)
    {
        var energy = snapshot.Energy;

        Status(
            $"{Units.Format(energy.Draw)} MW DRAW",
            energy.Reserve == 0 ? ShellPalette.StateFault : ShellPalette.StateWarn);

        _capacity.Text =
            $"CAP {Units.Format(energy.Capacity)} · RESERVE {Units.Format(energy.Reserve)}";
        _draw.Value = Fill(energy.Draw, energy.Capacity);

        // Cap hits and starvation answer different questions and neither implies the other, so
        // both are shown even when they read zero.
        _faults.Text = $"CAP HITS {energy.CapHits} · STARVED {energy.StarvedTicks}";
    }
}
