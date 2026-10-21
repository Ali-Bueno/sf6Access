#nullable enable
namespace SF6Access.Services.WorldTour;

/// <summary>
/// Whether a target is dead ahead, with a few degrees of margin to leave so the answer does
/// not flicker while the camera settles on the edge. Fed the <c>Ahead</c>/<c>Right</c> pair of
/// a camera bearing.
/// </summary>
public sealed class AxisLatch
{
    private readonly float _enterDeg, _leaveDeg;

    public AxisLatch(float enterDeg, float leaveDeg)
    {
        _enterDeg = enterDeg;
        _leaveDeg = leaveDeg;
    }

    public bool On { get; private set; }

    /// <summary>Takes a new bearing and says whether the target is (still) ahead.</summary>
    public bool Update(float ahead, float right)
    {
        float off = OffDeg(ahead, right);
        On = On ? off <= _leaveDeg : off <= _enterDeg;
        return On;
    }

    public void Reset() => On = false;

    /// <summary>Degrees between straight ahead and the bearing, either side (0–180).</summary>
    public static float OffDeg(float ahead, float right)
        => (float)System.Math.Abs(System.Math.Atan2(right, ahead) * 180.0 / System.Math.PI);
}
