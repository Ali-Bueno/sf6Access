#nullable enable
using System.Collections.Generic;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// "Passing by" bookkeeping: a place is news once when the player comes within
/// <c>enterM</c>, and becomes news again only after they have been farther than
/// <c>rearmM</c> from it, so hovering at the edge of the range says nothing twice.
/// </summary>
public sealed class ProximityMemory
{
    private readonly float _enterM, _rearmM;
    private readonly HashSet<string> _announced = new();

    public ProximityMemory(float enterM, float rearmM)
    {
        _enterM = enterM;
        _rearmM = rearmM;
    }

    /// <summary>True when the place at <paramref name="distM"/> is close and not yet announced;
    /// re-arms it when it is far.</summary>
    public bool IsNews(string key, float distM)
    {
        if (distM > _rearmM) { _announced.Remove(key); return false; }
        return distM <= _enterM && !_announced.Contains(key);
    }

    public void Announced(string key) => _announced.Add(key);

    public void Clear() => _announced.Clear();
}
