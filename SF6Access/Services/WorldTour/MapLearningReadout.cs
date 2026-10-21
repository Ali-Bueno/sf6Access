using System;
using System.Collections.Generic;

namespace SF6Access.Services.WorldTour;

/// <summary>Stable landmark references for deliberate destination browsing, independent of camera turns.</summary>
internal static class MapLearningReadout
{
    internal static string Reference(Destination destination)
    {
        if (!destination.HasPosition || destination.Action != DestinationAction.Go
            || destination.Actor.IsValid || destination.Category is not
                (DestinationCategory.Rest or DestinationCategory.Places or DestinationCategory.Shops
                or DestinationCategory.Jobs or DestinationCategory.Pins or DestinationCategory.Saved)) return null;

        var landmarks = new List<RegionTracker.Landmark>();
        foreach (var point in FastTravelCatalog.Points())
            if (!point.IsMyRoom) landmarks.Add(new(point.Name, point.X, point.Z));
        var relation = MapLandmarkRelation.Nearest(landmarks, destination.X, destination.Z);
        if (relation is not { } reference) return null;

        int meters = (int)MathF.Round(reference.Meters);
        // Do not teach map bearings from the provisional north used before the minimap binds.
        if (FieldHeadingService.GetNorth().Source == FieldHeadingService.NorthSource.MiniMap)
        {
            float heading = FieldHeadingService.HeadingDegrees(
                new FieldDirectionService.FlatDir(reference.X, reference.Z, true));
            if (float.IsFinite(heading))
                return string.Format(LangFile.Get("wt.dest.reference_direction",
                    "Reference: {0}. This destination is {1}, {2} meters away"), reference.Name,
                    FieldHeadingService.SectorName(FieldHeadingService.Sector(heading)), meters);
        }
        return string.Format(LangFile.Get("wt.dest.reference_distance",
            "Reference: {0}, {1} meters from this destination"), reference.Name, meters);
    }
}
