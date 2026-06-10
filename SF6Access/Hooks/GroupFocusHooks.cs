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
    private static readonly string[] WatchPrefixes = { "app.UIFlowCustomRoom", "app.UIFlowAvatarArcade" };

    // Types with dedicated hooks — skipped here to avoid double announcements
    private static readonly string[] ExcludedTypes = { "app.UIFlowCustomRoomTop.Param" };

    private static readonly string[] GroupFieldTypes = { "app.UIPartsGroup", "app.UIPartsGroupScroll" };
    private static readonly string[] ListFieldTypes = { "app.UIPartsScrollList", "app.UIPartsSimpleList" };

    private sealed class TrackedField
    {
        public string FieldName;
        public bool IsList;        // SelectedIndex-based instead of _FocusIndex
        public int LastIndex = -2;
        public string LastText;
    }

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;
    private const int MAX_ROW_TEXTS = 3;

    private static string _activeType;
    private static ManagedObject _param;
    private static readonly List<TrackedField> _fields = new();

    // Dedupe: two trackers can resolve the same row (e.g. MenuGroup + ButtonList)
    private static string _lastAnnouncement;
    private static int _lastAnnouncementFrame;
    private const int DEDUPE_FRAMES = 40;

    // Contextual guide text (GameGuideWidget) — the focused item's description
    // on screens whose item labels are images (avatar arcade menu)
    private static string _lastGuideText;

    public static bool IsActive => _activeType != null;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] GroupFocusHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
            RefreshActiveParam();

        if (_param != null && _pollCounter % POLL_READ_INTERVAL == 0)
        {
            PollFields();
            if (_pollCounter % 10 == 0)
                PollGuideText();
        }
    }

    /// <summary>Announce changes of the on-screen guide description widget.</summary>
    private static void PollGuideText()
    {
        try
        {
            var texts = GuiTextReader.ReadTextsByOwner("GameGuideWidget");
            string text = JoinTexts(texts);
            if (string.IsNullOrEmpty(text) || text == _lastGuideText)
                return;

            _lastGuideText = text;

            API.LogInfo($"[SF6Access] GroupFocus guide: {text}");
            ScreenReaderService.Speak(text, interrupt: false);
        }
        catch { }
    }

    private static void RefreshActiveParam()
    {
        string foundType = null;
        ManagedObject foundParam = null;

        foreach (var prefix in WatchPrefixes)
        {
            var matches = FlowHelper.FindFlowParamsByPrefix(prefix);
            // Newest handle comes FIRST in _Handles (verified via F9 dump)
            foreach (var (typeName, param) in matches)
            {
                if (System.Array.IndexOf(ExcludedTypes, typeName) >= 0) continue;
                foundType = typeName;
                foundParam = param;
                break;
            }
            if (foundParam != null) break;
        }

        if (foundParam == null)
        {
            if (_activeType != null)
            {
                API.LogInfo($"[SF6Access] GroupFocus ended: {_activeType}");
                _activeType = null;
                _param = null;
                _fields.Clear();
                _lastAnnouncement = null;
                _lastGuideText = null;
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
        var td = param.GetTypeDefinition();
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
                            _fields.Add(new TrackedField { FieldName = cleanName, IsList = isList });
                            API.LogInfo($"[SF6Access] GroupFocus tracking {fieldType} '{cleanName}'");
                        }
                        catch { }
                    }
                }
            }
            catch { }
            td = td.ParentType;
        }
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

                string text = ReadRowText(obj, idx);

                bool first = f.LastIndex == -2;
                bool indexChanged = idx != f.LastIndex;
                bool textChanged = !string.IsNullOrEmpty(text) && text != f.LastText;
                string previousText = f.LastText;

                f.LastIndex = idx;
                if (!string.IsNullOrEmpty(text)) f.LastText = text;

                if (first || string.IsNullOrEmpty(text)) continue;
                if (!indexChanged && !textChanged) continue;

                // Same row, value edited: announce only what changed
                string announcement = !indexChanged
                    ? FlowHelper.DiffSegments(previousText, text)
                    : text;

                // Skip when another tracker just announced the same row
                if (announcement == _lastAnnouncement &&
                    _pollCounter - _lastAnnouncementFrame < DEDUPE_FRAMES)
                    continue;
                _lastAnnouncement = announcement;
                _lastAnnouncementFrame = _pollCounter;

                API.LogInfo($"[SF6Access] GroupFocus [{f.FieldName},{idx}]: {announcement}");
                ScreenReaderService.Speak(announcement);
            }
            catch { }
        }
    }

    private static string ReadRowText(ManagedObject obj, int idx)
    {
        var child = GetChildAt(obj, idx);

        // Descend nested groups to the actually focused row
        int depth = 0;
        while (child != null && depth++ < 4)
        {
            int subIdx = FlowHelper.ReadIntField(child, "_FocusIndex");
            if (subIdx < 0) break;
            var subChild = GetChildAt(child, subIdx);
            if (subChild == null) break;
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
