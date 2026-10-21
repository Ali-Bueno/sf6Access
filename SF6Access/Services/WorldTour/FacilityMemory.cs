using System.Collections.Generic;

namespace SF6Access.Services.WorldTour;

/// <summary>Known static facilities for one readable city session, independent of loaded scenery.</summary>
internal sealed class FacilityMemory
{
    // The catalog's existing empty-list retry also bounds refreshes after discovering a facility.
    internal const long RefreshIntervalMs = 5000;
    private readonly List<Destination> _places = new();
    private uint _city;
    private ulong _owner;
    private bool _attempted;
    private long _attemptedAt;

    internal IReadOnlyList<Destination> Places => _places;

    internal bool Bind(uint city, ulong owner)
    {
        if (city == 0 || owner == 0 || city != _city || owner != _owner)
        {
            _places.Clear();
            _attempted = false;
        }
        _city = city;
        _owner = owner;
        return city != 0 && owner != 0;
    }

    internal bool TryBeginRefresh(long now)
    {
        if (_city == 0 || _owner == 0 || _attempted && now - _attemptedAt < RefreshIntervalMs)
            return false;
        _attempted = true;
        _attemptedAt = now;
        return true;
    }

    internal bool Remember(Destination place, float mergeM)
    {
        if (_city == 0 || _owner == 0 || place.Category != DestinationCategory.Shops
            || place.Action != DestinationAction.Go || place.Actor != default
            || string.IsNullOrWhiteSpace(place.Name) || !place.HasPosition
            || !float.IsFinite(mergeM) || mergeM < 0) return false;
        foreach (var known in _places)
        {
            if (known.Name != place.Name || known.Kind != place.Kind) continue;
            float dx = known.X - place.X, dy = known.Y - place.Y, dz = known.Z - place.Z;
            if (dx * dx + dy * dy + dz * dz <= mergeM * mergeM) return false;
        }
        _places.Add(place);
        return true;
    }
}
