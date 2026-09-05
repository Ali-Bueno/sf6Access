using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// Continuous field tracker (WT-1 follow-up, user-requested): hands-free
/// guidance toward the nearest avatar without hammering the radar key. The
/// nearest avatar's camera-relative clock hour and distance are spoken
/// periodically ("at 12 o'clock, 4 meters"), with the full name repeated only
/// when the nearest target CHANGES.
///
/// <para><b>Always on</b> (user rule 2026-08-14): no toggle key. The silence
/// rules below are what keeps that bearable — it is quiet unless the reading
/// actually changed, so standing still costs nothing.</para>
///
/// Silence rules (so it never talks over what matters):
/// <list type="bullet">
/// <item>only speaks when the spoken text actually changed — standing still
///   stays silent;</item>
/// <item>holds while the panel guide is running
///   (<see cref="PadGuideHooks.Active"/>), which owns the objective and the mic
///   during that tutorial;</item>
/// <item>holds while a World Tour dialogue is on screen
///   (<see cref="SF6Access.Hooks.SpTalkNovelHooks.DialogueActive"/>);</item>
/// <item>holds while any target is in interaction range — arrival is announced
///   by <see cref="FieldAwarenessHooks"/>'s target-change reader, which owns
///   that moment;</item>
/// <item>auto-stops silently when the field unloads (leaving World Tour).</item>
/// </list>
/// </summary>
public class FieldTrackingHooks
{
    // Spoken-update cadence: ~2 s between announcements at 60 fps LateUpdate
    // ticks (same frame-tick convention as FieldAwarenessHooks.POLL_INTERVAL).
    // A UX choice: fast enough to steer by, slow enough for the phrase to finish.
    private const int ANNOUNCE_TICKS = 120;

    // Hold after the reader speaks with an interrupt, so an update never lands on
    // top of a tutorial line or an arrival announcement.
    private const long READER_HOLD_MS = 1200;

    // How much closer somebody else must be before the tracker abandons its
    // current target. Without this, a crowd steals the target every step.
    private const float SWITCH_MARGIN_M = 2f;

    private static int _tick;
    private static ulong _trackedAddress;
    private static string _lastTargetDesc;
    private static string _lastSpoken;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] FieldTrackingHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();

        // ONLY WHILE WALKING (user rule 2026-08-14). Without this the reader
        // repeated distances endlessly while the player stood reading a tutorial
        // or a menu, talking across the game's own text. Standing still is also
        // exactly when the reading is least useful: it cannot have changed for
        // any reason the player caused.
        if (!FieldPresenceService.CanSpeakWhileMoving)
        {
            // Forget the last reading so the next walk speaks again rather than
            // suppressing itself as a duplicate.
            Reset();
            return;
        }

        var mgr = WorldTourStateService.GetAvatarManager();
        if (mgr == null) { Reset(); return; }

        // Hold (without disabling) while a dialogue line is on screen, while the
        // panel guide is running, or while something is already in interaction
        // range — those readers own the mic.
        if (SF6Access.Hooks.SpTalkNovelHooks.DialogueActive) return;
        if (PadGuideHooks.Active) return;
        if (AvatarFieldReader.GetAccessInfoCount(mgr) > 0) return;
        // Anything the reader just announced with an interrupt — a tutorial line,
        // an arrival — gets to finish. The WT dialogue flag above only covers
        // novel-style dialogue, not tutorial text, so this is what protects it.
        if (System.Environment.TickCount64 - ScreenReaderService.LastInterruptTick < READER_HOLD_MS) return;

        if (++_tick < ANNOUNCE_TICKS) return;
        _tick = 0;

        var others = AvatarFieldReader.ReadOthers(mgr);
        if (others.Count == 0) return;

        var nearest = Sticky(others);
        int meters = (int)System.Math.Round(nearest.Dist);
        int hour = FieldDirectionService.ClockHour(
            FieldDirectionService.GetCameraForward(), nearest.Dx, nearest.Dz);

        string desc = AvatarFieldReader.DescribeAvatar(nearest.Avatar) ?? LocalizedText.ContactPerson();
        bool newTarget = desc != _lastTargetDesc;
        _lastTargetDesc = desc;

        // Full sentence when the target changes; terse "hour, meters" updates
        // while walking toward the same one. Distance-only when no clock frame
        // could be read (keeps the name — there is no terse nameless variant).
        string spoken = hour > 0
            ? (newTarget ? LocalizedText.AtClockMeters(desc, hour, meters)
                         : LocalizedText.ClockShort(hour, meters))
            : LocalizedText.AtMeters(desc, meters);

        // Standing still produces the identical phrase — stay silent.
        if (spoken == _lastSpoken) return;
        _lastSpoken = spoken;

        ScreenReaderService.Speak(spoken, interrupt: false);
    }

    /// <summary>Keep guiding toward the SAME person until somebody else is
    /// clearly closer.
    ///
    /// <para>In a crowd the literal nearest avatar changes with almost every
    /// step, and a tracker that renames its target every two seconds is reading
    /// out a census, not guiding anyone anywhere. The margin means a passer-by
    /// has to actually beat the current target by a couple of metres to steal
    /// it.</para>
    ///
    /// <para>The current target is remembered by ADDRESS, never as a cached
    /// <c>ManagedObject</c>: the address is just a number, so it is safe to hold
    /// across frames, and a stale one simply fails to match.</para>
    /// </summary>
    private static AvatarFieldReader.Other Sticky(System.Collections.Generic.List<AvatarFieldReader.Other> others)
    {
        var nearest = others[0];
        if (_trackedAddress != 0)
            foreach (var o in others)
            {
                if (o.Avatar == null || o.Avatar.GetAddress() != _trackedAddress) continue;
                if (o.Dist <= nearest.Dist + SWITCH_MARGIN_M) return o;
                break;
            }

        _trackedAddress = nearest.Avatar?.GetAddress() ?? 0;
        return nearest;
    }

    private static void Reset()
    {
        _trackedAddress = 0;
        _tick = 0;
        _lastTargetDesc = null;
        _lastSpoken = null;
    }
}
