using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// Find people by LOOKING. As the camera turns, every named person it sweeps
/// across is spoken with their distance ("Chun-Li, 12 meters away"), so a
/// player hunting for someone in particular can stand still, turn, and hear
/// who is where — no key, no menu, no census. The nearest person (the shared
/// <see cref="StickyTarget.NearestPerson"/> the homing pulse follows) is marked
/// with a rising tone as well, and "straight ahead" if they have a name,
/// because that is the one the pulse has been pointing at.
///
/// <para><b>Only while the camera is turning (2026-09-07).</b> Walking down a
/// street, people walk INTO the aim cone by themselves, and a sweep that spoke
/// them was a running commentary the player never asked for. A Hero's Call
/// gates its automatic scan the same way — silent while walking straight,
/// spoken on a turn — so the sentence is always an answer to a look. The cone
/// bookkeeping runs regardless, so a look-back after a straight walk finds
/// exactly the people it should.</para>
///
/// <para><b>Quiet by construction, but never stale.</b> A person is spoken when
/// they ENTER the aim cone (half a clock hour) and not again until they have
/// LEFT it by a full hour: wobbling on one person is one sentence, and the
/// only way to hear a name twice is to look away and look back. That second
/// reading is deliberate — "found them, overshot, coming back" must answer
/// (user rule 2026-09-07; a 10 s cooldown was tried first and swallowed
/// exactly that case). One sentence per poll, nearest first. Everyone with a
/// name is spoken, the crowd included: in World Tour a passer-by can be fought
/// or talked to for an item, so unlike the tracker's voice (which follows
/// notable people only, see <see cref="AvatarNameCache"/>) the sweep hides
/// nobody — a look is the player asking. The sweep sentence does not
/// interrupt: it is a background listing, and the reader is free to finish.
/// The nearest person is spoken with an interrupt, and each alignment with
/// them freezes the look input for a beat (<see cref="CameraHoldService"/>).</para>
///
/// <para>Silence rules match the tracker's: nothing during a dialogue, during
/// the panel guide, or while somebody is already in interaction range (the
/// arrival reader owns that moment). Only people within the homing range are
/// swept: beyond it the pulse is silent too, and a name a street away is not
/// something the player can walk to by ear.</para>
/// </summary>
public class FieldAimHooks
{
    /// <summary>Scan cadence in LateUpdate ticks: 10 Hz at 60 fps. Reading the
    /// avatar list is the expensive part; the camera turns faster than the
    /// tracker's beat but not faster than a tenth of a second matters.</summary>
    private const int POLL_TICKS = 6;

    /// <summary>Entering the cone: within half an hour of dead ahead, i.e. the
    /// same precision the clock readout already promises.</summary>
    private const float ENTER_DEG = FieldDirectionService.DEGREES_PER_HOUR / 2f;

    /// <summary>Leaving the cone: a full hour off. The gap between the two is
    /// the whole anti-spam: a person near the edge has to move well out before
    /// they can be announced again, and a deliberate look-back always can.</summary>
    private const float LEAVE_DEG = FieldDirectionService.DEGREES_PER_HOUR;

    /// <summary>Slower than this and the camera is not being turned, it is
    /// drifting (the follow camera settling behind a walking avatar): half a
    /// clock hour per second, the slowest a deliberate look-around goes.</summary>
    private const float SCAN_TURN_DEG_PER_S = FieldDirectionService.DEGREES_PER_HOUR / 2f;

    /// <summary>Only people the homing pulse would sound for are swept.</summary>
    private const float SCAN_RANGE_M = FieldBeaconHooks.HOME_RANGE_M;

    /// <summary>Two rising notes, distinct from the radar's cues and the drop
    /// motif: "lined up with the nearest".</summary>
    private static readonly float[] ALIGNED_TONE = { AudioService.NoteMi, AudioService.NoteLaHigh };

    private static StickyTarget Target => StickyTarget.NearestPerson;

    /// <summary>Who is currently inside the cone, keyed by avatar ADDRESS (a
    /// number, safe to hold across frames; a stale one simply stops matching
    /// and is pruned).</summary>
    private static readonly Dictionary<ulong, bool> InCone = new();
    private static readonly HashSet<ulong> Alive = new();
    private static readonly List<ulong> Gone = new();

