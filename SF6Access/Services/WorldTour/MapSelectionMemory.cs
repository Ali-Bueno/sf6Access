using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>The player's navigation choice, stored as plain values across menus and fights.</summary>
public static class MapSelectionMemory
{
    /// <summary>A remembered destination, without retaining its native map or scene object.</summary>
    public readonly struct Selection
    {
        public readonly bool Ok;

        /// <summary>Already localized by the game and worded by the map reader —
        /// "Beat Square, shop". Spoken verbatim.</summary>
        public readonly string Label;

        public readonly float X, Y, Z;
        public readonly string Key;
        public readonly bool Lost, Pending, InContact, IsPerson;

        public Selection(string label, float x, float y, float z, string key,
            bool lost = false, bool pending = false, bool inContact = false, bool isPerson = false)
        {
            Ok = true; Label = label; X = x; Y = y; Z = z; Key = key;
            Lost = lost; Pending = pending; InContact = inContact; IsPerson = isPerson;
        }
    }

    private static Selection _current;
    private static ActorTrackingState _actor;
    private static long _generation, _nextRefresh;
    private const long RefreshMs = 500; // Match the route's two updates per second.

    /// <summary>Free exploration suspends the automatic mission destination.</summary>
    public static bool Exploring { get; private set; } = true;

    /// <summary>What the player last picked, or a default whose <c>Ok</c> is false.</summary>
    public static Selection Current => _current;

    /// <summary>Refresh a live destination without changing its route identity or retaining native objects.</summary>
    public static Selection Refresh()
    {
        if (_actor == null || _actor.Lost) return _current;
        long now = System.Environment.TickCount64;
        if (now < _nextRefresh) return _current;
        _nextRefresh = now + RefreshMs;
        ActorTrackingState.Observation observation;
        try { observation = LiveDestinationReader.Read(_actor.Current.Actor); }
        catch { observation = default; }
        _actor.Observe(observation);
        var updated = _actor.Current;
        bool lost = !_current.Lost && _actor.Lost;
        _current = new Selection(updated.Label, updated.X, updated.Y, updated.Z, _current.Key,
            _actor.Lost, _actor.Pending, _actor.InContact, _current.IsPerson);
        if (lost) API.LogInfo($"[SF6Access] Selected actor lost: '{_current.Label}'; kept its last known position");
        return _current;
    }

    public static void Remember(Destination destination)
        => Remember(destination.Label, destination.X, destination.Y, destination.Z, destination);

    /// <summary>Follow a deliberately chosen destination, rejecting unreadable coordinates.</summary>
    public static void Remember(string label, float x, float y, float z)
        => Remember(label, x, y, z, default);

    private static void Remember(string label, float x, float y, float z, Destination destination)
    {
        if (string.IsNullOrWhiteSpace(label)) return;
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) return;
        if (x == 0f && y == 0f && z == 0f) return;

        CityListeningMode.Exit();
        bool changed = !_current.Ok || _current.Label != label
                       || _current.X != x || _current.Y != y || _current.Z != z;
        _generation++;
        _current = new Selection(label, x, y, z, "pick:" + _generation,
            isPerson: destination.Actor.IsValid || destination.Category is DestinationCategory.People or DestinationCategory.Masters);
        _actor = destination.Actor.IsValid ? new ActorTrackingState(destination) : null;
        _nextRefresh = 0;
        Exploring = false;
        if (changed)
            API.LogInfo($"[SF6Access] Map selection: '{label}' at ({x:0.0}, {y:0.0}, {z:0.0})");
    }

    public static void Explore() => Reset("free exploration");

    public static void FollowMission()
    {
        Reset("follow the tracked mission");
        Exploring = false;
    }

    /// <summary>Forget the choice when changing cities; menus and fights keep it.</summary>
    public static void Reset(string reason)
    {
        CityListeningMode.Exit();
        if (_current.Ok) API.LogInfo($"[SF6Access] Map selection cleared ({reason})");
        _current = default;
        _actor = null;
        _nextRefresh = 0;
        Exploring = true;
    }
}
