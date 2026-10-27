namespace SF6Access.Services;

/// <summary>
/// The mod's own spoken phrases — used ONLY where the game gives us no text to
/// reuse (texture captions, panel labels, invented labels like "Slot 3").
/// The translations live in the SF6Access.lang\*.txt files (one per game
/// language, see <see cref="LangFile"/>); the strings here are the English
/// defaults that guarantee an announcement when a file or key is missing.
/// Established fighting-game anglicisms (Super Arts, Drive…) and currency
/// proper nouns (Zenny, Fighter Coins, Drive Tickets) stay untranslated.
/// </summary>
public static class LocalizedText
{
    public static string Damage() => LangFile.Get("damage", "Damage");

    public static string Price() => LangFile.Get("price", "Price");

    public static string Completed(bool clear) => clear
        ? LangFile.Get("completed", "Completed")
        : LangFile.Get("not_completed", "Not completed");

    public static string OnOff(bool on) => on
        ? LangFile.Get("on", "on")
        : LangFile.Get("off", "off");

    public static string Yes() => LangFile.Get("yes", "Yes");

    public static string No() => LangFile.Get("no", "No");

    /// <summary>"locked" for a masculine noun (move slot).</summary>
    public static string LockedM() => LangFile.Get("locked_m", "locked");

    /// <summary>"locked" for a feminine noun (skill).</summary>
    public static string LockedF() => LangFile.Get("locked_f", "locked");

    public static string Acquired() => LangFile.Get("acquired", "acquired");

    public static string Available() => LangFile.Get("available", "available");

    public static string Unavailable() => LangFile.Get("unavailable", "unavailable");

    public static string SkillPoints() => LangFile.Get("skill_points", "Skill points");

    public static string Coins() => LangFile.Get("coins", "Coins");

    public static string CannotResetTree() => LangFile.Get("cannot_reset_tree", "Can't reset on this tree");

    public static string Tree() => LangFile.Get("tree", "Tree");

    public static string Cost() => LangFile.Get("cost", "Cost");

    public static string UnlockSkillQuestion() => LangFile.Get("unlock_skill_q", "Unlock this skill?");

    public static string ResetSkillsTitle() => LangFile.Get("reset_skills_title", "Reset Skills. Reset your skills?");

    public static string ViewSkills() => LangFile.Get("view_skills", "View Skills");

    public static string ResetResource() => LangFile.Get("reset_resource", "Reset resource");

    public static string Slot() => LangFile.Get("slot", "Slot");

    public static string Preset() => LangFile.Get("preset", "Preset");

    public static string Empty() => LangFile.Get("empty", "Empty");

    // Generic numbered fallbacks for rows whose label could not be read.
    public static string OptionN(int n) => string.Format(LangFile.Get("option_n", "Option {0}"), n);

    public static string TabN(int n) => string.Format(LangFile.Get("tab_n", "Tab {0}"), n);

    public static string SettingN(int n) => string.Format(LangFile.Get("setting_n", "Setting {0}"), n);

    public static string PresetN(int n) => string.Format(LangFile.Get("preset_n", "Preset {0}"), n);

    public static string SlotN(int n) => string.Format(LangFile.Get("slot_n", "Slot {0}"), n);

    public static string ItemN(int n) => string.Format(LangFile.Get("item_n", "Item {0}"), n);

    public static string CategoryN(int n) => string.Format(LangFile.Get("category_n", "Category {0}"), n);

    public static string ShortcutN(int n) => string.Format(LangFile.Get("shortcut_n", "Shortcut {0}"), n);

    public static string FighterN(int n) => string.Format(LangFile.Get("fighter_n", "Fighter {0}"), n);

    public static string StatN(int n) => string.Format(LangFile.Get("stat_n", "Stat {0}"), n);

    /// <summary>Cancel / Confirm buttons the game draws as images.</summary>
    public static string Cancel() => LangFile.Get("cancel", "Cancel");

