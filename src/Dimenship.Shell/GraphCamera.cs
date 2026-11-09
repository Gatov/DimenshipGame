namespace Dimenship.Shell;

/// <summary>
/// The base graph's camera: a zoom step and a pan, and the rules for moving them. Engine-free so
/// the rules are tested here rather than by eye; the Godot view holds a <see cref="View"/> and
/// applies it as the canvas's position and scale.
/// <para>
/// Zoom moves in fixed steps rather than continuously, so text stays on a few known sizes.
/// Zooming in is anchored on the cursor, so whatever is under it stays under it, which is what
/// zooming in to read something needs. Zooming out has a floor and a destination. The floor is
/// the largest step at which the whole scheme fits, because zooming out further only shrinks a
/// picture already in full view into a smaller one, which is what the camera did before. The
/// destination is the scheme centred in the view. Each step out moves the pan from the cursor's
/// anchor towards that centre by one part in however many steps remain to the floor, so the last
/// step lands exactly centred and the ones before it drift there rather than jumping. A step out
/// at the floor re-centres, so a scheme panned off to one side is always one gesture from home.
/// </para>
/// </summary>
public static class GraphCamera
{
    /// <summary>The zoom steps, in percent.</summary>
    public static readonly IReadOnlyList<int> Steps = new[] { 50, 75, 100, 150, 200 };

    /// <summary>100%, the step a session opens at.</summary>
    public const int RestingStep = 2;

    /// <summary>A camera: the zoom step, and where the content's origin sits in the view.</summary>
    public readonly record struct View(int Step, float PanX, float PanY)
    {
        public float Scale => Steps[Step] / 100f;
    }

    /// <summary>
    /// The largest step at which content of this size fits the viewport on both axes, or the
    /// smallest step when none does. With nothing measured yet, the smallest step: no floor until
    /// there is something to fit.
    /// </summary>
    public static int FitStep(float contentW, float contentH, float viewW, float viewH)
    {
        if (contentW <= 0 || contentH <= 0 || viewW <= 0 || viewH <= 0)
        {
            return 0;
        }

        for (var step = Steps.Count - 1; step >= 0; step--)
        {
            var scale = Steps[step] / 100f;
            if (contentW * scale <= viewW && contentH * scale <= viewH)
            {
                return step;
            }
        }

        return 0;
    }

    /// <summary>The whole scheme at <see cref="FitStep"/>, centred. What the F key does.</summary>
    public static View Fit(float contentW, float contentH, float viewW, float viewH)
    {
        var step = FitStep(contentW, contentH, viewW, viewH);
        var (x, y) = Centred(step, contentW, contentH, viewW, viewH);
        return new View(step, x, y);
    }

    /// <summary>One step in, keeping the content point under <paramref name="cursorX"/>, <paramref name="cursorY"/> where it is.</summary>
    public static View ZoomIn(View view, float cursorX, float cursorY)
    {
        if (view.Step >= Steps.Count - 1)
        {
            return view;
        }

        var (x, y) = Anchored(view, view.Step + 1, cursorX, cursorY);
        return new View(view.Step + 1, x, y);
    }

    /// <summary>
    /// One step out, towards the scheme centred at <see cref="FitStep"/>, and never past it. At or
    /// below the floor already, the step stays and the scheme is centred.
    /// </summary>
    public static View ZoomOut(
        View view, float cursorX, float cursorY, float contentW, float contentH, float viewW, float viewH)
    {
        var floor = Math.Min(FitStep(contentW, contentH, viewW, viewH), view.Step);
        if (view.Step <= floor)
        {
            var (cx, cy) = Centred(view.Step, contentW, contentH, viewW, viewH);
            return view with { PanX = cx, PanY = cy };
        }

        var next = view.Step - 1;
        var remaining = view.Step - floor;
        var (ax, ay) = Anchored(view, next, cursorX, cursorY);
        var (tx, ty) = Centred(next, contentW, contentH, viewW, viewH);

        return new View(
            next,
            MathF.Floor(ax + ((tx - ax) / remaining)),
            MathF.Floor(ay + ((ty - ay) / remaining)));
    }

    /// <summary>The pan that keeps the content point under the cursor there at another step.</summary>
    private static (float X, float Y) Anchored(View view, int step, float cursorX, float cursorY)
    {
        var ratio = (Steps[step] / 100f) / view.Scale;
        return (cursorX - ((cursorX - view.PanX) * ratio), cursorY - ((cursorY - view.PanY) * ratio));
    }

    /// <summary>The pan that puts content of this size in the middle of the view, on whole pixels.</summary>
    private static (float X, float Y) Centred(int step, float contentW, float contentH, float viewW, float viewH)
    {
        var scale = Steps[step] / 100f;
        return (MathF.Floor((viewW - (contentW * scale)) / 2f), MathF.Floor((viewH - (contentH * scale)) / 2f));
    }
}
