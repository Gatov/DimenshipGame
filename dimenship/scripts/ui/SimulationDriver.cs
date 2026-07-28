using System;
using Dimenship.Core.Simulation;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// Owns the kernel and decides how much time it is given. Everything the kernel is forbidden
/// to know about — wall-clock delta, pause, speed multiplier, failure recovery — lives here.
/// </summary>
public sealed partial class SimulationDriver : Node
{
    /// <summary>Index 0 is paused. The remaining entries are the selectable multipliers.</summary>
    public static readonly int[] Speeds = { 0, 1, 5, 30 };

    private readonly SimulationEngine _engine = new(WorldDefinition.CreateDefault());

    private double _accumulator;
    private int _speedIndex = 1;
    private int _resumeIndex = 1;

    public WorldSnapshot Snapshot => _engine.Snapshot;

    public int Speed => Speeds[_speedIndex];

    public bool IsPaused => _speedIndex == 0;

    /// <summary>Non-null once the kernel has thrown. The driver stays stopped until restart.</summary>
    public string? FaultMessage { get; private set; }

    public override void _Process(double delta)
    {
        if (FaultMessage is not null || IsPaused)
        {
            return;
        }

        _accumulator += delta * Speed;

        var ticks = (long)_accumulator;
        if (ticks <= 0)
        {
            return;
        }

        _accumulator -= ticks;
        SafeAdvance(ticks);
    }

    public void TogglePause()
    {
        if (IsPaused)
        {
            _speedIndex = _resumeIndex;
        }
        else
        {
            _resumeIndex = _speedIndex;
            _speedIndex = 0;
        }

        // Dropping the fraction avoids a burst of catch-up ticks on resume.
        _accumulator = 0;
    }

    /// <summary>Advances exactly one tick. Only meaningful while paused.</summary>
    public void Step()
    {
        if (IsPaused)
        {
            SafeAdvance(1);
        }
    }

    public void SpeedUp()
    {
        if (_speedIndex < Speeds.Length - 1)
        {
            _speedIndex++;
        }
    }

    public void SpeedDown()
    {
        if (_speedIndex > 0)
        {
            _speedIndex--;
        }
    }

    public void SetSpeed(int speed)
    {
        var index = Array.IndexOf(Speeds, speed);
        if (index >= 0)
        {
            _speedIndex = index;
        }
    }

    private void SafeAdvance(long ticks)
    {
        try
        {
            _engine.Advance(ticks);
        }
        catch (Exception e)
        {
            _speedIndex = 0;
            FaultMessage = $"Simulation fault at tick {_engine.Snapshot.Tick}: {e.GetType().Name}: {e.Message}";
            GD.PushError(FaultMessage);
        }
    }
}
