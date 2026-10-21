using System;
using System.Globalization;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// <b>The World Tour radar</b> — three beams around the camera, each cast at three
/// body heights, each answering one question: <b>can the player go that way?</b>
///
/// <para>A beam is PASSABLE when the avatar's body can advance far enough along it to
/// be going somewhere (<c>exit.mp3</c>) and BLOCKED when it cannot (<c>impassable.mp3</c>),
/// panned to its side. Nothing sounds while a verdict holds — only a beam that CHANGES
/// verdict speaks. A wall the player is walking into gets a rising tone instead, on
/// every change of the rounded metre; stairs get a rising three-note motif.</para>
///
/// <para><b>Why it is no longer A Hero's Call's line memory.</b> This started as a
/// faithful port of AHC's <c>ReactiveRadar</c> by way of the RE7 mod
/// (<c>D:\code\re engine\Re7Access\src\RadarService.cs</c>), which remembers the LINE
/// each beam looks at and speaks when that line breaks. Measured in Metro City it fired
/// 109 cues in 90 seconds at 109 DIFFERENT places — not malfunctioning, but correctly
/// reporting that a city facade changes slope about once a second. AHC's world is a
/// tile dungeon where a slope change IS a corner or a door; a street is not. Worse, a
/// line break means "this beam now sees past where the boundary was", which a 40 cm
/// shopfront recess satisfies: the player was told "exit right", turned right, and the
/// spoken readout said blocked. See <see cref="FieldRadarClearance"/> for the full
/// account. What survives from AHC is everything that was never the problem — three
/// beams, the pan, the approach tone, the turn behaviour, the cadence.</para>
///
/// <para><b>Turning is still the scan.</b> There is no movement or turn gate: every
/// beam keeps measuring and cueing while the camera turns, sensed faster during the
/// turn. Laterals were once muted mid-turn; the tester asked for them back (2026-09-26).</para>
///
/// <para><b>Division of labour.</b> This layer owns the CUES. The spoken class
/// ("wall", "blocked", "wall you can run along") remains
/// <see cref="FieldNavVerdictService"/>'s through <see cref="NavReading.Block"/> —
/// the game's own collision verdict, which already works in play.</para>
///
/// <para>Layers: <see cref="FieldRadarSense"/> (casting), <see cref="FieldRadarClearance"/>
/// (deciding), <see cref="FieldRadarCues"/> (sounding), <see cref="FieldRadarTravel"/>
/// (where the avatar is going), <see cref="FieldRadarTuning"/> (every constant with
/// its source line).</para>
/// </summary>
public static class FieldRadarService
{
    /// <summary>AHC's own evaluation order — Left, Front, Right
    /// (<c>ReactiveRadar.cs:108-110</c>) — and the first beam to sound wins the
    /// evaluation, which is what stops a corner firing three cues at once.</summary>
    private static readonly RadarBeam[] EvalOrder = { RadarBeam.Left, RadarBeam.Front, RadarBeam.Right };

    private static readonly long[] StairCooldownAt = new long[FieldRadarSense.BEAM_COUNT];

    private static long _nextSenseAt, _nextCueAt, _lastEvalAt;
    private static float _bodyRadius;
    private static bool _armedLogged, _failLogged;

