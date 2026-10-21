using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;
using Kind = SF6Access.Services.WorldObjectKind;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// Earcons for ladders, doors, shops, places and breakables, including during guidance and walking.
/// Facing an object repeats its cue; nearby kinds take sparse turns. Shift+N toggles world sounds.
/// </summary>
public class WorldSonarHooks
{
    private static float AroundM => FieldBeaconHooks.HOME_RANGE_M;
    private static float NearM => MissionBeaconHooks.ARRIVED_M;
    // The flashlight reaches four beacon ranges: about a street's length.
    private static float AheadM => 4f * AroundM;

    /// <summary>10 Hz, the look sweep's cadence, so a turn lights up what it names.</summary>
    private const int POLL_TICKS = 6;

    // Nearby kinds use the far-focus interval, so doors/ladders remain discoverable while walking.
    private const int AROUND_GAP_MS = AHEAD_FAR_GAP_MS;
    // Ahead: the homing beacons' near silence, and a far one a little shorter than theirs
    // (HomingCue.GAP_FAR_MS) so a far thing ahead is still heard while turning.
    private const int AHEAD_NEAR_GAP_MS = 250, AHEAD_FAR_GAP_MS = 2000;

    // Both under the beacons' AudioService.DEFAULT_VOLUME; ahead is the thing being listened
    // to, around is background.
    private const float AHEAD_LOUD = 0.4f, AHEAD_QUIET = 0.12f;
    private const float AROUND_LOUD = 0.25f, AROUND_QUIET = 0.08f;

    // Focus changes can come several times a second while turning; log at most this often.
    private const long LOG_GAP_MS = 2000;

    private const int VK_N = 0x4E;
    private static readonly ReadoutShortcut ToggleKey = new(VK_N, ReadoutShortcut.PAD_NONE, shift: true);

    private readonly record struct Source(string Key, Kind Kind, float Dist, float Pan, bool Behind, float OffDeg);

