using SF6Access.Services.WorldTour;

internal static class CityListeningModeChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(!CityListeningMode.Active, "citywide listening is quiet by default");
        var listen = new Destination("Listen across the city", null, DestinationCategory.Mission,
            0, 0, 0, DestinationAction.ListenCity);
        var explore = listen with { Name = "Explore freely", Action = DestinationAction.Explore };
        var mission = listen with { Name = "Follow mission", Action = DestinationAction.FollowMission };
        var shop = new Destination("Shop", null, DestinationCategory.Shops, 100, 0, 1);
        var entries = new[] { mission, explore, listen, shop };
        var groups = DestinationPicker.Build(entries, 0, 0, 8);
        var guidance = groups.Single(g => g.Category == DestinationCategory.Mission);
        check(guidance.Items.Count == 3 && guidance.Items.Any(d => d.Action == DestinationAction.ListenCity),
            "city listening remains selectable even before any destinations have known coordinates");
        check(!listen.HasPosition && !(listen with { X = 25, Y = 2, Z = 10 }).HasPosition,
            "the city listening action can never become a walking destination");
        check(CityPlaceDirectory.ByName(groups).SequenceEqual(new[] { shop }),
            "guidance mode actions do not enter the named city directory or its location cues");

        var picker = new DestinationPicker();
        picker.Load(groups, DestinationCategory.Mission);
        for (int i = 0; i < picker.Count && picker.Current.Action != DestinationAction.ListenCity; i++) picker.MoveItem(1);
        check(picker.Current.Action == DestinationAction.ListenCity && !CityListeningMode.Active,
            "merely browsing the city listening entry does not enable global sounds");
        CityListeningMode.Enter();
        try
        {
            picker.MoveCategory(1);
            picker.MoveCategory(-1);
            check(CityListeningMode.Active && picker.Current.Action == DestinationAction.ListenCity,
                "explicit city listening and its menu cursor survive browsing another category");
            picker.Load(DestinationPicker.Build(entries, 500, 0, 8), DestinationCategory.Mission);
            check(picker.Current.Action == DestinationAction.ListenCity,
                "reopening after walking preserves the city listening action independently of nearest-place order");
        }
        finally { CityListeningMode.Exit(); }
        check(!CityListeningMode.Active, "exiting city listening restores ordinary nearby exploration");
    }
}
