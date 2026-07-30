using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Production;

public class EnergyTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly ExecutorId Refinery = new("refinery");

    private static WorldBuilder Smelter(
        long effort,
        long energy,
        long workRate,
        long standingDraw = 0,
        long capacity = 1_000_000,
        int runs = 1,
        long switchOverTicks = 0,
        SchematicId? initialSchematic = null) =>
        new WorldBuilder()
            .Energy(capacity)
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 10_000))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.Refinery,
                effort: effort, energy: energy, inputs: new ItemAmount(Ore, 10))
            .Producer(
                Refinery, FacilityType.Refinery, initialSchematic ?? Smelt,
                workRate: workRate, standingDraw: standingDraw, switchOverTicks: switchOverTicks)
            .Task(Smelt, runs, Refinery);

    /// <summary>Total draw across <paramref name="ticks"/> ticks, one tick at a time.</summary>
    private static long DrawOver(SimulationEngine engine, int ticks)
    {
        var total = 0L;
        for (var i = 0; i < ticks; i++)
        {
            engine.Advance(1);
            total += engine.Snapshot.Energy.Draw;
        }

        return total;
    }

    [Test]
    public void AnIdleExecutor_DrawsExactlyItsStandingPower()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.Refinery, energy: 5_000)
            .Producer(Refinery, FacilityType.Refinery, Smelt, standingDraw: 250)
            .Engine();

        engine.Advance(1);

        Assert.That(engine.Snapshot.Energy.Draw, Is.EqualTo(250));
        Assert.That(engine.Snapshot.Executors[0].PowerDraw, Is.EqualTo(250));
        Assert.That(engine.Snapshot.Executors[0].Status, Is.EqualTo(ExecutorStatus.NoTasksQueued));
    }

    [TestCase(100L, 100L, 1, TestName = "EnergyPerRun_effort_equals_rate")]
    [TestCase(250L, 100L, 3, TestName = "EnergyPerRun_effort_not_divisible_by_rate")]
    [TestCase(300L, 100L, 3, TestName = "EnergyPerRun_effort_divisible_by_rate")]
    [TestCase(90L, 7L, 13, TestName = "EnergyPerRun_awkward_ratio")]
    public void ACompletedRun_ChargesExactlyTheSchematicsEnergy(long effort, long workRate, int ticks)
    {
        // Whatever the effort-to-work-rate ratio, and however the proportional charge rounds on
        // the way, the total over a finished run is the schematic's energy to the unit. The
        // remainder settles on the final tick because that tick's cumulative work is the whole
        // effort — no special case, and no drift over thousands of runs.
        const long energyPerRun = 1_234;
        var engine = Smelter(effort, energyPerRun, workRate).Engine();

        var total = DrawOver(engine, ticks);

        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(1), "the run did finish");
        Assert.That(total, Is.EqualTo(energyPerRun));
    }

    [Test]
    public void ManyRuns_DoNotAccumulateRoundingError()
    {
        const long energyPerRun = 1_234;
        const int runs = 50;
        var engine = Smelter(effort: 250, energy: energyPerRun, workRate: 100, runs: runs).Engine();

        var total = DrawOver(engine, runs * 3);

        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(runs));
        Assert.That(total, Is.EqualTo(energyPerRun * runs));
    }

    [Test]
    public void APartlyDoneRun_HasChargedOnlyForTheWorkDone()
    {
        // 1,000 energy over 400 effort at 100 per tick: after one tick a quarter of the work is
        // done, so a quarter of the energy is spent — not all of it up front, and not none.
        var engine = Smelter(effort: 400, energy: 1_000, workRate: 100).Engine();

        engine.Advance(1);
        Assert.That(engine.Snapshot.Energy.Draw, Is.EqualTo(250));

        engine.Advance(1);
        Assert.That(engine.Snapshot.Energy.Draw, Is.EqualTo(250));
        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(0), "still mid-run");
    }

    [Test]
    public void SwitchingOver_CostsStandingPowerAndNothingElse()
    {
        // Reconfiguration does no work, so it takes no production charge. This is the one place
        // where "energy is proportional to work" is directly observable as a zero.
        var fabricate = new SchematicId("fabricate");
        var factory = new ExecutorId("factory");
        var chip = WorldBuilder.Chip;

        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Item(chip)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 1_000))
            .Schematic(fabricate, new ItemAmount(chip, 1), FacilityType.Factory,
                energy: 9_000, inputs: new ItemAmount(Ore, 10))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.Factory,
                energy: 9_000, inputs: new ItemAmount(Ore, 10))
            .Producer(factory, FacilityType.Factory, fabricate, standingDraw: 300, switchOverTicks: 4)
            .Task(fabricate, 1, factory)
            .Task(Smelt, 1, factory)
            .Engine();

        engine.Advance(1);
        Assert.That(engine.Snapshot.Energy.Draw, Is.EqualTo(9_300), "the chip run, plus standing");

        for (var tick = 2; tick <= 5; tick++)
        {
            engine.Advance(1);
            Assert.That(engine.Snapshot.Executors[0].Status, Is.EqualTo(ExecutorStatus.SwitchingOver));
            Assert.That(engine.Snapshot.Energy.Draw, Is.EqualTo(300), $"tick {tick} is reconfiguration only");
        }
    }

    [Test]
    public void AProductionChargeThatWouldExceedCapacity_IsRefusedAndTheRunHolds()
    {
        // The run is already under way and its inputs are spent, so voiding it would destroy
        // them. It postpones and picks up exactly where it stopped when power frees up.
        var hog = new ExecutorId("hog");
        var mine = new SchematicId("mine");

        var engine = new WorldBuilder()
            .Energy(10_000)
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 1_000))
            // Listed first, so it claims power first, and needs two ticks to get out of the way.
            .Schematic(mine, new ItemAmount(Ore, 1), FacilityType.Extractor, effort: 200, energy: 18_000)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.Refinery,
                effort: 200, energy: 4_000, inputs: new ItemAmount(Ore, 10))
            .Producer(hog, FacilityType.Extractor, mine)
            .Producer(Refinery, FacilityType.Refinery, Smelt)
            .Task(mine, 1, hog)
            .Task(Smelt, 1, Refinery)
            .Engine();

        engine.Advance(1);

        var refinery = engine.Snapshot.Executors.Single(e => e.Id == Refinery);
        Assert.That(refinery.BlockReason, Is.EqualTo(PostponeReason.InsufficientEnergy));
        Assert.That(engine.Snapshot.Energy.StarvedTicks, Is.EqualTo(1));
        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(990), "its inputs were taken and are not lost");

        var task = engine.Snapshot.ProductionTasks.Single(t => t.Executor == Refinery);
        Assert.That(task.State, Is.EqualTo(TaskState.Postponed));

        // Once the hog's run finishes, the refinery gets its power and completes the run it held.
        engine.Advance(3);
        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(1));
    }

    [Test]
    public void StandingDrawBeyondCapacity_IsADefinitionError()
    {
        // Standing draw is never refused, so a vessel that cannot cover it could not honour both
        // that rule and the invariant that draw never exceeds capacity. Better a loud failure at
        // construction than a negative reserve a thousand ticks later.
        var world = new WorldBuilder()
            .Energy(1_000)
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.Refinery)
            .Producer(Refinery, FacilityType.Refinery, Smelt, standingDraw: 900)
            .Sink("stabilization_field", 400);

        var thrown = Assert.Throws<ArgumentException>(() => world.Engine());

        Assert.That(thrown!.Message, Does.Contain("1300").Or.Contain("1,300"));
    }

    [Test]
    public void DrawNeverExceedsCapacity_OverALongRun()
    {
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        for (var i = 0; i < 500; i++)
        {
            engine.Advance(1);
            Assert.That(
                engine.Snapshot.Energy.Draw,
                Is.LessThanOrEqualTo(engine.Snapshot.Energy.Capacity));
        }
    }
}
