using System.Collections.Generic;
using System.Globalization;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// The places-only catalog (no crowd) for readers that look at it many times a second —
/// the look sweep and "passing by" — rebuilt at most every <see cref="REFRESH_MS"/>, each
/// place with a key that stays the same across rebuilds.
/// </summary>
public static class PlaceCache
{
    public readonly record struct Place(Destination Destination, string Key);

    // Places do not move; the one that does (the story objective) moves between missions.
    private const long REFRESH_MS = 5000;

    private static List<Place> _places = new();
    private static long _builtAt;
    private static bool _built;
    private static uint _city, _situation;

    public static IReadOnlyList<Place> Places()
    {
        if (!FieldPresenceService.InField
            || !WorldTourStateService.TryReadContext(out uint city, out uint situation))
        { Reset(); return _places; }
        if (_city != city || _situation != situation) Reset();
        _city = city; _situation = situation;
        long now = System.Environment.TickCount64;
        if (_built && now - _builtAt < REFRESH_MS) return _places;
        _built = true;
        _builtAt = now;

        var me = FieldPresenceService.PlayerPos;
        var places = new List<Place>();
        foreach (var group in DestinationCatalog.Build(me.X, me.Z, people: false))
            foreach (var d in group.Items)
                if (d.HasPosition) places.Add(new Place(d, KeyOf(d)));
        _places = places;
        return _places;
    }

    /// <summary>Forget the catalog, e.g. after a spot is saved, so the next read rebuilds it.</summary>
    public static void Reset()
    {
        _places = new List<Place>();
        _built = false;
        _city = _situation = 0;
    }

    /// <summary>The story objective and masters move (the objective's access point follows the
    /// player), so they are keyed by what they are; every other place also by where it is.</summary>
    private static string KeyOf(in Destination d) => d.Category switch
    {
        DestinationCategory.Mission => d.Category.ToString(),
        DestinationCategory.Masters => $"{d.Category}|{d.Name}",
        _ => string.Create(CultureInfo.InvariantCulture,
            $"{d.Category}|{d.Name}|{System.Math.Round(d.X)}|{System.Math.Round(d.Z)}"),
    };
}
