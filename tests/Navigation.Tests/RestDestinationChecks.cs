using SF6Access.Services.WorldTour;

internal static class RestDestinationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(DestinationCategoryRules.FastTravel(true) == DestinationCategory.Rest,
            "the native IsMyRoom flag promotes a city point to the rest category");
        check(DestinationCategoryRules.FastTravel(false) == DestinationCategory.Travel,
            "an ordinary native point retains fast-travel semantics");
        check(DestinationCategoryRules.MapIcon("HOME", DestinationCategory.Travel) == DestinationCategory.Rest
            && DestinationCategoryRules.MapIcon("HOME", DestinationCategory.Shops) == DestinationCategory.Rest,
            "the HOME icon type takes precedence over both travel and facility groups");
        check(DestinationCategoryRules.MapIcon("HOME", null) == DestinationCategory.Rest,
            "an exact native HOME icon needs no invented HOME icon group");
        foreach (string icon in new[] { "HOME_OTHER", "HOMETOWN", "home", "", "INVALID" })
            check(!DestinationCategoryRules.IsRestIcon(icon)
                && DestinationCategoryRules.MapIcon(icon, DestinationCategory.Travel) == DestinationCategory.Travel,
                $"unrelated or unreadable icon names cannot become rest destinations ({icon})");
        check(DestinationCategoryRules.MapIcon("UNKNOWN", null) == null,
            "an unknown icon and group remain unclassified");

        var guidance = new Destination("Explore freely", null, DestinationCategory.Mission, 0, 0, 0, DestinationAction.Explore);
        var nativeRoom = new Destination("Native room name", "change day or night",
            DestinationCategoryRules.FastTravel(true), 500, 1, 600);
        var homeIcon = nativeRoom with { X = 501, Category = DestinationCategoryRules.MapIcon("HOME", DestinationCategory.Shops)!.Value };
        var bus = new Destination("Native bus stop", "fast travel", DestinationCategoryRules.FastTravel(false), 10, 1, 20);
        var namedHomeShop = new Destination("HOME", "shop", DestinationCategory.Shops, 30, 1, 40);
        var rememberedTravel = nativeRoom with { Category = DestinationCategory.Travel, Kind = "fast travel" };
        check(DestinationCategoryRules.RepeatsKnownRest(rememberedTravel, [nativeRoom], 8),
            "a remembered generic travel entry does not duplicate a natively identified hangout");
        check(!DestinationCategoryRules.RepeatsKnownRest(rememberedTravel with { Name = "Different stop" }, [nativeRoom], 8),
            "a distinct nearby travel point is not hidden by a hangout");
        check(!DestinationCategoryRules.RepeatsKnownRest(rememberedTravel with { Y = 30 }, [nativeRoom], 8),
            "a same-name travel point on another floor is not merged into the hangout");
        check(!DestinationCategoryRules.RepeatsKnownRest(rememberedTravel with { X = 900 }, [nativeRoom], 8),
            "a remote same-name stop remains a separate destination");
        check(!DestinationCategoryRules.RepeatsKnownRest(namedHomeShop, [nativeRoom], 8)
            && !DestinationCategoryRules.RepeatsKnownRest(bus, [rememberedTravel], 8),
            "rest deduplication does not rewrite shops or ordinary travel groups");
        var groups = DestinationPicker.Build([bus, namedHomeShop, nativeRoom, homeIcon, guidance], 0, 0, 8);
        check(groups.Count == 4 && groups[0].Category == DestinationCategory.Mission
            && groups[1].Category == DestinationCategory.Rest, "rest destinations follow guidance before other picker categories");
        check(groups[1].Items.Count == 1 && groups[1].Items[0] == nativeRoom,
            "a remembered HOME icon merges into the live native rest destination without replacing its coordinates");
        check(groups[1].Items[0].HasPosition && groups[1].Items[0].X == 500,
            "a known rest destination stays selectable outside local scanning range");
        check(groups.Any(g => g.Category == DestinationCategory.Travel && g.Items.Contains(bus)),
            "separating rest destinations preserves ordinary fast-travel points");
        check(groups.Any(g => g.Category == DestinationCategory.Shops && g.Items.Contains(namedHomeShop)),
            "a localized place name alone never determines rest semantics");
        check(nativeRoom.Action == DestinationAction.Go && !nativeRoom.Actor.IsValid,
            "selecting a rest destination requests walking guidance without changing time or claiming a live NPC");
        var invalidRoom = nativeRoom with { X = float.NaN };
        check(DestinationPicker.Build([invalidRoom], 0, 0, 8).Count == 0,
            "a rest flag cannot make an unreadable native position navigable");
        var picker = new DestinationPicker();
        picker.Load(groups, DestinationCategory.Rest);
        check(picker.Current == nativeRoom, "the dedicated rest category can be reached directly in the existing picker");
    }
}
