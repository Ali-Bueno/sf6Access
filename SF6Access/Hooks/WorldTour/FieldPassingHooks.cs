using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// A known nearby place is named once on arrival or while passing it,
/// with its clock direction ("Style Lab Beauty Salon, tienda, a las 3, a 9 metros") — the
/// shop signs a sighted player reads along a street. People are left to the people readers,
/// the story objective and the chosen destination to the beacon.
/// Design: <c>docs/wt-free-navigation.md</c>.
/// </summary>
public class FieldPassingHooks
{
    /// <summary>About four times a second, the zone reader's cadence.</summary>
    private const int POLL_TICKS = 15;

    // Close enough to be "on this stretch of street": three arrival bands (12 m). Re-armed
    // only past twice that, so walking along the edge of the range says nothing twice.
    private static float RangeM => 3f * MissionBeaconHooks.ARRIVED_M;

    // Leave a quiet interval between incidental discoveries, even along a row of shops.
    private const long GAP_MS = 3000;

    private static readonly ProximityMemory _memory = new(RangeM, 2f * RangeM);
    private static int _tick;
    private static long _spokeAt;
    private static bool _primed;
    private static bool _skipCurrentOnce;
    private static uint _city;
    private static float _lastX, _lastZ;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] FieldPassingHooks initialized (names places while walking past them)");
    }

    /// <summary>After saving a spot, mark the current surroundings known to avoid reading the save back.</summary>
    public static void Prime() { _primed = false; _skipCurrentOnce = true; }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();
        if (!FieldPresenceService.InField) { _memory.Clear(); _primed = _skipCurrentOnce = false; return; }
        if (!FieldFeedback.CanOutput || CityListeningMode.Active) return;
        if (++_tick < POLL_TICKS) return;
        _tick = 0;

        var me = FieldPresenceService.PlayerPos;
        uint city = WorldTourStateService.CityId;
        if (city != 0 && _city != 0 && city != _city) _primed = false;
        if (city != 0) _city = city;
        // A jump no walk could make between two polls is a fast travel: landing is arriving.
        float jx = me.X - _lastX, jz = me.Z - _lastZ;
        if (jx * jx + jz * jz > 4f * RangeM * RangeM) _primed = false;
        if (!_primed) _memory.Clear();
        _lastX = me.X; _lastZ = me.Z;
        string destination = MapSelectionMemory.Current.Ok ? MapSelectionMemory.Current.Label : null;

        PlaceCache.Place best = default;
        float bestM = float.MaxValue;
        foreach (var place in PlaceCache.Places())
        {
            var d = place.Destination;
            // Masters are people; ladders and doors are spoken by the exploration reader.
            if (d.Action != DestinationAction.Go || d.Category is DestinationCategory.Masters or DestinationCategory.Access)
                continue;
            float dx = d.X - me.X, dz = d.Z - me.Z;
            float m = (float)System.Math.Sqrt(dx * dx + dz * dz);
            if (!_memory.IsNews(place.Key, m)) continue;
            if (_skipCurrentOnce || d.Label == destination) { _memory.Announced(place.Key); continue; }
            if (m < bestM) { best = place; bestM = m; }
        }
        _primed = true;
        _skipCurrentOnce = false;

        bool maySpeak = FieldFeedback.CanOutput
                        && !SF6Access.Hooks.SpTalkNovelHooks.DialogueActive && !PadGuideHooks.Active
                        && !DestinationPickerHooks.IsOpen
                        && System.Environment.TickCount64 - _spokeAt >= GAP_MS;
        if (!maySpeak || bestM == float.MaxValue) return;

        var p = best.Destination;
        var offered = best;
        FieldFeedback.Say("passing", p.Label + ", " + DestinationText.Where(me.X, me.Y, me.Z, p.X, p.Y, p.Z),
            FieldSpeechPriority.Ambient, onSpoken: () =>
            {
                _memory.Announced(offered.Key);
                _spokeAt = System.Environment.TickCount64;
            }, stillValid: () => FieldFeedback.CanOutput
                && offered.Destination.FlatDistanceTo(FieldPresenceService.PlayerPos.X, FieldPresenceService.PlayerPos.Z) <= RangeM);
    }
}
