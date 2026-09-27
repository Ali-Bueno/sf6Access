using System;
using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Names the part of the World Tour city the player is in. Three routes, most precise first:
/// the game's own section (<c>CitySectionManager.CurrentSectionId</c> → <c>CitySectionDataUserDataDict</c>),
/// else the region of the nearest fast-travel landmark (<see cref="RegionTracker"/>),
/// else the city. Details: <c>docs/wt-orientation.md</c>.
/// </summary>
public static class ZoneNameService
{
    private const string SECTION_DICT = "CitySectionDataUserDataDict";
    private const string SECTION_RECORD = "CitySectionDataUserDataRecord";
    private const string CITY_DICT = "CityMessageUserDataDict";
    private const string CITY_RECORD = "CityMessageUserDataRecord";

    // Walking into the next district is not a sub-second event; this keeps table and
    // landmark reads off the per-frame path.
    private const long REFRESH_MS = 500;

    private const long EMPTY_RETRY_MS = 5000;

    /// <summary>How far past the record count the section key probe may reach; only a spin guard.</summary>
    private const uint KEY_PROBE_SLACK = 8;

    // A landmark region changes only when the new landmark is nearer by a fifth of the
    // distance to the old one (never less than the arrival band) for two refreshes:
    // at street scale that is tens of metres, far past boundary jitter.
    private const float REGION_MARGIN_RATIO = 0.2f;
    private static readonly RegionTracker _region = new(
        Hooks.WorldTour.MissionBeaconHooks.ARRIVED_M, REGION_MARGIN_RATIO, 2 * REFRESH_MS);

    private static ZoneReading _current;
    private static long _refreshedAt;
    private static ZoneReading _lastSection;

    /// <summary>True once the game's own section route has named a place in this city.</summary>
    public static bool SectionsSeen => _lastSection.Ok;

    /// <summary>The current reading, recomputed at most every <see cref="REFRESH_MS"/>.</summary>
    public static ZoneReading Current()
    {
        long now = Environment.TickCount64;
        if (_refreshedAt != 0 && now - _refreshedAt < REFRESH_MS) return _current;
        _refreshedAt = now;
        _current = Resolve(now);
        return _current;
    }

    public static void Reset()
    {
        _current = default;
        _lastSection = default;
        _refreshedAt = 0;
        _sectionNames.Clear();
        _sectionMisses.Clear();
        _sectionsBuilt = false;
        _sectionsAt = 0;
        _region.Reset();
        FastTravelCatalog.Reset();
        _cityNameId = 0;
        _cityName = null;
    }

    private static ZoneReading Resolve(long now)
    {
        uint sectionId = WorldTourStateService.CurrentSectionId;
        string section = SectionName(sectionId);
        if (!string.IsNullOrEmpty(section))
            return _lastSection = new ZoneReading(section, ZoneSource.Section, 0f, sectionId);

        // Once sections work in this city, a gap between two of them (or an unnamed section)
        // keeps the last name rather than flipping to a landmark region and back. It is
        // held over, not current: id 0 tells ZoneHooks never to announce it again.
        if (_lastSection.Ok) return new ZoneReading(_lastSection.Name, ZoneSource.Section, 0f);

        var region = Region(now);
        if (region.Ok) return region;

        string city = CityName(WorldTourStateService.CityId);
        return string.IsNullOrEmpty(city) ? default : new ZoneReading(city, ZoneSource.City, 0f);
    }

    // ---------- the landmark region and the nearest landmark ----------

    private static readonly List<RegionTracker.Landmark> _landmarks = new();

    private static ZoneReading Region(long now)
    {
        var player = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());
        if (!player.ok) return default;

