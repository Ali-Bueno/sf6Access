using System;
using System.Collections.Generic;
using REFrameworkNET;
using static SF6Access.Services.WorldTour.LevelRoutePlanner;

namespace SF6Access.Services.WorldTour;

/// <summary>One log line naming the world objects between two points the navmesh does not connect, so the obstacle
/// behind a blocked ladder leg (Chapter 2-2's sign) can be identified from the next test's log.</summary>
internal static class ObstacleProbe
{
    /// <summary>Base class of every World Tour gimmick object (ladders, doors, breakables, signs).</summary>
    private const string CityObjectType = "app.worldtour.om.WTCityOM";
    private const float CorridorM = 3f;   // Either side of the straight walk: about a street sign's width.
    private const int Shown = 12;

    internal static void Log(Spot from, Spot to)
    {
        var found = new List<(float along, string text)>();
        int count = 0;
        try
        {
            var list = FieldProbeService.SceneComponents(CityObjectType);
            count = FlowHelper.GetListCount(list);
            for (int i = 0; i < count; i++)
            {
                var om = FlowHelper.GetListItem(list, i);
                var go = FlowHelper.Call(om, "get_GameObject") as ManagedObject;
                var transform = FlowHelper.Call(go, "get_Transform");
                if (!FieldAccessCatalog.Vector(transform, "Position", out float x, out float y, out float z)) continue;
                var at = new Spot(x, y, z);
                float along = Along(from, to, at, out float aside);
                if (aside > CorridorM || y < MathF.Min(from.Y, to.Y) - LevelM || y > MathF.Max(from.Y, to.Y) + 2 * LevelM) continue;
                string type = om.GetTypeDefinition()?.GetFullName() ?? "?";
                string name = FlowHelper.Call(go, "get_Name") as string ?? "?";
                found.Add((along, FormattableString.Invariant($"{type} '{name}' ({x:F1}, {y:F1}, {z:F1})")));
            }
        }
        catch (Exception ex) { found.Add((0f, "read failed: " + ex.GetType().Name)); }
        found.Sort((a, b) => a.along.CompareTo(b.along));
        var shown = found.ConvertAll(f => f.text);
        if (shown.Count > Shown) shown.RemoveRange(Shown, shown.Count - Shown);
        API.LogInfo(FormattableString.Invariant(
            $"[SF6Access] Level route: blocked walk ({from.X:F1}, {from.Y:F1}, {from.Z:F1}) -> ({to.X:F1}, {to.Y:F1}, {to.Z:F1}); ") +
            $"{found.Count} of {count} city objects within {CorridorM:0} m: {(shown.Count == 0 ? "none" : string.Join("; ", shown))}");
    }

    /// <summary>How far along the flat segment the point projects (clamped), and how far it stands from it.</summary>
    private static float Along(Spot a, Spot b, Spot p, out float aside)
    {
        float dx = b.X - a.X, dz = b.Z - a.Z, len2 = dx * dx + dz * dz;
        float t = len2 <= 0f ? 0f : Math.Clamp(((p.X - a.X) * dx + (p.Z - a.Z) * dz) / len2, 0f, 1f);
        float cx = a.X + t * dx, cz = a.Z + t * dz;
        aside = MathF.Sqrt((p.X - cx) * (p.X - cx) + (p.Z - cz) * (p.Z - cz));
        return t;
    }
}
