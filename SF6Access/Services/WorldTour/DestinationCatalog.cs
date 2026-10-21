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
    internal static float MergeM => 2f * MissionBeaconHooks.ARRIVED_M;

    /// <param name="people">False leaves out the crowd (avatars around the player and the
    /// registry's plain NPCs): the places-only view the look sweep and "passing by" use.</param>
    public static List<DestinationPicker.Group> Build(float px, float pz, bool people = true)
    {
        var all = new List<Destination>();

        var mission = MissionTargetService.Find();
        all.Add(new Destination(DestinationText.MissionEntry(), null, DestinationCategory.Mission,
            mission.Ok ? mission.X : 0f, mission.Ok ? mission.Y : 0f, mission.Ok ? mission.Z : 0f,
            DestinationAction.FollowMission));
        all.Add(new Destination(DestinationText.ExploreEntry(), null, DestinationCategory.Mission,
            0f, 0f, 0f, DestinationAction.Explore));
        all.Add(new Destination(DestinationText.ListenCityEntry(), null, DestinationCategory.Mission,
            0f, 0f, 0f, DestinationAction.ListenCity));

        foreach (var p in FastTravelCatalog.Points())
            all.Add(new Destination(p.Name, p.IsMyRoom ? DestinationText.RestKind() : DestinationText.FastTravelKind(),
                DestinationCategoryRules.FastTravel(p.IsMyRoom), p.X, p.Y, p.Z));

        AddAccesses(all);
        if (people) AddNearbyAvatars(all);
        AccessTargetCatalog.AddTo(all, people);

        uint city = WorldTourStateService.CityId;
        foreach (var e in MapIconSnapshot.For(city))
        {
            var category = MapIconSnapshot.CategoryOf(e.Group, e.Type);
            if (category == null) continue;
            string kind = category == DestinationCategory.Rest ? DestinationText.RestKind()
                : category == DestinationCategory.Travel ? DestinationText.FastTravelKind()
                : category == DestinationCategory.Shops ? DeviceMapText.IconType(e.Type) : null;
            var remembered = new Destination(e.Name, kind, category.Value, e.X, e.Y, e.Z);
            if (!DestinationCategoryRules.RepeatsKnownRest(remembered, all, MergeM)) all.Add(remembered);
        }

        all.Add(new Destination(DestinationText.SaveHereEntry(), null, DestinationCategory.Saved,
                                0f, 0f, 0f, DestinationAction.SaveHere));
        foreach (var s in SavedPlaces.For(city))
            all.Add(new Destination(s.Name, null, DestinationCategory.Saved, s.X, s.Y, s.Z));

        var groups = DestinationPicker.Build(all, px, pz, MergeM);
        if (people)
        {
            var named = CityPlaceDirectory.ByName(groups);
            if (named.Count > 0) groups.Insert(1, new(DestinationCategory.KnownPlaces, named));
        }
        return groups;
    }

    /// <summary>Ladders and doors around the player (the loaded scene only, within the beacons' range).</summary>
    private static void AddAccesses(List<Destination> into)
    {
        var me = FieldPresenceService.PlayerPos;
        foreach (var a in FieldAccessCatalog.Nearby(me.X, me.Y, me.Z, System.Environment.TickCount64))
            into.Add(new Destination(a.Text, null, DestinationCategory.Access, a.X, a.Y, a.Z));
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
            into.Add(new Destination(name, null, category, x, y, z, Actor: LiveDestinationReader.ForAvatar(other)));
        }
    }
}
