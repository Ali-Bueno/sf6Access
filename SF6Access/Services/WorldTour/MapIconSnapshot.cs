using System.Collections.Generic;
using System.IO;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Everything the phone map shows (shops, masters, jobs, missions, pins, fast-travel
/// points), captured whenever the map is open and kept on disk per city, so the
/// destination picker can offer it later from anywhere in the field.
/// Source: <c>UIDeviceMapWindow.GetMapIconPanelList(ICON_GROUP_TYPE)</c> → each panel's
/// <c>Info</c> (<c>TitleText</c>, <c>Type</c>, <c>GlobalPos</c>) — the same record the
/// map-cursor reader already reads in game.
/// </summary>
public static class MapIconSnapshot
{
    private const string GROUP_ENUM = "app.UIMapWindowBase.ICON_GROUP_TYPE";
    private const string FILE_NAME = "sf6access_map_places.txt";

    /// <summary>Icon groups captured, by the game's member names, with the picker category each feeds.</summary>
    private static readonly (string Name, DestinationCategory Category, bool Persist)[] GROUPS =
    {
        ("TRAVEL", DestinationCategory.Places, true),
        ("FACILITY", DestinationCategory.Shops, true),
        ("JOB", DestinationCategory.Jobs, true),
        // Missions come and go with progress: offered this session, never written to disk.
        ("MISSION", DestinationCategory.Missions, false),
        ("MASTER", DestinationCategory.Masters, true),
        ("PIN", DestinationCategory.Pins, true),
    };

    // The map stays open for seconds; its icon set does not change within one capture interval.
    // The first capture also waits this long after the map opens, while its panels are built.
    private const long CAPTURE_INTERVAL_MS = 2000;
    private static ulong _mapAddress;

    private static int[] _groupValues;
    private static Dictionary<uint, List<PlaceFile.Entry>> _byCity;
    private static long _capturedAt;
    private static string _lastSummary;

    /// <summary>The picker category for a stored entry's icon group, or null if not captured.</summary>
    public static DestinationCategory? CategoryOf(int group)
    {
        ResolveGroups();
        for (int i = 0; i < GROUPS.Length; i++)
            if (_groupValues[i] == group) return GROUPS[i].Category;
        return null;
    }

    /// <summary>What the map last showed for <paramref name="city"/> (this session or a previous one).</summary>
    public static IReadOnlyList<PlaceFile.Entry> For(uint city)
    {
        Load();
        return _byCity.TryGetValue(city, out var list) ? list : (IReadOnlyList<PlaceFile.Entry>)System.Array.Empty<PlaceFile.Entry>();
    }

    /// <summary>Whether a capture is due; lets the caller skip its own checks between captures.</summary>
    public static bool Due => System.Environment.TickCount64 - _capturedAt >= CAPTURE_INTERVAL_MS;

    /// <summary>Reads the open map's icons; called from the map reader's poll.</summary>
    public static void Capture(ManagedObject mapParam)
    {
        long now = System.Environment.TickCount64;
        if (mapParam == null || !Due) return;
        _capturedAt = now;
        ulong address = mapParam.GetAddress();
        if (address != _mapAddress)
        {
            // A newly opened map: its panels are still being built, capture next time.
            _mapAddress = address;
            return;
        }

        uint city = FlowHelper.ReadUIntField(mapParam, "DispCityId");
        if (city == 0) city = WorldTourStateService.CityId;
        var window = FlowHelper.GetObjectField(mapParam, "DeviceMap");
        if (city == 0 || window == null) return;

        ResolveGroups();
        var entries = new List<PlaceFile.Entry>();
        var counts = new List<string>();
        for (int g = 0; g < GROUPS.Length; g++)
        {
            if (_groupValues[g] < 0) continue;
            int before = entries.Count;
            ReadGroup(window, city, _groupValues[g], entries);
            counts.Add($"{GROUPS[g].Name}={entries.Count - before}");
        }
        if (entries.Count == 0) return;

        Load();
        _byCity[city] = Merge(_byCity.TryGetValue(city, out var old) ? old : null, entries);
        string summary = $"city {city}: " + string.Join(", ", counts);
        if (summary == _lastSummary) return;
        _lastSummary = summary;
        bool saved = PlaceFile.Save(FilePath, Persisted());
        API.LogInfo($"[SF6Access] Map places captured - {summary}{(saved ? "" : " (not saved to disk)")}");
    }

    private static void ReadGroup(ManagedObject window, uint city, int group, List<PlaceFile.Entry> into)
    {
        var panels = FlowHelper.Call(window, "GetMapIconPanelList", group) as ManagedObject;
        int n = FlowHelper.GetListCount(panels);
        for (int i = 0; i < n; i++)
        {
            var info = FlowHelper.GetObjectField(FlowHelper.GetListItem(panels, i), "Info");
            string title = Flatten(FlowHelper.ReadStringField(info, "TitleText"));
            object pos = info == null ? null : FieldProbeService.Member(info, "GlobalPos");
            if (string.IsNullOrEmpty(title) || pos == null) continue;

            float x = FlowHelper.ReadVecComponent(pos, "x");
            float y = FlowHelper.ReadVecComponent(pos, "y");
            float z = FlowHelper.ReadVecComponent(pos, "z");
            // An exact origin is a failed read, not a place (same rule as the map selection).
            if (!float.IsFinite(x) || x == 0f && y == 0f && z == 0f) continue;
            var entry = new PlaceFile.Entry(city, group, FlowHelper.ReadIntField(info, "Type", -1), title, x, y, z);
            if (!into.Contains(entry)) into.Add(entry);
        }
    }

    /// <summary>Groups read this time replace their old entries; a group that came back
    /// empty keeps what an earlier capture learned (its panels may not be built yet).</summary>
    private static List<PlaceFile.Entry> Merge(List<PlaceFile.Entry> old, List<PlaceFile.Entry> fresh)
    {
        var freshGroups = new HashSet<int>();
        foreach (var e in fresh) freshGroups.Add(e.Group);
        var merged = new List<PlaceFile.Entry>(fresh);
        if (old != null)
            foreach (var e in old)
                if (!freshGroups.Contains(e.Group)) merged.Add(e);
        return merged;
    }

    private static IEnumerable<PlaceFile.Entry> Persisted()
    {
        foreach (var list in _byCity.Values)
            foreach (var e in list)
                if (IsPersisted(e.Group)) yield return e;
    }

    private static bool IsPersisted(int group)
    {
        for (int i = 0; i < GROUPS.Length; i++)
            if (_groupValues[i] == group) return GROUPS[i].Persist;
        return false;
    }

    private static void ResolveGroups()
    {
        if (_groupValues != null) return;
        _groupValues = new int[GROUPS.Length];
        for (int i = 0; i < GROUPS.Length; i++)
            _groupValues[i] = FieldProbeService.EnumValue(GROUP_ENUM, GROUPS[i].Name);
        API.LogInfo($"[SF6Access] Map places: {GROUP_ENUM} = {string.Join(", ", _groupValues)}");
    }

    private static void Load()
    {
        if (_byCity != null) return;
        _byCity = new Dictionary<uint, List<PlaceFile.Entry>>();
        foreach (var e in PlaceFile.Load(FilePath))
        {
            if (!_byCity.TryGetValue(e.City, out var list)) _byCity[e.City] = list = new List<PlaceFile.Entry>();
            list.Add(e);
        }
    }

    private static string FilePath => Path.Combine(ObjectDumper.DumpDir, FILE_NAME);

    private static string Flatten(string text) =>
        string.IsNullOrEmpty(text) ? null : System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
}
