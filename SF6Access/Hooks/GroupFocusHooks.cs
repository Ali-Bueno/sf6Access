using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Generic reader for flow screens that navigate via UIPartsGroup/list fields
/// (custom room create form, join list, avatar arcade menu...). Discovers group
/// and list fields on the active Param by type and announces the focused row's
/// GUI text, descending into nested groups to reach the actual focused row.
/// Left/right value edits re-announce only the changed text segment.
/// </summary>
public class GroupFocusHooks
{
    private static readonly string[] WatchPrefixes =
    {
        "app.UIFlowCustomRoom", "app.UIFlowAvatarArcade",
        "app.gallery.UIFlowGallery",      // gallery top / illustration screens
        // app.UIFlowReward handled by RewardHooks (nested tier/kudos/challenge lists)
        "app.UICFNFightersProfile",       // fighter profile menu (incl. gear effects tab)
        "app.UICFNFightersList",          // friends / followed / search tabs
        "app.UICFNTop",                   // CFN top grid (players/clubs/replays/rankings)
        "app.UIFlowOnlineShop",           // shop (categories + goods lists)
        "app.esports.UI11413",            // character guides item list
        "app.esports.UI11414",            // combo trial list
        "app.esports.UIFlowESportsPauseMenu", // tutorial / combo trial pause menu
        "app.esports.UIReplayPauseMenu",      // replay playback pause menu (_Group)
        "app.UITipsMenu",                 // tips/guides menu (category + item lists)
        "app.UIStatusMenu_",              // status menu child tabs (master, super arts...)
        "app.UICFNReplay",                // CFN replays (tabs, search, results)
        "app.UICFNRanking",               // CFN rankings
        "app.UICFNDetailedMenu",          // CFN player context menu (view replays...)
        "app.UIFlowDailyTournament",      // tournaments
        "app.UIFlowServerSelect",         // server list
        "app.esports.UIFlowResultMenu",   // post-match menu (rematch / leave...)
        "app.UIFlowMailBox",              // notifications / mailbox list
        // Battle Hub navigable menus (all expose UIPartsGroup/SimpleList fields)
        "app.UIFlowAccessOtherPlayerMenu", // walk up to a player (access / challenge / profile)
        "app.UIFlowCabinetMenu",           // sit at an arcade cabinet (change character, start...)
        "app.UIFlowRivalAi",               // Rival AI server menu (train / fight your learning CPU)
        "app.UIFlowAvatarRandomMatch",     // avatar random match top (mode list)
        "app.UIFlowAvatarMatchingSetting", // avatar Battle Settings (Control Type / Button
                                           // Preset / Control Settings) + matching setting tabs;
                                           // values render as text (Modern / Custom 1)
        "app.training.UIWorldTourTrainingMenu", // avatar (World Tour) training menu — its
                                           // own type, separate from normal training
                                           // (TrainingManager); exposes _OptionGroup /
                                           // _TabSimpleList / _TrainingScrollGrid. Options
                                           // (Opponent state, Block, Counter...) and their
                                           // spin values were silent on left/right.
        // Battle Hub social / chat menus (L1+X). Each exposes a text list field:
        // FixedPhraseList=PartsScrollList, StampList=PartsScrollGrid,
        // BattleHubPlayerList=_ScrollList/_ListGroup. Stamps render as images, so
        // that grid may stay silent — the fixed phrases and player list carry text.
        "app.UIFlowFixedPhraseList",       // fixed phrases (social wheel)
        "app.UIFlowStampList",             // stamps (social wheel; icon grid)
        "app.UIFlowBattleHubPlayerList",   // player list (fast travel / send message)
        "app.UIFlowChat",                  // Battle Hub text-chat window (RootGroup/InputGroup/ButtonsGroup)
        "app.UIFlowTextList",              // preset text picker (room Comment submenu — PartsList)
        "app.UIFlowWTDeviceEmoteShortCut", // emote wheel (tabs/buttons/lists; cells may be icons)
        "app.UIFlowWTMPauseMenu",          // World Tour master-fight pause menu: Main (_menuTab),
                                           // Escape (_simpleList), Item/PerkList/BattleInfo lists, and
                                           // the move tabs OtherMoves/SpecialMoves/SuperArts
                                           // (mSkillList/ActionSkillList + category/set-type tabs)
        // app.UICFNSelectLeague / ...Detail handled by LeagueSelectHooks — their
        // grid/list cells render the rank as an icon (text is only "Unspecified"),
        // so the name must be resolved from the league data, not the focused row.
    };

