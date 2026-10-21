using SF6Access.Services.WorldTour;

internal static class RadarCueChecks
{
    private const float RadiusM = .38f; // Capsule recorded in the user's 2026-10-01 session.
    private static float OpenM => FieldRadarTuning.PassableEnterRadii * RadiusM;
    private static float BlockedM => FieldRadarClearance.BlockedAtM(RadiusM);

    internal static void Run(Action<bool, string> check)
    {
        FieldNavSideRays.PublishedReachM = 0;
        DelayedOpening(check, .24f);
        DelayedOpening(check, .91f);
        DelayedWall(check);
        InvalidAndUncertainEvidence(check);
        LongDelayAndFairLane(check);
        FrontWallBookkeeping(check);
        EarlyWallToneRecovery(check);
        FieldRadarClearance.WipeAll();
        FieldNavSideRays.PublishedReachM = 0;
    }

    private static bool Supports(PassState pending, float clearM, bool measured = true) =>
        RadarCueAdmission.Supports(pending, measured, clearM, OpenM, BlockedM);

    private static void DelayedOpening(Action<bool, string> check, float obstructionM)
    {
        FieldRadarClearance.WipeAll();
        var lane = new FieldCuePolicy();
        var beam = RadarBeam.Right;
        FieldRadarClearance.Step(beam, .5f, RadiusM, 0);
        lane.Reserve(0, FieldCueLane.Geometry, "radar", TimeSpan.FromMilliseconds(280));
        var pending = FieldRadarClearance.Step(beam, 9, RadiusM, 250);
        check(pending == PassState.Passable && !lane.Available(250, FieldCueLane.Geometry, "radar"),
            $"opening queues while the previous radar sound owns its lane ({obstructionM})");

        var changed = FieldRadarClearance.Step(beam, obstructionM, RadiusM, 280);
        check(changed == PassState.Unknown && FieldRadarClearance.Current(beam) == PassState.Passable,
            $"a fresh obstruction leaves the settled state passable during actual debounce ({obstructionM})");
        check(lane.Available(280, FieldCueLane.Geometry, "radar") && !Supports(pending, obstructionM),
            $"the old opening cannot play over the currently measured obstacle ({obstructionM})");

        // The obstruction clears before blocked confirmation: no second Passable transition is produced.
        changed = FieldRadarClearance.Step(beam, 9, RadiusM, 310);
        check(changed == PassState.Unknown && Supports(pending, 9),
            $"retaining the unsupported pending opening allows recovery without another state transition ({obstructionM})");
        lane.Reserve(310, FieldCueLane.Geometry, "radar", TimeSpan.FromMilliseconds(280));
        check(!lane.Available(311, FieldCueLane.Geometry, "radar"), "a delivered recovered opening still respects normal audio admission");
    }

    private static void DelayedWall(Action<bool, string> check)
    {
        FieldRadarClearance.WipeAll();
        var beam = RadarBeam.Left;
        FieldRadarClearance.Step(beam, 9, RadiusM, 0);
        check(FieldRadarClearance.Step(beam, .5f, RadiusM, 100) == PassState.Unknown,
            "a new wall retains the existing confirmation interval");
        var pending = FieldRadarClearance.Step(beam, .5f, RadiusM, 100 + FieldRadarTuning.BlockedConfirmMs);
        check(pending == PassState.Blocked && Supports(pending, .5f), "a confirmed wall can play while its measurement still supports it");
        check(!Supports(pending, 9), "a pending wall is inadmissible after the beam sees an opening");
        var latest = FieldRadarClearance.Step(beam, 9, RadiusM, 250);
        check(latest == PassState.Passable && Supports(latest, 9), "a newer settled opening replaces the old queued wall");
    }

    private static void InvalidAndUncertainEvidence(Action<bool, string> check)
    {
        check(!Supports(PassState.Unknown, 30), "an unknown pending verdict cannot become an opening by distance alone");
        check(!Supports(PassState.Passable, 30, false) && !Supports(PassState.Blocked, .2f, false),
            "unreadable measurements admit neither pending verdict");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
            check(!Supports(PassState.Passable, invalid) && !Supports(PassState.Blocked, invalid),
                $"invalid clearance cannot support either cue ({invalid})");
        float middle = (OpenM + BlockedM) / 2;
        check(!Supports(PassState.Passable, middle) && !Supports(PassState.Blocked, middle),
            "the dead band supports neither deferred claim even though a settled state may persist");
        check(Supports(PassState.Passable, OpenM) && Supports(PassState.Blocked, BlockedM),
            "admission uses the same inclusive boundaries as clearance classification");
        check(!RadarCueAdmission.Supports(PassState.Passable, true, 30, float.NaN, BlockedM)
            && !RadarCueAdmission.Supports(PassState.Blocked, true, .2f, OpenM, float.PositiveInfinity),
            "unreadable thresholds cannot manufacture support");
        FieldNavSideRays.PublishedReachM = 2;
        check(Supports(PassState.Blocked, 1.8f), "wall admission uses the current game-published reach when available");
        FieldNavSideRays.PublishedReachM = 0;
    }

