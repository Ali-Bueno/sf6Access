using System;
using System.Globalization;
using REFrameworkNET;
using SF6Access.Services;
// For MissionBeaconHooks.ARRIVED_M: the mod's single definition of "close enough to be
// the same place". Restating the number here to avoid the reference would be exactly the
// duplicated magic constant the project rules forbid.
using SF6Access.Hooks.WorldTour;

namespace SF6Access.Services.WorldTour;

/// <summary>Read-only path queries on a borrowed NPC surface; never steers an actor.
/// Binding, report reads and native diagnostics live in separate partial files.
/// Runtime constraints and crash history: docs/sf6-screens.md, navigation infrastructure.</summary>
public static partial class FieldNavPath
{
    /// <summary><b>ON, with a kill switch on Shift+V</b>
    /// (<c>Hooks/WorldTour/MapAimHooks.cs</c>).
    ///
    /// <para>It started off, because nothing in this file had ever run in game and the
    /// previous navmesh route crashed SF6 on its first press (2026-09-08). It was turned on
    /// by default on 2026-09-09 once a session proved both halves: no crash, and
    /// <c>queryPathSync</c> returning real routes (<c>Exist=True</c>, 65 / 12 / 2 points,
    /// and a correct <c>Exist=False</c> for an objective 12 m up a building). A live
    /// property rather than a compile-time constant so a session that starts misbehaving can
    /// still be rescued without a rebuild: turning it off puts every beacon back on the
    /// straight line.</para></summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Kept as the internal name so the guarded code below still reads as one
    /// switch rather than a scattered property test.</summary>
    private static bool PATHFINDER_ENABLED => Enabled;

    private const string SURFACE_TYPE = "via.navigation.NavigationSurface";
    private const string DISTANCE_ENUM = "via.navigation.map.PathInfo.DistanceType";

    /// <summary>Why a query failed (<c>FailReport.cs:114</c>).</summary>
    private const string FAIL_ENUM = "via.navigation.FailReport.FailAttribute";

    /// <summary>Matched EXACTLY. <c>queryPath</c> (no <c>Sync</c>) is the async family and
    /// carries a delegate parameter — see the class remarks.</summary>
    private const string QUERY_PATH_SYNC = "queryPathSync";

    /// <summary>Shortest gap between two synchronous path queries, in milliseconds.
    /// <c>queryPathSync</c> is a BLOCKING A* across a city and must never run at sensing
    /// cadence, so the floor is the beacon's own fastest ping gap (<see cref="HomingCue"/>'s
    /// private <c>GAP_NEAR_MS</c>, a quarter of a second — the shortest silence that still
    /// reads as two pings; restated here because it cannot be reached). One answer per
    /// beacon ping however often a caller polls, charged for the whole ATTEMPT so a query that
    /// faults or cannot resolve its surface is not retried at frame rate.</summary>
    public const long MinQueryIntervalMs = 250;
    internal static long NextQueryTick => _nextQueryAt;

    /// <summary>What the pathfinder said. <c>Ok == false</c> means "could not answer" and
    /// a caller must NEVER read that as walkable.</summary>
    public readonly struct PathAnswer
    {
        /// <summary><see cref="Ok"/>: the query ran and was read. <see cref="Exists"/>:
        /// the engine found a route (<c>PathQueryReport.Exist</c>), meaningful only when
        /// <see cref="Ok"/>. <see cref="Throttled"/>: refused by the rate limiter, not by
        /// the engine — ask again later, keeping the previous answer.</summary>
        public readonly bool Ok, Exists, Throttled;

        /// <summary>Metres actually WALKED along the route (<c>DistanceType.Path</c>).</summary>
        public readonly float PathM;

        /// <summary>The route's STRAIGHT-line length (<c>DistanceType.Raw</c>), or 0 when
        /// the enum could not be read. <see cref="DetourRatio"/> divides the two: one
        /// number for "how far out of my way does this destination actually take me".</summary>
        public readonly float StraightM;

        /// <summary>Corners in the route (<c>PathInfo.PathPointCount</c>).</summary>
        public readonly int Points;

        /// <summary>Where the route first turns — what a beacon should point AT while the
        /// destination itself is round a corner. <see cref="HasCorner"/> says the position
        /// was readable at all.</summary>
        public readonly bool HasCorner;
        /// <summary>The route was string-pulled successfully; directional previews also require a readable corner.</summary>
        public readonly bool GeometryVerified;
        public readonly float CornerX, CornerY, CornerZ;
        public readonly int Turn;

        /// <summary>The engine found no navmesh node under the start or end point
        /// (<c>StartNodeNotFound</c> / <c>DestNodeNotFound</c>), as opposed to two
        /// points on the mesh that no walk connects.</summary>
        public readonly bool StartOffMesh, EndOffMesh;

        public PathAnswer(bool ok, bool exists, bool throttled, float pathM, float straightM,
                          int points, bool hasCorner, float cx, float cy, float cz, int turn = 0,
                          bool startOffMesh = false, bool endOffMesh = false, bool geometryVerified = false)
        {
            Ok = ok; Exists = exists; Throttled = throttled;
            PathM = pathM; StraightM = straightM; Points = points;
            HasCorner = hasCorner; CornerX = cx; CornerY = cy; CornerZ = cz;
            GeometryVerified = geometryVerified;
            Turn = turn; StartOffMesh = startOffMesh; EndOffMesh = endOffMesh;
        }

        /// <summary>The detour ratio, or 0 when the straight-line length is unknown.</summary>
        public float DetourRatio => StraightM > 0f ? PathM / StraightM : 0f;
    }

