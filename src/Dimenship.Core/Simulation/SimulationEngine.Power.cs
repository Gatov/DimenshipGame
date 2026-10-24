using Dimenship.Core.Production;
using Dimenship.Core.State;

namespace Dimenship.Core.Simulation;

public sealed partial class SimulationEngine
{
    /// <summary>
    /// D3, Decision 4: when power is short, it goes by (effective priority descending, task id
    /// ascending) rather than by where a facility sits in the declaration. Every run already in
    /// progress at the start of the tick is granted or refused here, before any facility steps,
    /// skipping a charge that no longer fits as the visit-order loop always did. The facilities
    /// then step in visit order as before, and each applies its grant at its own turn.
    /// <para>
    /// A run that starts this tick is not known until its facility selects it, so it draws on what
    /// the grants left, in visit order. That is one tick of one run; from its second tick it ranks
    /// by priority like every other. D3's literal three-pass order (select every facility, then
    /// grant, then advance every facility) was rejected because it reorders the journal and lets
    /// one facility's deposit land after the next facility's selection. A tick with enough power
    /// would then no longer be identical to the code before this, which D3 itself requires it to
    /// be. Here, if every charge fits, every grant succeeds and each facility advances exactly
    /// where and when it did.
    /// </para>
    /// </summary>
    private void GrantPowerToRunsInProgress(List<FacilityInstance> producers)
    {
        _powerGrants.Clear();
        _reservedPower = 0;

        var inProgress = new List<(TaskInstance Task, long Charge)>();
        foreach (var executor in producers)
        {
            if (executor.SwitchOverRemaining == 0
                && CurrentOf(executor) is { RunActive: true, RunAwaitingDeposit: false } task)
            {
                inProgress.Add((task, RunCharge(executor, task, out _, out _)));
            }
        }

        inProgress.Sort((a, b) =>
        {
            var byPriority = PriorityOf(b.Task).CompareTo(PriorityOf(a.Task));
            return byPriority != 0 ? byPriority : a.Task.Id.Value.CompareTo(b.Task.Id.Value);
        });

        var available = State.Vessel.Energy.Capacity - State.Vessel.Energy.DrawLastTick;
        foreach (var (task, charge) in inProgress)
        {
            if (charge <= available)
            {
                available -= charge;
                _reservedPower += charge;
                _powerGrants[task.Id] = (true, 0);
            }
            else
            {
                _powerGrants[task.Id] = (false, available);
            }
        }
    }
}
