using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the ranked/casual match standby screen (app.UIFlowMatchingSetting).
/// Announces league points on entry, tab changes, and changes of any of the
/// on-screen value texts (BGM, commentator, controller, outfit, etc.).
/// Focused-row reading is handled by the generic FocusChanged path.
/// </summary>
public class MatchingSettingHooks
{
    private const string PARAM_TYPE = "app.UIFlowMatchingSetting.Param";

    // Value texts shown on the settings tabs — announced when they change
    private static readonly string[] ValueTextFields =
    {
        "TextOperation", "TextPreset", "TextAnimType", "TextSkin", "TextEffect",
        "TextEffectColor", "TextSound", "TextSoundType", "TextController", "TextBgm",
        "TextCommentatorEnable", "TextCommentator", "TextCaster", "TextCheerOnline",
        "TextCommentatorVolume", "TextCommentatorSubtitles", "TextSide", "TextBattleHud",
        "TextLeaguePoint", "TextMasterLeaguePoint", "TextCirtifiedCount",
    };

    /// <summary>True for unloaded placeholder values like "--- PL".</summary>
    private static bool IsPlaceholder(string text)
    {
        return string.IsNullOrEmpty(text) || text.Contains("--");
    }

    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 10;

    private static ManagedObject _param;
    private static ManagedObject _tabList;

    // Row groups of every settings tab — only the matchmaking tab was tracked
    // before, leaving the battle and profile tabs silent. partsField == null
    // means the group lives directly on the Param.
    private static readonly (string partsField, string groupField)[] GroupSources =
    {
        (null, "Group"),
        ("MatchingSettingMatching", "mGroup"),
        ("MatchingSettingBattle", "mGroup"),
        ("MatchingSettingBattle", "mSimpleList"),
        ("FighterProfileSetting", "mGroup"),
        ("FighterProfileSetting", "mTopList"),
    };
    private static readonly int[] _lastFocusIdx = new int[GroupSources.Length];
    private static readonly string[] _lastFocusText = new string[GroupSources.Length];
    private static readonly Dictionary<string, ManagedObject> _valueTexts = new();
    private static readonly Dictionary<string, string> _lastValues = new();
    private static int _lastTabIdx = -1;

