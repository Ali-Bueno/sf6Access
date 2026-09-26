using System;
using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

public partial class FieldNavRadarHooks
{
    /// <summary>The full situation, in answer to a press. Spoken with an interrupt:
    /// a request must always be answered, ahead of whatever was being said.</summary>
    private static void ReadOutOnce()
    {
        var r = FieldNavRadarService.Sample();
        if (!r.Ok)
        {
            API.LogInfo("[SF6Access] Nav radar: no reading (avatar field state or ray API unreachable)");
            return;
        }
        ScreenReaderService.Speak(Describe(r), interrupt: true);
        // The continuous mode's baseline is now stale relative to what the player
        // has just been told; re-seed it so the next change is measured from here.
        if (_continuous) Seed(r);
    }

    private static string Describe(NavReading r)
    {
        // Measure the sides with the radar's own body-clearance sensor so the spoken
        // readout and the cues quote ONE measurement. The game-state verdict stays as
        // the fallback: a side reported open because the sensor failed is the one
        // answer a navigation readout may never give.
        var cam = FieldDirectionService.GetCameraForward();
        bool sensed = cam.Ok && FieldRadarSense.Measure(cam);

        var parts = new List<string>(4 + MAX_WAYS_SPOKEN)
        {
            FrontPhrase(r),
            SidePhrase(RadarBeam.Left, sensed, r.LeftBlocked),
            SidePhrase(RadarBeam.Right, sensed, r.RightBlocked),
            r.GroundSolid ? LocalizedText.NavFloorSolid() : LocalizedText.NavFloorDrop(),
        };
        AppendWaysOut(parts);
        return string.Join(", ", parts);
    }

    /// <summary>One side, with the distance the avatar's BODY could travel that way.
    ///
    /// <para>"left open" on its own was true of anything past two metres, so the
    /// readout could confirm a cue about a shopfront recess and then, three steps
    /// later, report the wall behind it. The metre figure is the whole difference
    /// between "you can go left" and "there is two metres of left".</para></summary>
    private static string SidePhrase(RadarBeam beam, bool sensed, bool blockedFallback)
    {
        if (sensed)
        {
            var reading = FieldRadarSense.Beam(beam);
            if (reading.Ok)
            {
                float clear = reading.Mid;
                bool blocked = clear <= FieldRadarClearance.BlockedAtM(FieldRayCaster.CapsuleRadius());
                if (beam == RadarBeam.Left)
                    return blocked ? LocalizedText.NavObstacleAt(LocalizedText.NavLeftBlocked(), clear)
                                   : LocalizedText.NavLeftClearFor(clear);
                return blocked ? LocalizedText.NavObstacleAt(LocalizedText.NavRightBlocked(), clear)
                               : LocalizedText.NavRightClearFor(clear);
            }
        }

        if (beam == RadarBeam.Left)
            return blockedFallback ? LocalizedText.NavLeftBlocked() : LocalizedText.NavLeftOpen();
        return blockedFallback ? LocalizedText.NavRightBlocked() : LocalizedText.NavRightOpen();
    }

    /// <summary>How many ways out one press may list. A readout has to stay a
    /// sentence the player can hold in their head, and the list arrives nearest
    /// first, so the ones past this are the ones a second press from a few steps
    /// along would answer better anyway.</summary>
    private const int MAX_WAYS_SPOKEN = 3;

