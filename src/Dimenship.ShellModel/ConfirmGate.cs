namespace Dimenship.ShellModel;

/// <summary>
/// A control that acts only on its second press: the first arms it, the second confirms, and
/// anything else in between disarms it. Inspector BUILD and plan CANCEL are the two today. Each
/// once carried its own flag and its own copy of the arm, confirm and reset steps, and a third
/// irreversible control would have been a third copy to keep in step by hand.
/// <para>
/// It holds no subject. What disarms it (another selection, another plan, the draft stopping being
/// committable) belongs to the view that owns it, which calls <see cref="Disarm"/>. A gate that
/// tracked its subject would need a type parameter and an equality rule to answer a question each
/// owner already asks in one line.
/// </para>
/// </summary>
public sealed class ConfirmGate
{
    /// <summary>True between the arming press and whatever comes next.</summary>
    public bool Armed { get; private set; }

    /// <summary>
    /// A press of the guarded control. True when this press confirms, so the caller acts; false
    /// when it only armed the gate, so the caller relabels the control and waits.
    /// </summary>
    public bool Press()
    {
        if (Armed)
        {
            Armed = false;
            return true;
        }

        Armed = true;
        return false;
    }

    /// <summary>Any other input: the next press arms again rather than confirming.</summary>
    public void Disarm() => Armed = false;
}
