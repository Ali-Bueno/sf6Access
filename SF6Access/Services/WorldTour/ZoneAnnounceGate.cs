#nullable enable
using System.Collections.Generic;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Stops zone names bouncing at a boundary: a section entered for the first time in a while
/// is announced at once; one heard recently (walking back and forth over its edge) only
/// after the player has stayed in it for a while.
/// </summary>
public sealed class ZoneAnnounceGate
{
    private readonly long _repeatWindowMs, _dwellMs;
    private readonly Dictionary<uint, long> _spokenAt = new();

    /// <param name="repeatWindowMs">A section heard within this long is a bounce.</param>
    /// <param name="dwellMs">How long the player must stay in a bounced-back section.</param>
    public ZoneAnnounceGate(long repeatWindowMs, long dwellMs)
    {
        _repeatWindowMs = repeatWindowMs;
        _dwellMs = dwellMs;
    }

    /// <summary>Whether entering <paramref name="sectionId"/> now is a bounce back into it.</summary>
    public bool IsBounce(uint sectionId, long nowMs) =>
        sectionId != 0 && _spokenAt.TryGetValue(sectionId, out long at) && nowMs - at < _repeatWindowMs;

    /// <summary>When an announcement for the section may be spoken.</summary>
    public long DueAt(uint sectionId, long nowMs) => IsBounce(sectionId, nowMs) ? nowMs + _dwellMs : nowMs;

    public void Spoke(uint sectionId, long nowMs)
    {
        if (sectionId != 0) _spokenAt[sectionId] = nowMs;
    }

    public void Clear() => _spokenAt.Clear();
}
