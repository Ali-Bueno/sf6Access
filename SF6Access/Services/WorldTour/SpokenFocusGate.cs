namespace SF6Access.Services.WorldTour;

/// <summary>A spoken name survives small camera corrections; a sustained look away invalidates it.</summary>
internal sealed class SpokenFocusGate
{
    private readonly float _keepDegrees;
    private readonly long _leaveMs;
    private long _outsideAt = -1;
    private bool _left;

    internal SpokenFocusGate(float keepDegrees, long leaveMs)
    { _keepDegrees = keepDegrees; _leaveMs = leaveMs; }

    internal SpokenCandidateState Observe(float offDegrees, long now)
    {
        if (_left) return SpokenCandidateState.Invalid;
        if (!float.IsFinite(offDegrees)) return SpokenCandidateState.Pending;
        if (offDegrees <= _keepDegrees) { _outsideAt = -1; return SpokenCandidateState.Current; }
        if (_outsideAt < 0) _outsideAt = now;
        if (offDegrees >= 90f || now - _outsideAt >= _leaveMs)
        { _left = true; return SpokenCandidateState.Invalid; }
        return SpokenCandidateState.Pending;
    }
}