    // SimpleLists whose get_SelectedItem returns the MIRROR of the focused row
    // (the room search menu announced "Ver convites" while focus was on
    // "Buscar salas"; the log showed get_SelectedItem giving item 3 for index 0
    // and item 0 for index 3). _Children, however, is in visual order, so for
    // these read _Children[SelectedIndex] directly instead of get_SelectedItem.
    private static readonly string[] ReversedListTypes =
    {
        "app.UIFlowCustomRoomSearchMenu.Param",
    };

    // Types with dedicated hooks — skipped here to avoid double announcements
    private static readonly string[] ExcludedTypes =
    {
        "app.UIFlowCustomRoomTop.Param",
        "app.UIStatusMenu_Equip.Param",   // StatusMenuHooks handles the equip tab
        "app.UIStatusMenu_SpecialMoves.Param", // StatusActionSkillHooks handles these tabs
        "app.UIStatusMenu_SuperArts.Param",
        "app.UIStatusMenu_MySetActionSkill.Param", // StatusMySetActionSkillHooks handles the Move Set screen
        "app.UIStatusMenu_Skill.Param",            // StatusSkillHooks handles the skill tree
        "app.UIFlowOnlineShopGoodsBuy.UIFlowParam", // OnlineShopBuyHooks handles the buy dialog
        "app.UIFlowCustomRoomJoin.Param",           // CustomRoomJoinHooks handles join/invitations
        // WTMPauseHooks handles these WT master-fight pause submenus (the generic
        // reader gave template junk / bare numbers); only Main (the tab bar) stays generic.
        "app.UIFlowWTMPauseMenu.SpecialMoves.Param",
        "app.UIFlowWTMPauseMenu.SuperArts.Param",
        "app.UIFlowWTMPauseMenu.OtherMoves.Param",
        "app.UIFlowWTMPauseMenu.Item.Param",
        "app.UIFlowWTMPauseMenu.Escape.Param",
        "app.UIFlowWTMPauseMenu.PerkList.Param",
        "app.UIFlowWTMPauseMenu.BattleInfo.Param",
    };

    private static readonly string[] GroupFieldTypes = { "app.UIPartsGroup", "app.UIPartsGroupScroll" };
    private static readonly string[] ListFieldTypes = { "app.UIPartsScrollList", "app.UIPartsSimpleList", "app.UIPartsScrollGrid" };

    private sealed class TrackedField
    {
        public string FieldName;
        public bool IsList;        // SelectedIndex-based instead of _FocusIndex
        public int LastIndex = -2;
        public string LastText;
        public string LastPath;    // nested focus path ("3>1") to tell row moves from value edits
    }

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;
    // 6, not 3: friend-list rows carry name, title, label and two time texts —
    // the active-hours value was cut off at three segments
    private const int MAX_ROW_TEXTS = 6;

    private sealed class TrackedParam
    {
        public string TypeName;
        public ManagedObject Param;
        public ulong Address;
        public readonly List<TrackedField> Fields = new();
    }

    // Track SEVERAL active params at once: tab lists often live on a parent
    // param (tournament menu) while the content rows live on per-tab params
    // that come and go — tracking only the newest one muted the tab switches
    private const int MAX_TRACKED_PARAMS = 4;
    private static readonly List<TrackedParam> _params = new();

    // Dedupe: several trackers can resolve the same row (shop GoodsGroup and
    // GoodsListGroup both wrap the goods list) — remember recent texts, not
    // just the last one
    private static string _lastAnnouncement;
    private static int _lastAnnouncementFrame;
    private static readonly Dictionary<string, int> _recentAnnouncements = new();
    // Multi-tracker duplicates land within one or two 5-frame polls — a longer
    // window also muted rows the user re-visited by navigating fast
    private const int DEDUPE_FRAMES = 15;

    // Only suppress other hooks when this one can actually announce something
    public static bool IsActive
    {
        get
        {
            foreach (var tp in _params)
                if (tp.Fields.Count > 0) return true;
            return false;
        }
    }

    // Suppress FocusChanged only while this hook is actually announcing rows.
    // A silently-active tracker (Battle Hub room while navigating the avatar
    // battle menu) must not mute the generic focus reader.
    private const int SUPPRESS_WINDOW_FRAMES = 240;
    public static bool ShouldSuppressFocus =>
        IsActive && _pollCounter - _lastAnnouncementFrame < SUPPRESS_WINDOW_FRAMES;

