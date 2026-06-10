using System;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the control preset menu (app.UIFlowKeyConfig.Menu).
/// This screen is reached from Options or from fighter settings and contains:
/// left column spins (control type, preset, negative edge, low stick sensitivity),
/// a menu list (Edit / Initialize / Copy / Test Input) and the right-side list
/// with one row per action and its assigned button/key.
/// Spin values are read from the on-screen via.gui.Text components.
/// </summary>
public class KeyConfigHooks
{
    private const string PARAM_TYPE = "app.UIFlowKeyConfig.Menu.Param";

    // UIKeyConfig.LeftGroupItemId values
    private const int LEFT_MODE = 0;
    private const int LEFT_PRESET = 1;
    private const int LEFT_NEGATIVE_EDGE = 2;
    private const int LEFT_LOW_STICK = 3;
    private const int LEFT_LIST = 4;

    private static readonly string[] LeftGroupLabels =
        { "Control Type", "Preset", "Negative Edge", "Low Stick Sensitivity" };

    private static readonly string[] ListItemFallbacks =
        { "Edit", "Initialize", "Copy", "Test Input" };

    private static readonly string[] SpinTextFields =
        { "TextSpinMode", "TextSpinPreset", "TextSpinNegativeEdge", "TextSpinLowStickSensitivity" };

    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    private static ManagedObject _param;
    private static ManagedObject _menuMessageData;
    private static ManagedObject _partsListSetting;
    private static readonly ManagedObject[] _spinTexts = new ManagedObject[4];
    private static int _gameMode;

    private static int _lastLeftId = -2;
    private static int _lastListItemId = -2;
    private static int _lastSettingIdx = -1;
    private static readonly string[] _lastSpinValues = new string[4];

