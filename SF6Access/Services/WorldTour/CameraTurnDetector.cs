#nullable enable
namespace SF6Access.Services.WorldTour;

/// <summary>
/// Tells a deliberate look-around from the follow camera drifting behind a walking avatar:
/// the camera "turns" when its heading changes faster than <c>minDegPerSecond</c> between
/// two samples. The first sample after a reset is never a turn.
/// </summary>
public sealed class CameraTurnDetector
{
    private readonly double _minDegPerSecond;
    private float _lastX, _lastZ;
    private long _lastMs;
    private bool _have;

    public CameraTurnDetector(double minDegPerSecond) { _minDegPerSecond = minDegPerSecond; }

    /// <param name="x">Camera forward, ground plane, X.</param>
    /// <param name="z">Camera forward, ground plane, Z.</param>
    /// <param name="ignoreUntilMs">A sample pair that starts before this time is not a turn
    /// (the camera was being turned by something other than the player).</param>
    public bool Sample(float x, float z, long nowMs, long ignoreUntilMs = long.MinValue)
    {
        bool turning = false;
        if (_have && nowMs > _lastMs && _lastMs > ignoreUntilMs)
        {
            double dot = x * _lastX + z * _lastZ;
            double cross = z * _lastX - x * _lastZ;
            double deg = System.Math.Abs(System.Math.Atan2(cross, dot) * 180.0 / System.Math.PI);
            turning = deg / ((nowMs - _lastMs) / 1000.0) >= _minDegPerSecond;
        }
        _lastX = x;
        _lastZ = z;
        _lastMs = nowMs;
        _have = true;
        return turning;
    }

    public void Reset() => _have = false;
}
