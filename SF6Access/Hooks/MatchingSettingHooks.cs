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
    // before, leaving the battle and profile tabs silent. The Param's own
    // Group wraps whole tab panels: the poller's segment cap skips those and
    // the nested groups read the actual row.
    private static readonly GroupFocusPoller FocusPoller = new(
        "MatchingSetting", announceFirst: false,
        new GroupFocusPoller.Source(null, "Group"),
        new GroupFocusPoller.Source("MatchingSettingMatching", "mGroup"),
        new GroupFocusPoller.Source("MatchingSettingBattle", "mGroup"),
        new GroupFocusPoller.Source("MatchingSettingBattle", "mSimpleList"),
        new GroupFocusPoller.Source("FighterProfileSetting", "mGroup"),
        new GroupFocusPoller.Source("FighterProfileSetting", "mTopList"));

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
            FocusPoller.Poll(_param);
            PollValueChanges();
        }
    }

    private static void TryActivate()
    {
        var param = FlowHelper.FindFlowParam(PARAM_TYPE);
        if (param == null) return;

        _param = param;
        _tabList = FlowHelper.GetObjectField(param, "TabList");

        FocusPoller.Reset();
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
        FocusPoller.Reset();
        _valueTexts.Clear();
        _lastValues.Clear();
        _lastTabIdx = -1;
    }
}
