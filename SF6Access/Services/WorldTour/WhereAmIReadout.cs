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

        if (me.ok && ZoneNameService.NearestLandmark(me.x, me.z, out var landmark, out float landmarkM))
            parts.Add(landmark.Name + ", " + DestinationText.Where(me.x, me.y, me.z, landmark.X, landmark.Y, landmark.Z));

        string facing = FieldHeadingHooks.CurrentFacing();
        if (facing != null) parts.Add(LocalizedText.Facing(facing));

        var dest = BeaconDestination.Resolve();
        if (me.ok && dest.Ok)
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