    public static bool IsInKeyConfig => _isActive;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] KeyConfigHooks initialized");
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

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0 && FlowHelper.FindFlowParam(PARAM_TYPE) == null)
        {
            Reset();
            return;
        }

        if (_pollCounter % POLL_READ_INTERVAL == 0)
        {
            PollFocus();
            PollSpinValues();
            PollSettingList();
        }
    }

    private static void TryActivate()
    {
        var param = FlowHelper.FindFlowParam(PARAM_TYPE);
        if (param == null) return;

        _param = param;
        _menuMessageData = FlowHelper.GetObjectField(param, "MenuMessageData");
        _partsListSetting = FlowHelper.GetObjectField(param, "PartsListSetting");
        for (int i = 0; i < SpinTextFields.Length; i++)
            _spinTexts[i] = FlowHelper.GetObjectField(param, SpinTextFields[i]);

        var inParam = FlowHelper.GetObjectField(param, "In");
        _gameMode = FlowHelper.ReadIntField(inParam, "GameMode", 0);

        _lastLeftId = -2;
        _lastListItemId = -2;
        _lastSettingIdx = -1;
        for (int i = 0; i < _lastSpinValues.Length; i++)
            _lastSpinValues[i] = FlowHelper.ReadGuiText(_spinTexts[i]);

        _isActive = true;
        API.LogInfo($"[SF6Access] KeyConfig active (gameMode={_gameMode}, " +
            $"msgData={_menuMessageData != null}, settingList={_partsListSetting != null}, " +
            $"spins={Array.FindAll(_spinTexts, s => s != null).Length})");

        PollFocus();
    }

    // --- Focus on the left column (spins + menu list) ---

    private static void PollFocus()
    {
        if (_param == null) return;

        int leftId = FlowHelper.CallInt(_param, "GetFocusLeftGroupItemId", -2);
        int listItemId = FlowHelper.CallInt(_param, "GetLeftListItemId", -2);

        bool leftChanged = leftId != _lastLeftId && leftId >= 0;
        bool listChanged = listItemId != _lastListItemId && leftId == LEFT_LIST;

        bool first = _lastLeftId == -2;
        _lastLeftId = leftId >= 0 ? leftId : _lastLeftId;
        _lastListItemId = listItemId;

        if (first) return; // Initialize silently; announce only user navigation

        if (leftChanged || listChanged)
            AnnounceLeftGroup(leftId, listItemId);
    }

    private static void AnnounceLeftGroup(int leftId, int listItemId)
    {
        string announcement = null;

        if (leftId >= LEFT_MODE && leftId <= LEFT_LOW_STICK)
        {
            string value = FlowHelper.ReadGuiText(_spinTexts[leftId]);
            string label = LeftGroupLabels[leftId];
            announcement = string.IsNullOrEmpty(value) ? label : $"{label}: {value}";

            string guide = GetLeftGroupGuide(leftId);
            if (!string.IsNullOrEmpty(guide))
                announcement = $"{announcement}. {guide}";
        }
        else if (leftId == LEFT_LIST)
        {
            announcement = ResolveListItemName(listItemId);
        }

        if (string.IsNullOrEmpty(announcement)) return;

        API.LogInfo($"[SF6Access] KeyConfig focus [{leftId}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    private static string GetLeftGroupGuide(int leftId)
    {
        var msg = FlowHelper.Call(_menuMessageData, "GetLeftGroupItemMessage", leftId) as ManagedObject;
        return FlowHelper.ResolveGuidField(msg, "Guide");
    }

    private static string ResolveListItemName(int listItemId)
    {
        if (listItemId < 0) return null;

        var msg = FlowHelper.Call(_menuMessageData, "GetListItemMessage", listItemId) as ManagedObject;
        string name = FlowHelper.ResolveGuidField(msg, "Name");
        if (!string.IsNullOrEmpty(name)) return name;

        return listItemId < ListItemFallbacks.Length ? ListItemFallbacks[listItemId] : $"Item {listItemId}";
    }

    // --- Spin value changes (left/right on a spin) ---

    private static void PollSpinValues()
    {
        for (int i = 0; i < _spinTexts.Length; i++)
        {
            if (_spinTexts[i] == null) continue;

            string value = FlowHelper.ReadGuiText(_spinTexts[i]);
            if (string.IsNullOrEmpty(value) || value == _lastSpinValues[i]) continue;

            bool first = _lastSpinValues[i] == null;
            _lastSpinValues[i] = value;
            if (first) continue;

            API.LogInfo($"[SF6Access] KeyConfig spin [{i}]: {value}");
            ScreenReaderService.Speak(value);
        }
    }

    // --- Setting rows (right-side list: action name + assigned input) ---

    private static void PollSettingList()
    {
        if (_partsListSetting == null) return;

        int idx = FlowHelper.CallInt(_partsListSetting, "get_SelectedIndex");
        if (idx < 0 || idx == _lastSettingIdx) return;

        bool first = _lastSettingIdx == -1;
        _lastSettingIdx = idx;
        if (first) return;

        string announcement = ReadSettingRow(idx);
        if (string.IsNullOrEmpty(announcement)) return;

        API.LogInfo($"[SF6Access] KeyConfig setting [{idx}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    private static string ReadSettingRow(int listIndex)
    {
        var settingParam = FlowHelper.Call(_param, "GetSettingParam", listIndex) as ManagedObject;
        if (settingParam == null) return null;

        string name = null;
        try
        {
            var method = settingParam.GetTypeDefinition()?.GetMethod("GetName(app.AppDefine.GameMode)");
            name = method?.InvokeBoxed(typeof(string), settingParam, new object[] { _gameMode }) as string;
        }
        catch { }
        if (string.IsNullOrEmpty(name))
            name = FlowHelper.Call(settingParam, "GetName") as string;
        name = FlowHelper.CleanTags(name);

        // Assigned input: the icon string is what's shown on screen (may be an icon tag)
        string input = FlowHelper.CleanTags(FlowHelper.Call(settingParam, "GetInputIcon") as string);
        if (string.IsNullOrEmpty(input))
        {
            // Last resort: raw button/key enum value, logged for future mapping
            var button = FlowHelper.Call(settingParam, "GetGamePadButton") ?? FlowHelper.Call(settingParam, "GetKeyboardKey");
            if (button != null)
            {
                API.LogInfo($"[SF6Access] KeyConfig raw input value: {button}");
                input = button.ToString();
            }
        }

        if (string.IsNullOrEmpty(name)) return input;
        return string.IsNullOrEmpty(input) ? name : $"{name}: {input}";
    }

    private static void Reset()
    {
        API.LogInfo("[SF6Access] KeyConfig ended");
        _isActive = false;
        _param = null;
        _menuMessageData = null;
        _partsListSetting = null;
        for (int i = 0; i < _spinTexts.Length; i++) _spinTexts[i] = null;
        for (int i = 0; i < _lastSpinValues.Length; i++) _lastSpinValues[i] = null;
        _lastLeftId = -2;
        _lastListItemId = -2;
        _lastSettingIdx = -1;
    }
}