    /// <summary>Run one evaluation if the clock says it is due. Safe to call every
    /// frame; it owns its own cadence. Never throws.</summary>
    public static void Update()
    {
        long now = Environment.TickCount64;
        if (now < _nextSenseAt) return;
        // Last evaluation's turn state sets this one's rate: one evaluation of lag
        // entering or leaving a turn is nothing at these intervals.
        _nextSenseAt = now + (FieldRadarTravel.Turning
            ? FieldRadarTuning.TurnSenseMs
            : FieldRadarTuning.DirectSenseMs);

        float dtSec = Math.Clamp((now - _lastEvalAt) / 1000f, 0.001f, 0.5f);
        _lastEvalAt = now;

        try { Evaluate(now, dtSec); }
        catch (Exception ex)
        {
            Reset();
            _nextSenseAt = now + FieldRadarTuning.FallbackSenseMs;
            if (_failLogged) return;
            _failLogged = true;
            API.LogWarning($"[SF6Access] Field radar evaluation failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Evaluate(long now, float dtSec)
    {
        var cam = FieldDirectionService.GetCameraForward();
        var me = AvatarFieldReader.ReadPlayerPos(null);
        if (!cam.Ok || !me.ok)
        {
            Reset();
            _nextSenseAt = now + FieldRadarTuning.FallbackSenseMs;
            return;
        }

        FieldRadarTravel.Update(me.x, me.y, me.z, cam, dtSec);
        // A teleport is not a walk: every line would span a loading boundary.
        if (FieldRadarTravel.Teleported) { Reset(); return; }

        if (!FieldRadarSense.Measure(cam))
        {
            // Reseeding must also discard unheard cues; otherwise an old opening can
            // survive the failure and sound over a new blocked baseline.
            Reset();
            _nextSenseAt = now + FieldRadarTuning.FallbackSenseMs;
            return;
        }

        if (!(_bodyRadius > 0f)) _bodyRadius = FieldRayCaster.CapsuleRadius();
        LogArmedOnce();

        // The post-cue gate holds EMISSION only. Sensing has already run, and a verdict
        // that settles while the gate is shut is HELD rather than dropped: a state can
        // only settle once per change, so swallowing it would lose it for good.
        bool canCue = now >= _nextCueAt;

        if (ClearancePass(now, canCue)) return;
        ProximityPass(now, canCue);
    }

    /// <summary>What each beam has settled into but not yet said, and what the player
    /// was last told. Pending verdicts also need supporting evidence at emission.
    ///
    /// <para>At most one cue leaves an evaluation, so entering a plaza — where both
    /// laterals turn passable at once — speaks them in order instead of over each
    /// other. And a verdict formed while a beam was not allowed to speak WAITS here
    /// instead of being thrown away: walking past an alley and turning back swept the
    /// alley into a lateral beam during the turn, when laterals are silent, so the
    /// player arrived at its mouth having been told nothing.</para>
    ///
    /// <para>The heard verdict stops that becoming chatter. A beam that
    /// changed and changed back while silent ends on the verdict the player already
    /// holds, and says nothing.</para></summary>
    private static readonly RadarCueMemory[] CueMemory = new RadarCueMemory[FieldRadarSense.BEAM_COUNT];

    /// <summary>Pass one: passability. Every beam's verdict is kept current every
    /// evaluation, whether or not it may speak; only a beam that CHANGES verdict has
    /// anything to say. Returns true when a cue fired.</summary>
    private static bool ClearancePass(long now, bool canCue)
    {
        float radius = BodyHalfWidth();

        foreach (var beam in EvalOrder)
        {
            var r = FieldRadarSense.Beam(beam);
            if (!r.Ok) continue;

            var settled = FieldRadarClearance.Step(beam, r.Mid, radius, now);

            // The approach tone owns a nearby wall ahead. Suppress the duplicate wall
            // sound, but count it as heard only after that tone actually plays.
            if (settled == PassState.Blocked && Role(beam) == 0
                && r.Mid < FieldRadarTuning.PitchReachM)
            {
                CueMemory[(int)beam].SuppressPending();
                settled = PassState.Unknown;
            }

            CueMemory[(int)beam].Offer(settled);
        }

        if (!canCue) return false;

        foreach (var beam in EvalOrder)
        {
            int b = (int)beam;
            // Every beam may speak while the camera turns (tester request 2026-09-26).
            var pending = CueMemory[b].Candidate();
            if (pending == PassState.Unknown) continue;

            var reading = FieldRadarSense.Beam(beam);
            // Debouncing may still hold Passable while a new obstruction is being confirmed.
            // Retain the pending cue for recovery, but let other supported beams use this slot.
            if (!RadarCueAdmission.Supports(pending, reading.Ok, reading.Mid,
                FieldRadarTuning.PassableEnterRadii * radius, FieldRadarClearance.BlockedAtM(radius))) continue;
            if (!FieldRadarCues.Say(beam, pending, reading.Mid, now)) return false;
            CueMemory[b].Acknowledge(pending);
            _nextCueAt = now + FieldRadarTuning.CueCooldownMs;
            return true;
        }
        return false;
    }

    /// <summary>Pass two: the approach tone and the stairs motif, reached only when no
    /// open/close cue fired.
    ///
    /// <para><b>Exactly one beam may tone.</b> AHC admits the travel sector and both
    /// its diagonal neighbours (<c>ReactiveRadar.cs:149-151</c>) and then relies on two
    /// sensing guards to stop more than one claiming the same geometry: face-on-ness
    /// (<see cref="FieldRadarTuning.FaceOnCos"/>) and a cross-ray oblique veto. The
    /// face-on gate is ported below; the cross-ray veto is not, because it costs two
    /// extra casts per tone. So the neighbours stay ELIGIBLE — dropping them would
    /// leave a diagonal walk with no approach warning at all, since with beams at 90°
    /// no beam sits in a diagonal sector — but only the best of them SOUNDS: most
    /// aligned with travel first, nearest as the tie-break. Reported in play as several
    /// tones at once while walking one way, which is what two eligible beams alternating
    /// at 30 Hz sounds like.</para></summary>
    private static void ProximityPass(long now, bool canCue)
    {
        var best = (RadarBeam)(-1);
        int bestRank = int.MaxValue;
        float bestDist = 0f;

        foreach (var beam in EvalOrder)
        {
            var r = FieldRadarSense.Beam(beam);
            int diff = Role(beam);
            // Travelling roughly this way: the beam's own sector or either diagonal
            // beside it. Anything else re-arms, so walking back at a wall tones again.
            bool eligible = r.Ok && r.HasWaist && (diff == 0 || diff == 1 || diff == 7)
                            && r.WaistFace >= FieldRadarTuning.FaceOnCos
                            && !FieldRadarSense.StairsAhead(in r);
            if (!eligible) { FieldRadarCues.RearmPitch(beam); continue; }

            // Dead ahead beats a diagonal; between two diagonals, the nearer wall.
            int rank = diff == 0 ? 0 : 1;
            if (rank < bestRank || (rank == bestRank && (bestDist <= 0f || r.WaistDist < bestDist)))
            {
                if ((int)best >= 0) FieldRadarCues.RearmPitch(best);
                best = beam; bestRank = rank; bestDist = r.WaistDist;
            }
            else FieldRadarCues.RearmPitch(beam);
        }

        if ((int)best >= 0 && canCue && FieldRadarCues.Pitch(best, bestDist))
        {
            var reading = FieldRadarSense.Beam(best);
            float radius = BodyHalfWidth();
            CueMemory[(int)best].AcknowledgeWallTone(true,
                RadarCueAdmission.Supports(PassState.Blocked, reading.Ok, reading.Mid,
                    FieldRadarTuning.PassableEnterRadii * radius, FieldRadarClearance.BlockedAtM(radius)),
                FieldRadarClearance.Current(best));
            _nextCueAt = now + FieldRadarTuning.CueCooldownMs;
            return;
        }

        // Stairs are a separate channel: any beam, its own per-beam cooldown.
        foreach (var beam in EvalOrder)
        {
            var r = FieldRadarSense.Beam(beam);
            if (!r.Ok || !FieldRadarSense.StairsAhead(in r)) continue;
            if (!canCue || now < StairCooldownAt[(int)beam]) continue;
            if (!FieldRadarCues.Stairs(beam)) return;
            StairCooldownAt[(int)beam] = now + FieldRadarTuning.StairMotifCooldownMs;
            _nextCueAt = now + FieldRadarTuning.CueCooldownMs;
            return;
        }
    }

    /// <summary>How far this beam's sector sits from the travel sector, 0-7, or -1
    /// when the avatar is not travelling — in which case NO beam is excluded and all
    /// three evaluate. 0 is the travel beam, 4 its inverse.</summary>
    private static int Role(RadarBeam beam)
    {
        int travel = FieldRadarTravel.TravelSector;
        if (travel < 0) return -1;
        return ((FieldRadarSense.SectorOf(beam) - travel) % 8 + 8) % 8;
    }

    /// <summary>The avatar's own collision radius: the continuous stand-in for AHC's
    /// tile resolution. The documented fallback covers the evaluations before the
    /// controller is readable, and is the capsule radius this game reported in play
    /// (docs, § World Tour — spatial navigation APIs §4).</summary>
    private const float FallbackBodyRadiusM = 0.5f;

    private static float BodyHalfWidth() => _bodyRadius > 0f ? _bodyRadius : FallbackBodyRadiusM;

    /// <summary>Forget everything: the mode was switched off, the field gate closed, or
    /// the avatar teleported.</summary>
    public static void Reset()
    {
        FieldRadarClearance.WipeAll();
        FieldRadarCues.Reset();
        FieldRadarTravel.Reset();
        for (int b = 0; b < FieldRadarSense.BEAM_COUNT; b++)
        {
            StairCooldownAt[b] = 0;
            CueMemory[b] = default;
        }
        _nextCueAt = 0;
        _bodyRadius = 0f;
    }

    /// <summary>One line, once: what the radar actually bound. Read it first when a
    /// test in game does not sound the way it should.</summary>
    private static void LogArmedOnce()
    {
        if (_armedLogged) return;
        _armedLogged = true;
        float radius = BodyHalfWidth();
        API.LogInfo(string.Format(CultureInfo.InvariantCulture,
            "[SF6Access] Field radar armed: body-clearance passability; speed={0}; body radius={1:F3} m; " +
            "passable at >={2:F2} m ({3:F0} radii), blocked at <={4:F2} m ({5:F0} radii), " +
            "dead band {6:F2} m; passable fires immediately, blocked confirmed after {7} ms; reach={8:F0} m; body sweep at the " +
            "capsule radius; sense {9}/{10} ms (walk/turn); {11}",
            FieldRadarTravel.Describe(), radius,
            FieldRadarTuning.PassableEnterRadii * radius, FieldRadarTuning.PassableEnterRadii,
            FieldRadarTuning.BlockedEnterRadii * radius, FieldRadarTuning.BlockedEnterRadii,
            (FieldRadarTuning.PassableEnterRadii - FieldRadarTuning.BlockedEnterRadii) * radius,
            FieldRadarTuning.BlockedConfirmMs, FieldRadarTuning.ReachM,
            FieldRadarTuning.DirectSenseMs, FieldRadarTuning.TurnSenseMs,
            FieldRayCaster.RouteDescription));
    }
}
