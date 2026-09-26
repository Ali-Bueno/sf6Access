using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// Audio beacons on World Tour NPCs, so the field can be explored by ear with
/// no key and no spoken census.
///
/// <para><b>Two layers, because one sound cannot do both jobs.</b></para>
/// <list type="bullet">
/// <item><b>HOMING</b> — the nearest person, pulsed through the mod's own mixer
///   with the same <see cref="HomingCue"/> the mission beacon uses: panned to
///   the side they are on, an octave down when they are behind, and repeated
///   faster the closer they get. Turning the camera is heard as the pan sliding
///   toward the centre; the aim tone (<see cref="FieldAimHooks"/>) then marks the
///   moment it gets there. The game's own emitter was tried first for this
///   (2026-08-14) and dropped: engine attenuation made it inaudible past a few
///   metres and its 2-7 s beat was far too slow to steer by.</item>
/// <item><b>AMBIENT</b> — a sparse, randomised ping from somebody else, played
///   through THAT NPC's own emitter (<see cref="NpcBeaconService"/>) so it lands
///   in real 3D and reads as the city being alive rather than as the mod beeping
///   over it.</item>
/// </list>
///
/// <para>The homing target is the shared <see cref="StickyTarget.NearestPerson"/>
/// — the same person the tracker names and the aim tone confirms — and the
/// pulse stops beyond <see cref="HOME_RANGE_M"/>: a beacon toward somebody a
/// street away is noise, and silence there is the information "nobody near".
/// It also yields to the mission beacon, since two homing pulses are two
/// directions at once.</para>
///
/// <para><b>Always on</b> (user rule 2026-08-14): no toggle key. The field being
/// loaded is the only switch.</para>
///
/// <para>Silence rules — the beacon must never cost the player information:
/// held during the panel guide (<see cref="PadGuideHooks.Active"/>), during a
/// World Tour dialogue (<see cref="SF6Access.Hooks.SpTalkNovelHooks.DialogueActive"/>),
/// while anything is in interaction range (where <see cref="FieldAwarenessHooks"/>
/// owns the moment), and briefly after the screen reader speaks.</para>
/// </summary>
public class FieldBeaconHooks
{
    /// <summary>Beyond this the nearest person is not "near": the homing pulse is
    /// silent, and the ambient layer is all that sounds. Carried over from the
    /// emitter-based homing this replaces (same felt range, tested 2026-08).
    /// Internal: the look-sweep and the tracker's cadence share this radius.</summary>
    internal const float HOME_RANGE_M = 25f;

    /// <summary>While the nearest person is out of range, how long to wait before
    /// reading the avatar list again to see whether that changed. The other slow
    /// field readers use the same 2 s beat.</summary>
    private const long OUT_OF_RANGE_RECHECK_MS = 2000;

    // The user's own NPC beacon sample, deployed next to the plugin by the build.
    private const string HOME_FILE = "npc beacon.mp3";

    /// <summary>The homing pulse: tightest at the interaction radius, where the
    /// arrival reader takes over, and slowest from the edge of the range.</summary>
    private static readonly HomingCue Home = new(HOME_FILE, MissionBeaconHooks.ARRIVED_M, HOME_RANGE_M);

    // Ambient cadence: a random gap in this range. The two layers stack, so the
    // felt density is the sum of both, not either one alone.
    private const int AMBIENT_MIN_TICKS = 420;   // 7 s
    private const int AMBIENT_MAX_TICKS = 900;   // 15 s

    // How many of the nearest NPCs the ambient layer may pick from.
    private const int AMBIENT_CANDIDATES = 5;

    // Voices are long and collide with dialogue, so they stay a minority: about
    // one ambient ping in three.
    private const int VOICE_ONE_IN_AMBIENT = 3;

    // Hold after the reader speaks, so a ping never lands on top of an
    // announcement (same intent as ScreenReaderService's duplicate window).
    private const long READER_HOLD_MS = 1200;

    private static int _ambientCountdown;
    private static readonly System.Random Rng = new System.Random();

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] FieldBeaconHooks initialized (homing pulse + ambient pings)");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();

        // ONLY WHILE MOVING THROUGH THE WORLD (user rule 2026-08-14): World Tour
        // and the Battle Hub, never in a fight and never in menus. The beacons
        // say "the city is inhabited", which is information only while the player
        // is going somewhere; standing in a menu it is just noise.
        if (!FieldPresenceService.CanSpeakWhileMoving) { Reset(); return; }

        // Both layers are clock checks first, so the avatar list — the expensive
        // read — is only walked on a frame that will actually sound.
        bool homeDue = Home.Due;
        bool ambientDue = --_ambientCountdown <= 0;
        if (!homeDue && !ambientDue) return;

        var mgr = WorldTourStateService.GetAvatarManager();
        if (mgr == null) { Reset(); return; }

        if (SF6Access.Hooks.SpTalkNovelHooks.DialogueActive) return;
        if (PadGuideHooks.Active) return;
        if (System.Environment.TickCount64 - ScreenReaderService.LastInterruptTick < READER_HOLD_MS) return;

        // In interaction range the arrival reader owns the moment, and this is
        // where prompts and tutorial text appear.
        if (AvatarFieldReader.GetAccessInfoCount(mgr) > 0) { Home.Snooze(OUT_OF_RANGE_RECHECK_MS); return; }

        var others = AvatarFieldReader.ReadOthers(mgr);
        if (others.Count == 0) { Home.Snooze(OUT_OF_RANGE_RECHECK_MS); return; }

        if (homeDue) PulseHome(others);

        if (ambientDue)
        {
            _ambientCountdown = Rng.Next(AMBIENT_MIN_TICKS, AMBIENT_MAX_TICKS);
            int pool = System.Math.Min(others.Count, AMBIENT_CANDIDATES);
            if (pool > 1)
            {
                var pick = others[1 + Rng.Next(pool - 1)];
                bool ok = NpcBeaconService.Ping(pick.Avatar, AllowVoice(VOICE_ONE_IN_AMBIENT));
                // Logged because a beacon is otherwise UNOBSERVABLE from the log: a
                // silent failure and a working beacon the player simply did not
                // notice look identical, and that cost a whole test round.
                API.LogInfo($"[SF6Access] Beacon ambient {pick.Dist:0.0}m {(ok ? "played" : "FAILED")}");
            }
        }
    }

    /// <summary>One homing repeat toward the shared nearest person, or a pause
    /// when there is nothing to home in on: out of range, or the mission beacon
    /// already pulsing.</summary>
    private static void PulseHome(System.Collections.Generic.List<AvatarFieldReader.Other> others)
    {
        if (MissionBeaconHooks.Homing) { Home.Snooze(OUT_OF_RANGE_RECHECK_MS); return; }
        // ...and to the breakable beacon: something a mission needs smashed is more
        // use to the player than a passer-by, and two pulses are two directions.
        if (BreakableBeaconHooks.Homing) { Home.Snooze(OUT_OF_RANGE_RECHECK_MS); return; }

        var target = StickyTarget.NearestPerson.Pick(others);
        if (target.Dist > HOME_RANGE_M) { Home.Snooze(OUT_OF_RANGE_RECHECK_MS); return; }

        Home.Sound(FieldDirectionService.GetCameraForward(), target.Dx, target.Dz, target.Dist);
    }

    private static bool AllowVoice(int oneIn) => Rng.Next(oneIn) == 0;

    private static void Reset()
    {
        Home.Reset();
        _ambientCountdown = 0;
    }
}
