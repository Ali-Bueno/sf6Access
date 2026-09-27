#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SF6Access.Services.WorldTour;

/// <summary>A small tab-separated file of named world points, one per line, keyed by city.</summary>
public static class PlaceFile
{
    /// <summary>One stored point. <see cref="Group"/>/<see cref="Type"/> are the map's icon
    /// group and type for map places, and 0 for the player's own saved places.</summary>
    public readonly record struct Entry(uint City, int Group, int Type, string Name, float X, float Y, float Z);

    private const char SEP = '\t';
    private const int FIELDS = 7;

    public static string Format(Entry e)
    {
        var inv = CultureInfo.InvariantCulture;
        return string.Join(SEP, e.City.ToString(inv), e.Group.ToString(inv), e.Type.ToString(inv),
                           Clean(e.Name), e.X.ToString("R", inv), e.Y.ToString("R", inv), e.Z.ToString("R", inv));
    }

    public static bool TryParse(string? line, out Entry entry)
    {
        entry = default;
        var parts = line?.Split(SEP);
        if (parts == null || parts.Length != FIELDS || string.IsNullOrWhiteSpace(parts[3])) return false;
        var inv = CultureInfo.InvariantCulture;
        const NumberStyles F = NumberStyles.Float;
        if (!uint.TryParse(parts[0], NumberStyles.Integer, inv, out uint city)) return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, inv, out int group)) return false;
        if (!int.TryParse(parts[2], NumberStyles.Integer, inv, out int type)) return false;
        if (!float.TryParse(parts[4], F, inv, out float x) || !float.IsFinite(x)) return false;
        if (!float.TryParse(parts[5], F, inv, out float y) || !float.IsFinite(y)) return false;
        if (!float.TryParse(parts[6], F, inv, out float z) || !float.IsFinite(z)) return false;
        entry = new Entry(city, group, type, parts[3], x, y, z);
        return true;
    }

    public static List<Entry> Load(string path)
    {
        var result = new List<Entry>();
        try
        {
            if (!File.Exists(path)) return result;
            foreach (string line in File.ReadAllLines(path))
                if (TryParse(line, out var e)) result.Add(e);
        }
        catch { }
        return result;
    }

    public static bool Save(string path, IEnumerable<Entry> entries)
    {
        try
        {
            var lines = new List<string>();
            foreach (var e in entries) lines.Add(Format(e));
            File.WriteAllLines(path, lines);
            return true;
        }
        catch { return false; }
    }

    private static string Clean(string? name) =>
        (name ?? "").Replace(SEP, ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