    // ---- the only survivors between calls: TDB metadata and unmanaged memory ----
    private static Method _queryPathSync, _hasValidMap, _calcDistance, _calcDistanceTyped, _getPathPointInfo;
    private static Method _createTraceLine, _isFailed;
    private static TypeDefinition _vecType, _reportType;
    private static FieldOutBuffer _start, _end;
    private static int _rawId = -1, _pathId = -1;
    private static long _nextQueryAt;
    private static string _lastFailVerdict;
    private static bool _distanceIdsTried, _bindTried, _bound, _tracing = true, _failLogged, _summaryLogged;

    /// <summary>Cached <c>AgentRad</c>; negative means "not read yet", which is a state a
    /// real radius cannot take.</summary>
    private static float _agentRadius = -1f;

    /// <summary>Switch on, the avatar's surface resolved, and the engine itself says this
    /// place has a navmesh. It touches engine objects, so it is <c>false</c> immediately
    /// when the switch is off; call it when arming a beacon, not every frame.</summary>
    public static bool Available => PATHFINDER_ENABLED && ReadySurface() != null;

    /// <summary>One line for the arming log: what bound, or why nothing did.</summary>
    public static string RouteDescription { get; private set; } = "not probed yet";

    /// <summary>Is there a walkable route from the avatar to this world point, how long is
    /// it, and where does it first turn? Never throws.</summary>
    public static PathAnswer TryReach(float x, float y, float z)
    {
        if (!PATHFINDER_ENABLED) return default;
        var me = AvatarFieldReader.ReadPlayerPos(null);
        if (!me.ok) { Fail("the avatar's own position is unreadable"); return default; }
        return Query(me.x, me.y, me.z, x, y, z, readAim: true);
    }

    /// <summary>Preview from an exact sampled origin, sharing the same native query and rate limit.</summary>
    internal static PathAnswer TryReachFrom(float sx, float sy, float sz, float x, float y, float z)
    {
        if (!PATHFINDER_ENABLED || !float.IsFinite(sx) || !float.IsFinite(sy) || !float.IsFinite(sz)
            || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) return default;
        return Query(sx, sy, sz, x, y, z, readAim: true);
    }

    /// <summary>Is there a walkable route between two arbitrary world points, and how long
    /// is it? Shares the rate limit with <see cref="TryReach"/>; no aim is read.</summary>
    public static PathAnswer TryConnect(float ax, float ay, float az, float bx, float by, float bz)
        => PATHFINDER_ENABLED ? Query(ax, ay, az, bx, by, bz, readAim: false) : default;

