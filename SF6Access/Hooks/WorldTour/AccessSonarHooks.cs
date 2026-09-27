using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// Hear the ways through: ladders and doors within the beacons' range each give a short
/// earcon from where they are — a rising metal "tink-tink-tink" for a ladder, a knock for a
/// door — one cue at a time, taking turns among the nearest, panned to their side, an octave
/// down when behind, quieter when farther. An awareness lane, not a homing pulse: it says
/// "there is a ladder over there"; looking at it names it, and R3 or the picker leads there.
/// Design: <c>docs/wt-soundscape.md</c>.
/// </summary>
public class AccessSonarHooks
{
    private const string LADDER_SOUND = "ladder.mp3";
    private const string DOOR_SOUND = "door knock.mp3";

    private static float RangeM => FieldBeaconHooks.HOME_RANGE_M;
    private static float NearM => MissionBeaconHooks.ARRIVED_M;

    // Silence after each cue before the next: one cue at a time, never a chord, and slow
    // enough to sit under the homing beacons (their far gap is 0.5 s at the fastest).
    private const int GAP_MS = 700;

    // At most three accesses take turns: with more, each would come round too rarely to follow.
    private const int VOICES = 3;

    // Ambient lane, below the homing beacons' AudioService.DEFAULT_VOLUME.
    private const float LOUD = 0.4f, QUIET = 0.12f;

    // Same hold the beacons keep after an interrupting spoken line.
    private const long READER_HOLD_MS = 1200;

    private static readonly List<(FieldAccessCatalog.Access Access, float Distance)> _near = new();
    private static long _nextAt;
    private static int _turn;
    private static bool _logged;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] AccessSonarHooks initialized (earcons for nearby ladders and doors)");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        long now = System.Environment.TickCount64;
        if (now < _nextAt) return;
        FieldPresenceService.Refresh();
        if (!FieldPresenceService.CanSpeak || SF6Access.Hooks.SpTalkNovelHooks.DialogueActive
            || PadGuideHooks.Active || DestinationPickerHooks.IsOpen
            || now - ScreenReaderService.LastInterruptTick < READER_HOLD_MS)
            return;

        var me = FieldPresenceService.PlayerPos;
        var destination = MapSelectionMemory.Current;
        _near.Clear();
        foreach (var access in FieldAccessCatalog.Nearby(me.X, me.Y, me.Z, now))
        {
            float d = (float)System.Math.Sqrt(FieldAccessCatalog.DistanceSq(access.X, access.Y, access.Z, me.X, me.Y, me.Z));
            if (d > RangeM) continue;
            // The chosen destination already has the homing beacon.
            if (destination.Ok && FieldAccessCatalog.DistanceSq(access.X, access.Y, access.Z,
                    destination.X, destination.Y, destination.Z) <= NearM * NearM) continue;
            _near.Add((access, d));
        }
        int pick = SonarRota.Pick(_near.Count, VOICES, _turn);
        if (pick < 0) { _nextAt = now + GAP_MS; return; }
        _near.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var (target, dist) = _near[pick];
        _turn++;

        var forward = FieldDirectionService.GetCameraForward();
        var bearing = FieldDirectionService.GetBearing(forward, target.X - me.X, target.Z - me.Z);
        float pan = bearing.Ok ? bearing.Right : 0f;
        float rate = bearing.Ok && bearing.Behind ? HomingCue.BEHIND_RATE : HomingCue.AHEAD_RATE;
        var length = AudioService.PlaySound(target.Kind == AccessKind.Ladder ? LADDER_SOUND : DOOR_SOUND,
            pan, SonarRota.Volume(dist, NearM, RangeM, LOUD, QUIET), rate);
        _nextAt = now + (long)length.TotalMilliseconds + GAP_MS;

        if (_logged) return;
        _logged = true;
        API.LogInfo($"[SF6Access] Access sonar: first cue, {target.Kind} at {dist:0.0} m ({_near.Count} in range)");
    }
}
