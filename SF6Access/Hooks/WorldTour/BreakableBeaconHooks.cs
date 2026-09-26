using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// A looping homing pulse on the nearest breakable street prop — crates, balls,
/// signs, barrels — so a blind player can find the things a mission needs smashed.
///
/// <para><b>It goes quiet the moment the thing is broken.</b> That is the whole
/// contract: <see cref="BreakableCatalog"/> only ever reports props whose own state
/// still says intact, so smashing one drops it from the list and the pulse moves to
/// the next within a scan. A prop whose state cannot be read at all is never cued —
/// a beacon over rubble would send the player to hit something that is not there.</para>
///
/// <para><b>It does NOT yield to the mission beacon, and the first version's doing
/// so was a design mistake.</b> The mission beacon pulses whenever an objective is
/// active, which is nearly always — so standing down for it meant this cue was
/// silent almost all the time, and the tester reported exactly that ("no me indica
/// mucho"). The two are different tasks with different sounds: the objective is
/// usually far away while the crate you must smash is at arm's reach, and hearing
/// both is informative rather than confusing. Only the NPC beacon still stands
/// down (<see cref="FieldBeaconHooks"/>), because a passer-by is not a task.</para>
///
/// <para>Same shape as the NPC beacon in every other respect — panned to the
/// camera-relative bearing, cadence tightening as you close in, silent outside
/// <see cref="RANGE_M"/> and while anything else owns the ear.</para>
/// </summary>
public class BreakableBeaconHooks
{
    /// <summary>The bundled cue, supplied by the tester 2026-09-09.</summary>
    private const string CUE_FILE = "breakable object.mp3";

    /// <summary>How far a breakable can be and still be worth pointing at. The same
    /// radius the NPC beacon uses, so the two cues have the same sense of "near".</summary>
    private const float RANGE_M = FieldBeaconHooks.HOME_RANGE_M;

    /// <summary>Where the cadence bottoms out at its SLOWEST. Deliberately shorter
    /// than <see cref="RANGE_M"/>: the ramp from fast to slow is the only thing that
    /// tells the player they are getting warmer, so it is spent over the stretch
    /// where they are actually closing in rather than smeared across the whole
    /// detection radius. Beyond this a breakable still pulses, just at the slow
    /// rate — "there is one somewhere over there".</summary>
    private const float GRADIENT_FROM_M = RANGE_M * 0.6f;

    /// <summary>Hold after the reader speaks with an interrupt, so a pulse never
    /// lands on top of a line the player is still hearing.</summary>
    private const long READER_HOLD_MS = 1200;

    /// <summary>How long to wait before looking again once nothing is in range.
    /// Re-checking at frame rate would walk the scene for nothing.</summary>
    private const long OUT_OF_RANGE_RECHECK_MS = 1000;

    private static readonly HomingCue Cue = new(CUE_FILE, MissionBeaconHooks.ARRIVED_M, GRADIENT_FROM_M);

    private static bool _wasInField;

    /// <summary>True while this beacon owns the ear, so the NPC beacon can stand
    /// down rather than talk over it.</summary>
    internal static bool Homing { get; private set; }

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] BreakableBeaconHooks initialized (pulse on the nearest intact breakable)");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();
        if (!FieldPresenceService.CanSpeak)
        {
            if (_wasInField) { _wasInField = false; Reset(); }
            return;
        }
        _wasInField = true;

        // The cue decides its own cadence; everything below runs only on the frame
        // it is actually due, so a quiet stretch costs a clock comparison.
        if (!Cue.Due) return;

        // Nothing may sound over the game's own dialogue voice, the tutorial guide,
        // or a line the reader has just started.
        if (SF6Access.Hooks.SpTalkNovelHooks.DialogueActive) return;
        if (PadGuideHooks.Active) return;
        if (System.Environment.TickCount64 - ScreenReaderService.LastInterruptTick < READER_HOLD_MS) return;

        var mgr = WorldTourStateService.GetAvatarManager();
        var me = AvatarFieldReader.ReadPlayerPos(mgr);
        if (!me.ok) { Stand(); return; }

        var target = BreakableCatalog.Nearest(me.x, me.y, me.z, RANGE_M);
        if (!target.HasValue) { Stand(); return; }

        var t = target.Value;
        float dx = t.X - me.x, dy = t.Y - me.y, dz = t.Z - me.z;
        float dist = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);

        Homing = true;
        Cue.Sound(FieldDirectionService.GetCameraForward(), dx, dz, dist);
    }

    /// <summary>Nothing to point at right now: go quiet and let the NPC beacon have
    /// the ear back.</summary>
    private static void Stand()
    {
        Homing = false;
        Cue.Snooze(OUT_OF_RANGE_RECHECK_MS);
    }

    private static void Reset()
    {
        Homing = false;
        Cue.Reset();
        BreakableCatalog.Reset();
    }
}
