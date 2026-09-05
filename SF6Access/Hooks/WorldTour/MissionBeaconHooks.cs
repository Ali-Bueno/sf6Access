using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// Homing beacon on the current World Tour mission objective — the point the
/// game's own on-screen marker points at.
///
/// <para>Two channels, deliberately unequal. The SPOKEN line is the precise one
/// ("mission at 2 o'clock, 30 meters") but it is rare, because a mission target
/// is a destination you walk to over a minute or two and a reminder every few
/// seconds would be nagging. The SOUND is the continuous one: the mod's own
/// beacon sample, panned toward the objective, dropped an octave when the
/// objective is behind you, and repeated faster the closer you get — so the
/// question "am I still heading the right way" is answered without having to be
/// asked. See <see cref="HomingCue"/> for how those three channels divide up.</para>
///
/// <para><b>The game's own sounds were tried and dropped.</b> The tutorial panel
/// cue lives in a bank tied to the tutorial, so it could only be learned by
/// standing next to one — useless to anyone past that point. The objective's own
/// voice, which replaced it, is a different line every time and was inaudible at
/// 5.5 m while still reporting success, so it SUPPRESSED the fallback and left
/// the player in silence. A beacon has to be one recognisable sound, audible at
/// the range an objective actually sits at; Wwise cannot give that from a bank
/// that is loaded everywhere, and the mod's own mixer can, so the mixer wins.</para>
///
/// <para>No toggle key, like the rest of the World Tour readers, and it stands
/// down for the panel guide: during that tutorial the panels are the objective.</para>
/// </summary>
public class MissionBeaconHooks
{
    // Announce cadence. Deliberately slower than the panel guide's: a mission
    // target is a destination you walk to over a minute or two, not a thing you
    // hunt for in a five-metre circle, and at that timescale a frequent
    // reminder is nagging rather than guidance.
    private const int ANNOUNCE_NEAR_TICKS = 480;    // 8 s once close
    private const int ANNOUNCE_FAR_TICKS = 900;     // 15 s further out
    private const float NEAR_M = 15f;

    // How often the objective is re-resolved. Walking the mission system's target
    // lists is not free, and the objective changes on the scale of minutes.
    private const int RESOLVE_TICKS = 60;           // 1 s

    // Below this the objective is effectively reached; the game takes over with
    // its own prompt, so the beacon goes quiet rather than talking over it.
    private const float ARRIVED_M = 4f;

    private const long READER_HOLD_MS = 1200;

    // The user's own beacon sample, deployed next to the plugin by the build.
    private const string CUE_FILE = "mission beacon.mp3";

    // Distance from which the ping is at its slowest. Mission objectives usually
    // sit tens of metres out, so the cadence ramp has to span that whole walk: a
    // ramp that saturated after a few metres would be pinned at its slowest for
    // almost the entire approach and would say nothing about progress. The other
    // end of the ramp is ARRIVED_M, so the ping is tightest exactly as the beacon
    // hands over to the game's own prompt.
    private const float PING_FAR_M = 60f;

    private static readonly HomingCue Cue = new(CUE_FILE, ARRIVED_M, PING_FAR_M);

    private static int _resolveCountdown;
    private static int _announceCountdown;
    private static bool _wasArrived;
    private static string _lastSpoken;