    public static bool IsInMatchingSetting => _isActive;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] MatchingSettingHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (!_isActive)
        {
            if (_pollCounter % POLL_SEARCH_INTERVAL != 0) return;
            TryActivate();
            return;
        }

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var current = FlowHelper.TrackFlowParam(PARAM_TYPE, _param, out bool changed);
            if (current == null)
            {
                Reset();
                return;
            }
            if (changed)
                TryActivate(); // menu was recreated — re-bind param and child caches
        }

        if (_pollCounter % POLL_READ_INTERVAL == 0)
        {
            PollTab();
            PollGroupFocus();
            PollValueChanges();
        }
    }

    /// <summary>
    /// Announce the focused row by reading its child parts' GUI texts.
    /// Re-announces when the row's text changes in place (left/right value edits).
    /// </summary>
    private static void PollGroupFocus()
    {
        for (int g = 0; g < GroupSources.Length; g++)
        {
            // Re-resolve each tick: tab parts initialize after the Param appears
            var group = ResolveGroup(g);
            if (group == null) continue;

            int idx = FlowHelper.ReadIntField(group, "_FocusIndex");
            if (idx < 0) continue;

            string text = null;
            try
            {
                // GetFocusChild is authoritative — _Children order can be
                // reversed relative to the focus index
                var child = FlowHelper.Call(group, "GetFocusChild") as ManagedObject;
                if (child == null)
                {
                    var children = FlowHelper.GetObjectField(group, "_Children");
                    child = FlowHelper.GetListItem(children, idx);
                }
                var control = FlowHelper.GetObjectField(child, "Control")
                    ?? FlowHelper.Call(child, "get_Control") as ManagedObject;
                text = GuiTextReader.ReadControlTextJoined(control);
            }
            catch { }

            bool first = _lastFocusIdx[g] == -2;
            bool indexChanged = idx != _lastFocusIdx[g];
            bool textChanged = !string.IsNullOrEmpty(text) && text != _lastFocusText[g];
            string previousText = _lastFocusText[g];

            _lastFocusIdx[g] = idx;
            if (!string.IsNullOrEmpty(text)) _lastFocusText[g] = text;

            if (first || string.IsNullOrEmpty(text)) continue;
            if (!indexChanged && !textChanged) continue;

            // Same row, value edited with left/right: announce only the new value
            string announcement = !indexChanged
                ? FlowHelper.DiffSegments(previousText, text)
                : text;

            API.LogInfo($"[SF6Access] MatchingSetting focus [g{g},{idx}]: {announcement}");
            ScreenReaderService.Speak(announcement);
        }
    }

    private static ManagedObject ResolveGroup(int index)
    {
        var (partsField, groupField) = GroupSources[index];
        var owner = partsField == null ? _param : FlowHelper.GetObjectField(_param, partsField);
        return FlowHelper.GetObjectField(owner, groupField);
    }

    private static void TryActivate()
    {
        var param = FlowHelper.FindFlowParam(PARAM_TYPE);
        if (param == null) return;

        _param = param;
        _tabList = FlowHelper.GetObjectField(param, "TabList");

        for (int g = 0; g < GroupSources.Length; g++)
        {
            _lastFocusIdx[g] = -2;
            _lastFocusText[g] = null;
        }

        _lastTabIdx = -1;
        _valueTexts.Clear();
        _lastValues.Clear();

        foreach (var field in ValueTextFields)
        {
            var text = FlowHelper.GetObjectField(param, field);
            if (text == null) continue;
            _valueTexts[field] = text;
            _lastValues[field] = FlowHelper.ReadGuiText(text);
        }

        _isActive = true;
        API.LogInfo($"[SF6Access] MatchingSetting active (tabList={_tabList != null}, " +
            $"valueTexts={_valueTexts.Count})");

        AnnounceLeagueInfo();
    }

    /// <summary>Announce rank/league point info shown on the matching tab.</summary>
    private static void AnnounceLeagueInfo()
    {
        var parts = new List<string>();
        foreach (var field in new[] { "TextLeaguePoint", "TextMasterLeaguePoint", "TextCirtifiedCount" })
        {
            string text = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(_param, field));
            if (!IsPlaceholder(text)) parts.Add(text);
        }
        if (parts.Count == 0) return;

        string announcement = string.Join(". ", parts);
        API.LogInfo($"[SF6Access] MatchingSetting league info: {announcement}");
        ScreenReaderService.Speak(announcement, interrupt: false);
    }

    private static void PollTab()
    {
        if (_tabList == null) return;

        int idx = FlowHelper.CallInt(_tabList, "get_SelectedIndex");
        if (idx < 0 || idx == _lastTabIdx) return;

        bool first = _lastTabIdx == -1;
        _lastTabIdx = idx;
        if (first) return;

        // Read the tab's on-screen label from the tab list control
        string label = null;
        try
        {
            var control = FlowHelper.GetObjectField(_tabList, "Control")
                ?? FlowHelper.Call(_tabList, "get_Control") as ManagedObject;
            var texts = GuiTextReader.ReadControlTexts(control);
            if (idx < texts.Count) label = texts[idx].Text;
        }
        catch { }

        string announcement = !string.IsNullOrEmpty(label) ? label : $"Tab {idx + 1}";
        API.LogInfo($"[SF6Access] MatchingSetting tab [{idx}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    private static void PollValueChanges()
    {
        foreach (var pair in _valueTexts)
        {
            string value = FlowHelper.ReadGuiText(pair.Value);
            if (string.IsNullOrEmpty(value)) continue;

            _lastValues.TryGetValue(pair.Key, out var last);
            if (value == last) continue;
            _lastValues[pair.Key] = value;

            if (last == null) continue; // First read, don't announce
            if (IsPlaceholder(value)) continue; // Unloaded "---" values

            API.LogInfo($"[SF6Access] MatchingSetting {pair.Key}: {value}");
            ScreenReaderService.Speak(value);
        }
    }

    private static void Reset()
    {
        API.LogInfo("[SF6Access] MatchingSetting ended");
        _isActive = false;
        _param = null;
        _tabList = null;
        for (int g = 0; g < GroupSources.Length; g++)
        {
            _lastFocusIdx[g] = -2;
            _lastFocusText[g] = null;
        }
        _valueTexts.Clear();
        _lastValues.Clear();
        _lastTabIdx = -1;
    }
}
