using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the training shortcut settings menu (app.UIFlowShortcutSetting).
/// Param fields (verified via F9 dump): _MenuList (UIPartsGroupScroll with _FocusIndex)
/// and ShortcutData (app.ShortcutSettingData[] with ItemMessage/GuideMessage Guids).
/// Migrated to ScreenAdapter.
/// </summary>
public sealed class ShortcutSettingHooks : SingleParamScreenAdapter
{
    private const string PARAM = "app.UIFlowShortcutSetting.Param";
    protected override string ParamType => PARAM;

    private ManagedObject _menuList;
    private ManagedObject _shortcutData;
    private int _lastFocusIndex = -2;
    private string _lastRowText;
    private bool? _lastRowState;

    // PlayState strings the game assigns to each row's on/off switch control
    // (Param constants SWITCH_STATE_ON / SWITCH_STATE_OFF, read once from TDB)
    private readonly string _stateOn;
    private readonly string _stateOff;

    public ShortcutSettingHooks()
    {
        SearchInterval = 60;
        ReadInterval = 5;
        try
        {
            var td = TDB.Get().FindType(PARAM);
            _stateOn = td?.GetField("SWITCH_STATE_ON")?.GetDataBoxed(typeof(string), 0, false) as string;
            _stateOff = td?.GetField("SWITCH_STATE_OFF")?.GetDataBoxed(typeof(string), 0, false) as string;
        }
        catch { }
        API.LogInfo($"[SF6Access] ShortcutSettingHooks initialized (on='{_stateOn}', off='{_stateOff}')");
    }

    protected override void OnBind()
    {
        _menuList = FlowHelper.GetObjectField(Param, "_MenuList");
        _shortcutData = FlowHelper.GetObjectField(Param, "ShortcutData");
        _lastFocusIndex = -2;

        API.LogInfo($"[SF6Access] Shortcut settings active (menuList={_menuList != null}, " +
            $"data={FlowHelper.GetListCount(_shortcutData)} items)");

        PollFocus(); // baseline the focused row without announcing
    }

    protected override void OnExit()
    {
        API.LogInfo("[SF6Access] Shortcut settings ended");
        _menuList = null;
        _shortcutData = null;
        _lastFocusIndex = -2;
        _lastRowText = null;
        _lastRowState = null;
    }

    protected override void Poll() => PollFocus();

    private void PollFocus()
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
    private void PollRowStateChange()
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

    private ManagedObject FocusedRowControl()
    {
        try
        {
            var child = FlowHelper.Call(_menuList, "GetFocusChild") as ManagedObject;
            return FlowHelper.GetObjectField(child, "Control")
                ?? FlowHelper.Call(child, "get_Control") as ManagedObject;
        }
        catch { return null; }
    }

    private string ReadFocusedRowText()
    {
        return GuiTextReader.ReadControlTextJoined(FocusedRowControl());
    }

    /// <summary>True/false when the row's switch PlayState matches the game's
    /// SWITCH_STATE_ON/OFF constants, null when undetermined.</summary>
    private bool? ReadFocusedRowState()
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

    private void AnnounceItem(int index)
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
}
