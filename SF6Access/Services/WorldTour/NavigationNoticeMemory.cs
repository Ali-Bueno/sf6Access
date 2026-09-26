using System;
using System.Collections.Generic;

namespace SF6Access.Services.WorldTour;

/// <summary>Copied identities and announcement state; never owns native objects.</summary>
internal sealed class NavigationNoticeMemory
{
    // Leave room for a sentence before another approach/alignment update.
    internal const long RepeatFloorMs = 4000;
    internal const long ForgetMs = 15000;
    private readonly Dictionary<string, Notice> _notices = new();
    private readonly List<string> _expired = new();
    private record struct Notice(string State, float Distance, bool Aligned, long SpokenAt, long SeenAt);

    public bool Wants(string key, string state, float distance, bool aligned, long now)
    {
        if (string.IsNullOrEmpty(key) || !float.IsFinite(distance) || distance < 0) return false;
        if (!_notices.TryGetValue(key, out var old)) return true;
        _notices[key] = old with { SeenAt = now };
        if (state != old.State) return true;
        if (now - old.SpokenAt < RepeatFloorMs) return false;
        return (aligned && !old.Aligned) || distance < old.Distance / 2f
            || distance > Math.Max(old.Distance * 2f, old.Distance + 1f);
    }

    public void Spoke(string key, string state, float distance, bool aligned, long now)
        => _notices[key] = new(state, distance, aligned, now, now);

    public void Prune(long now)
    {
        _expired.Clear();
        foreach (var item in _notices)
            if (now - item.Value.SeenAt > ForgetMs) _expired.Add(item.Key);
        foreach (string key in _expired) _notices.Remove(key);
    }

    public void Clear() => _notices.Clear();
}
