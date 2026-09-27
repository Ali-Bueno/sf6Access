#nullable enable
using System.Collections.Generic;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Which things are inside the camera's aim cone, with hysteresis: a thing ENTERS within
/// <c>enterDeg</c> of dead ahead and only LEAVES beyond <c>leaveDeg</c>, so wobbling on it is
/// one entry and looking away and back is a new one. Keys not offered in a poll are forgotten.
/// </summary>
public sealed class ConeSweep
{
    private readonly float _enterDeg, _leaveDeg;
    private readonly HashSet<string> _inCone = new();
    private readonly HashSet<string> _seen = new();

    public ConeSweep(float enterDeg, float leaveDeg)
    {
        _enterDeg = enterDeg;
        _leaveDeg = leaveDeg;
    }

    public void BeginPoll() => _seen.Clear();

    /// <summary>Offers one thing at <paramref name="offDeg"/> from dead ahead (absolute);
    /// returns true only on the poll it enters the cone.</summary>
    public bool Offer(string key, float offDeg)
    {
        _seen.Add(key);
        if (_inCone.Contains(key))
        {
            if (offDeg > _leaveDeg) _inCone.Remove(key);
            return false;
        }
        if (offDeg > _enterDeg) return false;
        _inCone.Add(key);
        return true;
    }

    public bool Contains(string key) => _inCone.Contains(key);

    /// <summary>Forgets what was not offered since <see cref="BeginPoll"/> (gone from the world).</summary>
    public void EndPoll() => _inCone.IntersectWith(_seen);

    public void Clear()
    {
        _inCone.Clear();
        _seen.Clear();
    }
}
