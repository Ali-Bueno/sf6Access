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
        "app.UIFlowReward",               // rewards (fighting pass) menu
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
    };

    // Types with dedicated hooks — skipped here to avoid double announcements
    private static readonly string[] ExcludedTypes =
    {
        "app.UIFlowCustomRoomTop.Param",
        "app.UIStatusMenu_Equip.Param",   // StatusMenuHooks handles the equip tab
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
    private const int MAX_ROW_TEXTS = 3;

    private static string _activeType;
    private static ManagedObject _param;
    private static readonly List<TrackedField> _fields = new();

    // Dedupe: several trackers can resolve the same row (shop GoodsGroup and
    // GoodsListGroup both wrap the goods list) — remember recent texts, not
    // just the last one
    private static string _lastAnnouncement;
    private static int _lastAnnouncementFrame;
    private static readonly Dictionary<string, int> _recentAnnouncements = new();
    private const int DEDUPE_FRAMES = 40;

    // Only suppress other hooks when this one can actually announce something
    public static bool IsActive => _activeType != null && _fields.Count > 0;

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
        int searchInterval = _activeType == null ? 20 : POLL_SEARCH_INTERVAL;
        if (_pollCounter % searchInterval == 0)
            RefreshActiveParam();

        if (_param != null && _pollCounter % POLL_READ_INTERVAL == 0)
            PollFields();

        ProcessFocusFallback();
    }

    private static void RefreshActiveParam()
    {
        string foundType = null;
        ManagedObject foundParam = null;

        // Prefer the newest matching param that actually has trackable fields:
        // gallery keeps a fieldless Main.Param alive next to the screen params.
        // Single handle pass keeps global newest-first order — per-prefix
        // iteration made the lingering avatar arcade param outrank the newer
        // master menu param.
        var candidates = new List<(string typeName, ManagedObject param)>();
        foreach (var (typeName, param) in FlowHelper.FindFlowParamsMatchingPrefixes(WatchPrefixes))
        {
            if (System.Array.IndexOf(ExcludedTypes, typeName) >= 0) continue;
            candidates.Add((typeName, param));
        }

        // Newest handle comes FIRST in _Handles (verified via F9 dump)
        foreach (var (typeName, param) in candidates)
        {
            if (typeName == _activeType || CountTrackableFields(param) > 0)
            {
                foundType = typeName;
                foundParam = param;
                break;
            }
        }
        if (foundParam == null && candidates.Count > 0)
            (foundType, foundParam) = candidates[0];

        if (foundParam == null)
        {
            if (_activeType != null)
            {
                API.LogInfo($"[SF6Access] GroupFocus ended: {_activeType}");
                _activeType = null;
                _param = null;
                _fields.Clear();
                _lastAnnouncement = null;
                _recentAnnouncements.Clear();
            }
            return;
        }

        if (foundType == _activeType)
        {
            _param = foundParam;
            // Fields may appear after the screen finishes initializing
            if (_fields.Count == 0) DiscoverFields(foundParam);
            return;
        }

        _activeType = foundType;
        _param = foundParam;
        _fields.Clear();
        DiscoverFields(foundParam);
        API.LogInfo($"[SF6Access] GroupFocus active: {foundType} ({_fields.Count} fields)");
    }

    /// <summary>Find UIPartsGroup/list fields on the param type (walking parent types).</summary>
    private static void DiscoverFields(ManagedObject param)
    {
        foreach (var (fieldType, cleanName, isList) in GetTrackableFields(param))
        {
            _fields.Add(new TrackedField { FieldName = cleanName, IsList = isList });
            API.LogInfo($"[SF6Access] GroupFocus tracking {fieldType} '{cleanName}'");
        }
    }

    private static int CountTrackableFields(ManagedObject param)
    {
        int count = 0;
        foreach (var unused in GetTrackableFields(param)) count++;
        return count;
    }

    private static List<(string fieldType, string cleanName, bool isList)> GetTrackableFields(ManagedObject param)
    {
        var results = new List<(string, string, bool)>();
        var td = param?.GetTypeDefinition();
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
        foreach (var f in _fields)
        {
            try
            {
                // Re-read the object each tick: it may be created after the param appears
                var obj = FlowHelper.GetObjectField(_param, f.FieldName);
                if (obj == null) continue;

                int idx = f.IsList
                    ? FlowHelper.CallInt(obj, "get_SelectedIndex")
                    : FlowHelper.ReadIntField(obj, "_FocusIndex");
                if (idx < 0) continue;

                // Lists: read the actually-selected item (child order can be
                // reversed relative to SelectedIndex); groups: descend by focus
                string path = idx.ToString();
                string text = f.IsList
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

                if (first || string.IsNullOrEmpty(text)) continue;
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

                // Skip when any tracker recently announced the same text
                if (_recentAnnouncements.TryGetValue(announcement, out int lastFrame) &&
                    _pollCounter - lastFrame < DEDUPE_FRAMES)
                    continue;
                _recentAnnouncements[announcement] = _pollCounter;
                if (_recentAnnouncements.Count > 32) PruneRecentAnnouncements();
                _lastAnnouncement = announcement;
                _lastAnnouncementFrame = _pollCounter;

                API.LogInfo($"[SF6Access] GroupFocus [{f.FieldName},{idx}]: {announcement}");
                ScreenReaderService.Speak(announcement);
            }
            catch { }
        }
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

        foreach (var other in _fields)
        {
            if (other == current || !other.IsList) continue;
            try
            {
                var obj = FlowHelper.GetObjectField(_param, other.FieldName);
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
            }
            catch { }
        }
        return null;
    }

    private static string ReadRowText(ManagedObject obj, int idx) => ReadRowText(obj, idx, out string ignoredPath);

    private static string ReadRowText(ManagedObject obj, int idx, out string focusPath)
    {
        focusPath = idx.ToString();
        var child = GetFocusedChild(obj, idx);

        // Descend nested groups to the actually focused row
        int depth = 0;
        while (child != null && depth++ < 4)
        {
            int subIdx = FlowHelper.ReadIntField(child, "_FocusIndex");
            if (subIdx < 0) break;
            var subChild = GetFocusedChild(child, subIdx);
            if (subChild == null) break;
            focusPath += ">" + subIdx;
            child = subChild;
        }

        if (child != null)
        {
            var control = FlowHelper.GetObjectField(child, "Control")
                ?? FlowHelper.Call(child, "get_Control") as ManagedObject;
            string text = JoinTexts(GuiTextReader.ReadControlTexts(control));
            if (!string.IsNullOrEmpty(text)) return text;
        }

        // No child parts: stay silent. Mapping SelectedIndex onto the list
        // control's Nth text produced wrong rows (verified in the join screen).
        return null;
    }

    /// <summary>Focused child of a UIPartsGroup: GetFocusChild is authoritative —
    /// _Children order can be REVERSED relative to the focus index (the trial
    /// pause menu announced the wrong row).</summary>
    private static ManagedObject GetFocusedChild(ManagedObject partsObj, int idx)
    {
        var focused = FlowHelper.Call(partsObj, "GetFocusChild") as ManagedObject;
        return focused ?? GetChildAt(partsObj, idx);
    }

    private static ManagedObject GetChildAt(ManagedObject partsObj, int idx)
    {
        var children = FlowHelper.GetObjectField(partsObj, "_Children");
        return FlowHelper.GetListItem(children, idx);
    }

    private static string JoinTexts(List<GuiTextReader.GuiText> texts)
    {
        var parts = new List<string>();
        foreach (var t in texts)
        {
            if (string.IsNullOrWhiteSpace(t.Text)) continue;
            parts.Add(t.Text.Trim());
            if (parts.Count >= MAX_ROW_TEXTS) break;
        }
        return parts.Count > 0 ? string.Join(". ", parts) : null;
    }
}
