#nullable enable
namespace SF6Access.Services.WorldTour;

/// <summary>Tells a short press (reported on release) from a long one (reported once,
/// as soon as the hold time is reached), so one button can carry two actions.</summary>
public sealed class HoldTapDetector
{
    public enum Gesture { None, Tap, Hold }

    private readonly long _holdMs;
    private long _downAt = -1;
    private bool _consumed;

    public HoldTapDetector(long holdMs) { _holdMs = holdMs; }

    public Gesture Update(bool down, long nowMs)
    {
        if (!down)
        {
            bool tap = _downAt >= 0 && !_consumed;
            _downAt = -1;
            _consumed = false;
            return tap ? Gesture.Tap : Gesture.None;
        }

        if (_downAt < 0) _downAt = nowMs;
        if (_consumed || nowMs - _downAt < _holdMs) return Gesture.None;
        _consumed = true;
        return Gesture.Hold;
    }

    /// <summary>Report nothing for the press in progress, or for one that starts before
    /// the next release is seen (a button still held when control comes back).</summary>
    public void Swallow() => _consumed = true;
}