    private static void LongDelayAndFairLane(Action<bool, string> check)
    {
        FieldRadarClearance.WipeAll();
        FieldRadarClearance.Step(RadarBeam.Left, .5f, RadiusM, 0);
        var pendingLeft = FieldRadarClearance.Step(RadarBeam.Left, 9, RadiusM, 100);
        var lane = new FieldCuePolicy();
        lane.Reserve(0, FieldCueLane.Geometry, "radar", TimeSpan.FromSeconds(10));
        for (long now = 500; now < 10000; now += 500)
            FieldRadarClearance.Step(RadarBeam.Left, 9, RadiusM, now);
        FieldRadarClearance.Step(RadarBeam.Left, .24f, RadiusM, 10000);
        check(lane.Available(10000, FieldCueLane.Geometry, "radar") && !Supports(pendingLeft, .24f),
            "even after a prolonged audio delay the current measurement vetoes an outdated opening");

        // Drive the production admission predicate and lane in the same Left/Front/Right order as the radar.
        var pending = new[] { pendingLeft, PassState.Blocked, PassState.Passable };
        var measured = new[] { .24f, .7f, 9f };
        int winner = -1;
        for (int i = 0; i < pending.Length; i++)
        {
            if (!Supports(pending[i], measured[i])) continue;
            if (!lane.Available(10000, FieldCueLane.Geometry, "radar")) break;
            winner = i;
            lane.Reserve(10000, FieldCueLane.Geometry, "radar", TimeSpan.FromMilliseconds(280));
            break;
        }
        check(winner == (int)RadarBeam.Front, "an unsupported pending left beam does not starve a supported front wall");
        check(!lane.Available(10001, FieldCueLane.Geometry, "radar"), "only the supported winner consumes the geometry lane");
        check(lane.Available(10280, FieldCueLane.Geometry, "radar") && Supports(pending[2], measured[2]),
            "the next supported beam remains eligible when the geometry lane becomes free");
    }

    private static void FrontWallBookkeeping(Action<bool, string> check)
    {
        FieldRadarClearance.WipeAll();
        var beam = RadarBeam.Front;
        var memory = new RadarCueMemory();
        FieldRadarClearance.Step(beam, .5f, RadiusM, 0);
        memory.Offer(FieldRadarClearance.Step(beam, 9, RadiusM, 10));
        check(memory.Candidate() == PassState.Passable, "the front opening is initially available to play");
        memory.Acknowledge(PassState.Passable);

        FieldRadarClearance.Step(beam, .5f, RadiusM, 20);
        var wall = FieldRadarClearance.Step(beam, .5f, RadiusM, 20 + FieldRadarTuning.BlockedConfirmMs);
        check(wall == PassState.Blocked, "the actual clearance logic settles the wall ahead");
        memory.SuppressPending(); // Nearby front walls belong to the proximity tone.
        memory.AcknowledgeWallTone(false, Supports(PassState.Blocked, .5f), FieldRadarClearance.Current(beam));
        check(memory.Heard == PassState.Passable && memory.Pending == PassState.Unknown,
            "an audio-busy front wall suppresses its duplicate sample without claiming the tone was heard");
        memory.Offer(FieldRadarClearance.Step(beam, 9, RadiusM, 200));
        check(memory.Candidate() == PassState.Unknown,
            "returning to open space after an unheard front wall cannot manufacture a reopening cue");

        FieldRadarClearance.Step(beam, .5f, RadiusM, 300);
        FieldRadarClearance.Step(beam, .5f, RadiusM, 300 + FieldRadarTuning.BlockedConfirmMs);
        memory.SuppressPending();
        memory.AcknowledgeWallTone(true, Supports(PassState.Blocked, .5f), FieldRadarClearance.Current(beam));
        check(memory.Heard == PassState.Blocked, "an actually played supported wall tone commits the heard wall");
        memory.Offer(FieldRadarClearance.Step(beam, 9, RadiusM, 500));
        check(memory.Candidate() == PassState.Passable, "the opening after a heard wall tone can be announced");
        memory.Acknowledge(PassState.Passable);
        memory.Offer(PassState.Blocked);
        memory.AcknowledgeWallTone(false, true, FieldRadarClearance.Current(beam));
        check(memory.Pending == PassState.Blocked && memory.Heard == PassState.Passable,
            "failed audio cannot consume a pending verdict or alter what was heard");
        memory.SuppressPending();
        memory.AcknowledgeWallTone(true, Supports(PassState.Blocked, (OpenM + BlockedM) / 2), FieldRadarClearance.Current(beam));
        check(memory.Heard == PassState.Passable, "a farther approach tone does not claim the beam is currently blocked");
        memory.AcknowledgeWallTone(true, Supports(PassState.Blocked, .5f, false), FieldRadarClearance.Current(beam));
        check(memory.Heard == PassState.Passable, "a tone cannot acknowledge an unreadable blocked measurement");
        memory.Offer(PassState.Passable);
        FieldRadarClearance.Step(beam, .5f, RadiusM, 600);
        FieldRadarClearance.Step(beam, .5f, RadiusM, 600 + FieldRadarTuning.BlockedConfirmMs);
        memory.AcknowledgeWallTone(true, true, FieldRadarClearance.Current(beam));
        check(memory.Heard == PassState.Blocked && memory.Pending == PassState.Unknown,
            "an actual wall tone also discards an obsolete queued opening");
        memory.Offer(PassState.Passable);
        memory.Offer(PassState.Unknown);
        check(memory.Pending == PassState.Passable, "a debounce tick without a settled change retains the pending cue");
        memory = default;
        check(memory.Heard == PassState.Unknown && memory.Pending == PassState.Unknown,
            "a radar reset discards both pending and heard history");
    }

