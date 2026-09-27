using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// Find PLACES by looking, the way <see cref="FieldAimHooks"/> finds people: while the camera
/// turns, the nearest known place entering the aim cone is named with its distance ("Beat
/// Square, 120 meters"). Named while standing still, it becomes R3's target: a tap of R3
/// within a few seconds, still standing and facing it, makes it the destination — choosing
/// without a menu. Design: <c>docs/wt-free-navigation.md</c>.
/// </summary>
public class FieldPlaceSweepHooks
{
    /// <summary>10 Hz, the people sweep's cadence, offset by half a poll so the two do not
    /// speak on the same frame.</summary>
    private const int POLL_TICKS = 6;
    private const int POLL_OFFSET = POLL_TICKS / 2;

    // Same cone and turn speed as the people sweep, so both answer the same look.
    private const float ENTER_DEG = FieldDirectionService.DEGREES_PER_HOUR / 2f;
    private const float LEAVE_DEG = FieldDirectionService.DEGREES_PER_HOUR;
    private const double SCAN_TURN_DEG_PER_S = FieldDirectionService.DEGREES_PER_HOUR / 2.0;

    /// <summary>How long after a place is named a tap of R3 still means "go there".</summary>
    private const long CANDIDATE_MS = 4000;

    /// <summary>After a dialogue, a fight or a menu the camera blends back by itself.</summary>
    private const long RESUME_SETTLE_MS = 1000;

    private static readonly ConeSweep _cone = new(ENTER_DEG, LEAVE_DEG);
    private static readonly CameraTurnDetector _turn = new(SCAN_TURN_DEG_PER_S);
    private static int _tick = POLL_OFFSET;
    private static PlaceCache.Place _candidate;
    private static bool _hasCandidate, _paused;
    private static long _candidateAt, _resumedAt;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] FieldPlaceSweepHooks initialized (look-sweep: places; R3 = go to the one just named)");
    }

    /// <summary>The place just named, if the player is still standing and facing it; consumed.</summary>
    public static bool TryTakeCandidate(out Destination place)
    {
        place = _candidate.Destination;
        bool fresh = _hasCandidate && !FieldPresenceService.Moving
                     && System.Environment.TickCount64 - _candidateAt <= CANDIDATE_MS
                     && _cone.Contains(_candidate.Key);
        _hasCandidate = false;
        return fresh;
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();
        long now = System.Environment.TickCount64;
        bool blocked = !FieldPresenceService.CanSpeak || SF6Access.Hooks.SpTalkNovelHooks.DialogueActive
                       || PadGuideHooks.Active;
        if (blocked) { Pause(); return; }
        if (_paused) { _paused = false; _resumedAt = now; }

        if (++_tick < POLL_TICKS) return;
        _tick = 0;
        if (DestinationPickerHooks.IsOpen) return;
        // An interaction prompt owns the moment, as for the people sweep.
        if (AvatarFieldReader.GetAccessInfoCount(WorldTourStateService.GetAvatarManager()) > 0) return;

        var forward = FieldDirectionService.GetCameraForward();
        if (!forward.Ok) return;
        var me = FieldPresenceService.PlayerPos;
        long ownTurnUntil = System.Math.Max(CameraAimService.OwnTurnUntil, _resumedAt + RESUME_SETTLE_MS);
        bool turning = _turn.Sample(forward.X, forward.Z, now, ownTurnUntil);

        PlaceCache.Place best = default;
        float bestM = float.MaxValue;
        _cone.BeginPoll();
        foreach (var place in PlaceCache.Places())
        {
            var d = place.Destination;
            float dx = d.X - me.X, dz = d.Z - me.Z;
            float m = (float)System.Math.Sqrt(dx * dx + dz * dz);
            // A master close by is a person the people sweep already names.
            if (d.Category == DestinationCategory.Masters && m <= FieldBeaconHooks.HOME_RANGE_M) continue;
            var b = FieldDirectionService.GetBearing(forward, dx, dz);
            if (!b.Ok) continue;
            float offDeg = (float)System.Math.Abs(System.Math.Atan2(b.Right, b.Ahead) * 180.0 / System.Math.PI);
            // Cone state is kept while walking straight too; only the speech waits for a turn.
            if (_cone.Offer(place.Key, offDeg) && turning && m < bestM) { best = place; bestM = m; }
        }
        _cone.EndPoll();
        if (bestM == float.MaxValue) return;

        // Standing, this is a scan: the newest name is what the camera points at, so it
        // interrupts, the look input freezes for a beat (as for a compass point) and R3 is
        // offered it. Walking, it is background: queued, and R3 keeps meaning "my route".
        bool standing = !FieldPresenceService.Moving;
        ScreenReaderService.Speak(LocalizedText.AtMeters(best.Destination.Label, (int)System.Math.Round(bestM)),
                                  interrupt: standing);
        _hasCandidate = standing;
        if (standing) CameraHoldService.Hold();
        _candidate = best;
        _candidateAt = now;
    }

    private static void Pause()
    {
        if (_paused) return;
        _paused = true;
        _cone.Clear();
        _turn.Reset();
        _hasCandidate = false;
        _tick = POLL_OFFSET;
    }
}
