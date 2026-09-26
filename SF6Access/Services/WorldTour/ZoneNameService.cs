using System;
using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Resolves the localized name of the World Tour area the player is standing in —
/// the RE7 mod's room announcements, ported to an open city.
///
/// <para>Three routes, tried in order of how precisely they answer the question:</para>
/// <list type="number">
/// <item><b>Section</b> — <c>CitySectionManager.CurrentSectionId</c> in
///   <c>CitySectionDataUserDataDict</c>, whose record carries the district's
///   <c>SectionNameID.GUID</c>. The game's own zone concept. That dictionary is
///   keyed by <c>ManageId</c>, so rather than assume it equals the section id the
///   lookup CROSS-CHECKS the record's own <c>id</c> and refuses it when they
///   disagree: a wrong key must never yield a confident wrong district.</item>
/// <item><b>Nearest fast-travel point</b> — <c>WTCityManager.GetFastTravelPointList</c>,
///   real localized landmark names with world positions. It answers "nearest to",
///   not "inside", so it is reported with its distance and the caller hedges it as
///   "near X" instead of naming it as a zone.</item>
/// <item><b>City</b> — <c>CityMessageUserDataDict</c>. Coarse, but never wrong.</item>
/// </list>
///
/// <para>No table is queried per frame: the point list is fetched once per
/// city+situation, names are memoised, and the whole reading is recomputed at most
/// a couple of times a second.</para>
/// </summary>
public static class ZoneNameService
{
    private const string SECTION_DICT = "CitySectionDataUserDataDict";
    private const string SECTION_RECORD = "CitySectionDataUserDataRecord";
    private const string CITY_DICT = "CityMessageUserDataDict";
    private const string CITY_RECORD = "CityMessageUserDataRecord";

    // How often the whole reading is recomputed. Walking out of one district into
    // the next is not a sub-second event, and this keeps the nearest-landmark
    // sweep off the per-frame path.
    private const long REFRESH_MS = 500;

    // How much closer a different landmark must be before it takes over as "the
    // place you are near". Without it, standing on the midpoint between two
    // points renames the area every refresh. Same value and same reason as
    // FieldTrackingHooks.SWITCH_MARGIN_M, which fixed exactly this flapping for
    // the nearest-avatar tracker.
    private const float SWITCH_MARGIN_M = 2f;

    private static ZoneReading _current;
    private static long _refreshedAt;

    /// <summary>The current zone reading, recomputed at most every
    /// <see cref="REFRESH_MS"/>. One shared answer, so the automatic
    /// change announcement and the on-demand key can never disagree.</summary>
    public static ZoneReading Current()
    {
        long now = Environment.TickCount64;
        if (_refreshedAt != 0 && now - _refreshedAt < REFRESH_MS) return _current;
        _refreshedAt = now;
        _current = Resolve();
        return _current;
    }

    /// <summary>Forget everything learned about the current city. Called when the
    /// field is left, so re-entering re-announces and a reloaded table is not read
    /// through a stale cache.</summary>
    public static void Reset()
    {
        _current = default;
        _refreshedAt = 0;
        _sectionNames.Clear();
        _sectionsBuilt = false;
        _sectionsAt = 0;
        _points = null;
        _pointsLogged = false;
        _stickyPoint = null;
        _cityNameId = 0;
        _cityName = null;
    }

    private static ZoneReading Resolve()
    {
        string section = SectionName(WorldTourStateService.CurrentSectionId);
        if (!string.IsNullOrEmpty(section))
            return new ZoneReading(section, ZoneSource.Section, 0f);

        var near = NearestPoint();
        if (near.Ok) return near;

        string city = CityName(WorldTourStateService.CityId);
        return string.IsNullOrEmpty(city)
            ? default : new ZoneReading(city, ZoneSource.City, 0f);
    }

    // ---------- route B: the district the player is standing in ----------

    // Section id -> localized name. SUCCESSES only, dropped when the city changes
    // (which is when the tables themselves are swapped). A miss is remembered as a
    // timestamp instead of as a cached null: caching the failure would make one
    // unlucky query during the city's load kill that district's name for the whole
    // session, while re-querying every refresh would flood the log.
    private static readonly Dictionary<uint, string> _sectionNames = new();
    private static uint _sectionCity;
    private static bool _sectionsBuilt;
    private static long _sectionsAt;

    /// <summary>How far past the record count the key probe may reach before giving
    /// up. These tables' ManageIds are dense in practice, so a small multiple is
    /// generous; it exists only so a sparse table cannot spin forever.</summary>
    private const uint KEY_PROBE_SLACK = 8;

