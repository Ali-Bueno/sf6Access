using System;

namespace SF6Access.Services.WorldTour;

/// <summary>Retries unanswered approach candidates without exhausting the search or crowding route queries.</summary>
internal sealed class ApproachProbeProgress
{
    internal enum Verdict { Deferred, Unavailable, Rejected, Reached }

    // Up to sixteen query intervals (four seconds): unavailable streaming data stays cheap to retry.
    private const int MaxBackoffShift = 4;
    private readonly long _queryIntervalMs;
    private int _backoffShift;
    private long _nextAttemptAt;

    internal int Candidate { get; private set; }
    internal bool Found { get; private set; }

    internal ApproachProbeProgress(long queryIntervalMs)
        => _queryIntervalMs = Math.Max(1, queryIntervalMs);

    internal bool Ready(long now) => !Found && now >= _nextAttemptAt;

    internal void Record(Verdict verdict, long now)
    {
        switch (verdict)
        {
            case Verdict.Deferred:
                _nextAttemptAt = now + _queryIntervalMs;
                return;
            case Verdict.Unavailable:
                _nextAttemptAt = now + _queryIntervalMs * (1L << _backoffShift);
                _backoffShift = Math.Min(_backoffShift + 1, MaxBackoffShift);
                return;
            case Verdict.Rejected:
                Candidate++;
                break;
            case Verdict.Reached:
                Found = true;
                break;
        }
        _backoffShift = 0;
        _nextAttemptAt = 0;
    }

    internal void Reset()
    {
        Candidate = _backoffShift = 0;
        Found = false;
        _nextAttemptAt = 0;
    }
}
