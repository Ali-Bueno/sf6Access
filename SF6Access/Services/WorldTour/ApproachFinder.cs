using System;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Hooks.WorldTour;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// <b>When the objective cannot be walked to, find the nearest place that CAN.</b>
///
/// <para><b>The problem this exists for.</b> A World Tour objective can sit on a
/// disconnected island of navmesh — an upper floor or roof reached through a door
/// or lift the navigation graph does not model. Confirmed in game 2026-09-09, 69
/// times: <c>PathNotFound (destNode=10421)</c>, a perfectly real destination node
/// with no walkable link to the player's. The engine is right and there is nothing
/// to fix in the query. But the STREET UNDER that objective is walkable, and
/// standing there turns "there is no way to get there" into "you are at the foot of
/// it" — which is a problem a player can actually solve.</para>
///
/// <para><b>How it searches.</b> Candidates in order of increasing distance from
/// the objective, so the FIRST one that routes is also the closest:</para>
/// <list type="number">
/// <item>the objective projected straight down to the player's own height — the
///   ground directly beneath it, which is the answer nearly every time;</item>
/// <item>a ring of eight points around it at <see cref="RING_A_M"/>;</item>
/// <item>a wider ring at <see cref="RING_B_M"/>, for a building whose footprint
///   swallows the first ring.</item>
/// </list>
/// <para>Eight is <see cref="FieldHeadingService.SECTORS"/> — the compass this mod
/// already speaks in — and the radii are multiples of
/// <see cref="MissionBeaconHooks.ARRIVED_M"/>, the distance it already calls
/// "arrived". Nothing here is a tuned number.</para>
///
/// <para><b>ONE probe per call, and never two queries in a frame.</b> This is the
/// hard-won rule. An earlier attempt ran a second <c>queryPathSync</c> inside the
/// same call, into the SAME <c>PathQueryReport</c>, and killed the game on the
/// loading screen. Each probe here is an ordinary
/// <see cref="FieldNavPath.TryReach"/> — its own fresh report, its own rate limit —
/// spent on a tick where the direct query is already known to be pointless. The
/// search therefore costs nothing extra: it uses the query the beacon was going to
/// make anyway, and it stops the moment something routes.</para>
///
/// <para>Once a substitute is found it is kept for as long as the objective stays
/// put, so the beacon simply guides there with corners, distances and everything
/// else working normally.</para>
/// </summary>
public static class ApproachFinder
{
    /// <summary>First ring: twice the arrival radius out from the objective.</summary>
    private static float RING_A_M => 2f * MissionBeaconHooks.ARRIVED_M;

    /// <summary>Second ring, for a footprint the first one lands inside.</summary>
    private static float RING_B_M => 4f * MissionBeaconHooks.ARRIVED_M;

    /// <summary>How far the objective may drift before the search restarts. An NPC
    /// shifting its weight must not throw away a good answer; one that has walked
    /// somewhere else must. The arrival radius is exactly this mod's own definition
    /// of "still the same place".</summary>
    private static float MOVED_M => MissionBeaconHooks.ARRIVED_M;

    // The objective this search belongs to, and the player's height when it began.
    private static bool _have;
    private static float _tx, _ty, _tz, _py;

    // How far through the candidate list we are, and the answer if we have one.
    private static int _probe;
    private static bool _found;
    private static float _ax, _ay, _az;
    private static bool _exhaustedLogged;

    /// <summary>Total candidates: the point below, then two rings of eight.</summary>
    private static int CandidateCount => 1 + 2 * FieldHeadingService.SECTORS;

    /// <summary>True once a reachable stand-in has been found for the current
    /// objective.</summary>
    public static bool HasSubstitute => _found;

    /// <summary>The player has stood at the stand-in; from then on it has done its job (cleared with the search).</summary>
    public static bool Arrived { get; private set; }

    public static void MarkArrived() => Arrived = true;

    /// <summary>The reachable stand-in, if one has been found.</summary>
    public static bool TryGet(float tx, float ty, float tz, float py,
                              out float ax, out float ay, out float az)
    {
        ax = ay = az = 0f;
        Retarget(tx, ty, tz, py);
        if (!_found) return false;
        ax = _ax; ay = _ay; az = _az;
        return true;
    }

    /// <summary>Spend this tick's one query on the next candidate. Returns true when
    /// that candidate routed and the search is over.</summary>
    public static bool Probe(float tx, float ty, float tz, float px, float py, float pz,
                             float longestM = float.PositiveInfinity)
    {
        Retarget(tx, ty, tz, py);
        if (_found) return true;
        if (_probe >= CandidateCount)
        {
            if (!_exhaustedLogged)
            {
                _exhaustedLogged = true;
                API.LogInfo($"[SF6Access] Approach: no reachable stand-in within {RING_B_M:0} m of " +
                            $"({tx:0.0}, {ty:0.0}, {tz:0.0}) after {CandidateCount} probes");
            }
            return false;
        }

        Candidate(_probe, tx, tz, py, out float cx, out float cz);
        _probe++;

        var answer = FieldNavPath.TryReach(cx, py, cz);
        // Throttled means the query never ran, and a player off the navmesh (on a ladder,
        // mid-jump) says nothing about the candidate: give it back for the next tick.
        if (answer.Throttled || (answer.Ok && answer.StartOffMesh)) { _probe--; return false; }
        if (!answer.Ok || !answer.Exists || answer.PathM > longestM) return false;

        _found = true;
        _ax = cx; _ay = py; _az = cz;
        API.LogInfo($"[SF6Access] Approach: probe {_probe} of {CandidateCount} reached " +
                    $"({cx:0.0}, {py:0.0}, {cz:0.0}) - guiding there instead of the objective, " +
                    "which is on a disconnected piece of navmesh");
        return true;
    }

    /// <summary>Candidate <paramref name="index"/>: 0 is the ground directly under
    /// the objective, then two rings of <see cref="FieldHeadingService.SECTORS"/>
    /// points around it.</summary>
    private static void Candidate(int index, float tx, float tz, float py,
                                  out float cx, out float cz)
    {
        if (index == 0) { cx = tx; cz = tz; return; }

        int n = FieldHeadingService.SECTORS;
        int ring = (index - 1) / n;
        int step = (index - 1) % n;
        float radius = ring == 0 ? RING_A_M : RING_B_M;
        double angle = 2.0 * Math.PI * step / n;

        cx = tx + (float)(Math.Sin(angle) * radius);
        cz = tz + (float)(Math.Cos(angle) * radius);
    }

    /// <summary>Start over when the objective is somewhere else, or the player on another
    /// floor (after a ladder): a stand-in found from elsewhere is a confident walk to the
    /// wrong place.</summary>
    private static void Retarget(float tx, float ty, float tz, float py)
    {
        if (_have)
        {
            float dx = tx - _tx, dy = ty - _ty, dz = tz - _tz;
            float moved = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (moved <= MOVED_M && Math.Abs(py - _py) < LevelRoute.LevelM) return;
        }

        _have = true;
        Arrived = false;
        _tx = tx; _ty = ty; _tz = tz; _py = py;
        _probe = 0;
        _found = false;
        _exhaustedLogged = false;
    }

    /// <summary>Forget everything (left the field, new city).</summary>
    public static void Reset()
    {
        _have = _found = _exhaustedLogged = false;
        Arrived = false;
        _probe = 0;
    }
}