    private static PathAnswer Query(float sx, float sy, float sz, float x, float y, float z, bool readAim)
    {
        long now = Environment.TickCount64;
        if (now < _nextQueryAt) return new PathAnswer(false, false, true, 0f, 0f, 0, false, 0f, 0f, 0f);
        // Charged for the whole ATTEMPT, before anything is resolved: a query that faults,
        // hangs or cannot resolve its surface must not be retried at frame rate either.
        _nextQueryAt = now + MinQueryIntervalMs;
        try
        {
            var surface = ReadySurface();
            if (surface == null) return default;

            if (!Write(_start, sx, sy, sz) || !Write(_end, x, y, z))
            { Fail("could not write the query endpoints"); return default; }

            Step(3, $"allocating a {_reportType?.FullName}");
            var report = FieldProbeService.NewInstance(_reportType);
            if (report == null) { Fail($"{_reportType?.FullName} is not constructible"); return default; }

            Step(4, string.Format(CultureInfo.InvariantCulture,
                "calling {0}(vec3, vec3, report) from ({1:F2}, {2:F2}, {3:F2}) to ({4:F2}, {5:F2}, {6:F2}) " +
                "- the first call in this route that has never run in game",
                QUERY_PATH_SYNC, sx, sy, sz, x, y, z));
            var ran = _queryPathSync.InvokeBoxed(typeof(bool), surface,
                                                 new object[] { _start.View, _end.View, report });

            // NO RETRY ON A FAILED QUERY. Asking again with the destination pushed
            // down to the player's height was tried on 2026-09-09 and KILLED THE GAME
            // on the World Tour loading screen: the log stops dead at step 6, one line
            // after the retry, with no shutdown sequence. A second queryPathSync into
            // the SAME PathQueryReport evidently leaves its PathInfo in a state that
            // createTraceLine and the point reads cannot survive — and until this
            // session a failed query simply produced points=0 and was harmless. If the
            // "objective is on a roof" case is worth solving, it needs a FRESH report
            // per query, and it needs proving before it ships.

            Step(5, "reading PathQueryReport.Exist / PathInfo");
            bool exists = FieldProbeService.Member(report, "Exist", typeof(bool)) is bool e && e;
            string verdict = exists ? null : Diagnose(report, log: readAim);
            var info = FieldProbeService.Member(report, "PathInfo") as ManagedObject;

            float pathM = 0f, rawM = 0f, cx = 0f, cy = 0f, cz = 0f;
            int points = 0;
            bool hasCorner = false;
            bool geometryVerified = false;
            int turn = 0;
            if (exists && ran is true && info != null)
            {
                // STRING-PULLING, and it is the difference between a corridor and a
                // route. Until this runs, the path points are raw PORTAL crossings —
                // the boundaries between navmesh polygons — and their positions sit
                // somewhere along an EDGE, not on any line a walker would take. That
                // is why two completely different destinations produced almost the
                // same bearing in game on 2026-09-09: both were reading the same few
                // edges around the polygon the player was standing in.
                // `void createTraceLine()` (PathInfo.cs:307) takes no arguments and
                // returns nothing, and it mutates OUR PathQueryReport's PathInfo, not
                // the borrowed NPC's navigation. A connectivity question needs no line.
                if (readAim) geometryVerified = TraceLine(info);

                Step(6, "reading PathInfo.calcDistance / PathPointCount / walking getPathPointInfo(i) to where the route bends");
                EnsureDistanceIds();
                pathM = Distance(info, _pathId, orUntyped: true);
                rawM = Distance(info, _rawId, orUntyped: false);
                var n = FieldProbeService.Member(info, "PathPointCount", typeof(uint));
                points = n == null ? 0 : (int)Convert.ToUInt32(n);
                if (readAim)
                    hasCorner = AimPoint(info, points, sx, sy, sz, AgentRadius(surface),
                                         out cx, out cy, out cz, out turn);
            }

            Step(7, string.Format(CultureInfo.InvariantCulture,
                "{0} returned {1}; Exist={2}; points={3}; aim {4} vs start ({5:F2}, {6:F2}, {7:F2})",
                QUERY_PATH_SYNC, ran, exists, points,
                hasCorner ? string.Format(CultureInfo.InvariantCulture, "({0:F2}, {1:F2}, {2:F2})", cx, cy, cz)
                          : "unreadable",
                sx, sy, sz));
            LogRouteOnce(surface);
            _tracing = false;
            return new PathAnswer(true, exists, false, pathM, rawM, points, hasCorner, cx, cy, cz, turn,
                                  verdict?.Contains("StartNodeNotFound") == true,
                                  verdict?.Contains("DestNodeNotFound") == true, geometryVerified);
        }
        catch (Exception ex)
        {
            Fail($"{ex.GetType().Name}: {ex.Message}");
            return default;
        }
    }

    /// <summary>One line describing an answer, so every consumer words it the same way.</summary>
    public static string Describe(in PathAnswer a)
    {
        if (a.Throttled) return "not asked yet (rate limited)";
        if (!a.Ok) return "no answer";
        if (!a.Exists) return "NO WALKABLE PATH";
        return string.Format(CultureInfo.InvariantCulture,
            "path {0:F2} m walked / {1:F2} m straight (detour x{2:F2}), {3} points",
            a.PathM, a.StraightM, a.DetourRatio, a.Points);
    }

    /// <summary>Drop everything derived from the current city. The unmanaged endpoint
    /// buffers stay — unmanaged memory does not move, and nothing about a city load
    /// invalidates them.</summary>
    public static void Reset()
    {
        _queryPathSync = _hasValidMap = _calcDistance = _calcDistanceTyped = _getPathPointInfo = null;
        _createTraceLine = _isFailed = null;
        _lastFailVerdict = null;
        _vecType = _reportType = null;
        _bindTried = _bound = _failLogged = _summaryLogged = false;
        _tracing = true;
        _nextQueryAt = 0;
        _agentRadius = -1f;
        RouteDescription = "not probed yet";
    }

}
