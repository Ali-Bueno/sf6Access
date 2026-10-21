#nullable enable
namespace SF6Access.Services.WorldTour;

/// <summary>
/// The world sonar's "flashlight": of everything that can sound, the nearest thing the camera
/// points at becomes the focus and plays alone. The focus is kept until it leaves a slightly
/// wider cone, so a camera settling on its edge does not swap it back and forth. One poll is
/// <see cref="Begin"/>, an <see cref="Offer"/> per candidate, then <see cref="End"/>.
/// </summary>
public sealed class SonarFocus
{
    private readonly float _enterDeg, _leaveDeg;
    private string? _best;
    private float _bestM;
    private bool _keep;

    public SonarFocus(float enterDeg, float leaveDeg)
    {
        _enterDeg = enterDeg;
        _leaveDeg = leaveDeg;
    }

    /// <summary>The focused thing's key; null when the camera points at nothing that sounds.</summary>
    public string? Key { get; private set; }

    public void Begin()
    {
        _best = null;
        _bestM = float.MaxValue;
        _keep = false;
    }

    public void Offer(string key, float offDeg, float distM)
    {
        if (key == Key && offDeg <= _leaveDeg) _keep = true;
        if (offDeg <= _enterDeg && distM < _bestM) { _best = key; _bestM = distM; }
    }

    /// <summary>Ends the poll; true when the focus changed to a new thing (not to nothing).</summary>
    public bool End()
    {
        if (_keep) return false;
        bool changed = _best != null && _best != Key;
        Key = _best;
        return changed;
    }

    public void Clear() => Key = null;
}