    private static int _tick;
    private static ulong _lastTarget;
    private static readonly CameraTurnDetector _turn = new(SCAN_TURN_DEG_PER_S);

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] FieldAimHooks initialized (look-sweep: named people + nearest tone)");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();
        if (!FieldPresenceService.CanSpeak) { Reset(); return; }

        // Cadence gate FIRST: every engine read below creates short-lived managed
        // wrappers, and at 60 Hz that is exactly the GC pressure REFramework warns
        // about; at POLL_TICKS it is a tenth of that.
        if (++_tick < POLL_TICKS) return;
        _tick = 0;

        var mgr = WorldTourStateService.GetAvatarManager();
        if (mgr == null) { Reset(); return; }

        if (SF6Access.Hooks.SpTalkNovelHooks.DialogueActive) return;
        if (PadGuideHooks.Active) return;
        if (AvatarFieldReader.GetAccessInfoCount(mgr) > 0) return;

        var forward = FieldDirectionService.GetCameraForward();
        if (!forward.Ok) return;
        // A turn the mod makes itself (R3 aiming) is not the player looking around.
        bool turning = _turn.Sample(forward.X, forward.Z, System.Environment.TickCount64,
                                    CameraAimService.OwnTurnUntil);

        var others = AvatarFieldReader.ReadOthers(mgr);
        if (others.Count == 0) { Reset(); return; }

        ulong targetAddress = Target.Pick(others).Avatar?.GetAddress() ?? 0;
        // A new nearest person already under the cone is a new alignment, not
        // something that happened silently while they were somebody else.
        if (targetAddress != _lastTarget && InCone.ContainsKey(targetAddress))
            InCone[targetAddress] = false;
        _lastTarget = targetAddress;

        Alive.Clear();
        AvatarFieldReader.Other? toSpeak = null;
        string toSpeakName = null;
        bool speakIsTarget = false;

        // Nearest first (ReadOthers is sorted), so the first NAMED person
        // entering is the nearest one entering — the one sentence this poll may
        // say. The cone state is kept whether or not the player is turning; only
        // the OUTPUT is gated on it.
        foreach (var o in others)
        {
            ulong address = o.Avatar?.GetAddress() ?? 0;
            if (address == 0) continue;
            Alive.Add(address);

            InCone.TryGetValue(address, out bool inCone);

            if (o.Dist > SCAN_RANGE_M) { InCone[address] = false; continue; }

            var b = FieldDirectionService.GetBearing(forward, o.Dx, o.Dz);
            if (!b.Ok) continue;
            float offDeg = (float)System.Math.Abs(System.Math.Atan2(b.Right, b.Ahead) * 180.0 / System.Math.PI);

            if (inCone)
            {
                if (offDeg > LEAVE_DEG) InCone[address] = false;
                continue;
            }
            if (offDeg > ENTER_DEG) continue;
            InCone[address] = true;
            if (!turning) continue;

            bool isTarget = address == targetAddress;
            if (isTarget)
            {
                CameraHoldService.Hold();
                AudioService.PlayTone(ALIGNED_TONE);
            }

            if (toSpeak == null)
            {
                // Everyone with a name, crowd included: a passer-by can be
                // fought or talked to for an item, and a look is the player
                // asking (user rule 2026-09-07). The turn gate is what keeps
                // this from being a census; the nameless are left to the tone.
                string name = AvatarNameCache.NameOf(o);
                if (name == null) continue;
                toSpeak = o;
                toSpeakName = name;
                speakIsTarget = isTarget;
            }
        }

        Prune();

        if (toSpeak == null) return;
        var person = toSpeak.Value;
        if (speakIsTarget)
        {
            ScreenReaderService.Speak(LocalizedText.AimAhead(toSpeakName), interrupt: true);
            return;
        }
        int meters = (int)System.Math.Round(person.Dist);
        ScreenReaderService.Speak(LocalizedText.AtMeters(toSpeakName, meters), interrupt: false);
    }

    /// <summary>Forget people who are no longer in the field's avatar list.</summary>
    private static void Prune()
    {
        Gone.Clear();
        foreach (var address in InCone.Keys)
            if (!Alive.Contains(address)) Gone.Add(address);
        foreach (var address in Gone) InCone.Remove(address);
    }

    private static void Reset()
    {
        Target.Reset();
        InCone.Clear();
        _tick = 0;
        _lastTarget = 0;
        _turn.Reset();
    }
}
