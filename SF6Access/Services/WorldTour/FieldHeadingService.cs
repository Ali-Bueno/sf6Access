using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Compass for the World Tour field: which way is NORTH, and which of the eight
/// compass points the camera is looking at.
///
/// <para><b>Where north comes from.</b> The game defines no compass constant
/// anywhere (searched the decompiled types: no "north", no map rotation offset).
/// What it does have is the minimap: <c>app.UIMapWindowBase.ConvertTo_UIPos(vec3)</c>
/// turns a world position into a position on the map picture, and in the
/// "Fixing" (north-up) minimap mode that picture is what a sighted player calls
/// north. So north is DERIVED at runtime: probe the map with two world offsets
/// (+X and +Z), which gives the world-to-map linear map, and invert it to find
/// the world direction that goes straight UP the picture. That answer is the
/// game's own, not a guessed axis, and it survives a map that is rotated by any
/// angle. GUI coordinates grow downward (top-left origin), so "up" is −Y.</para>
///
/// <para>If the map window cannot be reached (menus, loading, the HUD hidden),
/// <see cref="NORTH_FALLBACK"/> is used and the source is reported, so a wrong
/// guess is visible in the log rather than silently trusted.</para>
///
/// <para><b>Headings</b> are camera-relative like the clock hours (movement in
/// World Tour is camera-relative), computed with the same
/// <see cref="FieldDirectionService.GetBearing"/> maths so the compass and the
/// clock can never disagree about left and right.</para>
/// </summary>
public static class FieldHeadingService
{
    /// <summary>Eight compass points, clockwise from north.</summary>
    public const int SECTORS = 8;
    public const float DEGREES_PER_SECTOR = 360f / SECTORS;

    /// <summary>World-axis north used when the minimap cannot be probed. Chosen
    /// as +Z pending in-game confirmation (2026-09-05); the minimap derivation
    /// above is the authoritative answer and replaces this whenever it binds.</summary>
    private static readonly (float x, float z) NORTH_FALLBACK = (0f, 1f);

    private const string MINIMAP_WINDOW = "app.UIMiniMapWindow";

    /// <summary>The HUD component that OWNS the minimap window
    /// (<c>UICityHud_CityMiniMap.cs:7</c>), and the member it holds it in
    /// (<c>:27</c>). This is the route that actually binds — see
    /// <see cref="FindMiniMapWindow"/>.</summary>
    private const string MINIMAP_HUD = "app.UICityHud_CityMiniMap";
    private const string MINIMAP_WINDOW_MEMBER = "MiniMapWindow";

    private const string CONVERT_TO_UI = "ConvertTo_UIPos";
    private const string VECTOR_SUFFIX = "vec3";

    /// <summary>Probe offset in metres for the world→map derivation. Any
    /// non-zero length works (the map is linear); one metre keeps the numbers
    /// readable in the log.</summary>
    private const float PROBE_M = 1f;

    /// <summary>A map that projects both probes to (almost) the same point is
    /// not a map; below this determinant the derivation is refused.</summary>
    private const float MIN_DETERMINANT = 1e-6f;

    public enum NorthSource { None, MiniMap, Fallback }

    public readonly struct North
    {
        public readonly float X;
        public readonly float Z;
        public readonly NorthSource Source;
        public bool Ok => Source != NorthSource.None;
        public North(float x, float z, NorthSource source) { X = x; Z = z; Source = source; }
    }

    /// <summary>How often to retry the minimap derivation while it is unavailable
    /// (HUD not built yet, loading). Same 2 s beat as the other field readers'
    /// slow paths; the fallback axis serves in the meantime.</summary>
    private const long RETRY_MS = 2000;

    private static North _north;
    private static long _nextTryTick;
    private static bool _fallbackLogged;
    private static bool _windowRouteLogged;
    private static Method _convertToUi;
    private static Method _rotateFlag;
    private static TypeDefinition _vecType;

