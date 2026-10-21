using System;
using System.Globalization;
using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

public static partial class FieldNavPath
{
    // ---------- reading the answer ----------

    /// <summary><c>DistanceType.Raw</c> / <c>.Path</c> straight out of the TDB, once —
    /// never the literals 0 and 1.</summary>
    private static void EnsureDistanceIds()
    {
        if (_distanceIdsTried) return;
        _distanceIdsTried = true;
        _rawId = FieldProbeService.EnumValue(DISTANCE_ENUM, "Raw");
        _pathId = FieldProbeService.EnumValue(DISTANCE_ENUM, "Path");
    }

    /// <summary>One <c>PathInfo.calcDistance</c> reading in metres.
    /// <paramref name="orUntyped"/> allows the no-argument overload (<c>PathInfo.cs:289</c>)
    /// as a fallback — right for the walked length, wrong for the straight-line one, which
    /// returns 0 rather than a number that is not what it claims to be.</summary>
    private static float Distance(ManagedObject info, int distanceId, bool orUntyped)
    {
        var td = info.GetTypeDefinition();
        _calcDistanceTyped ??= Find(td, "calcDistance",
            m => ParamCount(m) == 1 && m.GetParameters()[0].Type?.IsValueType() == true);
        _calcDistance ??= Find(td, "calcDistance", m => ParamCount(m) == 0);

        if (distanceId >= 0 && _calcDistanceTyped != null)
            return FieldProbeService.ToFloat(
                _calcDistanceTyped.InvokeBoxed(typeof(float), info, new object[] { distanceId }));
        if (!orUntyped || _calcDistance == null) return 0f;
        return FieldProbeService.ToFloat(_calcDistance.InvokeBoxed(typeof(float), info, null));
    }

    /// <summary>Read the first straight run after string pulling; preserve close bends.</summary>
    private static bool AimPoint(ManagedObject info, int points, float px, float py, float pz,
                                 float minAwayM, out float x, out float y, out float z, out int turn)
    {
        x = y = z = 0f;
        turn = 0;
        if (points < 1) return false;
        _getPathPointInfo ??= Find(info.GetTypeDefinition(), "getPathPointInfo",
            m => ParamCount(m) == 1 && m.GetParameters()[0].Type?.IsValueType() == true);
        if (_getPathPointInfo == null) return false;
        var aim = new RouteLookAhead(px, pz, minAwayM);
        // A malformed or very dense path must not cause an unbounded wrapper walk.
        const int MaxPointsRead = 64;
        for (uint i = 0; i < (uint)Math.Min(points, MaxPointsRead); i++)
        {
            var point = _getPathPointInfo.InvokeBoxed(typeof(object), info, new object[] { i }) as ManagedObject;
            if (point == null) break;
            if (FieldProbeService.Member(point, "IsSkiped", typeof(bool)) is true) continue;
            var pos = FieldProbeService.Member(point, "PortalPos");
            if (pos == null) break;
            if (!aim.Add(FlowHelper.ReadVecComponent(pos, "x"), FlowHelper.ReadVecComponent(pos, "y"),
                         FlowHelper.ReadVecComponent(pos, "z"))) break;
        }
        x = aim.X; y = aim.Y; z = aim.Z;
        turn = aim.Turn;
        return aim.HasPoint;
    }

    /// <summary>String-pull this query's corridor; raw portal positions cannot verify a direction.</summary>
    private static bool TraceLine(ManagedObject info)
    {
        try
        {
            _createTraceLine ??= Find(info.GetTypeDefinition(), "createTraceLine", m => ParamCount(m) == 0);
            if (_createTraceLine == null)
            {
                Step(5, "createTraceLine() not found; reading RAW portal positions instead");
                return false;
            }
            Step(5, "calling PathInfo.createTraceLine() - string-pulling the corridor into a route");
            // This method returns void, so boxed null cannot distinguish success from a native exception.
            var result = _createTraceLine.Invoke(info, null);
            if (result.ExceptionThrown)
            {
                Fail("createTraceLine: native exception");
                return false;
            }
            return true;
        }
        catch (Exception ex) { Fail($"createTraceLine: {ex.GetType().Name}: {ex.Message}"); }
        return false;
    }

    /// <summary>The radius of the navmesh agent the query ran on
    /// (<c>NavigationSurface.AgentRad</c>) — the engine's own answer to "how much room
    /// does this body need". Read once and kept: the agent is a borrowed NPC and so may
    /// differ between queries, but it is used only as the "is this corner just my own feet"
    /// threshold, where any human-sized body gives the same answer. Zero when unreadable, which makes the
    /// corner test fall back to "any point that is not exactly the player".</summary>
    private static float AgentRadius(ManagedObject surface)
    {
        if (_agentRadius >= 0f) return _agentRadius;
        _agentRadius = FieldProbeService.ToFloat(FieldProbeService.Member(surface, "AgentRad", typeof(float)));
        if (!float.IsFinite(_agentRadius) || _agentRadius < 0f) _agentRadius = 0f;
        return _agentRadius;
    }

    private static bool Write(FieldOutBuffer buf, float x, float y, float z)
    {
        if (!buf.SetComponent("x", x) || !buf.SetComponent("y", y) || !buf.SetComponent("z", z)) return false;
        // Read back through the same field metadata the engine uses: evidence the value
        // landed where native code looks for it, not a half-filled struct.
        return buf.Component("x") == x && buf.Component("y") == y && buf.Component("z") == z;
    }

}
