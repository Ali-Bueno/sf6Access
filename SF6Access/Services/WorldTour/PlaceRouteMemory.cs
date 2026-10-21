#nullable enable
using System;
using System.Collections.Generic;
using static SF6Access.Services.WorldTour.LevelRoutePlanner;

namespace SF6Access.Services.WorldTour;

/// <summary>Fairly samples local destinations and keeps only fresh, spatially relevant walking evidence.</summary>
internal sealed class PlaceRouteMemory
{
    internal readonly record struct Target(string Key, Destination Destination);
    internal readonly record struct Route(Target Target, Spot Origin, Spot Next, float Meters, long SampledAt);
    private sealed class Entry
    {
        internal Target Target;
        internal long AttemptedAt = long.MinValue;
        internal Route? Route;
    }

    private readonly long _lifetimeMs;
    private readonly float _moveM;
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly List<string> _order = new();
    private uint _city, _situation;

    internal PlaceRouteMemory(long lifetimeMs, float moveM)
    { _lifetimeMs = Math.Max(0, lifetimeMs); _moveM = Math.Max(0, moveM); }

    internal void Load(IEnumerable<Target> targets, uint city, uint situation)
    {
        if (city != _city || situation != _situation) Clear();
        _city = city; _situation = situation;
        var retained = new HashSet<string>();
        _order.Clear();
        foreach (var target in targets)
        {
            if (city == 0 || string.IsNullOrEmpty(target.Key) || !target.Destination.HasPosition
                || !retained.Add(target.Key)) continue;
            _order.Add(target.Key);
            if (!_entries.TryGetValue(target.Key, out var entry) || entry.Target != target)
                _entries[target.Key] = new Entry { Target = target };
        }
        var removed = new List<string>();
        foreach (string key in _entries.Keys) if (!retained.Contains(key)) removed.Add(key);
        foreach (string key in removed) _entries.Remove(key);
    }

    internal bool TryNext(out Target target)
    {
        Entry? oldest = null;
        foreach (string key in _order)
        {
            var entry = _entries[key];
            if (oldest == null || entry.AttemptedAt < oldest.AttemptedAt) oldest = entry;
        }
        target = oldest?.Target ?? default;
        return oldest != null;
    }

    internal void Record(Target target, Spot origin, Spot next, float meters, bool confirmed, long now)
    {
        if (!_entries.TryGetValue(target.Key, out var entry) || entry.Target != target) return;
        entry.AttemptedAt = now;
        entry.Route = confirmed && Finite(origin) && Finite(next) && float.IsFinite(meters) && meters > 0
            ? new Route(target, origin, next, meters, now) : null;
    }

    internal List<Route> Current(Spot player, long now, float maximumDeviationM)
    {
        var result = new List<Route>();
        if (!Finite(player) || !float.IsFinite(maximumDeviationM) || maximumDeviationM < 0) return result;
        foreach (string key in _order)
        {
            if (_entries[key].Route is not { } route || now < route.SampledAt
                || now - route.SampledAt > _lifetimeMs || Distance(player, route.Origin) > _moveM) continue;
            float initialX = route.Next.X - route.Origin.X, initialZ = route.Next.Z - route.Origin.Z;
            float remainingX = route.Next.X - player.X, remainingZ = route.Next.Z - player.Z;
            // Crossing the sampled first corner invalidates its outgoing direction even inside the move budget.
            if (initialX * remainingX + initialZ * remainingZ <= 0) continue;
            if (!NearSegment(player, route.Origin, route.Next, maximumDeviationM)) continue;
            result.Add(route);
        }
        return result;
    }

    internal void Clear() { _entries.Clear(); _order.Clear(); _city = _situation = 0; }
    private static bool Finite(Spot point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);

    private static bool NearSegment(Spot player, Spot origin, Spot next, float tolerance)
    {
        double dx = (double)next.X - origin.X, dy = (double)next.Y - origin.Y, dz = (double)next.Z - origin.Z;
        double px = (double)player.X - origin.X, py = (double)player.Y - origin.Y, pz = (double)player.Z - origin.Z;
        double lengthSq = dx * dx + dy * dy + dz * dz;
        if (lengthSq <= 0) return false;
        // Clamp at the origin too: a line would wrongly retain evidence after walking backwards.
        double along = Math.Clamp((px * dx + py * dy + pz * dz) / lengthSq, 0, 1);
        double offX = px - along * dx, offY = py - along * dy, offZ = pz - along * dz;
        return offX * offX + offY * offY + offZ * offZ <= (double)tolerance * tolerance;
    }
}
