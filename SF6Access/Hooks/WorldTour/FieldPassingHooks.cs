using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// "Passing by": while the player walks, a known place they come close to is named once,
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

    // At most one place every two seconds: several shops in a row still come one by one.
    private const long GAP_MS = 2000;

    private static readonly ProximityMemory _memory = new(RangeM, 2f * RangeM);
    private static int _tick;
    private static long _spokeAt;
    private static bool _primed;
    private static float _lastX, _lastZ;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] FieldPassingHooks initialized (names places while walking past them)");
    }

    /// <summary>Takes whatever is in range now as already known (arriving, saving a spot here):
    /// the player is at it, not passing it.</summary>
    public static void Prime() => _primed = false;

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();
        if (!FieldPresenceService.InField) { _memory.Clear(); _primed = false; return; }
        if (!FieldPresenceService.CanSpeak) return;
        if (++_tick < POLL_TICKS) return;
        _tick = 0;

        var me = FieldPresenceService.PlayerPos;
        // A jump no walk could make between two polls is a fast travel: landing is arriving.
        float jx = me.X - _lastX, jz = me.Z - _lastZ;
        if (jx * jx + jz * jz > 4f * RangeM * RangeM) _primed = false;
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
            if (!_primed || d.Label == destination) { _memory.Announced(place.Key); continue; }
            if (m < bestM) { best = place; bestM = m; }
        }
        _primed = true;

        bool maySpeak = FieldPresenceService.Moving
                        && !SF6Access.Hooks.SpTalkNovelHooks.DialogueActive && !PadGuideHooks.Active
                        && !DestinationPickerHooks.IsOpen
                        && System.Environment.TickCount64 - _spokeAt >= GAP_MS;
        if (!maySpeak || bestM == float.MaxValue) return;

        _memory.Announced(best.Key);
        _spokeAt = System.Environment.TickCount64;
        var p = best.Destination;
        ScreenReaderService.Speak(p.Label + ", " + DestinationText.Where(me.X, me.Y, me.Z, p.X, p.Y, p.Z),
                                  interrupt: false);
    }
}
