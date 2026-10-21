#nullable enable
using System;
using System.Collections.Generic;

namespace SF6Access.Services.WorldTour;

/// <summary>A place's position relative to a fixed, named map landmark.</summary>
internal static class MapLandmarkRelation
{
    internal readonly record struct Relation(string Name, float Meters, float X, float Z);

    internal static Relation? Nearest(IEnumerable<RegionTracker.Landmark> landmarks, float x, float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(z)) return null;
        Relation? nearest = null;
        foreach (var landmark in landmarks)
        {
            if (string.IsNullOrWhiteSpace(landmark.Name)
                || !float.IsFinite(landmark.X) || !float.IsFinite(landmark.Z)) continue;
            float dx = x - landmark.X, dz = z - landmark.Z;
            float meters = MathF.Sqrt(dx * dx + dz * dz);
            if (!float.IsFinite(meters)) continue;
            if (nearest is { } previous && meters >= previous.Meters) continue;
            nearest = new Relation(landmark.Name, meters, dx, dz);
        }
        return nearest;
    }
}