    public static string Confirm() => LangFile.Get("confirm", "Confirm");

    /// <summary>Result-screen score labels shared by the Arcade and Bonus results.</summary>
    public static string Score() => LangFile.Get("result.score", "Score");

    public static string TimeBonus() => LangFile.Get("result.time_bonus", "Time bonus");

    public static string Total() => LangFile.Get("result.total", "Total");

    public static string MovesLearned() => LangFile.Get("moves_learned", "Moves Learned");

    public static string MoveSet() => LangFile.Get("move_set", "Move Set");

    /// <summary>Equipped-slot counter phrase, e.g. "3 of 5 slots". The numbers
    /// come from the game; only the template/connector word is localized.</summary>
    public static string EquipSlotCount(int now, int max)
        => string.Format(LangFile.Get("slots_count", "{0} of {1} slots"), now, max);

    /// <summary>Announced when every equip slot in the category is full.</summary>
    public static string SlotsFull() => LangFile.Get("slots_full", "Slots full");

    public static string Perks() => LangFile.Get("perks", "Perks");

    /// <summary>The combo counter's "hits" word ("2500. 6 hits").</summary>
    public static string Hits() => LangFile.Get("hits", "hits");

    /// <summary>Training readout labels for the two sides — the frame meter's
    /// rows carry no label on screen (sighted players tell them apart by position).</summary>
    public static string You() => LangFile.Get("you", "You");

    public static string Opponent() => LangFile.Get("opponent", "Opponent");

    /// <summary>The Attack Data damage percent ("270 (30%)") has no on-screen label.</summary>
    public static string Scaling() => LangFile.Get("scaling", "scaling");

    /// <summary>Boot title-screen prompt (the on-screen prompt is an image and,
    /// despite it saying "any button", only these inputs advance).</summary>
    public static string TitleScreenPrompt() => LangFile.Get("title_prompt",
        "Title screen. Press F on keyboard, A on Xbox controller, or Cross on PlayStation controller");

    /// <summary>Startup notice when GitHub has a newer release ({0} latest, {1} installed).</summary>
    public static string UpdateAvailable(string latest, string installed) => string.Format(
        LangFile.Get("update_available",
            "A new version of SF6 Access is available: {0}. You have {1}. Download it from the mod's GitHub page."),
        latest, installed);

    /// <summary>Avatar combat stat label for a WTPlayerStatusType value
    /// (1 Vitality, 6 Punch, 7 Kick, 8 Throw, 9 Unique Attack, 10 Defense).</summary>
    public static string StatLabel(int type) => type switch
    {
        1 => LangFile.Get("stat.1", "Vitality"),
        6 => LangFile.Get("stat.6", "Punch"),
        7 => LangFile.Get("stat.7", "Kick"),
        8 => LangFile.Get("stat.8", "Throw"),
        9 => LangFile.Get("stat.9", "Unique Attack"),
        10 => LangFile.Get("stat.10", "Defense"),
        _ => null,
    };

    /// <summary>The Legend rank's name: the game draws it into the rank icon and
    /// has no message for it.</summary>
    public static string Legend() => LangFile.Get("rank.legend", "Legend");

    /// <summary>"Legend, ranked 123" — the player's world Master Rate ranking.</summary>
    public static string LegendRanked(string legend, int position)
        => string.Format(LangFile.Get("rank.legend_ranked", "{0}, ranked {1}"), legend, position);

    /// <summary>Said on the post-match rank gauge when you drop a rank (a demotion
    /// opens no screen of its own, unlike a promotion).</summary>
    public static string RankDown() => LangFile.Get("rank.down", "Rank down");

    /// <summary>Control-type fallback (Classic=0, Modern=1, Dynamic=2) — the
    /// game lookup is preferred; this only covers lookup failure.</summary>
    public static string ControlType(int index) => index switch
    {
        0 => LangFile.Get("control.0", "Classic"),
        1 => LangFile.Get("control.1", "Modern"),
        2 => LangFile.Get("control.2", "Dynamic"),
        _ => null,
    };

