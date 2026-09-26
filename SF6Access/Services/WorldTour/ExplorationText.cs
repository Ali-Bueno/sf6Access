namespace SF6Access.Services.WorldTour;

/// <summary>Mod descriptions for geometry and availability. Native prompt text wins when present.</summary>
internal static class ExplorationText
{
    internal static string Action(string name) => name switch
    {
        "Ladder_Up" or "Ladder_Up_Air" => Get("climb_up", "Climb up"),
        "Ladder_Down" => Get("climb_down", "Climb down"),
        "Ladder_UpDown" => Get("climb_both", "Climb up or down"),
        "Ladder_Slide" => Get("slide", "Slide down"),
        "Ladder_Catch" => Get("catch", "Grab ladder"),
        "Climb" => Get("climb", "Climb"),
        "GetOver" => Get("get_over", "Climb over"),
        "Open" => Get("open", "Open"),
        "Close" => Get("close", "Close"),
        "EnterShop" => Get("enter_shop", "Enter shop"),
        "GetOutCity" => Get("leave_city", "Leave city"),
        "Open_TreasureChest" => Get("chest", "Open chest"),
        "FindOut" => Get("examine", "Examine"),
        "StartUp" => Get("activate", "Activate"),
        "GetItem" => Get("get_item", "Pick up item"),
        _ => null,
    };
    internal static string Get(string key, string fallback) => LangFile.Get("wt.explore." + key, fallback);
    internal static string Nearby(string word) => string.Format(Get("nearby", "Near {0}. Interaction not confirmed"), word);
    internal static string Available(string word) => string.Format(Get("available", "Available: {0}"), word);
    internal static string RouteLost() => Get("route_lost", "Route unavailable. The sound points directly toward the destination");
    internal static string RouteRestored() => Get("route_restored", "Following the walking route");
    internal static string Approach() => Get("approach", "Nearby point only. An entrance to the objective has not been found");
}
