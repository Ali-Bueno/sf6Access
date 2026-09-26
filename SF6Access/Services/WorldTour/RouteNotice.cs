using System;

namespace SF6Access.Services.WorldTour;

/// <summary>Route states for the destination beacon and the sentence spoken when the state changes.</summary>
internal static class RouteNotice
{
    internal static string State(in BeaconDestination d, bool near) =>
        d.Searching ? "searching"
        : d.Ladder ? (near ? "ladder-near:" : d.Blocked && !d.ViaRoute ? "ladder-blocked:" : "ladder:") + d.Leg
        : d.Closest ? ((near && d.IsApproach) || d.AtFoot ? "closest-near" : "closest")
        : d.NoRoute ? "no-route"
        : !d.ViaRoute ? "unavailable"
        : d.IsApproach ? (near || d.AtFoot ? "approach-near" : "approach")
        : near ? "near" : "route";

    internal static bool IsNear(string state) => state is "near" or "approach-near" or "closest-near" || state?.StartsWith("ladder-near:") == true;

    /// <summary>What to say on entering <paramref name="state"/>, or null to stay quiet.</summary>
    internal static string Text(string state, string previous, in BeaconDestination d, float distance, float playerY)
    {
        int meters = Math.Max(1, (int)MathF.Round(d.RouteM > 0 ? d.RouteM : distance));
        int previousLeg = LegOf(previous);
        if (state.StartsWith("ladder-near:"))
        {
            int rung = (int)MathF.Round(d.BottomRungM);
            if (rung >= 1) return string.Format(Get("at_ladder_high", "Ladder here. It starts {0} meters above the ground: jump toward it to grab it"), rung);
            return d.LadderUp ? Get("at_ladder_up", "Ladder here. Face it and press the action button to climb up")
                              : Get("at_ladder_down", "Ladder here. Face it and press the action button to climb down");
        }
        if (state.StartsWith("ladder-blocked:"))
            return previousLeg == d.Leg ? null
                : string.Format(Get("blocked_ladder", "The next ladder is {0} meters away, but there is no clear way to it: look for something to break with a master action, or a jump. The sound points straight at it"), meters);
        if (state.StartsWith("ladder:"))
        {
            if (previous?.StartsWith("ladder-blocked:") == true && previousLeg == d.Leg)
                return string.Format(Get("path_open", "The way to the ladder is open, {0} meters"), meters);
            if (previousLeg == d.Leg) return null;
            if (previousLeg > d.Leg) return string.Format(Get("back_to_ladder", "Back to the ladder, {0} meters away"), meters);
            if (previousLeg >= 0) return string.Format(Get("next_ladder", "Ladder done. Next ladder {0} meters away"), meters);
            if (d.Partial)
                return d.LaddersLeft > 1
                    ? string.Format(Get("partial_many", "No ladder reaches the destination, but {0} ladders take you closer. The first is {1} meters away"), d.LaddersLeft, meters)
                    : string.Format(Get("partial_one", "No ladder reaches the destination, but one takes you closer. It is {0} meters away"), meters);
            return d.LaddersLeft > 1
                ? string.Format(Get("found_many", "Route found through {0} ladders. The first is {1} meters away"), d.LaddersLeft, meters)
                : string.Format(Get("found_one", "Route found through a ladder, {0} meters away"), meters);
        }
        if (previousLeg >= 0 && (state == "route" || state == "near"))
            return string.Format(Get("leg_done", "Ladder done. Continue to {0}, {1} meters"), d.Word, meters);
        bool afterSearch = previous == "searching";
        return state switch
        {
            "searching" => Searching(d.TargetY - playerY),
            "no-route" when afterSearch => Get("search_failed", "No ladder found that reaches the destination"),
            "no-route" => LocalizedText.AimNoRoute(d.Word, meters),
            "unavailable" => ExplorationText.RouteLost(),
            "approach" => ExplorationText.Approach(),
            "approach-near" when d.AboveM >= LevelRoute.LevelM => AtFoot(d),
            "approach-near" => ExplorationText.Get("approach_near", "Near the approach point. The objective still needs an entrance"),
            "near" => ExplorationText.Nearby(d.Word),
            "closest" when previous == "closest-near" => null,
            "closest" => Closest(d.AboveM),
            "closest-near" when d.AboveM >= LevelRoute.LevelM => AtFoot(d),
            "closest-near" => ExplorationText.Get("approach_near", "Near the approach point. The objective still needs an entrance"),
            "route" when previous is "no-route" or "unavailable" or "approach" => ExplorationText.RouteRestored(),
            _ => null,
        };
    }

    internal static string LadderWord() => Get("ladder", "ladder");
    internal static string LadderOffered() => Get("ladder_offered", "You can grab the ladder now");

    /// <summary>What the beacon is heading for: an approach point is the foot of the destination, not the destination.</summary>
    internal static string Heading(in BeaconDestination d)
        => d.IsApproach && !d.AtFoot ? string.Format(Get("foot_of", "foot of {0}"), d.Word) : d.Word;

    private static string AtFoot(in BeaconDestination d)
        => string.Format(Get("at_foot", "At the foot of {0}. It is {1} meters above you. The sound now points at it: walk toward it looking for a way up"),
                         d.Word, (int)MathF.Round(d.AboveM));
    internal static string SearchingNow() => Get("searching_now", "Searching for a route through ladders, one moment");

    /// <summary>A partial route is climbed: how far the destination still is in height, and what may close the gap.</summary>
    private static string Closest(float rise)
    {
        if (MathF.Abs(rise) < LevelRoute.LevelM)
            return Get("closest_level", "The ladders go no nearer. Something blocks the way: look for something to break with a master action, or a jump");
        return string.Format(rise > 0 ? Get("closest_up", "The ladders go no higher. The destination is {0} meters above: look for a ledge to climb, a jump, or something to break with a master action")
                                      : Get("closest_down", "The ladders go no lower. The destination is {0} meters below: look for a place to drop down"),
                             (int)MathF.Round(MathF.Abs(rise)));
    }

    private static string Searching(float rise)
    {
        int meters = (int)MathF.Round(MathF.Abs(rise));
        if (MathF.Abs(rise) < LevelRoute.LevelM) return Get("searching_level", "No direct walk to the destination. Searching for a ladder");
        return string.Format(rise > 0 ? Get("searching_up", "The destination is {0} meters above you. Searching for a ladder")
                                      : Get("searching_down", "The destination is {0} meters below you. Searching for a ladder"), meters);
    }

    private static int LegOf(string state)
    {
        if (state == null || !state.StartsWith("ladder")) return -1;
        return int.TryParse(state.AsSpan(state.IndexOf(':') + 1), out int leg) ? leg : -1;
    }

    private static string Get(string key, string fallback) => LangFile.Get("wt.route." + key, fallback);
}
