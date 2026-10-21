using System.Collections.Generic;
using SF6Access.Hooks.WorldTour;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// The on-demand "where am I": the zone, the nearest landmark with its direction and
/// distance, which way the camera faces, and where the current destination lies —
/// "In Beat Square. Urban Park at 3 o'clock, 80 meters. Facing north. Destination: …".
/// </summary>
public static class WhereAmIReadout
{
    public static string Compose(ZoneReading zone)
    {
        var parts = new List<string> { ZonePhrase(zone) };
        var me = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());

        Destination? local = null;
        if (me.ok)
        {
            float nearest = 3f * MissionBeaconHooks.ARRIVED_M; // Same nearby band as passing notices.
            foreach (var place in PlaceCache.Places())
            {
                var d = place.Destination;
                if (d.Action != DestinationAction.Go || d.Category is not
                    (DestinationCategory.Rest or DestinationCategory.Places or DestinationCategory.Travel or DestinationCategory.Shops)) continue;
                float meters = d.FlatDistanceTo(me.x, me.z);
                if (meters >= nearest) continue;
                nearest = meters; local = d;
            }
        }
        if (local is { } nearby)
            parts.Add(nearby.Label + ", " + DestinationText.Where(me.x, me.y, me.z, nearby.X, nearby.Y, nearby.Z));
        if (me.ok && ZoneNameService.NearestLandmark(me.x, me.z, out var landmark, out float landmarkM)
            && landmark.Name != local?.Name)
            parts.Add(landmark.Name + ", " + DestinationText.Where(me.x, me.y, me.z, landmark.X, landmark.Y, landmark.Z));

        if (me.ok)
        {
            Destination? rest = null;
            float nearest = float.MaxValue;
            foreach (var place in PlaceCache.Places())
            {
                var d = place.Destination;
                if (d.Category != DestinationCategory.Rest || !d.HasPosition) continue;
                float meters = d.FlatDistanceTo(me.x, me.z);
                if (meters >= nearest) continue;
                rest = d; nearest = meters;
            }
            if (rest is { } shelter && shelter != local
                && (MapSelectionMemory.Exploring || MapSelectionMemory.Current.Label != shelter.Label))
                parts.Add(shelter.Label + ", " + DestinationText.Where(me.x, me.y, me.z, shelter.X, shelter.Y, shelter.Z));
        }

        string facing = FieldHeadingHooks.CurrentFacing();
        if (facing != null) parts.Add(LocalizedText.Facing(facing));

        string interaction = FieldAwarenessHooks.CurrentInteraction();
        if (!string.IsNullOrEmpty(interaction)) parts.Add(interaction);

        if (CityListeningMode.Active)
        {
            parts.Add(LangFile.Get("wt.dest.city_status", "City listening: straight-line locations"));
            if (FieldAimHooks.TryCityFocus(out var focus, out string focusKey)
                && focus != local)
                parts.Add(focus.Label + ", " + DestinationText.Where(me.x, me.y, me.z, focus.X, focus.Y, focus.Z));
            return string.Join(". ", parts);
        }

        if (MapSelectionMemory.Exploring)
        {
            ExplorationRoutes.Update();
            var hours = new HashSet<int>();
            foreach (var choice in ExplorationRoutes.Current())
            {
                if (!hours.Add(choice.Hour)) continue;
                parts.Add(ExplorationRoutes.Describe(choice));
                if (hours.Count == 3) break; // Keep an on-demand junction overview short enough to remember.
            }
            if (hours.Count == 0) parts.Add(ExplorationText.Get("paths_unconfirmed", "No nearby walking routes checked yet"));
            return string.Join(". ", parts);
        }

        var dest = BeaconDestination.Resolve();
        if (dest.TargetLost)
            parts.Add(RouteNotice.Text("target-lost", null, dest, 0, 0));
        else if (me.ok && dest.Ok)
            parts.Add(DestinationText.Chosen(dest.Word,
                DestinationText.Where(me.x, me.y, me.z, dest.TargetX, dest.TargetY, dest.TargetZ)));

        return string.Join(". ", parts);
    }

    private static string ZonePhrase(ZoneReading zone) => zone.Source switch
    {
        ZoneSource.Section or ZoneSource.City => LocalizedText.ZoneHere(zone.Name),
        ZoneSource.Region => LocalizedText.RegionOf(zone.Name),
        _ => LocalizedText.ZoneUnknown(),
    };
}