    /// <summary>Chat input-bar buttons (0 Message, 1 Send, 2 Phrases, 3 Stickers).</summary>
    public static string ChatSlot(int slot) => slot switch
    {
        0 => LangFile.Get("chat.0", "Message"),
        1 => LangFile.Get("chat.1", "Send"),
        2 => LangFile.Get("chat.2", "Phrases"),
        3 => LangFile.Get("chat.3", "Stickers"),
        _ => null,
    };

    /// <summary>Move set-type fallback (WTActionSkillSetType: Ground=1, Air=2,
    /// SuperArts=3) — the game's own tab label is preferred.</summary>
    public static string SetType(int setType) => setType switch
    {
        1 => LangFile.Get("settype.1", "Grounded"),
        2 => LangFile.Get("settype.2", "Air"),
        3 => LangFile.Get("settype.3", "Super Arts"),
        _ => null,
    };

    // --- World Tour field awareness (WT-1) ---

    /// <summary>Spoken when the on-demand nearby-interactables key finds nothing.</summary>
    public static string NothingNearby() => LangFile.Get("wt.nothing_nearby", "Nothing nearby");

    /// <summary>Header for the nearby-interactables list, e.g. "3 nearby". The
    /// count comes from the game; only the word is localized.</summary>
    public static string NearbyCount(int count)
        => string.Format(LangFile.Get("wt.nearby_count", "{0} nearby"), count);

    /// <summary>Kind word for a HudDef.ContactUIType interactable — a talkable
    /// NPC (ContactUIType.NPC).</summary>
    public static string ContactPerson() => LangFile.Get("wt.contact_person", "person");

    /// <summary>Kind word for a Master (ContactUIType.Legendary).</summary>
    public static string ContactMaster() => LangFile.Get("wt.contact_master", "master");

    /// <summary>"person, 12 meters away" — a distant avatar with its real
    /// distance (|otherPos − playerPos|; RE Engine world units are meters).</summary>
    public static string AtMeters(string what, int meters)
        => string.Format(LangFile.Get("wt.at_meters", "{0}, {1} meters away"), what, meters);

    /// <summary>"Leaving Shop counter" — the announced interactable just went
    /// out of interaction range (nothing else is in range either).</summary>
    public static string LeavingTarget(string what)
        => string.Format(LangFile.Get("wt.leaving", "Leaving {0}"), what);

    /// <summary>"person at 2 o'clock, 14 meters away" — distance plus the
    /// camera-relative clock direction (12 = straight ahead of the camera,
    /// i.e. stick up). Used when a direction frame could be read; plain
    /// <see cref="AtMeters"/> is the fallback.</summary>
    public static string AtClockMeters(string what, int hour, int meters)
        => string.Format(LangFile.Get("wt.at_clock_meters", "{0} at {1} o'clock, {2} meters away"), what, hour, meters);

    /// <summary>"at 2 o'clock, 14 meters" — terse tracking update while walking
    /// toward the SAME target (the name was already said when it became the
    /// target; repeating it every update drowns the useful numbers).</summary>
    public static string ClockShort(int hour, int meters)
        => string.Format(LangFile.Get("wt.clock_short", "at {0} o'clock, {1} meters"), hour, meters);

    /// <summary>Continuous field tracking toggled on.</summary>
    public static string TrackingOn() => LangFile.Get("wt.tracking_on", "Tracking on");

    /// <summary>Continuous field tracking toggled off.</summary>
    public static string TrackingOff() => LangFile.Get("wt.tracking_off", "Tracking off");

    /// <summary>Ambient NPC audio beacons toggled on.</summary>
    public static string BeaconOn() => LangFile.Get("wt.beacon_on", "Beacons on");

