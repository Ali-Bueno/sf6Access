#nullable enable
using System;
using System.Collections.Generic;

namespace SF6Access.Services.WorldTour;

public enum FieldSpeechPriority { Ambient, Zone, Heading, Scan, Route, Interaction }

/// <summary>One replaceable, expiring utterance per field reader; never builds a backend queue.</summary>
public sealed class FieldSpeechPolicy
{
    // One scan poll gathers competing readers before choosing a voice.
    public const long CoalesceMs = 100;
    private const long PreemptFloorMs = 500;
    // Fallback UX estimate: about 200 words/minute at five characters per word; room for punctuation.
    private const long MillisecondsPerCharacter = 60;
    private const long MinimumReadingMs = 1000, MaximumReadingMs = 18000;
    private readonly Dictionary<string, Notice> _pending = new();
    private readonly List<string> _expired = new();
    private long _busyUntil, _startedAt;
    private FieldSpeechPriority _active;
    private string? _activeSource, _activeText;

    public readonly record struct Notice(string Source, string Text, FieldSpeechPriority Priority,
        long OfferedAt, long ExpiresAt, Action? OnSpoken, Func<bool>? IsCurrent);

    public bool Offer(string source, string text, FieldSpeechPriority priority, long now,
        long lifetimeMs, Action? onSpoken = null, Func<bool>? isCurrent = null)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrWhiteSpace(text) || lifetimeMs <= 0) return false;
        if (_pending.TryGetValue(source, out var old) && old.Text == text && old.Priority == priority)
            return true;
        _pending[source] = new(source, text, priority, now, now + lifetimeMs, onSpoken, isCurrent);
        return true;
    }

    public bool TryTake(long now, long protectedUntil, out Notice notice)
    {
        notice = default;
        _expired.Clear();
        bool found = false;
        foreach (var pair in _pending)
        {
            var item = pair.Value;
            if (now >= item.ExpiresAt || (item.IsCurrent != null && !item.IsCurrent()))
            { _expired.Add(pair.Key); continue; }
            if (now - item.OfferedAt < CoalesceMs) continue;
            if (!found || item.Priority > notice.Priority ||
                (item.Priority == notice.Priority && item.OfferedAt > notice.OfferedAt))
            { notice = item; found = true; }
        }
        foreach (string key in _expired) _pending.Remove(key);
        if (!found || now < protectedUntil) return false;
        bool replacesSource = notice.Source == _activeSource && notice.Text != _activeText && notice.Priority == _active;
        if (now < _busyUntil && ((!replacesSource && notice.Priority <= _active) || now - _startedAt < PreemptFloorMs)) return false;
        _pending.Remove(notice.Source);
        _active = notice.Priority;
        _activeSource = notice.Source;
        _activeText = notice.Text;
        _startedAt = now;
        _busyUntil = now + ReadingMs(notice.Text);
        return true;
    }

    // A conservative reading estimate; Tolk has no portable completion notification.
    public static long ReadingMs(string? text) =>
        Math.Clamp((text?.Length ?? 0) * MillisecondsPerCharacter, MinimumReadingMs, MaximumReadingMs);
    public static long ProtectUntil(long now, long previous, string text, bool interrupt) =>
        (interrupt ? now : Math.Max(now, previous)) + ReadingMs(text);
    public bool Busy(long now) => now < _busyUntil;
    public void Forget(string source) => _pending.Remove(source);
    public void Clear() { _pending.Clear(); _busyUntil = 0; _activeSource = _activeText = null; }
}