    /// <summary>The district name for a section id, or null when the section is
    /// unavailable (id 0 means "no section", not "section zero") or the table has
    /// nothing for it.</summary>
    private static string SectionName(uint sectionId)
    {
        if (sectionId == 0) return null;

        uint city = WorldTourStateService.CityId;
        if (city != _sectionCity)
        {
            _sectionNames.Clear();
            _sectionCity = city;
            _sectionsBuilt = false;
            _sectionsAt = 0;
        }

        if (!_sectionsBuilt) BuildSectionNames();
        return _sectionNames.TryGetValue(sectionId, out string name) ? name : null;
    }

    /// <summary><b>Build the whole id-to-name map at once, indexed by each record's
    /// OWN id.</b>
    ///
    /// <para>This replaced a keyed lookup that asked the table for
    /// <c>get_Item(sectionId)</c> and then cross-checked the record's <c>id</c>
    /// against it. That check was there for a good reason — these dictionaries are
    /// keyed by <c>ManageId</c>, which is not guaranteed to be the section id, and
    /// handing back somebody else's district would be worse than silence — but its
    /// consequence was that whenever the two ids differ the route simply went mute.
    /// Reading every record and filing it under its own <c>id</c> makes the question
    /// moot: it no longer matters what the dictionary is keyed by.</para>
    ///
    /// <para><b>Bounded by the table's own count.</b> There is no safe way to walk an
    /// IL2CPP dictionary's entries in this codebase — no helper exists and the
    /// enumerator is a struct — so the keys are probed upward with
    /// <c>ContainsKey</c>, which is a by-value call returning a bool, and the scan
    /// stops the moment <c>get_Count</c> records have been found. The ceiling exists
    /// only so that a sparse table cannot spin: it is a multiple of the record
    /// count, generous because these ids are dense in practice, and stopping early
    /// is logged rather than hidden.</para>
    ///
    /// <para>Run once per city, and only re-attempted after
    /// <see cref="EMPTY_RETRY_MS"/> while the table is still empty — during a city
    /// load it legitimately is.</para></summary>
    private static void BuildSectionNames()
    {
        long now = Environment.TickCount64;
        if (now - _sectionsAt < EMPTY_RETRY_MS) return;
        _sectionsAt = now;

        var dict = FlowHelper.GetTableDict(SECTION_DICT);
        int total = FlowHelper.GetListCount(dict);
        if (dict == null || total <= 0) return;

        int found = 0;
        uint key = 0;
        uint ceiling = (uint)total * KEY_PROBE_SLACK;

        while (found < total && key < ceiling)
        {
            key++;
            bool present;
            try { present = Convert.ToBoolean(FlowHelper.Call(dict, "ContainsKey", key)); }
            catch { continue; }
            if (!present) continue;

            found++;
            var record = FlowHelper.GetTableRecord(SECTION_DICT, SECTION_RECORD, key);
            if (record == null) continue;

            uint id = FlowHelper.ReadUIntField(record, "id");
            if (id == 0) continue;

            var msg = AvatarFieldReader.GetProp(record, "SectionNameID");
            string name = FlowHelper.CleanTags(FlowHelper.ResolveGuidField(msg, "GUID"))?.Trim();
            if (string.IsNullOrEmpty(name)) continue;

            _sectionNames[id] = name;
        }

        _sectionsBuilt = _sectionNames.Count > 0;
        API.LogInfo($"[SF6Access] Zone: {SECTION_DICT} enumerated - {_sectionNames.Count} named sections " +
                    $"from {found} of {total} records" +
                    (found < total ? $" (stopped at key {key} of a {ceiling} ceiling)" : ""));
    }

    // ---------- route C: the nearest named landmark ----------

    /// <summary>One fast-travel point: its localized name and where it is.</summary>
    private readonly struct Point
    {
        public readonly string Name;
        public readonly float X, Z;
        public Point(string name, float x, float z) { Name = name; X = x; Z = z; }
    }

    private static List<Point> _points;
    private static uint _pointsCity, _pointsSituation;
    private static long _pointsAt;
    private static bool _pointsLogged;
    private static string _stickyPoint;

    // How long an EMPTY result is trusted before asking again. The field gate only
    // proves the avatar has spawned, not that the city has finished registering its
    // fast-travel points, and caching that race forever would leave the fallback
    // permanently mute for the session.
    private const long EMPTY_RETRY_MS = 5000;

    private static ZoneReading NearestPoint()
    {
        var points = Points();
        if (points.Count == 0) return default;

        var player = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());
        if (!player.ok) return default;