    /// <summary>The current north. Derived from the minimap once per field
    /// session and then cached; until that binds, the fallback axis is returned
    /// and the derivation is retried every <see cref="RETRY_MS"/>. Call
    /// <see cref="Reset"/> when the field unloads so the next city derives its
    /// own.</summary>
    public static North GetNorth()
    {
        if (_north.Source == NorthSource.MiniMap) return _north;

        long now = System.Environment.TickCount64;
        if (now >= _nextTryTick)
        {
            _nextTryTick = now + RETRY_MS;
            if (TryDeriveFromMiniMap(out var n))
            {
                _north = n;
                API.LogInfo($"[SF6Access] Heading: north from minimap = ({n.X:0.00}, {n.Z:0.00})");
                return _north;
            }
        }

        if (!_fallbackLogged)
        {
            _fallbackLogged = true;
            API.LogInfo($"[SF6Access] Heading: north from fallback axis = ({NORTH_FALLBACK.x:0.00}, {NORTH_FALLBACK.z:0.00}) until the minimap binds");
        }
        return new North(NORTH_FALLBACK.x, NORTH_FALLBACK.z, NorthSource.Fallback);
    }

    public static void Reset()
    {
        _north = default;
        _nextTryTick = 0;
        _fallbackLogged = false;
        _windowRouteLogged = false;
    }

    /// <summary>Compass heading of a ground-plane direction: 0 = north, 90 = east,
    /// clockwise, in [0, 360). NaN when either side is unreadable.</summary>
    public static float HeadingDegrees(FieldDirectionService.FlatDir dir)
    {
        var north = GetNorth();
        if (!north.Ok || !dir.Ok) return float.NaN;
        var b = FieldDirectionService.GetBearing(new FieldDirectionService.FlatDir(north.X, north.Z, true), dir.X, dir.Z);
        if (!b.Ok) return float.NaN;
        double deg = System.Math.Atan2(b.Right, b.Ahead) * 180.0 / System.Math.PI;
        if (deg < 0) deg += 360.0;
        return (float)deg;
    }

    /// <summary>Compass point index (0 = north, 1 = northeast, ... 7 = northwest)
    /// nearest to a heading.</summary>
    public static int Sector(float headingDeg)
        => (int)System.Math.Round(headingDeg / DEGREES_PER_SECTOR) % SECTORS;

    /// <summary>Signed distance (degrees, in [-half, +half)) from the CENTRE of a
    /// sector. Lets a caller add hysteresis at the sector edges.</summary>
    public static float OffsetFromSectorCentre(float headingDeg, int sector)
    {
        float delta = headingDeg - sector * DEGREES_PER_SECTOR;
        while (delta >= 180f) delta -= 360f;
        while (delta < -180f) delta += 360f;
        return delta;
    }

    /// <summary>Smallest signed angle from one heading to another, in (-180, 180].</summary>
    public static float DeltaDegrees(float fromDeg, float toDeg)
    {
        float d = toDeg - fromDeg;
        while (d > 180f) d -= 360f;
        while (d <= -180f) d += 360f;
        return d;
    }

    public static string SectorName(int sector) => LocalizedText.CompassPoint(sector);

    // --- minimap derivation ---

    private static bool TryDeriveFromMiniMap(out North north)
    {
        north = default;
        try
        {
            var window = FindMiniMapWindow();
            if (window == null) return false;

            // A minimap that turns with the camera projects "up" as "where the
            // camera looks", which is no compass at all. Only the fixed (north-up)
            // picture can be trusted; the rotate flag is the game's own setting.
            if (IsRotating(window))
            {
                API.LogInfo("[SF6Access] Heading: minimap is in rotating mode; keeping the fallback axis");
                return false;
            }

            if (_convertToUi == null)
            {
                _convertToUi = FieldProbeService.FindByShape(window.GetTypeDefinition(), CONVERT_TO_UI, 1, VECTOR_SUFFIX);
                _vecType = _convertToUi?.GetParameters()?[0].Type;
            }
            if (_convertToUi == null || _vecType == null)
            {
                API.LogWarning($"[SF6Access] Heading: {CONVERT_TO_UI}(vec3) not found on {window.GetTypeDefinition()?.FullName}");
                return false;
            }

            var mgr = WorldTourStateService.GetAvatarManager();
            var p = AvatarFieldReader.ReadPlayerPos(mgr);
            if (!p.ok) return false;

            // World→map is affine; three projections give its 2×2 linear part.
            if (!Project(window, p.x, p.y, p.z, out float ox, out float oy)) return false;
            if (!Project(window, p.x + PROBE_M, p.y, p.z, out float xx, out float xy)) return false;
            if (!Project(window, p.x, p.y, p.z + PROBE_M, out float zx, out float zy)) return false;

            // Columns of M: world +X -> (ax, ay); world +Z -> (bx, by).
            float ax = xx - ox, ay = xy - oy;
            float bx = zx - ox, by = zy - oy;
            float det = ax * by - ay * bx;
            if (!float.IsFinite(det) || System.Math.Abs(det) < MIN_DETERMINANT)
            {
                API.LogWarning($"[SF6Access] Heading: minimap projection degenerate (det={det})");
                return false;
            }

            // Solve M · n = (0, -1): the world direction that goes straight up the picture.
            float upX = 0f, upY = -1f;
            float nx = (by * upX - bx * upY) / det;
            float nz = (-ay * upX + ax * upY) / det;
            float len = (float)System.Math.Sqrt(nx * nx + nz * nz);
            if (!float.IsFinite(len) || len < MIN_DETERMINANT) return false;

            API.LogInfo($"[SF6Access] Heading: minimap columns +X->({ax:0.###},{ay:0.###}) +Z->({bx:0.###},{by:0.###})");
            north = new North(nx / len, nz / len, NorthSource.MiniMap);
            return true;
        }
        catch (System.Exception ex)
        {
            API.LogWarning("[SF6Access] Heading: minimap derivation failed: " + ex.Message);
            return false;
        }
    }

