using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// Ambient audio beacons on World Tour NPCs: instead of announcing people, let
/// the people make noise. Each ping plays one of that NPC's OWN sounds through
/// its OWN emitter, so it lands in real 3D at their position and reads as the
/// city being alive rather than as a mod beeping over it.
///
/// <para><b>Two layers, because one cadence cannot do both jobs.</b> A single
/// interval spread over several NPCs meant any one of them sounded only every
/// 20-40 s — fine for "there are people here", useless for walking towards
/// someone. So: a HOMING pulse from the nearest NPC on a regular beat that
/// tightens as you close in (a steady rhythm is far easier to walk towards than
/// a randomised one), plus a sparse AMBIENT ping from somebody else, randomised
/// because a mechanical rhythm would grate there.</para>
///
/// <para><b>Always on</b> (user rule 2026-08-14): no toggle key. The beacons are
/// how the field sounds inhabited, and a player should not have to know a
/// shortcut exists to get that. The field being loaded is the only switch.</para>
///
/// <para>Silence rules — the beacon must never cost the player information:
/// <list type="bullet">
/// <item>held while the panel guide is running
///   (<see cref="PadGuideHooks.Active"/>) — during that tutorial the panels are
///   the objective, and pings toward bystanders compete with the cue;</item>
/// <item>held while a World Tour dialogue is on screen
///   (<see cref="SF6Access.Hooks.SpTalkNovelHooks.DialogueActive"/>) — a beacon
///   voice competes with the game's own dialogue voice and makes lines drop;</item>
/// <item>held while anything is in interaction range, which is where prompts and
///   tutorial text appear, and where <see cref="FieldAwarenessHooks"/> owns the
///   announcement anyway;</item>
/// <item>held briefly after the screen reader speaks, so pings never bury an
///   announcement;</item>
/// <item>voices are suppressed near a target even when a ping IS allowed, since
///   spoken lines are what collide; the quieter noises still play.</item>
/// </list></para>
/// </summary>
public class FieldBeaconHooks
{
    // Homing cadence in LateUpdate ticks at 60 fps, interpolated over distance:
    // the quickening itself is the signal that you are getting closer.
    private const int HOME_NEAR_TICKS = 132;   // 2.2 s on top of them
    private const int HOME_FAR_TICKS = 420;    // 7 s at the edge of the range
    private const float HOME_RANGE_M = 25f;

    // Ambient cadence: a random gap in this range. The two layers stack, so the
    // felt density is the sum of both, not either one alone.
    private const int AMBIENT_MIN_TICKS = 420;   // 7 s
    private const int AMBIENT_MAX_TICKS = 900;   // 15 s

    // How many of the nearest NPCs the ambient layer may pick from.
    private const int AMBIENT_CANDIDATES = 5;

    // Voices are long and collide with dialogue, so they stay a minority: about
    // one homing ping in four, one ambient ping in three.
    private const int VOICE_ONE_IN_HOMING = 4;
    private const int VOICE_ONE_IN_AMBIENT = 3;

    // Hold after the reader speaks, so a ping never lands on top of an
    // announcement (same intent as ScreenReaderService's duplicate window).
    private const long READER_HOLD_MS = 1200;

    private static int _homeCountdown;
    private static int _ambientCountdown;
    private static readonly System.Random Rng = new System.Random();

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] FieldBeaconHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();

        // ONLY WHILE MOVING THROUGH THE WORLD (user rule 2026-08-14): World Tour
        // and the Battle Hub, never in a fight and never in menus. The beacons
        // say "the city is inhabited", which is information only while the player
        // is going somewhere; standing in a menu it is just noise.
        if (!FieldPresenceService.CanSpeakWhileMoving)
        {
            // Reset the cadences so moving again pings promptly.
            Reset();
            return;
        }

        var mgr = WorldTourStateService.GetAvatarManager();
        if (mgr == null) { Reset(); return; }

        if (SF6Access.Hooks.SpTalkNovelHooks.DialogueActive) return;
        if (PadGuideHooks.Active) return;
        if (System.Environment.TickCount64 - ScreenReaderService.LastInterruptTick < READER_HOLD_MS) return;

        // In interaction range the arrival reader owns the moment, and this is
        // where prompts and tutorial text appear.
        bool nearTarget = AvatarFieldReader.GetAccessInfoCount(mgr) > 0;
        if (nearTarget) return;

        var others = AvatarFieldReader.ReadOthers(mgr);
        if (others.Count == 0) return;

        if (--_homeCountdown <= 0)
        {
            _homeCountdown = HomeInterval(others[0].Dist);
            bool ok = NpcBeaconService.Ping(others[0].Avatar, AllowVoice(VOICE_ONE_IN_HOMING));
            // Logged because a beacon is otherwise UNOBSERVABLE from the log: a
            // silent failure and a working beacon the player simply did not
            // notice look identical, and that cost a whole test round.
            API.LogInfo($"[SF6Access] Beacon home {others[0].Dist:0.0}m {(ok ? "played" : "FAILED")}");
        }

        if (--_ambientCountdown <= 0)
        {
            _ambientCountdown = Rng.Next(AMBIENT_MIN_TICKS, AMBIENT_MAX_TICKS);
            int pool = System.Math.Min(others.Count, AMBIENT_CANDIDATES);
            if (pool > 1)
            {
                var pick = others[1 + Rng.Next(pool - 1)];
                bool ok = NpcBeaconService.Ping(pick.Avatar, AllowVoice(VOICE_ONE_IN_AMBIENT));
                API.LogInfo($"[SF6Access] Beacon ambient {pick.Dist:0.0}m {(ok ? "played" : "FAILED")}");
            }
        }
    }

    /// <summary>Ticks until the nearest NPC sounds again: fast up close, slow far
    /// away, so the rhythm itself tells the player they are getting warmer.</summary>
    private static int HomeInterval(float meters)
    {
        float t = meters / HOME_RANGE_M;
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;
        return (int)(HOME_NEAR_TICKS + t * (HOME_FAR_TICKS - HOME_NEAR_TICKS));
    }

    private static bool AllowVoice(int oneIn) => Rng.Next(oneIn) == 0;

    private static void Reset()
    {
        _homeCountdown = 0;
        _ambientCountdown = 0;
    }
}
