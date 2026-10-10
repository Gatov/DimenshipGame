using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;

namespace Dimenship.Replay;

/// <summary>
/// The improved reference controller (E2): replenishment with the next rungs of the scheduling
/// design's learning progression (§6) added, and only the player's own commands used to add them.
/// <list type="number">
/// <item><b>Stock for what leaves.</b> It keeps stock of an item only once a demand has sent that
/// item off the vessel, to a Launch Pad hold or anywhere but the main hold, and its target is the
/// largest such demand. A demand bound for the hold is itself a stock order, and replenishing
/// behind it is ordering it twice.</item>
/// <item><b>Accounting for outstanding work.</b> A reading counts free stock in the hold plus the
/// goal of every active plan bound for the hold with that item, the script's or its own. A
/// shortage already being made is not ordered again, which is the flaw
/// <see cref="SimpleReplenishment"/> keeps on purpose.</item>
/// <item><b>Departing work first.</b> A departing demand's plan is raised to
/// <see cref="Priority.High"/> the tick it is seen. That reaches every prerequisite task, because a
/// plan task's priority is its plan's.</item>
/// <item><b>Holding work that can wait.</b> Plans rank as its own restock, then work the script
/// ordered into the hold, then departing work. A plan bound for the hold is held while it has
/// production outstanding at a facility where a higher-ranked plan also has, and released once it
/// has not. Stock-building elsewhere runs on: holding work at a factory nothing higher uses idles
/// that factory and speeds up nothing, which in situation A held Factories Beta and Gamma for over
/// 3,000 ticks each. The middle rank is what keeps a restock from fighting an upgrade the player
/// ordered, which in situation B doubled the upgrade's delay. A departing plan served from stock
/// is only a haul, so it holds nothing. Holding stops unstarted work only: runs in progress and
/// cargo aboard still finish (K6c).</item>
/// <item><b>Campaigns.</b> A restock orders <see cref="Campaign"/> targets' worth, so one
/// changeover serves more than one departure. Chosen on situation A; B is insensitive to it once
/// the middle rank exists.</item>
/// </list>
/// <para>
/// Construction is never held: a facility being built is capacity, and holding it is the opposite
/// of making room.
/// </para>
/// <para>
/// What it cannot do is as much a result as what it does. No command chooses which facility runs a
/// stage: that is the planner's, at commit, and the planner does not count a changeover. No command
/// moves committed work to an idle reactor. The E2 review reports these rather than working around
/// them here.
/// </para>
/// </summary>
public sealed class ImprovedController : IController
{
    /// <summary>A restock orders this many targets' worth, less what is already there or coming.</summary>
    public const long Campaign = 2;

    private readonly List<ItemId> _stocked = new();
    private readonly Dictionary<ItemId, long> _targets = new();
    private readonly HashSet<PlanId> _held = new();
    private int _seen;

    public string Name => "improved";

    public void Decide(ControllerContext context)
    {
        for (; _seen < context.Plans.Count; _seen++)
        {
            var plan = context.Plans[_seen];
            if (!Departing(plan, context))
            {
                continue;
            }

            var item = plan.Goal.Item;
            if (!_targets.ContainsKey(item))
            {
                _stocked.Add(item);
            }

            _targets[item] = Math.Max(_targets.GetValueOrDefault(item), plan.Goal.Quantity);
            context.Execute(new SetPlanPriority(plan.Plan, Priority.High));
        }

        var snapshot = context.Snapshot;
        var states = snapshot.Plans.ToDictionary(p => p.Id);
        var producing = snapshot.Tasks
            .Where(t => t.Action is Produce && t.State != TaskState.Complete)
            .ToDictionary(t => t.Id, t => t.Executor);
        bool Active(KnownPlan plan) => states[plan.Plan].State == PlanState.Active;

        IEnumerable<ExecutorId> Producers(KnownPlan plan) =>
            states[plan.Plan].SpawnedTasks.Where(producing.ContainsKey).Select(t => producing[t]);

        // wanted[r]: the facilities where a plan ranked above r still has production outstanding.
        var wanted = new HashSet<ExecutorId>[Departs + 1];
        for (var rank = Departs; rank >= 0; rank--)
        {
            wanted[rank] = rank == Departs ? new HashSet<ExecutorId>() : new HashSet<ExecutorId>(wanted[rank + 1]);
            if (rank < Departs)
            {
                wanted[rank].UnionWith(context.Plans
                    .Where(p => Rank(p, context) == rank + 1 && Active(p))
                    .SelectMany(Producers));
            }
        }

        foreach (var plan in context.Plans)
        {
            if (!StockBuilding(plan, context) || !Active(plan))
            {
                continue;
            }

            var contends = Producers(plan).Any(wanted[Rank(plan, context)].Contains);
            if (contends && _held.Add(plan.Plan))
            {
                context.Execute(new HoldPlan(plan.Plan));
            }
            else if (!contends && _held.Remove(plan.Plan))
            {
                context.Execute(new ReleasePlan(plan.Plan));
            }
        }

        if (context.Now % SimpleReplenishment.ReadingTicks != 0)
        {
            return;
        }

        foreach (var item in _stocked)
        {
            var coming = context.Plans
                .Where(p => StockBuilding(p, context) && p.Goal.Item == item && Active(p))
                .Sum(p => p.Goal.Quantity);
            var shortBy = _targets[item] - context.FreeInHold(item) - coming;
            if (shortBy > 0)
            {
                // Held next tick, by the loop above, if it lands on a facility departing work wants.
                context.Order(new ItemAmount(item, shortBy + (Campaign - 1) * _targets[item]));
            }
        }
    }

    /// <summary>Ranks, lowest first: the controller's own restock, then work the script ordered,
    /// then work leaving the vessel.</summary>
    private const int Restock = 0;
    private const int Ordered = 1;
    private const int Departs = 2;

    private static int Rank(KnownPlan plan, ControllerContext context) =>
        plan.ByController ? Restock : Departing(plan, context) ? Departs : Ordered;

    private static bool Departing(KnownPlan plan, ControllerContext context) =>
        !plan.ByController && plan.Assemble is null && plan.Destination is { } to && to != context.Hold;

    private static bool StockBuilding(KnownPlan plan, ControllerContext context) =>
        plan.Assemble is null && (plan.Destination is null || plan.Destination == context.Hold);
}
