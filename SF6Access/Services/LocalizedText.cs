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

    /// <summary>Boot title-screen prompt (the on-screen prompt is an image and,
    /// despite it saying "any button", only these inputs advance).</summary>
    public static string TitleScreenPrompt() => LangFile.Get("title_prompt",
        "Title screen. Press F on keyboard, A on Xbox controller, or Cross on PlayStation controller");

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

    /// <summary>Kind word for an object / gimmick (ContactUIType.OM).</summary>
    public static string ContactObject() => LangFile.Get("wt.contact_object", "object");

    /// <summary>Kind word for another player (ContactUIType.OtherPlayer).</summary>
    public static string ContactPlayer() => LangFile.Get("wt.contact_player", "player");
}
