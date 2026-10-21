using System.Collections.Generic;

namespace SF6Access.Services.WorldTour;

/// <summary>Native semantic flags take precedence over the map's broad icon group.</summary>
internal static class DestinationCategoryRules
{
    internal static DestinationCategory FastTravel(bool isMyRoom) =>
        isMyRoom ? DestinationCategory.Rest : DestinationCategory.Travel;

    internal static bool IsRestIcon(string iconType) => iconType == "HOME";

    internal static DestinationCategory? MapIcon(string iconType, DestinationCategory? groupCategory) =>
        IsRestIcon(iconType) ? DestinationCategory.Rest : groupCategory;

    internal static bool RepeatsKnownRest(Destination candidate, IEnumerable<Destination> known, float mergeM)
    {
        if (candidate.Category != DestinationCategory.Travel || !candidate.HasPosition
            || !float.IsFinite(mergeM) || mergeM < 0) return false;
        foreach (var point in known)
        {
            if (point.Category != DestinationCategory.Rest || point.Name != candidate.Name || !point.HasPosition) continue;
            float dx = point.X - candidate.X, dy = point.Y - candidate.Y, dz = point.Z - candidate.Z;
            if (dx * dx + dy * dy + dz * dz <= mergeM * mergeM) return true;
        }
        return false;
    }
}
