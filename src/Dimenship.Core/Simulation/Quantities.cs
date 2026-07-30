using System.Globalization;

namespace Dimenship.Core.Simulation;

/// <summary>A quantity of one item. The unit of every schematic input and output.</summary>
public readonly record struct ItemAmount(ItemId Item, long Quantity)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Quantity} {Item.Value}");
}

/// <summary>
/// An amount of production work. Divided by an executor's work rate to give a duration in
/// ticks, which is the only thing work is ever used for.
/// </summary>
public readonly record struct WorkAmount(long Value)
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// An amount of energy, in the same milliwatt units as the vessel's capacity and draw.
/// Independent of <see cref="WorkAmount"/>: a low-energy item may take a long time.
/// </summary>
public readonly record struct EnergyAmount(long Value)
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