    private static void EarlyWallToneRecovery(Action<bool, string> check)
    {
        FieldRadarClearance.WipeAll();
        var beam = RadarBeam.Front;
        var memory = new RadarCueMemory();
        memory.Offer(FieldRadarClearance.Step(beam, 9, RadiusM, 0));
        check(memory.Candidate() == PassState.Unknown, "initially open radar beams still seed silently");
        var wall = FieldRadarClearance.Step(beam, .24f, RadiusM, 33);
        check(wall == PassState.Unknown && FieldRadarClearance.Current(beam) == PassState.Passable,
            "the first close-wall sample does not bypass blocked confirmation");
        memory.AcknowledgeWallTone(true, Supports(PassState.Blocked, .24f), FieldRadarClearance.Current(beam));
        check(memory.Heard == PassState.Blocked && memory.Pending == PassState.Passable
            && !Supports(memory.Candidate(), .24f), "an early heard wall retains a possible opening but cannot emit it through the wall");
        var recovery = FieldRadarClearance.Step(beam, 9, RadiusM, 66);
        memory.Offer(recovery);
        check(recovery == PassState.Unknown && memory.Candidate() == PassState.Passable
            && Supports(memory.Candidate(), 9), "early recovery after a heard wall announces opening even without a new settled transition");
        memory.Acknowledge(PassState.Passable);
        memory.Offer(FieldRadarClearance.Step(beam, 9, RadiusM, 99));
        check(memory.Candidate() == PassState.Unknown, "the early-recovery opening is heard only once");

        FieldRadarClearance.WipeAll();
        memory = default;
        FieldRadarClearance.Step(beam, 9, RadiusM, 0);
        FieldRadarClearance.Step(beam, .24f, RadiusM, 33);
        memory.AcknowledgeWallTone(true, Supports(PassState.Blocked, .24f), FieldRadarClearance.Current(beam));
        bool silentWhileConfirming = true;
        foreach (long now in new long[] { 66, 99, 132 })
        {
            memory.Offer(FieldRadarClearance.Step(beam, .24f, RadiusM, now));
            silentWhileConfirming &= !Supports(memory.Candidate(), .24f);
        }
        check(silentWhileConfirming, "a persistent obstruction never emits the retained recovery before confirmation");
        wall = FieldRadarClearance.Step(beam, .24f, RadiusM, 33 + FieldRadarTuning.BlockedConfirmMs);
        if (wall == PassState.Blocked) memory.SuppressPending();
        check(wall == PassState.Blocked && memory.Heard == PassState.Blocked && memory.Pending == PassState.Unknown,
            "confirmed front blockage discards the speculative recovery while preserving the heard wall");
        memory.Offer(FieldRadarClearance.Step(beam, 9, RadiusM, 198));
        check(memory.Candidate() == PassState.Passable && Supports(memory.Candidate(), 9),
            "a later genuine opening after persistent blockage is still announced");
    }
}

// Test-only inputs at the native boundary. Actual clearance, tuning, admission and audio-lane policy are linked above.
namespace SF6Access.Services.WorldTour
{
    public enum RadarBeam { Left, Front, Right }
    internal static class FieldRadarSense { public const int BEAM_COUNT = 3; }
    internal static class FieldNavSideRays { public static float PublishedReachM { get; set; } }
}
