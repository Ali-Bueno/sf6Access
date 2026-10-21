using System;
using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// The city's fast-travel points — localized landmark names with world positions
/// (<c>WTCityManager.GetFastTravelPointList</c>, confirmed in game 2026-09-04).
/// Fetched once per city+situation; an empty answer is retried, since the field can
/// spawn before the city has registered its points.
/// </summary>
public static class FastTravelCatalog
{
    public readonly record struct Point(string Name, float X, float Y, float Z, int Id = 0, bool IsMyRoom = false);

    private const long EMPTY_RETRY_MS = 5000;

    private static List<Point> _points;
    private static uint _city, _situation;
    private static long _fetchedAt;
    private static bool _logged;

    public static void Reset()
    {
        _points = null;
        _logged = false;
    }

    /// <summary>All points, unlocked or not: a locked point still names the place.</summary>
    public static IReadOnlyList<Point> Points()
    {
        uint city = WorldTourStateService.CityId;
        uint situation = WorldTourStateService.SituationId;
        long now = Environment.TickCount64;
        bool same = _points != null && city == _city && situation == _situation;
        if (same && (_points.Count > 0 || now - _fetchedAt < EMPTY_RETRY_MS)) return _points;

        if (!same) _logged = false;
        _city = city;
        _situation = situation;
        _fetchedAt = now;
        _points = Fetch(city, situation);

        // Once per city, or when a retry finally finds something — not every five seconds.
        if (!_logged && (_points.Count > 0 || !same))
        {
            _logged = _points.Count > 0;
            var rest = _points.FindAll(p => p.IsMyRoom);
            string restNames = string.Join("; ", rest.ConvertAll(p => FormattableString.Invariant(
                $"'{p.Name}' [point {p.Id}, {p.X:F2}, {p.Y:F2}, {p.Z:F2}]")));
            API.LogInfo($"[SF6Access] Zone: {_points.Count} named fast-travel points " +
                        $"for city {city} situation {situation}; {rest.Count} native hangouts: {restNames}");
        }
        return _points;
    }

    private static List<Point> Fetch(uint city, uint situation)
    {
        var result = new List<Point>();
        // releasedOnly false: orientation must not depend on progress; needSort false:
        // the game's sort is not by distance from us.
        var list = FlowHelper.Call(WorldTourStateService.GetCityManager(), "GetFastTravelPointList",
                                   city, situation, false, false) as ManagedObject;
        int n = FlowHelper.GetListCount(list);
        for (int i = 0; i < n; i++)
        {
            var pt = FlowHelper.GetListItem(list, i);
            var record = FieldProbeService.Member(pt, "mAttachedData") as ManagedObject;
            string name = PointName(record);
            if (string.IsNullOrEmpty(name)) continue;

            // Position lives on the base CityPointDataInfoBase and is a struct: read untyped.
            object pos = FieldProbeService.Member(pt, "Position");
            if (pos == null) continue;
            float x = FlowHelper.ReadVecComponent(pos, "x");
            float y = FlowHelper.ReadVecComponent(pos, "y");
            float z = FlowHelper.ReadVecComponent(pos, "z");
            // An exact origin is a failed read, not a landmark.
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || x == 0f && y == 0f && z == 0f) continue;
            // mPointId is a signed 32-bit field on the base type, confirmed in the city dump.
            int id = FieldProbeService.Member(pt, "mPointId", typeof(int)) is int value ? value : 0;
            bool isMyRoom = FieldProbeService.Member(record, "IsMyRoom", typeof(bool)) is true;
            result.Add(new Point(name, x, y, z, id, isMyRoom));
        }
        return result;
    }

    private static string PointName(ManagedObject record)
    {
        var msg = AvatarFieldReader.GetProp(record, "PointNameID");
        return FlowHelper.CleanTags(FlowHelper.ResolveGuidField(msg, "GUID"))?.Trim();
    }
}