        _landmarks.Clear();
        foreach (var p in FastTravelCatalog.Points())
            _landmarks.Add(new RegionTracker.Landmark(p.Name, p.X, p.Z));
        _region.Update(_landmarks, player.x, player.z, now);
        return _region.Current == null ? default : new ZoneReading(_region.Current, ZoneSource.Region, 0f);
    }

    /// <summary>The nearest fast-travel landmark on the ground plane, for "where am I".</summary>
    public static bool NearestLandmark(float px, float pz, out FastTravelCatalog.Point point, out float distance)
    {
        point = default;
        distance = float.MaxValue;
        foreach (var p in FastTravelCatalog.Points())
        {
            float dx = p.X - px, dz = p.Z - pz;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d < distance) { distance = d; point = p; }
        }
        return point.Name != null;
    }

    // ---------- the game's section ----------

    // Section id -> localized name. Successes only; a miss is retried on EMPTY_RETRY_MS so
    // one unlucky read during the city load does not silence a district for the session.
    private static readonly Dictionary<uint, string> _sectionNames = new();
    private static readonly Dictionary<uint, long> _sectionMisses = new();
    private static uint _sectionCity;
    private static bool _sectionsBuilt;
    private static long _sectionsAt;

    /// <summary>The localized name of a section id, or null (0 means "no section").</summary>
    public static string SectionName(uint sectionId)
    {
        if (sectionId == 0) return null;

        uint city = WorldTourStateService.CityId;
        if (city != _sectionCity)
        {
            _sectionNames.Clear();
            _sectionMisses.Clear();
            _sectionCity = city;
            _sectionsBuilt = false;
            _sectionsAt = 0;
        }

        if (!_sectionsBuilt) BuildSectionNames();
        if (_sectionNames.TryGetValue(sectionId, out string name)) return name;

        // Not found by enumeration: ask the table by key, trusting it only when the record's
        // own id agrees (the dictionary is keyed by ManageId). A miss is remembered for
        // EMPTY_RETRY_MS so an unnamed section is not re-queried on every refresh.
        long now = Environment.TickCount64;
        if (_sectionMisses.TryGetValue(sectionId, out long missAt) && now - missAt < EMPTY_RETRY_MS) return null;
        name = KeyedName(sectionId);
        if (string.IsNullOrEmpty(name)) { _sectionMisses[sectionId] = now; return null; }
        _sectionNames[sectionId] = name;
        return name;
    }

    private static string KeyedName(uint sectionId)
    {
        var dict = FlowHelper.GetTableDict(SECTION_DICT);
        bool present;
        try { present = Convert.ToBoolean(FlowHelper.Call(dict, "ContainsKey", sectionId)); }
        catch { return null; }
        if (!present) return null;
        var record = FlowHelper.GetTableRecord(SECTION_DICT, SECTION_RECORD, sectionId);
        return record == null || FlowHelper.ReadUIntField(record, "id") != sectionId ? null : RecordName(record);
    }

    /// <summary>Reads every record once per city and files it under its own id. Keys are
    /// probed upward with <c>ContainsKey</c> (no safe IL2CPP dictionary walk exists here)
    /// and the scan stops once <c>get_Count</c> records are found.</summary>
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
            uint id = record == null ? 0 : FlowHelper.ReadUIntField(record, "id");
            string name = id == 0 ? null : RecordName(record);
            if (!string.IsNullOrEmpty(name)) _sectionNames[id] = name;
        }

        _sectionsBuilt = _sectionNames.Count > 0;
        API.LogInfo($"[SF6Access] Zone: {SECTION_DICT} enumerated - {_sectionNames.Count} named sections " +
                    $"from {found} of {total} records" +
                    (found < total ? $" (stopped at key {key} of a {ceiling} ceiling)" : ""));
    }

    private static string RecordName(ManagedObject record) =>
        FlowHelper.CleanTags(FlowHelper.ResolveGuidField(
            AvatarFieldReader.GetProp(record, "SectionNameID"), "GUID"))?.Trim();

    // ---------- the city ----------

    private static uint _cityNameId;
    private static string _cityName;
    private static long _cityNameAt;

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
        _cityName = Message(record, "CityName") ?? Message(record, "CityUIName");
        if (_cityName != null || firstAttempt)
            API.LogInfo($"[SF6Access] Zone: city {cityId} -> {(_cityName == null ? "no name" : $"'{_cityName}'")}");
        return _cityName;
    }

    private static string Message(ManagedObject record, string fieldName) =>
        FlowHelper.CleanTags(FlowHelper.ResolveGuidField(
            AvatarFieldReader.GetProp(record, fieldName), "GUID"))?.Trim();
}
