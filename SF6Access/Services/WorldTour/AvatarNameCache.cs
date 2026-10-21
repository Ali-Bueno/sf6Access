using System.Collections.Generic;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Who is who — remembered, so the hands-free readers can tell a person worth
/// a sentence from the crowd at list-walking cost instead of component-walking
/// cost.
///
/// <para><b>What "notable" means (2026-09-07, from the session log).</b> In
/// World Tour the crowd is NAMED: "Kenneth, person", "Susana, person", every
/// passer-by carries a display name and an access target, so "has a name" is
/// no filter at all — the tracker followed a new pedestrian every few steps
/// and read out a census. What separates the people the player goes looking
/// for is the game's own contact KIND (<c>HudDef.ContactUIType</c>): a master
/// (Chun-Li, Luke) or another player is notable; a plain <c>NPC</c> is the
/// street. The homing pulse still follows the literal nearest person — a sound
/// toward a passer-by is fine, a sentence about one is not.</para>
///
/// <para><see cref="AvatarFieldReader.Classify"/> walks an avatar's component
/// array, which is fine once per sentence and not fine for every person in a
/// crowded street several times a second. Neither name nor kind changes for
/// the life of an avatar, so both are read once per address and kept while
/// that avatar keeps being seen.</para>
/// </summary>
public static class AvatarNameCache
{
    /// <summary>An address not seen for this long is forgotten, so an avatar
    /// that despawned and whose address the engine reuses is described afresh.
    /// Generous on purpose: the cost of keeping a stale entry is a few bytes,
    /// the cost of dropping a live one is a component walk.</summary>
    private const long FORGET_MS = 30000;
    // A component can appear after its avatar. Missing classification must be retried while visible.
    private const long RetryIncompleteMs = 4 * FieldActionReader.SampleMs;

    private sealed class Entry
    {
        public string Name;     // null = the game gives this avatar no name
        public int Kind;        // HudDef.ContactUIType
        public long SeenTick;
        public long CheckedTick;
    }

    private static readonly Dictionary<ulong, Entry> Names = new();
    private static readonly List<ulong> Stale = new();
    private static long _lastPruneTick;

    /// <summary>The avatar's name and kind ("Chun-Li, master"), or null for
    /// the nameless. Cached by address.</summary>
    public static string NameOf(AvatarFieldReader.Other o)
    {
        var e = Lookup(o);
        if (e?.Name == null) return null;
        string word = AvatarFieldReader.KindWord(e.Kind);
        return string.IsNullOrEmpty(word) ? e.Name : $"{e.Name}, {word}";
    }

    internal static (string Name, int Kind) Classify(AvatarFieldReader.Other o)
    {
        var entry = Lookup(o);
        return entry == null ? (null, AvatarFieldReader.CONTACT_NONE) : (entry.Name, entry.Kind);
    }

    /// <summary>Somebody the voice should talk about: named, and not one of
    /// the crowd.</summary>
    public static bool IsNotable(AvatarFieldReader.Other o)
    {
        var e = Lookup(o);
        return e?.Name != null && e.Kind != AvatarFieldReader.CONTACT_NPC;
    }

    /// <summary>The notable people in <paramref name="others"/>, in the same
    /// (nearest-first) order.</summary>
    public static List<AvatarFieldReader.Other> Notable(List<AvatarFieldReader.Other> others)
    {
        var notable = new List<AvatarFieldReader.Other>();
        foreach (var o in others)
            if (IsNotable(o)) notable.Add(o);
        return notable;
    }

    private static Entry Lookup(AvatarFieldReader.Other o)
    {
        ulong address = o.Avatar?.GetAddress() ?? 0;
        if (address == 0) return null;

        long now = System.Environment.TickCount64;
        bool cached = Names.TryGetValue(address, out var e);
        if (!cached || now - e.CheckedTick >= FORGET_MS ||
            ((string.IsNullOrWhiteSpace(e.Name) || e.Kind == AvatarFieldReader.CONTACT_NONE)
                && now - e.CheckedTick >= RetryIncompleteMs))
        {
            var (name, kind) = AvatarFieldReader.Classify(o.Avatar);
            e = new Entry { Name = name, Kind = kind, CheckedTick = now };
            Names[address] = e;
        }
        e.SeenTick = now;
        PruneIfDue(now);
        return e;
    }

    private static void PruneIfDue(long now)
    {
        if (now - _lastPruneTick < FORGET_MS) return;
        _lastPruneTick = now;
        Stale.Clear();
        foreach (var (address, e) in Names)
            if (now - e.SeenTick >= FORGET_MS) Stale.Add(address);
        foreach (var address in Stale) Names.Remove(address);
    }
}
