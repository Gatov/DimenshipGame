using System;
using System.Collections.Generic;
using System.Linq;
using Dimenship.Shell;

namespace Dimenship.Ui;

/// <summary>Maps panel identifiers to the factories that build them.</summary>
public sealed class PanelRegistry
{
    private readonly Dictionary<PanelId, PanelDescriptor> _descriptors = new();
    private readonly Dictionary<PanelId, Func<PanelBase>> _factories = new();

    public IReadOnlyDictionary<PanelId, PanelDescriptor> Descriptors => _descriptors;

    public void Register(PanelDescriptor descriptor, Func<PanelBase> factory)
    {
        _descriptors[descriptor.Id] = descriptor;
        _factories[descriptor.Id] = factory;
    }

    public IEnumerable<PanelDescriptor> OfKind(ZoneKind kind) =>
        _descriptors.Values.Where(d => d.Zone == kind);

    /// <summary>Null when the identifier is unknown. Callers render a fault surface rather than crashing.</summary>
    public PanelBase? Create(PanelId id) =>
        _factories.TryGetValue(id, out var factory) ? factory() : null;
}
