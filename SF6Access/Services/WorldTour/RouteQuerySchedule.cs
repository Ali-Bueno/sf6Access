using System;

namespace SF6Access.Services.WorldTour;

/// <summary>Reserves the next path-query slot for live steering when another reader delayed it.</summary>
internal sealed class RouteQuerySchedule
{
    private readonly long _intervalMs;
    private long _nextResolve;
    private bool _waitingForQuery;

    internal RouteQuerySchedule(long intervalMs) => _intervalMs = Math.Max(1, intervalMs);
    internal bool Due(long now) => now >= _nextResolve;
    internal bool MayPump => !_waitingForQuery;

    internal void Resolved(long now, bool waitingForQuery, long nextQueryTick, bool backgroundSearch)
    {
        _waitingForQuery = waitingForQuery;
        _nextResolve = waitingForQuery ? Math.Max(now, nextQueryTick)
            : now + (backgroundSearch ? 2 * _intervalMs : _intervalMs);
    }

    internal void Reset() { _nextResolve = 0; _waitingForQuery = false; }
}