    /// <summary>Ambient NPC audio beacons toggled off.</summary>
    public static string BeaconOff() => LangFile.Get("wt.beacon_off", "Beacons off");

    /// <summary>Sequential guide to the tutorial's step-on panels, toggled on.</summary>
    public static string PadGuideOn() => LangFile.Get("wt.pad_guide_on", "Panel guide on");

    /// <summary>Sequential guide to the tutorial's step-on panels, toggled off.</summary>
    public static string PadGuideOff() => LangFile.Get("wt.pad_guide_off", "Panel guide off");

    /// <summary>Every panel has been stepped on — the guide is finished.</summary>
    public static string PadsDone() => LangFile.Get("wt.pads_done", "All panels done");

    /// <summary>What one step-on panel is called when announced with a direction.
    /// "Panel" follows the game's own English tutorial dialogue.</summary>
    public static string PadWord() => LangFile.Get("wt.pad", "panel");

    /// <summary>A mission the player has finished.</summary>
    public static string MissionCleared() => LangFile.Get("wt.mission_cleared", "cleared");

    /// <summary>A mission the player has not taken on yet.</summary>
    public static string MissionNotAccepted() => LangFile.Get("wt.mission_not_accepted", "not accepted");

    /// <summary>What the mission objective is called when announced with a
    /// direction ("objective at 2 o'clock, 40 meters away").</summary>
    public static string MissionWord() => LangFile.Get("wt.mission_target", "objective");

    /// <summary>Spoken once on reaching the mission objective.</summary>
    public static string MissionHere() => LangFile.Get("wt.mission_here", "At the objective");

    /// <summary>"and 12 more" — the tail of a list too long to read out in full.</summary>
    public static string AndMore(int count)
        => string.Format(LangFile.Get("wt.and_more", "and {0} more"), count);

    /// <summary>Kind word for an object / gimmick (ContactUIType.OM).</summary>
    public static string ContactObject() => LangFile.Get("wt.contact_object", "object");

    /// <summary>Kind word for another player (ContactUIType.OtherPlayer).</summary>
    public static string ContactPlayer() => LangFile.Get("wt.contact_player", "player");

    // --- World Tour navigation radar (geometry: walls, openings, drops) ---

    /// <summary>Continuous reactive navigation radar toggled on.</summary>
    public static string NavRadarOn() => LangFile.Get("wt.nav_radar_on", "Navigation radar on");

    /// <summary>Continuous reactive navigation radar toggled off.</summary>
    public static string NavRadarOff() => LangFile.Get("wt.nav_radar_off", "Navigation radar off");

    /// <summary>The obstacle class in front of the avatar, as a spoken phrase.
    /// Every class gets its own key: the words differ in gender and number between
    /// languages, so a shared template would be untranslatable.</summary>
    public static string NavFront(SF6Access.Services.WorldTour.FrontProfile profile) => profile switch
    {
        SF6Access.Services.WorldTour.FrontProfile.Step => LangFile.Get("wt.nav_front_step", "Low step"),
        SF6Access.Services.WorldTour.FrontProfile.WaistHigh => LangFile.Get("wt.nav_front_waist", "Waist-high obstacle"),
        SF6Access.Services.WorldTour.FrontProfile.Wall => LangFile.Get("wt.nav_front_wall", "Wall"),
        SF6Access.Services.WorldTour.FrontProfile.TallWall => LangFile.Get("wt.nav_front_tall", "Tall wall"),
        _ => LangFile.Get("wt.nav_front_open", "Clear ahead"),
    };

    /// <summary>The game says the avatar is stopped, but the forward rays saw
    /// nothing to name — a fence or a prop the ray filter does not report though the
    /// capsule collides with it. Said instead of "clear ahead", which would be the
    /// exact false positive this phrase exists to prevent.</summary>
    public static string NavBlocked() => LangFile.Get("wt.nav_front_blocked", "Blocked");