    private static readonly List<Source> _sources = new();
    private static readonly List<Source> _around = new();
    private static readonly List<int> _aroundKinds = new();
    private static readonly SonarFocus _focus =
        new(FieldDirectionService.AHEAD_DEG, FieldDirectionService.AHEAD_LEAVE_DEG);
    private static bool _enabled = true, _aroundLogged;
    private static string _cityCueKey;
    private static int _tick, _turn;
    private static long _nextAheadAt, _nextAroundAt, _aheadEndsAt, _loggedAt;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] WorldSonarHooks initialized (earcons: ladders, doors, shops, places; Shift+N toggles)");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();
        // Polled every frame: a slower poll misses short presses.
        if (ToggleKey.Pressed() && FieldPresenceService.InField) Toggle();
        if (!_enabled || !FieldFeedback.CanOutput)
        {
            _focus.Clear();
            return;
        }
        if (++_tick < POLL_TICKS) return;
        _tick = 0;

        long now = System.Environment.TickCount64;
        Gather(now);
        if (CityListeningMode.Active)
        {
            _focus.Clear();
            if (_sources.Count == 0) { _cityCueKey = null; return; }
            var cityTarget = _sources[0];
            if (cityTarget.OffDeg > FieldDirectionService.AHEAD_LEAVE_DEG) return;
            if (_cityCueKey != cityTarget.Key)
            {
                _cityCueKey = cityTarget.Key;
                _nextAheadAt = _aheadEndsAt;
            }
            if (now >= _nextAheadAt) PlayAhead(now, cityTarget);
            return;
        }
        _cityCueKey = null;
        _focus.Begin();
        foreach (var s in _sources) _focus.Offer(s.Key, s.OffDeg, s.Dist);
        bool changed = _focus.End();
        if (changed)
        {
            // Something new ahead is heard as soon as the cue playing now ends: skip the
            // silence, never overlap (a fast sweep can change the focus every poll).
            _nextAheadAt = _aheadEndsAt;
            LogAhead(now);
        }
        bool ahead = TryAhead(out var target);
        if (ahead && now >= _nextAheadAt) PlayAhead(now, target);
        // A focused crate must not silence a ladder beside it for the whole walk.
        if (!CityListeningMode.Active && now >= _nextAroundAt) PlayAround(now, ahead ? target.Kind : null);
    }

    /// <summary>Everything that can sound from here, with its bearing in the camera's frame.</summary>
    private static void Gather(long now)
    {
        _sources.Clear();
        var forward = FieldDirectionService.GetCameraForward();
        var me = FieldPresenceService.PlayerPos;
        var destination = MapSelectionMemory.Current;

        void Add(string key, Kind kind, float x, float y, float z, float rangeM)
        {
            float dx = x - me.X, dy = y - me.Y, dz = z - me.Z;
            float dist = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dist > rangeM) return;
            // The chosen destination already has the homing beacon.
            if (destination.Ok && MissionBeaconHooks.Homing
                && FieldAccessCatalog.DistanceSq(x, y, z, destination.X, destination.Y, destination.Z)
                    <= NearM * NearM) return;
            var b = FieldDirectionService.GetBearing(forward, dx, dz);
            // No usable bearing (camera unreadable, or right underfoot): centred, never "ahead".
            _sources.Add(b.Ok
                ? new Source(key, kind, dist, b.Right, b.Behind, AxisLatch.OffDeg(b.Ahead, b.Right))
                : new Source(key, kind, dist, 0f, false, float.MaxValue));
        }

        if (CityListeningMode.Active)
        {
            // The city name, sound and R3 choice refer to the same focus. No nearby rota here.
            if (FieldAimHooks.TryCityFocus(out var place, out string key))
                Add(key, KindOf(place.Category) ?? Kind.Place, place.X, place.Y, place.Z, float.PositiveInfinity);
            return;
        }

        // Ladders and doors are only kept within the beacons' range by the catalog itself.
        foreach (var a in FieldAccessCatalog.Nearby(me.X, me.Y, me.Z, now))
            Add(a.Key, a.Kind == AccessKind.Ladder ? Kind.Ladder : Kind.Door, a.X, a.Y, a.Z, AroundM);
        foreach (var b in BreakableCatalog.All())
            Add(BreakableKey(b), Kind.Breakable, b.X, b.Y, b.Z, AroundM);
        foreach (var place in PlaceCache.Places())
        {
            var d = place.Destination;
            // People, the story, missions and jobs have their own beacons and readers.
            Kind? kind = KindOf(d.Category);
            if (kind.HasValue) Add(place.Key, kind.Value, d.X, d.Y, d.Z, AheadM);
        }
    }

    private static bool TryAhead(out Source target)
    {
        foreach (var s in _sources)
            if (s.Key == _focus.Key) { target = s; return true; }
        target = default;
        return false;
    }

    private static Kind? KindOf(DestinationCategory category) => category switch
    {
        DestinationCategory.Shops => Kind.Shop,
        DestinationCategory.Travel => Kind.Travel,
        DestinationCategory.Rest or DestinationCategory.Places or DestinationCategory.Jobs
            or DestinationCategory.Saved or DestinationCategory.Pins => Kind.Place,
        _ => null,
    };

    internal static string BreakableKey(BreakableCatalog.Breakable b) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"breakable:{b.X:0.0}:{b.Y:0.0}:{b.Z:0.0}");

    private static bool PlayAround(long now, Kind? focusedKind)
    {
        _around.Clear();
        foreach (var s in _sources)
            if (s.Dist <= AroundM && s.Kind != focusedKind) _around.Add(s);
        _around.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        _aroundKinds.Clear();
        foreach (var source in _around) _aroundKinds.Add((int)source.Kind);
        int pick = SonarRota.PickKind(_aroundKinds, _turn);
        if (pick < 0) { _nextAroundAt = now + AROUND_GAP_MS; return false; }
        var target = _around[pick];
        if (!FieldFeedback.CueAvailable(FieldCueLane.Nearby, target.Key)) return false;
        _turn++;
        var duration = Play(target, SonarRota.Volume(target.Dist, NearM, AroundM, AROUND_LOUD, AROUND_QUIET), FieldCueLane.Nearby);
        bool played = duration > System.TimeSpan.Zero;
        _nextAroundAt = now + (long)System.Math.Ceiling(duration.TotalMilliseconds) + AROUND_GAP_MS;

        if (played && !_aroundLogged)
        {
            _aroundLogged = true;
            API.LogInfo($"[SF6Access] World sonar: first cue around, {target.Kind} at {target.Dist:0.0} m ({_around.Count} in range)");
        }
        return played;
    }

    private static void PlayAhead(long now, in Source target)
    {
        if (!FieldFeedback.CueAvailable(FieldCueLane.Focused, target.Key)) return;
        float volume = CityListeningMode.Active ? AROUND_LOUD
            : SonarRota.Volume(target.Dist, NearM, AheadM, AHEAD_LOUD, AHEAD_QUIET);
        var duration = Play(target, volume, FieldCueLane.Focused);
        bool played = duration > System.TimeSpan.Zero;
        _aheadEndsAt = now + (long)System.Math.Ceiling(duration.TotalMilliseconds);
        long gap = played && !CityListeningMode.Active ? SonarRota.Gap(target.Dist, NearM, AheadM, AHEAD_NEAR_GAP_MS, AHEAD_FAR_GAP_MS)
                          : AHEAD_FAR_GAP_MS;
        _nextAheadAt = _aheadEndsAt + gap;
    }

    private static System.TimeSpan Play(in Source target, float volume, FieldCueLane lane)
    {
        volume *= FieldFeedback.SpeechActive ? FieldCuePolicy.SpeechGain : 1f;
        var length = AudioService.PlayWorldObject(target.Kind, target.Pan, volume, target.Behind);
        FieldFeedback.ReserveCue(lane, target.Key, length);
        return length;
    }

    private static void LogAhead(long now)
    {
        if (now - _loggedAt < LOG_GAP_MS || !TryAhead(out var s)) return;
        _loggedAt = now;
        API.LogInfo($"[SF6Access] World sonar: ahead now {s.Kind} at {s.Dist:0.0} m, {s.OffDeg:0} deg off");
    }

    private static void Toggle()
    {
        _enabled = !_enabled;
        _focus.Clear();
        ScreenReaderService.Speak(_enabled
            ? LangFile.Get("wt.sonar_on", "World sounds on")
            : LangFile.Get("wt.sonar_off", "World sounds off"));
        API.LogInfo($"[SF6Access] World sonar {(_enabled ? "on" : "off")}");
    }
}