    // Focus events suppressed by ShouldSuppressFocus are queued here: if this
    // hook doesn't announce a row shortly after, the focused item's text is
    // read anyway. Without this, CFN rows outside the tracked fields stayed
    // silent during the suppression window ("sometimes reads, sometimes not")
    private static ManagedObject _fallbackItem;
    private static string _fallbackName;
    private static int _fallbackFrame;
    private const int FALLBACK_DELAY_FRAMES = 15;

    public static void QueueFocusFallback(ManagedObject selectedItem, string rawName)
    {
        _fallbackItem = selectedItem;
        _fallbackName = rawName;
        _fallbackFrame = _pollCounter;
    }

    private static void ProcessFocusFallback()
    {
        if (_fallbackItem == null || _pollCounter - _fallbackFrame < FALLBACK_DELAY_FRAMES)
            return;

        var item = _fallbackItem;
        string name = _fallbackName;
        _fallbackItem = null;
        _fallbackName = null;

        // A row announcement at or after the focus event already covered it
        if (_lastAnnouncementFrame >= _fallbackFrame) return;

        try
        {
            string text = JoinTexts(GuiTextReader.ReadControlTexts(item));
            if (string.IsNullOrEmpty(text)) return;
            if (!GameStateTracker.HasChanged("focus_item", $"{name}|{text}")) return;

            _lastAnnouncementFrame = _pollCounter;
            API.LogInfo($"[SF6Access] GroupFocus fallback [{name}]: {text}");
            ScreenReaderService.Speak(text);
        }
        catch { }
    }

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] GroupFocusHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        // Search faster while idle: a 60-frame interval made pause menus
        // wait up to a second before their first announcement
        int searchInterval = _params.Count == 0 ? 20 : POLL_SEARCH_INTERVAL;
        if (_pollCounter % searchInterval == 0)
            RefreshActiveParams();

        if (_params.Count > 0 && _pollCounter % POLL_READ_INTERVAL == 0)
            PollFields();

        ProcessFocusFallback();
    }

    private static void RefreshActiveParams()
    {
        // Single handle pass keeps global newest-first order — per-prefix
        // iteration made the lingering avatar arcade param outrank the newer
        // master menu param. Newest handle comes FIRST (verified via F9 dump).
        var candidates = new List<(string typeName, ManagedObject param)>();
        foreach (var (typeName, param) in FlowHelper.FindFlowParamsMatchingPrefixes(WatchPrefixes))
        {
            if (System.Array.IndexOf(ExcludedTypes, typeName) >= 0) continue;
            candidates.Add((typeName, param));
            if (candidates.Count >= MAX_TRACKED_PARAMS) break;
        }

        // Drop params that disappeared from the handle list
        for (int i = _params.Count - 1; i >= 0; i--)
        {
            bool present = false;
            foreach (var (unused, param) in candidates)
            {
                if (FlowHelper.AddressOf(param) == _params[i].Address) { present = true; break; }
            }
            if (!present)
            {
                API.LogInfo($"[SF6Access] GroupFocus ended: {_params[i].TypeName}");
                _params.RemoveAt(i);
            }
        }

        // Add newly appeared params, keep state of the ones already tracked
        foreach (var (typeName, param) in candidates)
        {
            ulong addr = FlowHelper.AddressOf(param);
            TrackedParam existing = null;
            foreach (var tp in _params)
            {
                if (tp.Address == addr) { existing = tp; break; }
            }
            if (existing != null)
            {
                existing.Param = param;
                // Fields may appear after the screen finishes initializing
                if (existing.Fields.Count == 0) DiscoverFields(existing);
                continue;
            }

            var entry = new TrackedParam { TypeName = typeName, Param = param, Address = addr };
            DiscoverFields(entry);
            _params.Add(entry);
            API.LogInfo($"[SF6Access] GroupFocus active: {typeName} ({entry.Fields.Count} fields)");
        }

        if (_params.Count == 0)
        {
            _lastAnnouncement = null;
            _recentAnnouncements.Clear();
        }
    }

    /// <summary>Find UIPartsGroup/list fields on the param type (walking parent types).</summary>
    private static void DiscoverFields(TrackedParam entry)
    {
        foreach (var (fieldType, cleanName, isList) in GetTrackableFields(entry.Param))
        {
            entry.Fields.Add(new TrackedField { FieldName = cleanName, IsList = isList });
            API.LogInfo($"[SF6Access] GroupFocus tracking {fieldType} '{cleanName}'");
        }
    }

    private static List<(string fieldType, string cleanName, bool isList)> GetTrackableFields(ManagedObject param)
    {
        var results = new List<(string, string, bool)>();
        var td = param?.GetTypeDefinition();

        // The avatar Battle Settings overlay reuses the big matching-setting param,
        // whose TabList drives tabs that aren't active in that overlay — tracking it
        // announced phantom tabs ("Customize Challenges"...) on Q/E that do nothing
        // in-game, and re-read the content row after each. Skip the tab list here.
        string paramTypeName = td?.FullName ?? "";
        bool skipTabFields = paramTypeName.Contains("UIFlowAvatarMatchingSetting");

        int depth = 0;
        while (td != null && depth++ < 6)
        {
            try
            {
                var fields = td.GetFields();
                if (fields != null)
                {
                    foreach (var field in fields)
                    {
                        try
                        {
                            string fieldType = field.Type?.FullName;
                            if (fieldType == null) continue;

                            bool isGroup = System.Array.IndexOf(GroupFieldTypes, fieldType) >= 0;
                            bool isList = System.Array.IndexOf(ListFieldTypes, fieldType) >= 0;
                            if (!isGroup && !isList) continue;

                            string cleanName = field.Name?.Replace("<", "").Replace(">k__BackingField", "");
                            if (skipTabFields && cleanName != null && cleanName.Contains("Tab")) continue;
                            results.Add((fieldType, cleanName, isList));
                        }
                        catch { }
                    }
                }
            }
            catch { }
            td = td.ParentType;
        }
        return results;
    }

    private static void PruneRecentAnnouncements()
    {
        var stale = new List<string>();
        foreach (var kvp in _recentAnnouncements)
        {
            if (_pollCounter - kvp.Value >= DEDUPE_FRAMES)
                stale.Add(kvp.Key);
        }
        foreach (var key in stale)
            _recentAnnouncements.Remove(key);
    }

    private static void PollFields()
    {
        foreach (var tp in _params)
            PollParamFields(tp);
    }

    private static void PollParamFields(TrackedParam tp)
    {
        foreach (var f in tp.Fields)
        {
            try
            {
                // Re-read the object each tick: it may be created after the param appears
                var obj = FlowHelper.GetObjectField(tp.Param, f.FieldName);
                if (obj == null) continue;

                int idx = f.IsList
                    ? FlowHelper.CallInt(obj, "get_SelectedIndex")
                    : FlowHelper.ReadIntField(obj, "_FocusIndex");
                if (idx < 0) continue;

                // Lists: read the actually-selected item (child order can be
                // reversed relative to SelectedIndex); groups: descend by focus
                string path = idx.ToString();
                string text;
                if (f.IsList && System.Array.IndexOf(ReversedListTypes, tp.TypeName) >= 0)
                    text = ReadReversedListRow(obj, idx, out path);
                else
                    text = f.IsList
                        ? FlowHelper.ReadSelectedItemText(obj) ?? ReadRowText(obj, idx, out path)
                        : ReadRowText(obj, idx, out path);

                bool first = f.LastIndex == -2;
                // Row moves include nested focus changes inside the same group
                // slot (create-room participants all sit under _FocusIndex 0)
                bool rowChanged = idx != f.LastIndex || path != f.LastPath;
                bool textChanged = !string.IsNullOrEmpty(text) && text != f.LastText;
                string previousText = f.LastText;

                f.LastIndex = idx;
                f.LastPath = path;
                if (!string.IsNullOrEmpty(text)) f.LastText = text;

                if (string.IsNullOrEmpty(text)) continue;
                if (first)
                {
                    // Announce the initially-focused row (queued, after the
                    // screen title) — single-row lists and one-option menus
                    // were never read otherwise. Only the field that actually
                    // holds focus, or the room screen would read all 7 fields.
                    if (!IsPartsFocused(obj)) continue;
                    AnnounceRow(f, idx, text, interrupt: false);
                    continue;
                }
                if (!rowChanged && !textChanged) continue;

                // Same row, value edited: announce only what changed.
                // Row moves announce the full row — diffing across rows dropped
                // shared label segments ("Participant" was lost in Portuguese)
                string announcement = !rowChanged
                    ? FlowHelper.DiffSegments(previousText, text)
                    : text;

                // A group row can wrap a whole button strip ("Create room. Reset
                // to defaults"): if another tracked list's focused row is one of
                // the segments, announce only that focused button
                if (rowChanged && announcement.Contains(". "))
                {
                    string specific = FindFocusedSegment(f, announcement);
                    if (specific != null) announcement = specific;
                }

                bool announced = AnnounceRow(f, idx, announcement, interrupt: true);

                // Switching tab must re-read the content row even when its text
                // is unchanged (single-option lists were silent when returning
                // to their tab) — make the other fields announce again
                if (announced && rowChanged && f.FieldName.Contains("Tab"))
                    ResetOtherFields(tp, f);
            }
            catch { }
        }
    }

    /// <summary>Dedupe, log and speak a row announcement. False when suppressed.</summary>
    private static bool AnnounceRow(TrackedField f, int idx, string announcement, bool interrupt)
    {
        // Skip when any tracker recently announced the same text
        if (_recentAnnouncements.TryGetValue(announcement, out int lastFrame) &&
            _pollCounter - lastFrame < DEDUPE_FRAMES)
            return false;
        _recentAnnouncements[announcement] = _pollCounter;
        if (_recentAnnouncements.Count > 32) PruneRecentAnnouncements();
        _lastAnnouncement = announcement;
        _lastAnnouncementFrame = _pollCounter;

        API.LogInfo($"[SF6Access] GroupFocus [{f.FieldName},{idx}]: {announcement}");
        ScreenReaderService.Speak(announcement, interrupt);
        return true;
    }

    private static void ResetOtherFields(TrackedParam tp, TrackedField current)
    {
        foreach (var other in tp.Fields)
        {
            if (other == current || other.FieldName.Contains("Tab")) continue;
            other.LastIndex = -2;
            other.LastText = null;
            other.LastPath = null;
        }
    }

    /// <summary>True when the UIParts group/list reports holding input focus.</summary>
    private static bool IsPartsFocused(ManagedObject partsObj)
    {
        var result = FlowHelper.Call(partsObj, "get_IsFocus");
        if (result is bool b) return b;
        return FlowHelper.ReadBoolField(partsObj, "_IsFocus");
    }

    /// <summary>
    /// When a multi-segment row announcement contains the focused row text of
    /// another tracked list (e.g. a button strip with its own SimpleList),
    /// return that more specific text; null when no refinement applies.
    /// </summary>
    private static string FindFocusedSegment(TrackedField current, string announcement)
    {
        var segments = announcement.Split(new[] { ". " }, System.StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return null;

        foreach (var tp in _params)
        foreach (var other in tp.Fields)
        {
            if (other == current || !other.IsList) continue;
            try
            {
                var obj = FlowHelper.GetObjectField(tp.Param, other.FieldName);
                if (obj == null) continue;

                int idx = FlowHelper.CallInt(obj, "get_SelectedIndex");
                if (idx < 0) continue;

                string text = FlowHelper.ReadSelectedItemText(obj);
                if (string.IsNullOrEmpty(text))
                {
                    var child = GetChildAt(obj, idx);
                    var control = FlowHelper.GetObjectField(child, "Control")
                        ?? FlowHelper.Call(child, "get_Control") as ManagedObject;
                    text = JoinTexts(GuiTextReader.ReadControlTexts(control));
                }
                if (string.IsNullOrEmpty(text)) continue;

                foreach (var seg in segments)
                {
                    if (seg.Trim() == text.Trim())
                        return text;
                }

                // Subset match: a group row can wrap a whole table panel (room
                // booths) whose announcement repeats every segment of the other
                // list's focused row — return just that row; the recent-
                // announcement dedupe then drops the duplicate entirely
                var rowSegs = text.Split(new[] { ". " }, System.StringSplitOptions.RemoveEmptyEntries);
                if (rowSegs.Length >= 2)
                {
                    bool allContained = true;
                    foreach (var rs in rowSegs)
                    {
                        bool found = false;
                        foreach (var seg in segments)
                        {
                            if (seg.Trim() == rs.Trim()) { found = true; break; }
                        }
                        if (!found) { allContained = false; break; }
                    }
                    if (allContained) return text;
                }
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Read a list row directly from _Children[SelectedIndex] (visual order),
    /// bypassing get_SelectedItem — which returns the mirrored row for these
    /// lists, announcing "Ver convites" while focus was on "Buscar salas".
    /// </summary>
    private static string ReadReversedListRow(ManagedObject listObj, int idx, out string path)
    {
        path = idx.ToString();
        try
        {
            var children = FlowHelper.GetObjectField(listObj, "_Children");
            int count = FlowHelper.GetListCount(children);
            if (count <= 0 || idx < 0 || idx >= count)
                return FlowHelper.ReadSelectedItemText(listObj);

            var child = FlowHelper.GetListItem(children, idx);
            var control = FlowHelper.GetObjectField(child, "Control")
                ?? FlowHelper.Call(child, "get_Control") as ManagedObject;
            string text = JoinTexts(GuiTextReader.ReadControlTexts(control));
            return !string.IsNullOrEmpty(text) ? text : FlowHelper.ReadSelectedItemText(listObj);
        }
        catch { return FlowHelper.ReadSelectedItemText(listObj); }
    }

    private static string ReadRowText(ManagedObject obj, int idx) => ReadRowText(obj, idx, out string ignoredPath);

    private static string ReadRowText(ManagedObject obj, int idx, out string focusPath)
    {
        focusPath = idx.ToString();
        var child = GetFocusedChild(obj, idx);

        // Descend nested groups to the actually focused row. Remember the
        // DEEPEST level that still holds win/loss texts — that is the full
        // replay row; the list above it spans every row and the cells below
        // it (player banner, time) read as meaningless fragments.
        ManagedObject replayRowControl = null;
        int depth = 0;
        while (child != null && depth++ < 4)
        {
            var levelControl = FlowHelper.GetObjectField(child, "Control")
                ?? FlowHelper.Call(child, "get_Control") as ManagedObject
                ?? child; // a descent step can land on a bare via.gui control

            // A replay ROW holds one win/loss text per player (two); the list
            // above it holds them for every row and a single player cell only
            // one — keep the deepest control with at least two
            if (CountReplayResults(levelControl) >= 2) replayRowControl = levelControl;

            // List-type children track focus via SelectedIndex instead of
            // _FocusIndex; mediator parts (app.UIPartsReplayList) expose ONLY
            // GetFocusChild — descend by it even with no index, keying the
            // focus path on the child's address so row moves are detected
            int subIdx = FlowHelper.ReadIntField(child, "_FocusIndex");
            if (subIdx < 0) subIdx = FlowHelper.CallInt(child, "get_SelectedIndex");

            var subChild = subIdx >= 0
                ? GetFocusedChild(child, subIdx)
                : FlowHelper.Call(child, "GetFocusChild") as ManagedObject;
            if (subChild == null) break;

            focusPath += ">" + (subIdx >= 0
                ? subIdx.ToString()
                : FlowHelper.AddressOf(subChild).ToString("x"));
            child = subChild;
        }

        if (replayRowControl != null)
        {
            string rowText = FlowHelper.FormatRowTexts(
                GuiTextReader.ReadControlTexts(replayRowControl), 10);
            if (!string.IsNullOrEmpty(rowText)) return rowText;
        }

        if (child != null)
        {
            var control = FlowHelper.GetObjectField(child, "Control")
                ?? FlowHelper.Call(child, "get_Control") as ManagedObject
                ?? child;
            string text = JoinTexts(GuiTextReader.ReadControlTexts(control));
            if (!string.IsNullOrEmpty(text)) return text;
        }

        // No child parts: stay silent. Mapping SelectedIndex onto the list
        // control's Nth text produced wrong rows (verified in the join screen).
        return null;
    }

    private static int CountReplayResults(ManagedObject control)
    {
        if (control == null) return 0;
        int count = 0;
        try
        {
            foreach (var t in GuiTextReader.ReadControlTexts(control))
            {
                if (t.Name == "e_result_") count++;
            }
        }
        catch { }
        return count;
    }

    /// <summary>Focused child of a UIPartsGroup: GetFocusChild is authoritative —
    /// _Children order can be REVERSED relative to the focus index (the trial
    /// pause menu announced the wrong row). Lists expose the focused row as
    /// SelectedItem instead.</summary>
    private static ManagedObject GetFocusedChild(ManagedObject partsObj, int idx)
    {
        var focused = FlowHelper.Call(partsObj, "GetFocusChild") as ManagedObject
            ?? FlowHelper.Call(partsObj, "get_SelectedItem") as ManagedObject;
        return focused ?? GetChildAt(partsObj, idx);
    }

    private static ManagedObject GetChildAt(ManagedObject partsObj, int idx)
    {
        var children = FlowHelper.GetObjectField(partsObj, "_Children");
        return FlowHelper.GetListItem(children, idx);
    }

    private static string JoinTexts(List<GuiTextReader.GuiText> texts) =>
        FlowHelper.FormatRowTexts(texts, MAX_ROW_TEXTS);
}