    /// <summary>A wall the avatar can RUN ALONG. A route, not a dead end, so it is
    /// deliberately not spoken as a wall.</summary>
    public static string NavWallRide() => LangFile.Get("wt.nav_front_wallride", "Wall you can run along");

    /// <summary>"Wall at 0.5 meters" — the obstacle class plus the engine's own
    /// contact distance.</summary>
    public static string NavObstacleAt(string what, float meters)
        => string.Format(LangFile.Get("wt.nav_obstacle_at", "{0} at {1} meters"), what, Metres(meters));

    /// <summary>"Clear for 1.9 meters" — nothing in the height stack, but the long
    /// forward ray found something further out.</summary>
    public static string NavClearFor(float meters)
        => string.Format(LangFile.Get("wt.nav_clear_for", "Clear for {0} meters"), Metres(meters));

    public static string NavLeftOpen() => LangFile.Get("wt.nav_left_open", "left open");

    public static string NavLeftBlocked() => LangFile.Get("wt.nav_left_blocked", "left blocked");

    public static string NavRightOpen() => LangFile.Get("wt.nav_right_open", "right open");

    public static string NavRightBlocked() => LangFile.Get("wt.nav_right_blocked", "right blocked");

    /// <summary>"left clear for 14 meters" — an open side WITH the distance the
    /// avatar's body could actually travel that way.
    ///
    /// <para>The bare "left open" it replaces was true at anything past two metres,
    /// which is what let the readout agree with a cue about a shopfront recess. The
    /// number is the same clearance the cue channel decides on
    /// (<see cref="WorldTour.FieldRadarClearance"/>), so the two can be compared by
    /// ear.</para></summary>
    public static string NavLeftClearFor(float meters)
        => string.Format(LangFile.Get("wt.nav_left_clear", "left clear for {0} meters"), Metres(meters));

    /// <summary>"right clear for 14 meters" — see <see cref="NavLeftClearFor"/>.</summary>
    public static string NavRightClearFor(float meters)
        => string.Format(LangFile.Get("wt.nav_right_clear", "right clear for {0} meters"), Metres(meters));

    /// <summary>All four radar beams closed: "enclosed space".</summary>
    public static string NavEnclosed() => LangFile.Get("wt.nav_enclosed", "Enclosed space");

    /// <summary>The first beam to open after "enclosed": "exit on the left".</summary>
    public static string NavExit(WorldTour.RadarBeam beam) => beam switch
    {
        WorldTour.RadarBeam.Left => LangFile.Get("wt.nav_exit_left", "Exit on the left"),
        WorldTour.RadarBeam.Right => LangFile.Get("wt.nav_exit_right", "Exit on the right"),
        _ => LangFile.Get("wt.nav_exit_front", "Exit ahead"),
    };

    /// <summary>"opening at 10 o'clock, 2.1 meters wide, 6.0 meters away" — a gap
    /// the avatar fits through, found by <see cref="WorldTour.NavOpenings"/>. The
    /// WIDTH is the point: it is measured between the two pieces of geometry that
    /// flank the gap, so it tells a doorway from an alley from a decorative recess
    /// without any of them being a distance the player has to calibrate by ear.</summary>
    public static string NavOpening(int hour, float widthMeters, float distanceMeters)
        => string.Format(LangFile.Get("wt.nav_opening", "opening at {0} o'clock, {1} meters wide, {2} meters away"),
                         hour, Metres(widthMeters), Metres(distanceMeters));

    /// <summary>"gap at 10 o'clock, only 0.6 meters wide" — a break in the geometry
    /// the avatar's own collision capsule does NOT fit through. Spoken rather than
    /// hidden: "there is a hole there but you cannot use it" is the other half of
    /// knowing where you can go.</summary>
    public static string NavGapTooNarrow(int hour, float widthMeters)
        => string.Format(LangFile.Get("wt.nav_gap_narrow", "gap at {0} o'clock, only {1} meters wide"),
                         hour, Metres(widthMeters));