    private static bool IsRotating(ManagedObject window)
    {
        try
        {
            _rotateFlag ??= window.GetTypeDefinition()?.GetMethod("get_MapRotateFlag");
            if (_rotateFlag == null) return false;
            return _rotateFlag.InvokeBoxed(typeof(object), window, System.Array.Empty<object>()) is bool b && b;
        }
        catch { return false; }
    }

    private static bool Project(ManagedObject window, float x, float y, float z, out float ux, out float uy)
    {
        ux = uy = float.NaN;
        using var buf = FieldOutBuffer.Acquire(_vecType);
        if (buf == null || !buf.SetComponent("x", x) || !buf.SetComponent("y", y) || !buf.SetComponent("z", z))
            return false;
        var boxed = _convertToUi.InvokeBoxed(typeof(object), window, new object[] { buf.View });
        if (boxed == null) return false;
        ux = FlowHelper.ReadVecComponent(boxed, "x");
        uy = FlowHelper.ReadVecComponent(boxed, "y");
        return float.IsFinite(ux) && float.IsFinite(uy);
    }

    /// <summary>The live minimap window, for the readers that need more from it
    /// than a compass. There is exactly one search for it in the mod, here, so
    /// the compass and <see cref="MapIconCatalog"/> can never end up looking at
    /// two different windows. Not cached: the component is rebuilt on every city
    /// load, and a stale wrapper over a destroyed one is the finalizer crash this
    /// mod has already paid for once.</summary>
    public static ManagedObject MiniMapWindow() => FindMiniMapWindow();

    /// <summary>The live minimap window.
    ///
    /// <para><b>It is NOT a scene component, and asking for it as one silently
    /// returned nothing.</b> Confirmed in game 2026-09-09: searching the scene for
    /// <c>app.UIMiniMapWindow</c> found nothing for a whole session, so the
    /// compass ran on <see cref="NORTH_FALLBACK"/> — a guessed axis — the entire
    /// time, and the minimap icon catalogue came back empty. The window is OWNED
    /// by a HUD component: <c>app.UICityHud_CityMiniMap : UIHudBase</c> exposes
    /// <c>MiniMapWindow : UIMiniMapWindow</c>
    /// (<c>UICityHud_CityMiniMap.cs:7,27</c>), and <c>UICityHud_*</c> components
    /// ARE in the scene — <see cref="MissionGuideReader"/> finds
    /// <c>app.UICityHud_MissionGuide</c> the same way and has always worked.</para>
    ///
    /// <para>The direct search is kept as a second attempt, costing nothing when
    /// the first succeeds: if a future patch ever does put the window in the scene
    /// itself, that still binds rather than regressing.</para></summary>
    private static ManagedObject FindMiniMapWindow()
    {
        var hud = FieldProbeService.FirstSceneComponent(MINIMAP_HUD);
        var window = FieldProbeService.Member(hud, MINIMAP_WINDOW_MEMBER) as ManagedObject
                     ?? FieldProbeService.FirstSceneComponent(MINIMAP_WINDOW);

        if (window != null && !_windowRouteLogged)
        {
            _windowRouteLogged = true;
            API.LogInfo($"[SF6Access] Heading: minimap window via " +
                        (hud != null ? $"{MINIMAP_HUD}.{MINIMAP_WINDOW_MEMBER}" : MINIMAP_WINDOW));
        }
        return window;
    }


}