    /// <summary>Name the ways out of where the avatar is standing, from the GAME'S
    /// OWN navmesh (<see cref="NavMeshOpenings"/>) rather than from geometry rays.
    ///
    /// <para>This is the part the ray-based radar could never do. A ring of rays
    /// cannot answer "where is the exit": a 1.6 m doorway six metres away subtends
    /// about five degrees, so at any ray spacing coarse enough to afford, no ray
    /// goes through it — and a wall seen at a grazing angle produces exactly the
    /// same big jump between neighbouring rays that a real gap does. (Both were
    /// reproduced in a standalone simulation before this was written, which is why
    /// no ray-based gap finder ships here.) A navmesh has no such limit: a walkable
    /// polygon's links ARE the ways out, and the shared edge between two polygons
    /// has a real width, so "1.8 metres wide" is a measurement and "you fit"
    /// compares it against the avatar's own capsule.</para>
    ///
    /// <para>Passable ways are listed first and alone. Only when none of them fit
    /// is a too-narrow gap spoken, because "there is a gap there and you cannot use
    /// it" is worth knowing precisely when there is nothing better to report.</para></summary>
    /// <summary>The navmesh route is DISABLED after it crashed the game on its first
    /// in-game press (2026-09-08): the log stops dead at the radar sample that runs
    /// immediately before it, and no <c>NavMesh openings</c> line was ever written, so
    /// it died inside the first query. This mod has form here — constructing a generic
    /// interface through the TDB once produced a bogus managed wrapper whose finalizer
    /// raised an AccessViolationException on the GC thread, with an equally clean log —
    /// so the route stays off, rather than being left in behind a try/catch that a
    /// native fault walks straight through.
    ///
    /// <para>The reader itself was rewritten afterwards (<see cref="NavMeshNodes"/> +
    /// <see cref="NavMeshOpenings"/>): every engine call now writes a numbered
    /// <c>NavMesh step</c> line BEFORE it runs, so the next attempt cannot be silent —
    /// the last step in the log will name the call that does not return. Turning this
    /// back on is the USER'S decision, not a code decision; it stays <c>false</c> until
    /// they ask for the run.</para></summary>
    /// Kept as a field rather than a const so the disabled body still compiles as
    /// live code: a kill switch that rots the code behind it is one nobody can
    /// re-enable safely.
    private static readonly bool MESH_WAYS_ENABLED = false;

    private static void AppendWaysOut(List<string> parts)
    {
        if (!MESH_WAYS_ENABLED) return;
        var ways = NavMeshOpenings.FromPlayer();
        if (ways.Count == 0) return;

        var forward = FieldDirectionService.GetCameraForward();
        var me = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());
        if (!forward.Ok || !me.ok) return;

        int spoken = 0;
        foreach (var w in ways)
        {
            if (spoken >= MAX_WAYS_SPOKEN) break;
            if (!w.Passable) continue;
            int hour = FieldDirectionService.ClockHour(forward, w.X - me.x, w.Z - me.z);
            if (hour == 0) continue;
            parts.Add(LocalizedText.NavOpening(hour, w.WidthM, w.DistanceM));
            spoken++;
        }
        if (spoken > 0) return;

        foreach (var w in ways)
        {
            if (spoken >= MAX_WAYS_SPOKEN) break;
            int hour = FieldDirectionService.ClockHour(forward, w.X - me.x, w.Z - me.z);
            if (hour == 0) continue;
            parts.Add(LocalizedText.NavGapTooNarrow(hour, w.WidthM));
            spoken++;
        }
        if (spoken == 0) parts.Add(LocalizedText.NavNoOpenings());
    }

    /// <summary>The obstacle class with its distance. When nothing is blocking and
    /// the height stack is open the distance still matters — it is how far the long
    /// forward ray reached before finding something — but it is a different sentence,
    /// because "clear ahead at 1.9 meters" would read as an obstruction.</summary>
    private static string FrontPhrase(NavReading r)
    {
        if (r.Block != FrontBlock.None) return WithDistance(BlockPhrase(r), r);
        if (r.Front == FrontProfile.Open)
            return r.HasDistance ? LocalizedText.NavClearFor(r.Distance) : LocalizedText.NavFront(r.Front);
        return WithDistance(LocalizedText.NavFront(r.Front), r);
    }

    private static string WithDistance(string what, NavReading r)
        => r.HasDistance ? LocalizedText.NavObstacleAt(what, r.Distance) : what;

    /// <summary>The word for the game's own "you are stopped" verdict. The rays are
    /// used only to NAME what is stopping the avatar, never to decide that it is:
    /// when they saw nothing — a fence or a prop the ray filter does not report, but
    /// the capsule collides with — the plain "blocked" is the honest answer, and
    /// saying "clear ahead" there is precisely the false positive reported in
    /// play.</summary>
    private static string BlockPhrase(NavReading r) => r.Block switch
    {
        FrontBlock.WallRide => LocalizedText.NavWallRide(),
        _ when r.Front != FrontProfile.Open => LocalizedText.NavFront(r.Front),
        _ => LocalizedText.NavBlocked(),
    };

}