    /// <summary>The scan found no gap wide enough to walk through.</summary>
    public static string NavNoOpenings() => LangFile.Get("wt.nav_no_openings", "No way through in range");

    public static string NavFloorSolid() => LangFile.Get("wt.nav_floor_solid", "floor ahead");

    /// <summary>No floor under the downward probe — a ledge or a hole.</summary>
    public static string NavFloorDrop() => LangFile.Get("wt.nav_floor_drop", "drop ahead");

    /// <summary>Radar distances are spoken to ONE DECIMAL, unlike the people radar's
    /// whole metres: these readings live between 0.1 m and about 3 m, where rounding
    /// to an integer would turn every wall the player is standing against into
    /// "0 meters". Formatted invariantly so the decimal separator does not depend on
    /// the machine's locale, which has nothing to do with the game's language.</summary>
    private static string Metres(float meters)
        => meters.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    // --- World Tour zone / area name ---

    /// <summary>"In Beat Street" — the district the player is actually standing
    /// in. Containment, so it is stated plainly.</summary>
    public static string ZoneHere(string name)
        => string.Format(LangFile.Get("wt.zone_here", "In {0}"), name);

    /// <summary>"Near Urban Park" — the nearest named landmark, NOT the area the
    /// player is inside. Deliberately hedged, and combined with the distance by
    /// the caller, so a proximity answer is never mistaken for a district.</summary>
    public static string ZoneNear(string name)
        => string.Format(LangFile.Get("wt.zone_near", "Near {0}"), name);

    /// <summary>"Beat Square area" — the part of town nearest to a landmark: our own
    /// division, so worded as an area rather than as a place the player is inside.</summary>
    public static string RegionOf(string name)
        => string.Format(LangFile.Get("wt.zone_region", "{0} area"), name);

    /// <summary>Answer to the on-demand key when the place cannot be named at
    /// all. Silence would read as a broken key.</summary>
    public static string ZoneUnknown() => LangFile.Get("wt.zone_unknown", "Location unknown");

    // --- World Tour compass / aim ---

    /// <summary>The eight compass points, index 0 = north clockwise to 7 =
    /// northwest (see <c>FieldHeadingService.Sector</c>).</summary>
    public static string CompassPoint(int sector) => sector switch
    {
        0 => LangFile.Get("wt.dir_n", "north"),
        1 => LangFile.Get("wt.dir_ne", "northeast"),
        2 => LangFile.Get("wt.dir_e", "east"),
        3 => LangFile.Get("wt.dir_se", "southeast"),
        4 => LangFile.Get("wt.dir_s", "south"),
        5 => LangFile.Get("wt.dir_sw", "southwest"),
        6 => LangFile.Get("wt.dir_w", "west"),
        _ => LangFile.Get("wt.dir_nw", "northwest"),
    };

    /// <summary>"Facing north" — the on-demand answer; the hands-free compass
    /// speaks the bare point.</summary>
    public static string Facing(string point)
        => string.Format(LangFile.Get("wt.facing", "Facing {0}"), point);

    /// <summary>"Luke, master, straight ahead" — the camera has just lined up
    /// with the tracked person.</summary>
    public static string AimAhead(string what)
        => string.Format(LangFile.Get("wt.aim_ahead", "{0}, straight ahead"), what);

    // --- World Tour compass sweep ---

    /// <summary>"Open: north, east" — the compass points with nothing within the
    /// sweep's reach.</summary>
    public static string SweepOpen(string points)
        => string.Format(LangFile.Get("wt.sweep_open", "Open: {0}"), points);

    /// <summary>"Walls: south 3 meters, west 6 meters".</summary>
    public static string SweepWalls(string entries)
        => string.Format(LangFile.Get("wt.sweep_walls", "Walls: {0}"), entries);

    /// <summary>One wall entry of the sweep, whole metres: at street scale a
    /// decimal is noise.</summary>
    public static string SweepEntry(string point, int meters)
        => string.Format(LangFile.Get("wt.sweep_entry", "{0} {1} meters"), point, meters);

