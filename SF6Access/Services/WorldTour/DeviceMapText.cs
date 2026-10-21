using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// The phone map's own vocabulary: a word for what kind of thing a map icon is,
/// and a word for the screen's flow state. Everything the map *names* (landmarks,
/// masters, missions, shops) already arrives localized from the game — see
/// <see cref="Hooks.WorldTour.DeviceMapHooks"/> — so the only strings the mod has
/// to supply are the category words the icon art conveys visually.
///
/// <para><b>Keyed on the game's own enum member NAMES, not on numbers.</b>
/// <c>app.UIMapWindowBase.ICON_TYPE</c> has 39 members and
/// <c>app.UIFlowWTDeviceMap.eFlowState</c> six; both are read back from the TDB
/// through <see cref="FlowHelper.ResolveEnumName"/>, so this table maps
/// <i>names</i> ("SHOP_APPAREL", "MISSION_MAIN", "PIN_3") to a family word. A
/// renumbered enum in a game patch therefore cannot silently make the map say
/// "shop" where it means "enemy", and no icon needs a hardcoded ordinal.</para>
///
/// <para>This lives beside the map reader rather than in
/// <see cref="LocalizedText"/> because it is a lookup TABLE over a game enum, not
/// a flat list of phrases; the translations still live in the
/// <c>SF6Access.lang\*.txt</c> files like every other mod-supplied string.</para>
/// </summary>
public static class DeviceMapText
{
    /// <summary>The icon-kind enum carried by <c>UIMapWindowBase.IconInfo.Type</c>.</summary>
    public const string IconTypeEnum = "app.UIMapWindowBase.ICON_TYPE";

    /// <summary>The map screen's flow-state enum (<c>MapParam.FlowState</c>).</summary>
    public const string FlowStateEnum = "app.UIFlowWTDeviceMap.eFlowState";

    // Prefix -> family word. Order matters: MISSION_COLLECTION_RELATION_MASTER is
    // a mission, so MISSION_ must be tested before MASTER.
    private static readonly (string Prefix, string Key, string English)[] IconFamilies =
    {
        ("SHOP_",       "wt.map.icon_shop",        "shop"),
        ("MISSION_",    "wt.map.icon_mission",     "mission"),
        ("MASTER",      "wt.map.icon_master",      "master"),
        ("FAST_TRAVEL", "wt.map.icon_fast_travel", "fast travel"),
        ("MERCHANT",    "wt.map.icon_merchant",    "merchant"),
        ("CHALLENGER",  "wt.map.icon_challenger",  "challenger"),
        ("ENEMY",       "wt.map.icon_enemy",       "enemy"),
        ("PIN_",        "wt.map.icon_pin",         "marker"),
    };

    // Every flow state the enum declares, so an unexpected transition still says
    // something instead of going quiet.
    private static readonly (string Name, string Key, string English)[] FlowStates =
    {
        ("MapView",      "wt.map.state_map",           "Map"),
        ("TravelSelect", "wt.map.state_travel_list",   "Fast travel list"),
        ("FastTravel",   "wt.map.state_fast_travel",   "Fast travelling"),
        ("FailedTravel", "wt.map.state_failed_travel", "Can't fast travel"),
        ("OpenWorldMap", "wt.map.state_world_map",     "World map"),
        ("FailedAddPin", "wt.map.state_failed_pin",    "Can't place a marker"),
    };

    /// <summary>The spoken kind of a map icon ("shop", "master", "marker"…), or
    /// null when the value is INVALID or the enum cannot be resolved — in which
    /// case the icon still announces its own name, just without a category.</summary>
    public static string IconType(int value)
    {
        string name = FlowHelper.ResolveEnumName(IconTypeEnum, value);
        if (string.IsNullOrEmpty(name) || name == "INVALID" || name == "MAX") return null;
        if (DestinationCategoryRules.IsRestIcon(name)) return DestinationText.RestKind();

        foreach (var (prefix, key, english) in IconFamilies)
            if (name.StartsWith(prefix, System.StringComparison.Ordinal))
                return LangFile.Get(key, english);

        // Cabinets, portals, terminals, JOB… — kinds with no family word of
        // their own. Saying "point of interest" still tells the player the icon is
        // not a shop, an enemy or a mission.
        return LangFile.Get("wt.map.icon_other", "point of interest");
    }

    /// <summary>The spoken name of a map flow state, or null when unknown.</summary>
    public static string FlowState(int value)
    {
        string name = FlowHelper.ResolveEnumName(FlowStateEnum, value);
        if (string.IsNullOrEmpty(name)) return null;

        foreach (var (member, key, english) in FlowStates)
            if (name == member) return LangFile.Get(key, english);
        return null;
    }

    /// <summary>True while the fast-travel list is the active state — matched on
    /// the enum member NAME, so the check survives a renumbered enum.</summary>
    public static bool IsTravelSelect(int value) =>
        FlowHelper.ResolveEnumName(FlowStateEnum, value) == "TravelSelect";

    /// <summary>"Beat Square, shop" — an icon's name followed by its kind.</summary>
    public static string Icon(string title, string kind) =>
        string.IsNullOrEmpty(kind) ? title
                                   : string.Format(LangFile.Get("wt.map.icon_entry", "{0}, {1}"), title, kind);

    /// <summary>"Chinatown Plaza, 2 of 7" — a fast-travel point and its position
    /// in the list, so a run of similarly named points stays distinguishable
    /// (the speech duplicate filter drops identical consecutive text).</summary>
    public static string TravelPoint(string name, int index, int count) =>
        count > 0 && index >= 0
            ? string.Format(LangFile.Get("wt.map.travel_point", "{0}, {1} of {2}"), name, index + 1, count)
            : name;

    /// <summary>"Hong Hu Lu Chinatown - Plaza, Metro City".</summary>
    public static string Location(string area, string city)
    {
        if (string.IsNullOrEmpty(area)) return city;
        if (string.IsNullOrEmpty(city)) return area;
        return string.Format(LangFile.Get("wt.map.location", "{0}, {1}"), area, city);
    }
}
