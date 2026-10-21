using SF6Access.Services.WorldTour;

internal static class TravelDestinationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var point = new Destination("Beat Square", "fast travel", DestinationCategory.Travel, 10, 1, 20);
        var rememberedMapPoint = point with { X = 11 };
        var landmark = point with { Kind = null, Category = DestinationCategory.Places };
        var shop = new Destination("Shop", "shop", DestinationCategory.Shops, 30, 0, 40);
        var groups = DestinationPicker.Build(new[] { point, rememberedMapPoint, landmark, shop }, 0, 0, 8);
        check(groups.Count == 3 && groups[1].Category == DestinationCategory.Travel,
            "fast travel has its own picker group between places and facilities");
        check(groups[1].Items.Count == 1 && groups[1].Items[0] == point,
            "live and remembered map versions of one travel point merge without losing its kind");
        check(groups[0].Items[0] == landmark && groups[1].Items[0].Label == "Beat Square, fast travel",
            "a landmark and travel point with the same name retain different semantic roles");

        var picker = new DestinationPicker();
        picker.Load(groups, DestinationCategory.Travel);
        check(picker.Current.Action == DestinationAction.Go && picker.Current.HasPosition,
            "choosing a travel point is walking guidance, never an automatic travel action");
        picker.MoveCategory(1);
        check(picker.Current == shop, "the travel category preserves normal next-category navigation");
        picker.MoveCategory(-1);
        check(picker.Current == point, "returning to travel preserves the selected point");
    }
}