    /// <summary>The sweep found nothing anywhere within reach.</summary>
    public static string SweepAllOpen() => LangFile.Get("wt.sweep_all_open", "Open all around");

    /// <summary>The sweep could not be run (no field state, no ray API).</summary>
    public static string SweepUnavailable() => LangFile.Get("wt.sweep_unavailable", "Surroundings unavailable");

    // ---------- map selection + aiming (Hooks/WorldTour/MapAimHooks.cs) ----------

    /// <summary>"Following Beat Square, shop" — what the beacon is now guiding to,
    /// said when the player picks a point on the phone map.</summary>
    public static string TrackerFollowing(string what)
        => string.Format(LangFile.Get("wt.track.following", "Following {0}"), what);

    /// <summary>Reaching a point the player picked on the map. The mission objective
    /// has its own line (<see cref="MissionHere"/>): saying that one on arriving at a
    /// chosen shop would simply be untrue.</summary>
    public static string TrackerArrived(string what)
        => string.Format(LangFile.Get("wt.track.arrived", "At {0}"), what);

    /// <summary>"Entrando en Style Lab Beauty Salon" — the player just walked into
    /// a shop. Announced at the game's OWN threshold, not one we guessed.</summary>
    public static string ShopEntering(string what)
        => string.Format(LangFile.Get("wt.shop.entering", "Entering {0}"), what);

    /// <summary>"Saliendo de Style Lab Beauty Salon" — back out on the street.</summary>
    public static string ShopLeaving(string what)
        => string.Format(LangFile.Get("wt.shop.leaving", "Leaving {0}"), what);

    /// <summary>"Toward the foot of X, 40 meters"</summary>
    public static string AimApproach(string what, int meters)
        => string.Format(LangFile.Get("wt.aim.approach", "Toward the foot of {0}, {1} meters"),
                         what, meters);

    /// <summary>"No route on foot to X, 30 meters away" — the engine found no way to
    /// WALK there, typically a rooftop or an upper floor. The bearing is still given,
    /// because a direction beats nothing, but the player has to know it is a direction
    /// and not a way; without this the mod marches them into a wall confidently.</summary>
    public static string AimNoRoute(string what, int meters)
        => string.Format(LangFile.Get("wt.aim.no_route", "No route on foot to {0}, {1} meters away"),
                         what, meters);

    /// <summary>No story objective is active AND nothing is picked on the map, so
    /// there is nowhere to point.</summary>
    public static string AimNoSelection()
        => LangFile.Get("wt.aim.none", "No objective and nothing selected on the map");

    /// <summary>"Toward Beat Square, shop, 40 meters" — the turn happened, and
    /// forward now means the next leg of the route. Deliberately not "facing X":
    /// with route guidance on the camera is pointed at the next BEND, not at the
    /// destination, and the distance is how far there is left to WALK.</summary>
    public static string AimTurned(string what, int meters)
        => string.Format(LangFile.Get("wt.aim.turned", "Toward {0}, {1} meters"), what, meters);

    /// <summary>The camera could not be turned (the controller was not reachable),
    /// so the direction is spoken instead of pointed.</summary>
    public static string AimUnavailable(string what, int hour, int meters)
        => string.Format(LangFile.Get("wt.aim.unavailable", "{0} at {1} o'clock, {2} meters"),
                         what, hour, meters);

    /// <summary>The route pathfinder was switched on: beacons now aim at the next
    /// corner of a real walkable route instead of straight through the scenery.</summary>
    public static string PathfinderOn() => LangFile.Get("wt.pathfinder_on", "Route guidance on");

    /// <summary>The route pathfinder was switched off; beacons point straight at
    /// the target again.</summary>
    public static string PathfinderOff() => LangFile.Get("wt.pathfinder_off", "Route guidance off");
}
