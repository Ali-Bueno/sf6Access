using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the training shortcut settings menu (app.UIFlowShortcutSetting).
/// Param fields (verified via F9 dump): _MenuList (UIPartsGroupScroll with _FocusIndex)
/// and ShortcutData (app.ShortcutSettingData[] with ItemMessage/GuideMessage Guids).
/// </summary>
public class ShortcutSettingHooks
{
    private const string PARAM_TYPE = "app.UIFlowShortcutSetting.Param";

    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    private static ManagedObject _param;
    private static ManagedObject _menuList;
    private static ManagedObject _shortcutData;
    private static int _lastFocusIndex = -2;
    private static string _lastRowText;
    private static bool? _lastRowState;

    // PlayState strings the game assigns to each row's on/off switch control
    // (Param constants SWITCH_STATE_ON / SWITCH_STATE_OFF, read once from TDB)
    private static string _stateOn;
    private static string _stateOff;

    public static bool IsInShortcutSetting => _isActive;

    [PluginEntryPoint]
    public static void Initialize()
    {
        try
        {
            var td = TDB.Get().FindType(PARAM_TYPE);
            _stateOn = td?.GetField("SWITCH_STATE_ON")?.GetDataBoxed(typeof(string), 0, false) as string;
            _stateOff = td?.GetField("SWITCH_STATE_OFF")?.GetDataBoxed(typeof(string), 0, false) as string;
        }
        catch { }
        API.LogInfo($"[SF6Access] ShortcutSettingHooks initialized (on='{_stateOn}', off='{_stateOff}')");
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
            PollFocus();
    }

    private static void TryActivate()
    {
        var param = FlowHelper.FindFlowParam(PARAM_TYPE);
        if (param == null) return;

        _param = param;
        _menuList = FlowHelper.GetObjectField(param, "_MenuList");
        _shortcutData = FlowHelper.GetObjectField(param, "ShortcutData");
        _lastFocusIndex = -2;
        _isActive = true;

        API.LogInfo($"[SF6Access] Shortcut settings active (menuList={_menuList != null}, " +
            $"data={FlowHelper.GetListCount(_shortcutData)} items)");

        PollFocus();
    }

    private static void PollFocus()
    {
        if (_menuList == null) return;

        int focusIdx = FlowHelper.ReadIntField(_menuList, "_FocusIndex");
        if (focusIdx < 0) return;

        if (focusIdx == _lastFocusIndex)
        {
            PollRowStateChange();
            return;
        }

        bool first = _lastFocusIndex == -2;
        _lastFocusIndex = focusIdx;
        _lastRowText = ReadFocusedRowText();
        _lastRowState = ReadFocusedRowState();
        if (first) return;

        AnnounceItem(focusIdx);
    }

    /// <summary>
    /// Same row: detect the on/off toggle. The state is the switch control's
    /// PlayState (the switch itself is graphical); row text changes too on
    /// some rows and is preferred because it's localized.
    /// </summary>
    private static void PollRowStateChange()
    {
        string text = ReadFocusedRowText();
        if (!string.IsNullOrEmpty(text) && _lastRowText != null && text != _lastRowText)
        {
            string announcement = FlowHelper.DiffSegments(_lastRowText, text);
            _lastRowText = text;
            _lastRowState = ReadFocusedRowState();
            API.LogInfo($"[SF6Access] Shortcut row changed: {announcement}");
            ScreenReaderService.Speak(announcement);
            return;
        }
        if (!string.IsNullOrEmpty(text) && _lastRowText == null) _lastRowText = text;

        bool? state = ReadFocusedRowState();
        if (state == null || state == _lastRowState) return;

        bool firstState = _lastRowState == null;
        _lastRowState = state;
        if (firstState) return;

        // The switch has no text equivalent — spoken label is a last resort
        string label = state.Value ? "On" : "Off";
        API.LogInfo($"[SF6Access] Shortcut toggle: {label}");
        ScreenReaderService.Speak(label);
    }

    private static ManagedObject FocusedRowControl()
    {
        try
        {
            var child = FlowHelper.Call(_menuList, "GetFocusChild") as ManagedObject;
            return FlowHelper.GetObjectField(child, "Control")
                ?? FlowHelper.Call(child, "get_Control") as ManagedObject;
        }
        catch { return null; }
    }

    private static string ReadFocusedRowText()
    {
        return GuiTextReader.ReadControlTextJoined(FocusedRowControl());
    }

    /// <summary>True/false when the row's switch PlayState matches the game's
    /// SWITCH_STATE_ON/OFF constants, null when undetermined.</summary>
    private static bool? ReadFocusedRowState()
    {
        if (string.IsNullOrEmpty(_stateOn) && string.IsNullOrEmpty(_stateOff)) return null;
        var control = FocusedRowControl();
        if (control == null) return null;

        var states = new System.Collections.Generic.List<string>();
        GuiTextReader.ReadPlayStates(control, states);
        foreach (var s in states)
        {
            if (!string.IsNullOrEmpty(_stateOn) && s == _stateOn) return true;
            if (!string.IsNullOrEmpty(_stateOff) && s == _stateOff) return false;
        }
        return null;
    }

    private static void AnnounceItem(int index)
    {
        var data = FlowHelper.GetListItem(_shortcutData, index);
        if (data == null)
        {
            API.LogInfo($"[SF6Access] Shortcut item [{index}]: no data");
            return;
        }

        string name = FlowHelper.ResolveGuidField(data, "ItemMessage");
        string guide = FlowHelper.ResolveGuidField(data, "GuideMessage");

        string announcement = name;

        // Current on/off state right after the name
        if (_lastRowState != null)
        {
            string stateLabel = _lastRowState.Value ? "On" : "Off";
            announcement = string.IsNullOrEmpty(announcement) ? stateLabel : $"{announcement}. {stateLabel}";
        }

        if (!string.IsNullOrEmpty(guide) && guide != name)
            announcement = string.IsNullOrEmpty(announcement) ? guide : $"{announcement}. {guide}";

        if (string.IsNullOrEmpty(announcement))
        {
            announcement = $"Shortcut {index + 1}";
        }

        API.LogInfo($"[SF6Access] Shortcut [{index}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    private static void Reset()
    {
        API.LogInfo("[SF6Access] Shortcut settings ended");
        _isActive = false;
        _param = null;
        _menuList = null;
        _shortcutData = null;
        _lastFocusIndex = -2;
        _lastRowText = null;
        _lastRowState = null;
    }
}
