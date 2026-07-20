using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// Continuous field tracker (WT-1 follow-up, user-requested): hands-free
/// guidance toward the nearest avatar without hammering the radar key. A toggle
/// key turns tracking on/off; while on, the nearest avatar's camera-relative
/// clock hour and distance are spoken periodically ("at 12 o'clock, 4 meters"),
/// with the full name repeated only when the nearest target CHANGES.
///
/// Silence rules (so it never talks over what matters):
/// <list type="bullet">
/// <item>only speaks when the spoken text actually changed — standing still
///   stays silent;</item>
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
    // Toggle key. Provisional binding (keyboard M, next to the radar's N; no
    // gamepad button yet — Start is taken by the on-demand radar) — the tester
    // confirms a non-conflicting choice in-game, as with the other shortcuts.
    private const int VK_M = 0x4D;
    private static readonly ReadoutShortcut ToggleKey = new(VK_M, padFlag: 0);

    // Spoken-update cadence: ~2 s between announcements at 60 fps LateUpdate
    // ticks (same frame-tick convention as FieldAwarenessHooks.POLL_INTERVAL).
    // A UX choice: fast enough to steer by, slow enough for the phrase to finish.
    private const int ANNOUNCE_TICKS = 120;

    private static bool _on;
    private static int _tick;
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
        bool toggled = ToggleKey.Pressed();
        var mgr = WorldTourStateService.GetAvatarManager();

        if (mgr == null)
        {
            // Field unloaded: drop tracking silently; a fresh field starts off.
            if (_on) Reset();
            return;
        }

        if (toggled)
        {
            _on = !_on;
            ScreenReaderService.Speak(
                _on ? LocalizedText.TrackingOn() : LocalizedText.TrackingOff(), interrupt: true);
            if (!_on) { Reset(); return; }
            _tick = ANNOUNCE_TICKS; // speak the first update immediately
        }

        if (!_on) return;

        // Hold (without disabling) while a dialogue line is on screen or while
        // something is already in interaction range — those readers own the mic.
        if (SF6Access.Hooks.SpTalkNovelHooks.DialogueActive) return;
        if (AvatarFieldReader.GetAccessInfoCount(mgr) > 0) return;

        if (++_tick < ANNOUNCE_TICKS) return;
        _tick = 0;

        var others = AvatarFieldReader.ReadOthers(mgr);
        if (others.Count == 0) return;

        var nearest = others[0];
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

    private static void Reset()
    {
        _on = false;
        _tick = 0;
        _lastTargetDesc = null;
        _lastSpoken = null;
    }
}
