using SF6Access.Services.WorldTour;

internal static class DestinationWorkflowChecks
{
    public static void Run(Action<bool, string> check)
    {
        var mission = new Destination("Tracked mission", null, DestinationCategory.Mission,
            100, 0, 100, DestinationAction.FollowMission);
        var explore = new Destination("Explore", null, DestinationCategory.Mission,
            0, 0, 0, DestinationAction.Explore);
        var shop = new Destination("Shop", null, DestinationCategory.Shops, 40, 0, 0);
        var nearerShop = new Destination("Other shop", null, DestinationCategory.Shops, 10, 0, 0);
        var places = new[] { shop, explore, mission, nearerShop };
        var groups = DestinationPicker.Build(places, 0, 0, 8);
        var picker = new DestinationPicker();
        picker.Load(groups, DestinationCategory.Mission);
        check(picker.Current.Action == DestinationAction.FollowMission,
            "guidance starts with the tracked mission even when exploration has no position");
        picker.MoveItem(1);
        check(picker.Current.Action == DestinationAction.Explore && !picker.Current.HasPosition,
            "free exploration is selectable without pretending to be a world point");

        picker.MoveCategory(1);
        check(picker.Current == nearerShop, "a newly visited category still starts nearest first");
        picker.MoveItem(1);
        picker.Load(DestinationPicker.Build(places, 50, 0, 8), DestinationCategory.Shops);
        check(picker.Current == shop && picker.Index == 0,
            "reopening after walking preserves the chosen identity across distance reordering");
        picker.MoveCategory(-1);
        check(picker.Current.Action == DestinationAction.Explore,
            "returning to guidance preserves the previously browsed action");
        picker.MoveCategory(1);
        check(picker.Current == shop, "returning to a category restores its own previous item");

        picker.Load(DestinationPicker.Build(new[] { nearerShop, mission, explore }, 0, 0, 8), DestinationCategory.Shops);
        check(picker.Current == nearerShop, "a removed destination falls back to the available nearest entry");

        var unknownMission = mission with { X = 0, Y = 0, Z = 0 };
        picker.Load(DestinationPicker.Build(new[] { unknownMission, explore }, 0, 0, 8), DestinationCategory.Mission);
        picker.MoveItem(-1);
        check(picker.Current.Action == DestinationAction.FollowMission && !picker.Current.HasPosition,
            "following the tracked mission remains available when no target has streamed in");

        picker.Reset();
        picker.Load(groups, DestinationCategory.Mission);
        check(picker.Current.Action == DestinationAction.FollowMission,
            "changing city clears the remembered exploration cursor");
        check(groups.Count == 2, "clearing picker memory does not mutate a caller's catalog");

        foreach (var invalid in new[]
        {
            shop with { X = float.NaN }, shop with { Y = float.PositiveInfinity },
            shop with { Z = float.NegativeInfinity }, shop with { X = 0, Y = 0, Z = 0 },
        })
        {
            check(!invalid.HasPosition && DestinationPicker.Build(new[] { invalid }, 0, 0, 8).Count == 0,
                "invalid destination coordinates cannot enter the picker or guidance");
        }
    }
}
