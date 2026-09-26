using System;

namespace SF6Access.Services.WorldTour;

/// <summary>Bounded look-ahead along the first straight run. Close corners remain targets.</summary>
internal sealed class RouteLookAhead
{
    // Four metres limits point reads; it is not an arrival or corner-skipping radius.
    internal const float LookAheadM = 4f;
    private const double BendDegrees = 15.0; // Half the existing spoken clock sector.
    private readonly float _px, _pz, _reachedM;
    private double _bearing;
    public bool HasPoint { get; private set; }
    public int Turn { get; private set; } // -1 left, +1 right in the game's calibrated XZ basis.
    public float X { get; private set; }
    public float Y { get; private set; }
    public float Z { get; private set; }

    public RouteLookAhead(float px, float pz, float bodyRadius)
    {
        _px = px; _pz = pz;
        _reachedM = float.IsFinite(bodyRadius) ? Math.Max(0f, bodyRadius / 2f) : 0f;
    }

    /// <summary>False means stop reading; the retained point is the next aim.</summary>
    public bool Add(float x, float y, float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) return false;
        float dx = x - _px, dz = z - _pz;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance <= _reachedM) return true;
        double bearing = Math.Atan2(dx, dz) * 180.0 / Math.PI;
        double delta = Math.IEEERemainder(bearing - _bearing, 360.0);
        if (HasPoint && Math.Abs(delta) > BendDegrees)
        {
            float cross = (X - _px) * (z - Z) - (Z - _pz) * (x - X);
            Turn = Math.Sign(cross);
            return false;
        }
        if (!HasPoint) _bearing = bearing;
        HasPoint = true; X = x; Y = y; Z = z;
        return distance < LookAheadM;
    }
}
