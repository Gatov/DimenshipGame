using Dimenship.Core.Planning;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;

namespace Dimenship.Replay;

/// <summary>
/// A reference controller (E2): a policy the replay runs beside a script, deciding once per tick.
/// <para>
/// It is C#, not a program, by the scheduling plan's Decision 6: the program language does not
/// exist, and <c>programs.json</c> is required to be empty. What keeps it honest is the door it
/// uses. It reads the snapshot, and changes the world only through <see cref="ControllerContext"/>,
/// which is <see cref="SimulationEngine.Execute"/> and the composer's draft, approve and commit
/// path. Those are the player's own controls, so a policy that wins here is one a player could
/// play, and one a program could later run, with no command the other lacks.
/// </para>
/// <para>
/// A controller must be deterministic. Nothing it reads may be a clock or a random source, and
/// nothing it iterates may be a hash order, or the report stops being one report per script.
/// </para>
/// </summary>
public interface IController
{
    /// <summary>The policy's name, as the report prints it.</summary>
    string Name { get; }

    /// <summary>Called once per tick, after the script's demands and commands for that tick.</summary>
    void Decide(ControllerContext context);
}

/// <summary>
/// A plan the run committed, as a controller sees it: the demand or order that committed it, what
/// it asked for and where, and whether the controller placed it. <paramref name="Id"/> is the
/// script's demand id, or <c>{item}#{n}</c> for the controller's own order.
/// </summary>
public sealed record KnownPlan(
    string Id, ItemAmount Goal, StorageId? Destination, ExecutorId? Assemble, PlanId Plan, bool ByController);

/// <summary>
/// What a controller may read and do. Reading is the snapshot and the plans this run committed;
/// doing is a command or an order, and each one accepted is an intervention in the report.
/// </summary>
public sealed class ControllerContext
{
    private readonly Replay.Accumulators _run;

    internal ControllerContext(Replay.Accumulators run)
    {
        _run = run;
    }

    public long Now => _run.Engine.Snapshot.Tick;

    public WorldSnapshot Snapshot => _run.Engine.Snapshot;

    /// <summary>The storage the planner routes through and counts as supply.</summary>
    public StorageId Hold => ((IWorldView)_run.Engine).Hold;

    /// <summary>Every plan a scripted demand or the controller committed, in commit order.</summary>
    public IReadOnlyList<KnownPlan> Plans => _run.Known;

    /// <summary>
    /// What the hold has of an item that no plan holds: the planner's own reading of supply
    /// (<see cref="IWorldView.InHold"/>), so a controller and the planner it orders through never
    /// disagree about what is there.
    /// </summary>
    public long FreeInHold(ItemId item) => ((IWorldView)_run.Engine).InHold(item);

    /// <summary>One command through <see cref="SimulationEngine.Execute"/>.</summary>
    public CommandResult Execute(Command command) => _run.Execute(command);

    /// <summary>
    /// Orders <paramref name="goal"/> as the Operations composer would, and approves it. Null when
    /// approval refused it. <paramref name="destination"/> null means the hold.
    /// </summary>
    public PlanId? Order(ItemAmount goal, StorageId? destination = null) => _run.Order(goal, destination);
}

/// <summary>
/// The baseline: decides nothing. Every demand runs as committed, in the engine's own selection
/// order. This is what every replay before E2 measured.
/// </summary>
public sealed class QueueOrder : IController
{
    public string Name => "queue order";

    public void Decide(ControllerContext context)
    {
    }
}

/// <summary>
/// Simple replenishment, the first rung of the scheduling design's learning progression (§6): keep
/// a target stock of each item that has been demanded, and order more whenever a reading falls
/// short.
/// <para>
/// The target is learned, not configured: the largest single demand seen so far for that item,
/// construction demands aside. A configured target would be a parameter tuned per situation, and
/// the point of this policy is to be the one a player writes first, without tuning.
/// </para>
/// <para>
/// Its flaw is deliberate, and it is the one the design names. A reading counts only free stock in
/// the hold, never what is already being made or carried, so every reading taken while an order is
/// still in production orders again. The design's own sentence is that such a policy "repeatedly
/// changes recipes"; this one also over-produces, and the report's end stock shows by how much.
/// Readings are taken every <see cref="ReadingTicks"/>, not every tick: once a tick it would order
/// hundreds of duplicates per shortage, which measures nothing but the queue's length.
/// </para>
/// </summary>
public sealed class SimpleReplenishment : IController
{
    /// <summary>Five operational minutes between readings.</summary>
    public const long ReadingTicks = 5 * Units.TicksPerMinute;

    private readonly List<ItemId> _stocked = new();
    private readonly Dictionary<ItemId, long> _targets = new();
    private int _seen;

    public string Name => "simple replenishment";

    public void Decide(ControllerContext context)
    {
        for (; _seen < context.Plans.Count; _seen++)
        {
            var plan = context.Plans[_seen];
            if (plan.ByController || plan.Assemble is not null)
            {
                continue;
            }

            var item = plan.Goal.Item;
            if (!_targets.ContainsKey(item))
            {
                _stocked.Add(item);
            }

            _targets[item] = Math.Max(_targets.GetValueOrDefault(item), plan.Goal.Quantity);
        }

        if (context.Now % ReadingTicks != 0)
        {
            return;
        }

        foreach (var item in _stocked)
        {
            var shortBy = _targets[item] - context.FreeInHold(item);
            if (shortBy > 0)
            {
                context.Order(new ItemAmount(item, shortBy));
            }
        }
    }
}

/// <summary>
/// The reference controllers by the name the command line takes. A controller holds state across
/// ticks, so each run gets a fresh one.
/// </summary>
public static class Policies
{
    /// <summary>In the order the scheduling design lists them; the first is the default.</summary>
    public static IReadOnlyList<string> Names { get; } = new[] { "queue-order", "replenishment", "improved" };

    public static IController? Create(string name) => name switch
    {
        "queue-order" => new QueueOrder(),
        "replenishment" => new SimpleReplenishment(),
        "improved" => new ImprovedController(),
        _ => null,
    };
}
