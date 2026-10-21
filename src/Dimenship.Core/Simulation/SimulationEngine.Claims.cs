using Dimenship.Core.Production;
using Dimenship.Core.State;

namespace Dimenship.Core.Simulation;

public sealed partial class SimulationEngine
{
    /// <summary>
    /// What one task may withdraw from a storage: its plan's holding there plus free stock, or free
    /// stock alone for a task with no plan. Never another plan's holding (D3, Decision 5).
    /// </summary>
    private long Spendable(TaskInstance task, StorageId storage, ItemId item)
    {
        var own = State.Plans.Owning(task.Id) is { } plan ? State.Claims.Held(plan.Id, storage, item) : 0;
        return own + Free(storage, item);
    }

    /// <summary>A withdrawal by a plan's task draws its plan's holding first.</summary>
    private void ConsumeHeld(TaskInstance task, StorageId storage, ItemId item, long quantity)
    {
        if (State.Plans.Owning(task.Id) is not { } plan)
        {
            return;
        }

        var held = State.Claims.Held(plan.Id, storage, item);
        if (held > 0)
        {
            State.Claims.Change(plan.Id, storage, item, -Math.Min(held, quantity));
        }
    }

    /// <summary>
    /// D3, Decision 5's two steps for stock that just arrived. A delivery or deposit made by a plan's
    /// task is held for that plan, up to what its own withdrawals there still need: cargo keeps its
    /// owner. Whatever is left is free, and is offered to outstanding claims.
    /// </summary>
    private void AllocateArrival(TaskId by, StorageId storage, ItemId item, long quantity)
    {
        if (State.Plans.Owning(by) is { State: PlanState.Active } owner)
        {
            var room = ClaimMath.Need(Catalog, State, owner, storage, item) - State.Claims.Held(owner.Id, storage, item);
            var take = Math.Min(Math.Min(quantity, room), Free(storage, item));
            if (take > 0)
            {
                State.Claims.Change(owner.Id, storage, item, take);
            }
        }

        AllocateFree(storage, item);
    }

    /// <summary>
    /// Offers free stock at one storage to the outstanding claims there, by priority descending,
    /// then plan id ascending, skipping held plans; each takes as much as it is still short. Run on
    /// every change that can leave free stock beside an outstanding claim, which is what keeps the
    /// invariant (<see cref="ClaimInvariantViolations"/>).
    /// </summary>
    private void AllocateFree(StorageId storage, ItemId item)
    {
        var free = Free(storage, item);
        if (free <= 0)
        {
            return;
        }

        var eligible = State.Plans.Plans
            .Where(p => p.State == PlanState.Active && !p.Held)
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => p.Id.Value)
            .ToList();

        foreach (var plan in eligible)
        {
            var outstanding = Outstanding(plan, storage, item);
            if (outstanding <= 0)
            {
                continue;
            }

            var take = Math.Min(outstanding, free);
            State.Claims.Change(plan.Id, storage, item, take);
            free -= take;
            Emit(EventCategory.Planning, EventCode.ClaimAllocated, $"{storage}/{item}",
                new Dictionary<string, long> { ["plan"] = plan.Id.Value, ["quantity"] = take });

            if (free <= 0)
            {
                return;
            }
        }
    }

    private long Outstanding(CommittedPlan plan, StorageId storage, ItemId item) =>
        Math.Max(0,
            ClaimMath.Need(Catalog, State, plan, storage, item)
            - State.Claims.Held(plan.Id, storage, item)
            - ClaimMath.Inbound(Catalog, State, plan, storage, item));

    /// <summary>
    /// D3, Decision 5's invariant: at every storage and item, either no stock is free, or no plan
    /// that is active and not held has an outstanding claim there. Also every holding is within
    /// its plan's need and the stock present. Empty when it holds. For tests and the harness; the
    /// engine maintains it and never reads this.
    /// </summary>
    public IReadOnlyList<string> ClaimInvariantViolations()
    {
        var violations = new List<string>();
        foreach (var storage in State.Vessel.Storages)
        {
            foreach (var item in Catalog.Items)
            {
                var free = Available(storage.Id, item.Id) - State.Claims.HeldAt(storage.Id, item.Id);
                if (free < 0)
                {
                    violations.Add($"{storage.Id}/{item.Id}: {-free} more held than present");
                    continue;
                }

                if (free == 0)
                {
                    continue;
                }

                foreach (var plan in State.Plans.Plans.Where(p => p.State == PlanState.Active && !p.Held))
                {
                    if (Outstanding(plan, storage.Id, item.Id) > 0)
                    {
                        violations.Add($"{storage.Id}/{item.Id}: {free} free beside plan {plan.Id}'s outstanding claim");
                    }
                }
            }
        }

        foreach (var (plan, storage, item, held) in State.Claims.Entries)
        {
            var owner = State.Plans.Plans.FirstOrDefault(p => p.Id == plan);
            var need = owner is null ? 0 : ClaimMath.Need(Catalog, State, owner, storage, item);
            if (held > need)
            {
                violations.Add($"plan {plan} holds {held} of {item} at {storage}, needing {need}");
            }
        }

        return violations;
    }

    /// <summary>
    /// Gives up some of a plan's holding, which goes back through the allocation order (D3,
    /// Decision 6) — possibly straight back to the same plan, if it still ranks first. That is the
    /// command meaning "let the current order decide again".
    /// </summary>
    public void Relinquish(PlanId plan, StorageId storage, ItemId item, long quantity)
    {
        var committed = ActivePlan(plan);
        var give = Math.Min(Math.Max(0, quantity), State.Claims.Held(committed.Id, storage, item));
        if (give > 0)
        {
            State.Claims.Change(committed.Id, storage, item, -give);
            AllocateFree(storage, item);
        }

        Snapshot = BuildSnapshot();
    }

    /// <summary>
    /// Moves held stock from one plan to another, bounded by what the receiver is still short
    /// there: moving stock to a plan that does not need it would be a hoard no withdrawal backs.
    /// Any rest stays with the giver.
    /// </summary>
    public void Reassign(PlanId from, PlanId to, StorageId storage, ItemId item, long quantity)
    {
        var giver = ActivePlan(from);
        var receiver = ActivePlan(to);
        var move = Math.Min(
            Math.Min(Math.Max(0, quantity), State.Claims.Held(giver.Id, storage, item)),
            Outstanding(receiver, storage, item));

        if (move > 0)
        {
            State.Claims.Change(giver.Id, storage, item, -move);
            State.Claims.Change(receiver.Id, storage, item, move);
            Emit(EventCategory.Planning, EventCode.ClaimAllocated, $"{storage}/{item}",
                new Dictionary<string, long> { ["plan"] = receiver.Id.Value, ["quantity"] = move });
        }

        Snapshot = BuildSnapshot();
    }
}
