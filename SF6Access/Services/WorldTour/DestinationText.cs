using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>Everything the destination picker and the "where am I" readout say.</summary>
public static class DestinationText
{
    private static float HeightBandM => Hooks.WorldTour.MissionBeaconHooks.ARRIVED_M;

    public static string CategoryName(DestinationCategory category) => category switch
    {
        DestinationCategory.Mission => LangFile.Get("wt.dest.cat_mission", "Story objective"),
        DestinationCategory.Places => LangFile.Get("wt.dest.cat_places", "Places"),
        DestinationCategory.Access => LangFile.Get("wt.dest.cat_access", "Ladders and doors"),
        DestinationCategory.Shops => LangFile.Get("wt.dest.cat_shops", "Shops and services"),
        DestinationCategory.Masters => LangFile.Get("wt.dest.cat_masters", "Masters"),
        DestinationCategory.Missions => LangFile.Get("wt.dest.cat_missions", "Missions"),
        DestinationCategory.Jobs => LangFile.Get("wt.dest.cat_jobs", "Jobs"),
        DestinationCategory.People => LangFile.Get("wt.dest.cat_people", "People nearby"),
        DestinationCategory.Pins => LangFile.Get("wt.dest.cat_pins", "Map markers"),
        _ => LangFile.Get("wt.dest.cat_saved", "My places"),
    };

    /// <summary>"Shops and services, 12" — said when a category is entered.</summary>
    public static string CategoryLine(DestinationCategory category, int count) =>
        string.Format(LangFile.Get("wt.dest.category", "{0}, {1}"), CategoryName(category), count);

    /// <summary>"at 2 o'clock, 120 meters, 12 meters up" from the player to a point.</summary>
    public static string Where(float px, float py, float pz, float tx, float ty, float tz)
    {
        float dx = tx - px, dz = tz - pz, dy = ty - py;
        int meters = (int)System.Math.Round(System.Math.Sqrt(dx * dx + dz * dz));
        int hour = FieldDirectionService.ClockHour(FieldDirectionService.GetCameraForward(), dx, dz);
        string where = hour > 0
            ? LocalizedText.ClockShort(hour, meters)
            : string.Format(LangFile.Get("wt.dest.meters", "{0} meters"), meters);

        int height = (int)System.Math.Round(System.Math.Abs(dy));
        if (System.Math.Abs(dy) < HeightBandM) return where;
        return where + ", " + string.Format(dy > 0
            ? LangFile.Get("wt.dest.above", "{0} meters up")
            : LangFile.Get("wt.dest.below", "{0} meters down"), height);
    }

    /// <summary>"Beat Square, at 2 o'clock, 120 meters, 3 of 8".</summary>
    public static string Item(Destination d, string where, int index, int count) =>
        string.IsNullOrEmpty(where)
            ? string.Format(LangFile.Get("wt.dest.item_action", "{0}, {1} of {2}"), d.Label, index + 1, count)
            : string.Format(LangFile.Get("wt.dest.item", "{0}, {1}, {2} of {3}"), d.Label, where, index + 1, count);

    public static string Opened() => LangFile.Get("wt.dest.open", "Destinations");

    public static string Empty() => LangFile.Get("wt.dest.empty",
        "No destinations known yet. Open the phone map once so its places are learned");

    /// <summary>Said once per session while the list has only the basic places.</summary>
    public static string LearnHint() => LangFile.Get("wt.dest.learn_hint",
        "To add shops, masters and missions, open the phone map once");

    public static string Unavailable() => LangFile.Get("wt.dest.unavailable", "Destinations unavailable here");

    public static string Cancelled() => LangFile.Get("wt.dest.cancelled", "Destinations closed");

    /// <summary>"Destination: Beat Square, at 2 o'clock, 120 meters".</summary>
    public static string Chosen(string label, string where) =>
        string.Format(LangFile.Get("wt.dest.chosen", "Destination: {0}, {1}"), label, where);

    public static string FollowMission() =>
        LangFile.Get("wt.dest.follow_mission", "Following the story objective again");

    public static string MissionEntry() => LangFile.Get("wt.dest.mission_entry", "Story objective");

    public static string SaveHereEntry() => LangFile.Get("wt.dest.save_here", "Save this spot");

    public static string Saved(string name) =>
        string.Format(LangFile.Get("wt.dest.saved", "Saved: {0}"), name);

    public static string NotSaved() => LangFile.Get("wt.dest.not_saved", "This spot could not be saved");

    public static string Deleted(string name) =>
        string.Format(LangFile.Get("wt.dest.deleted", "Deleted: {0}"), name);

    /// <summary>"Beat Square, spot 3" — the name a saved spot gets.</summary>
    public static string SavedName(string zoneName, int number) =>
        string.Format(LangFile.Get("wt.dest.saved_name", "{0}, spot {1}"),
                      string.IsNullOrEmpty(zoneName) ? LangFile.Get("wt.dest.spot", "Spot") : zoneName, number);
}
