namespace Dimenship.Shell;

/// <summary>
/// What the shell knows about a panel without constructing it.
/// <para>
/// <paramref name="PreviewNote"/> marks a concept preview: a view whose system does not exist yet,
/// so nothing composed in it runs and nothing in it is kept. It is one plain sentence saying so,
/// shown before the player opens the view rather than only inside it (UX-08), because the place to
/// learn that an editor's work goes nowhere is before doing the work. Null for every live view.
/// It is a sentence rather than a flag so the rail cannot show a preview mark with nothing to say
/// beside it.
/// </para>
/// </summary>
public sealed record PanelDescriptor(PanelId Id, string Title, ZoneKind Zone, string? PreviewNote = null);
