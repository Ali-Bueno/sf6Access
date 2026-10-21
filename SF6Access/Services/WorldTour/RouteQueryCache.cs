#nullable enable
using System;

namespace SF6Access.Services.WorldTour;

/// <summary>Shares a recent answer for one destination without renewing the original sample's lifetime.</summary>
internal sealed class RouteQueryCache<T>
{
    private readonly long _lifetimeMs;
    private bool _have;
    private string? _key;
    private (float X, float Y, float Z) _point;
    private T _answer = default!;
    private long _sampledAt;

    internal RouteQueryCache(long lifetimeMs) => _lifetimeMs = Math.Max(0, lifetimeMs);

    internal void Store(string key, (float X, float Y, float Z) point, T answer, long sampledAt)
    {
        _have = true; _key = key; _point = point; _answer = answer; _sampledAt = sampledAt;
    }

    internal bool TryRead(string key, (float X, float Y, float Z) point, long now,
        out T answer, out long sampledAt)
    {
        answer = default!; sampledAt = 0;
        if (!_have || key != _key || point != _point || now < _sampledAt || now - _sampledAt > _lifetimeMs)
            return false;
        answer = _answer; sampledAt = _sampledAt;
        return true;
    }

    internal void Clear() { _have = false; _key = null; _answer = default!; }
}