    // Last resolved objective position. The ping re-reads the PLAYER and the
    // camera every time it fires but reuses this, because an objective barely
    // moves between resolves while the player and the camera move constantly.
    private static bool _haveFix;
    private static float _fixX, _fixY, _fixZ;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] MissionBeaconHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        if (--_resolveCountdown <= 0)
        {
            _resolveCountdown = RESOLVE_TICKS;
            Resolve();
        }
        Ping();
    }

    /// <summary>The once-a-second half: where the objective is, and the spoken
    /// line. Everything the ping needs is left behind in the fix.</summary>
    private static void Resolve()
    {
        FieldPresenceService.Refresh();
        if (!FieldPresenceService.CanSpeak) { Reset(); return; }
        // The panel tutorial owns the objective while it runs.
        if (PadGuideHooks.Active) { Hush(); return; }
        if (SF6Access.Hooks.SpTalkNovelHooks.DialogueActive) { Hush(); return; }
        if (System.Environment.TickCount64 - ScreenReaderService.LastInterruptTick < READER_HOLD_MS) return;

        var mgr = WorldTourStateService.GetAvatarManager();
        if (mgr == null) { Reset(); return; }

        var target = MissionTargetService.Find();
        if (!target.Ok) { Reset(); return; }

        var player = AvatarFieldReader.ReadPlayerPos(mgr);
        if (!player.ok) return;

        float dx = target.X - player.x, dy = target.Y - player.y, dz = target.Z - player.z;
        float dist = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);

        if (dist <= ARRIVED_M)
        {
            // Announce arrival once, then hand over: standing on the objective,
            // the game's own prompt and the interaction reader say more than a
            // repeated distance would.
            Hush();
            if (!_wasArrived)
            {
                _wasArrived = true;
                _lastSpoken = null;
                ScreenReaderService.Speak(LocalizedText.MissionHere(), interrupt: true);
            }
            return;
        }
        _wasArrived = false;

        _fixX = target.X; _fixY = target.Y; _fixZ = target.Z;
        _haveFix = true;

        _announceCountdown -= RESOLVE_TICKS;
        if (_announceCountdown > 0) return;
        _announceCountdown = dist <= NEAR_M ? ANNOUNCE_NEAR_TICKS : ANNOUNCE_FAR_TICKS;

        int meters = (int)System.Math.Round(dist);
        int hour = FieldDirectionService.ClockHour(
            FieldDirectionService.GetCameraForward(), dx, dz);

        string text = hour > 0
            ? LocalizedText.AtClockMeters(LocalizedText.MissionWord(), hour, meters)
            : LocalizedText.AtMeters(LocalizedText.MissionWord(), meters);

        // Standing still produces the identical phrase — stay silent.
        if (text != _lastSpoken)
        {
            _lastSpoken = text;
            ScreenReaderService.Speak(text, interrupt: true);
        }

        API.LogInfo($"[SF6Access] Mission beacon {dist:0.0}m at {hour} o'clock");
    }

    /// <summary>The sound half, checked every frame because the ping cadence is
    /// finer than the one-second resolve. The check itself is a clock comparison;
    /// the position reads happen only on the frame that actually sounds, so
    /// running this at frame rate costs nothing between pings.</summary>
    private static void Ping()
    {
        if (!_haveFix || !Cue.Due) return;
        // Never on top of a line the reader has just started: the cue is long
        // enough to bury one, and the beacon speaks rarely enough that waiting
        // out the sentence costs at most one repeat.
        if (System.Environment.TickCount64 - ScreenReaderService.LastInterruptTick < READER_HOLD_MS) return;

        var mgr = WorldTourStateService.GetAvatarManager();
        if (mgr == null) return;
        var player = AvatarFieldReader.ReadPlayerPos(mgr);
        if (!player.ok) return;

        float dx = _fixX - player.x, dy = _fixY - player.y, dz = _fixZ - player.z;
        float dist = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        // Arrival is the resolve's business — it has the announcement to make.
        if (dist <= ARRIVED_M) return;

        Cue.Sound(FieldDirectionService.GetCameraForward(), dx, dz, dist);
    }

    /// <summary>Stop the sound without disturbing the spoken channel's state —
    /// for the stretches where something else owns the objective (the panel
    /// tutorial, a dialogue) or where there is nothing left to home in on.</summary>
    private static void Hush()
    {
        _haveFix = false;
        Cue.Reset();
    }

    private static void Reset()
    {
        Hush();
        _announceCountdown = 0;
        _wasArrived = false;
        _lastSpoken = null;
    }
}