        string best = null;
        float bestDist = float.MaxValue, stickyDist = float.MaxValue;
        foreach (var p in points)
        {
            // GROUND-PLANE distance: "which part of town is this" does not care
            // that a landmark's marker sits a little above or below the pavement,
            // and the height difference would otherwise inflate every reading.
            float dx = p.X - player.x, dz = p.Z - player.z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d < bestDist) { bestDist = d; best = p.Name; }
            // Two points really do share a name ('Beat Square' appears twice), so
            // the sticky one is whichever copy is closest.
            if (p.Name == _stickyPoint && d < stickyDist) stickyDist = d;
        }
        if (best == null) return default;

        if (_stickyPoint != null && stickyDist < float.MaxValue &&
            bestDist > stickyDist - SWITCH_MARGIN_M)
            return new ZoneReading(_stickyPoint, ZoneSource.NearestPoint, stickyDist);

        _stickyPoint = best;
        return new ZoneReading(best, ZoneSource.NearestPoint, bestDist);
    }

    /// <summary>The city's fast-travel points, fetched once per city+situation.
    /// <c>releasedOnly: false</c> on purpose: a point the player has not unlocked
    /// yet still names the place they are standing in, and orientation must not
    /// depend on progress. <c>needSort: false</c> because the sort the game offers
    /// is not by distance from us.</summary>
    private static List<Point> Points()
    {
        uint city = WorldTourStateService.CityId;
        uint situation = WorldTourStateService.SituationId;
        long now = Environment.TickCount64;
        bool sameCity = _points != null && city == _pointsCity && situation == _pointsSituation;
        if (sameCity && (_points.Count > 0 || now - _pointsAt < EMPTY_RETRY_MS))
            return _points;

        if (!sameCity) { _pointsLogged = false; _stickyPoint = null; }
        _pointsCity = city;
        _pointsSituation = situation;
        _pointsAt = now;
        _points = new List<Point>();

        var list = FlowHelper.Call(WorldTourStateService.GetCityManager(), "GetFastTravelPointList",
                                   city, situation, false, false) as ManagedObject;
        int n = FlowHelper.GetListCount(list);
        for (int i = 0; i < n; i++)
        {
            var pt = FlowHelper.GetListItem(list, i);
            string name = PointName(pt);
            if (string.IsNullOrEmpty(name)) continue;

            // Position is declared on the BASE type CityPointDataInfoBase, which the
            // element's own TypeDefinition does not expose, and it is a struct — so
            // it is read UNTYPED (naming via.vec3 as the target type yields a
            // dispatch proxy that reads as all zeros).
            object pos = FieldProbeService.Member(pt, "Position");
            if (pos == null) continue;
            _points.Add(new Point(name,
                FlowHelper.ReadVecComponent(pos, "x"),
                FlowHelper.ReadVecComponent(pos, "z")));
        }

        // Once per city, or as soon as the retry finally finds something — a line
        // every five seconds while a city has no points would be log flood.
        if (!_pointsLogged && (_points.Count > 0 || !sameCity))
        {
            _pointsLogged = _points.Count > 0;
            API.LogInfo($"[SF6Access] Zone: {_points.Count} named fast-travel points " +
                        $"for city {city} situation {situation}");
        }
        return _points;
    }

    /// <summary>A fast-travel point's localized name, from the user-data record
    /// attached to it.</summary>
    private static string PointName(ManagedObject point)
    {
        var record = FieldProbeService.Member(point, "mAttachedData") as ManagedObject;
        var msg = AvatarFieldReader.GetProp(record, "PointNameID");
        return FlowHelper.CleanTags(FlowHelper.ResolveGuidField(msg, "GUID"))?.Trim();
    }

    // ---------- route A: the city ----------

    private static uint _cityNameId;
    private static string _cityName;
    private static long _cityNameAt;

    /// <summary>Same retry-a-failure rule as the section names: a hit sticks, a
    /// miss is re-asked a few seconds later in case the table was still loading.</summary>
    private static string CityName(uint cityId)
    {
        if (cityId == 0) return null;
        long now = Environment.TickCount64;
        if (cityId == _cityNameId && (_cityName != null || now - _cityNameAt < EMPTY_RETRY_MS))
            return _cityName;

        bool firstAttempt = cityId != _cityNameId;
        _cityNameId = cityId;
        _cityNameAt = now;
        var record = FlowHelper.GetTableRecord(CITY_DICT, CITY_RECORD, cityId);
        // CityName is the display name; CityUIName is the shorter HUD variant and
        // is the fallback when the table carries only one of them.
        _cityName = Message(record, "CityName") ?? Message(record, "CityUIName");
        if (_cityName != null || firstAttempt)
            API.LogInfo($"[SF6Access] Zone: city {cityId} -> {(_cityName == null ? "no name" : $"'{_cityName}'")}");
        return _cityName;
    }

    private static string Message(ManagedObject record, string fieldName)
    {
        var msg = AvatarFieldReader.GetProp(record, fieldName);
        return FlowHelper.CleanTags(FlowHelper.ResolveGuidField(msg, "GUID"))?.Trim();
    }
}
