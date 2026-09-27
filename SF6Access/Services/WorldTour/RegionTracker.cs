#nullable enable
using System.Collections.Generic;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Names the part of town the player is in after its nearest landmark, with hysteresis:
/// the first reading is immediate, a change needs the new landmark to be clearly nearer
/// for a while, so walking along a boundary does not flip the name back and forth.
/// </summary>
public sealed class RegionTracker
{
    public readonly record struct Landmark(string Name, float X, float Z);

    private readonly float _minMarginM, _marginRatio;
    private readonly long _dwellMs;
    private string? _candidate;
    private long _candidateSince;

    /// <param name="minMarginM">Smallest lead, in metres, a new landmark needs.</param>
    /// <param name="marginRatio">Lead as a fraction of the distance to the current landmark.</param>
    /// <param name="dwellMs">How long the lead must hold before the region changes.</param>
    public RegionTracker(float minMarginM, float marginRatio, long dwellMs)
    {
        _minMarginM = minMarginM;
        _marginRatio = marginRatio;
        _dwellMs = dwellMs;
    }

    /// <summary>The current region's landmark name, or null before the first reading.</summary>
    public string? Current { get; private set; }

    public void Reset() { Current = null; _candidate = null; }

    /// <summary>Returns true when <see cref="Current"/> changed on this update.</summary>
    public bool Update(IReadOnlyList<Landmark> landmarks, float px, float pz, long nowMs)
    {
        string? nearest = null;
        float nearestD = float.MaxValue, currentD = float.MaxValue;
        foreach (var l in landmarks)
        {
            float dx = l.X - px, dz = l.Z - pz;
            float d = (float)System.Math.Sqrt(dx * dx + dz * dz);
            if (d < nearestD) { nearestD = d; nearest = l.Name; }
            // Landmarks may share a name; the region is as near as its nearest copy.
            if (l.Name == Current && d < currentD) currentD = d;
        }
        if (nearest == null) return false;

        if (Current == null || currentD == float.MaxValue)
        {
            _candidate = null;
            return Set(nearest);
        }
        if (nearest == Current) { _candidate = null; return false; }

        float margin = System.Math.Max(_minMarginM, _marginRatio * currentD);
        if (currentD - nearestD < margin) { _candidate = null; return false; }

        if (_candidate != nearest) { _candidate = nearest; _candidateSince = nowMs; }
        if (nowMs - _candidateSince < _dwellMs) return false;

        _candidate = null;
        return Set(nearest);
    }

    private bool Set(string name)
    {
        bool changed = name != Current;
        Current = name;
        return changed;
    }
}
