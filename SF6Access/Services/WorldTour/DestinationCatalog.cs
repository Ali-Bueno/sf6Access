using System.Collections.Generic;
using SF6Access.Hooks.WorldTour;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Gathers every place the player can be guided to, live sources first (story objective,
/// fast-travel points, avatars around the player, the game's map registry), then what the
/// phone map showed last and the player's saved spots.
/// </summary>
public static class DestinationCatalog
{
    /// <summary>Two sources naming the same thing this close together are one place.</summary>
    private static float MergeM => 2f * MissionBeaconHooks.ARRIVED_M;

    public static List<DestinationPicker.Group> Build(float px, float pz)
    {
        var all = new List<Destination>();

        var mission = MissionTargetService.Find();
        if (mission.Ok)
            all.Add(new Destination(DestinationText.MissionEntry(), null, DestinationCategory.Mission,
                                    mission.X, mission.Y, mission.Z, DestinationAction.FollowMission));

        foreach (var p in FastTravelCatalog.Points())
            all.Add(new Destination(p.Name, null, DestinationCategory.Places, p.X, p.Y, p.Z));

        AddNearbyAvatars(all);
        AccessTargetCatalog.AddTo(all);

        uint city = WorldTourStateService.CityId;
        foreach (var e in MapIconSnapshot.For(city))
        {
            var category = MapIconSnapshot.CategoryOf(e.Group);
            if (category == null) continue;
            string kind = category == DestinationCategory.Shops ? DeviceMapText.IconType(e.Type) : null;
            all.Add(new Destination(e.Name, kind, category.Value, e.X, e.Y, e.Z));
        }

        all.Add(new Destination(DestinationText.SaveHereEntry(), null, DestinationCategory.Saved,
                                0f, 0f, 0f, DestinationAction.SaveHere));
        foreach (var s in SavedPlaces.For(city))
            all.Add(new Destination(s.Name, null, DestinationCategory.Saved, s.X, s.Y, s.Z));

        return DestinationPicker.Build(all, px, pz, MergeM);
    }

    /// <summary>The named avatars the N key reads: masters and people near the player.</summary>
    private static void AddNearbyAvatars(List<Destination> into)
    {
        foreach (var other in AvatarFieldReader.ReadOthers(WorldTourStateService.GetAvatarManager()))
        {
            var (name, kind) = AvatarFieldReader.Classify(other.Avatar);
            if (string.IsNullOrEmpty(name)) continue;
            var (x, y, z, ok) = AvatarFieldReader.ReadWorldPos(other.Avatar);
            if (!ok) continue;
            var category = kind == AvatarFieldReader.CONTACT_LEGENDARY
                ? DestinationCategory.Masters : DestinationCategory.People;
            into.Add(new Destination(name, null, category, x, y, z));
        }
    }
}
