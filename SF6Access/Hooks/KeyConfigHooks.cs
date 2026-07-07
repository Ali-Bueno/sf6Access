using System;
using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the control preset menu (app.UIFlowKeyConfig.Menu).
/// This screen is reached from Options or from fighter settings and contains:
/// left column spins (control type, preset, negative edge, low stick sensitivity),
/// a menu list (Edit / Initialize / Copy / Test Input) and the right-side list
/// with one row per action and its assigned button/key.
/// Spin values are read from the on-screen via.gui.Text components.
///
/// ScreenAdapter: the save/discard confirmation popup (MessageBox.Param) can
/// outlive the menu flow (exit confirm), so Locate() keeps the adapter active
/// while either is present; IsInKeyConfig (read by MainMenuHooks) stays true
/// only while the MENU param exists. The SetInputFlags hook stays in the
/// static [PluginEntryPoint]. Registered in ScreenRegistry.
/// </summary>
public sealed class KeyConfigHooks : ScreenAdapter
{
    private const string PARAM_TYPE = "app.UIFlowKeyConfig.Menu.Param";
    private const string MSGBOX_PARAM_TYPE = "app.UIFlowKeyConfig.MessageBox.Param";
    private const string INPUT_TEST_PARAM_TYPE = "app.UIFlowKeyConfig.InputTest.Param";

    private static readonly string[] Types = { PARAM_TYPE, MSGBOX_PARAM_TYPE, INPUT_TEST_PARAM_TYPE };
    public override string[] OwnedTypes => Types;

    // UIKeyConfig.ListItemId
    private const int LIST_TEST_INPUT = 3;

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

    private static KeyConfigHooks _self;
    public static bool IsInKeyConfig => _self != null && _self.Active && _self._param != null;

    public KeyConfigHooks()
    {
        SearchInterval = 60;
        ReadInterval = 5;
        _self = this;
    }

    private ManagedObject _param;
    private ManagedObject _menuMessageData;
    private ManagedObject _partsListSetting;
    private readonly ManagedObject[] _spinTexts = new ManagedObject[4];
    private int _gameMode;
    private int _tick;

    private int _lastLeftId = -2;
    private int _lastListItemId = -2;
    private int _lastSettingIdx = -1;
    private string _lastSettingText;
    private readonly string[] _lastSpinValues = new string[4];

    // Save/discard confirmation popup (app.UIFlowKeyConfig.MessageBox)
    private ManagedObject _msgBoxParam;
    private int _lastMsgBoxIndex = -2;
    private bool _msgBoxLabelPending;
    private int _msgBoxLabelPolls;

    // Input test screen (app.UIFlowKeyConfig.InputTest). Static: shared with
    // the SetInputFlags hook.
    private static ManagedObject _inputTestParam;
    private static Dictionary<string, string> _iconNameMap;
    private readonly bool[] _lastButtonOn = new bool[16];
    private static uint _lastInputFlags;
    private static int _setFlagsCalls;

    // Physical gamepad polling for the test screen (via.hid.GamePad)
    private uint _lastPadButtons;
    private static Dictionary<uint, string> _padActionMap;

    // Physical keyboard polling for the test screen (via.hid.Keyboard):
    // only the keys with an assignment are checked
    private static Dictionary<int, string> _kbActionMap;
    private readonly HashSet<int> _kbHeld = new();
    private Method _getSettingParamsMethod;

    // Right stick scrolls the assignment overview panel; movement shows up
    // as the emulated digital bits EmuRup/EmuRdown of the pad state
    private uint _lastMenuPadButtons;
    private string _r3GuiBefore;
    private int _r3CompareAtTick = -1;
    // 6 read ticks at the 5-frame interval = the original 30-frame settle delay
    private const int R3_COMPARE_DELAY_TICKS = 6;
    private const uint PAD_EMU_RUP = 0x1000000;
    private const uint PAD_EMU_RDOWN = 0x4000000;
    private const uint RSTICK_TRIGGER = PAD_EMU_RUP | PAD_EMU_RDOWN;

    // Physical pad bits worth announcing: LUp..CCenter (d-pad, face buttons,
    // triggers, stick pushes, select/start/touchpad). Excludes Decide/Cancel
    // aliases, platform home and the emulated stick-direction bits.
    private const uint PAD_ANNOUNCE_MASK = 0x1FFFF;

    // Device being edited (UIKeyConfig.TargetDevice): GamePad=0, Keyboard=1.
    // Switched in-place with the R key on PC — polled for changes.
    private int _lastDevice = -2;

    // Icon tokens by flag bit index, per EConfigInputType
    // (NormalButton/CasualButton/SuperEasyButton enum values are bit indices)
    private static readonly string[][] InputTokensByType =
    {
        new[] { "lp", "mp", "hp", null, "lk", "mk", "hk" },      // NORMAL (classic)
        new[] { "di", "dp", "sp", "auto", "l", "m", "h" },       // CASUAL (modern)
        new[] { "di", "dp", "sa", "od", "l", "m", "h", "throw" } // SUPER_EASY (dynamic)
    };

    [PluginEntryPoint]
    public static void Initialize()
    {
        // Pressed buttons on the input test screen: the game pushes the
        // currently held action flags here every frame — more reliable than
        // polling the button widgets' _On fields
        try
        {
            var setFlags = TDB.Get().FindType("app.UIPartsKeyConfigInputTestButtons")
                ?.GetMethod("SetInputFlags");
            if (setFlags != null)
            {
                var hook = setFlags.AddHook(false);
                hook.AddPre(args =>
                {
                    try
                    {
                        // Temporary diagnostic: confirm this hook fires at all
                        // (round 4 produced zero announcements in the test screen)
                        if (_setFlagsCalls < 5)
                        {
                            _setFlagsCalls++;
                            API.LogInfo($"[SF6Access] SetInputFlags fired: 0x{args[2]:X}");
                        }
                        if (_inputTestParam != null)
                            OnInputTestFlags(ManagedObject.ToManagedObject(args[1]), (uint)args[2]);
                    }
                    catch { }
                    return PreHookResult.Continue;
                });
                API.LogInfo("[SF6Access] SetInputFlags hook installed");
            }
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] SetInputFlags hook failed: {ex.Message}");
        }

        API.LogInfo("[SF6Access] KeyConfigHooks initialized");
    }

    /// <summary>Announce actions whose flag bit just turned on.</summary>
    private static void OnInputTestFlags(ManagedObject buttonsParts, uint flags)
    {
        uint rising = flags & ~_lastInputFlags;
        _lastInputFlags = flags;
        if (rising == 0) return;

        int inputType = FlowHelper.ReadIntField(buttonsParts, "_InputType", 0);
        var tokens = inputType >= 0 && inputType < InputTokensByType.Length
            ? InputTokensByType[inputType] : InputTokensByType[0];

        List<string> pressed = null;
        for (int bit = 0; bit < tokens.Length; bit++)
        {
            if ((rising & (1u << bit)) == 0 || tokens[bit] == null) continue;
            string name = ResolveIconKey(tokens[bit]);
            if (string.IsNullOrEmpty(name)) continue;
            pressed ??= new List<string>();
            if (!pressed.Contains(name)) pressed.Add(name);
        }

        if (pressed == null) return;
        string announcement = string.Join(" + ", pressed);
        API.LogInfo($"[SF6Access] Input test flags 0x{rising:X} ({inputType}): {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    protected override bool Locate()
    {
        var menu = FlowHelper.TrackFlowParam(PARAM_TYPE, _param, out bool changed);
        if (menu == null)
        {
            if (_param != null) ResetMenu();
            // The confirmation popup can show while the menu flow is gone
            // (e.g. exit confirm) — keep the adapter alive for it
            return _msgBoxParam != null || FlowHelper.FindFlowParam(MSGBOX_PARAM_TYPE) != null;
        }

        if (changed)
            BindMenu(menu); // menu opened or was recreated — re-bind child caches
        return true;
    }

    protected override void OnDeactivate()
    {
        if (_param != null) ResetMenu();
        _msgBoxParam = null;
        _lastMsgBoxIndex = -2;
        _msgBoxLabelPending = false;
    }

    protected override void OnPoll()
    {
        _tick++;

        PollMessageBox();

        if (_param == null) return; // popup-only mode (menu flow gone)

        PollInputTest();
        PollDeviceChange();
        PollFocus();
        PollSpinValues();
        PollSettingList();
        if (_inputTestParam == null)
            PollMenuR3();
    }

    /// <summary>
    /// Moving the right stick up/down scrolls the right-side panel with the
    /// full assignment overview (verified via state diff: no Param field
    /// changes, rows scroll into view). Announce only the NEWLY revealed
    /// rows; stay silent when the panel is already at the end.
    /// </summary>
    private void PollMenuR3()
    {
        uint buttons = ReadPadButtons();
        uint rising = buttons & ~_lastMenuPadButtons;
        _lastMenuPadButtons = buttons;

        if ((rising & RSTICK_TRIGGER) != 0 && _r3CompareAtTick < 0)
        {
            _r3GuiBefore = ReadKeyConfigGuiJoined();
            _r3CompareAtTick = _tick + R3_COMPARE_DELAY_TICKS;
        }

        if (_r3CompareAtTick > 0 && _tick >= _r3CompareAtTick)
        {
            _r3CompareAtTick = -1;

            string guiAfter = ReadKeyConfigGuiJoined();
            if (string.IsNullOrEmpty(guiAfter) || string.IsNullOrEmpty(_r3GuiBefore)) return;

            // Only genuinely new segments — no full-text fallback, scrolling
            // with nothing new must stay silent
            var oldSegments = new HashSet<string>(
                _r3GuiBefore.Split(new[] { ". " }, StringSplitOptions.RemoveEmptyEntries));
            var revealed = new List<string>();
            foreach (var raw in guiAfter.Split(new[] { ". " }, StringSplitOptions.RemoveEmptyEntries))
            {
                string segment = raw.Trim();
                if (segment.Length == 0 || segment == "-") continue;
                if (!oldSegments.Contains(segment) && !revealed.Contains(segment))
                    revealed.Add(segment);
            }
            if (revealed.Count == 0) return;

            string announcement = string.Join(". ", revealed);
            API.LogInfo($"[SF6Access] Assignment panel scrolled: {announcement}");
            Speak(announcement, interrupt: false);
        }
    }

    /// <summary>All visible texts of the key config GUIs, joined for diffing.</summary>
    private static string ReadKeyConfigGuiJoined()
    {
        try
        {
            var parts = new List<string>();
            foreach (var (owner, view) in GuiTextReader.FindGuiViews("KeyConfig"))
            {
                foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
                {
                    if (!string.IsNullOrWhiteSpace(t.Text)) parts.Add(t.Text.Trim());
                }
            }
            return parts.Count > 0 ? string.Join(". ", parts) : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// The R key (PC) switches the edited device between controller and
    /// keyboard, mutating TargetDevice on the same Param and rebuilding the
    /// whole screen — announce the switch and resync silently.
    /// </summary>
    private void PollDeviceChange()
    {
        int device = FlowHelper.ReadIntField(_param, "TargetDevice", -1);
        if (device < 0 || device == _lastDevice) return;

        bool first = _lastDevice == -2;
        _lastDevice = device;
        if (first) return; // BindMenu announces the initial device

        // The setting list and spin texts now show the other device's values
        _lastSettingIdx = -1;
        _lastSettingText = null;
        for (int i = 0; i < _lastSpinValues.Length; i++)
            _lastSpinValues[i] = FlowHelper.ReadGuiText(_spinTexts[i]);

        string name = device == 1 ? "Keyboard" : "Controller";
        API.LogInfo($"[SF6Access] KeyConfig device switched: {name}");
        Speak(name);
    }

    private void BindMenu(ManagedObject param)
    {
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
        _lastSettingText = null;
        for (int i = 0; i < _lastSpinValues.Length; i++)
            _lastSpinValues[i] = FlowHelper.ReadGuiText(_spinTexts[i]);

        // Device being edited on entry (the R key switches it later):
        // GamePad=0, Keyboard=1. Announce it so the user knows which device
        // they are configuring.
        int device = FlowHelper.ReadIntField(param, "TargetDevice", -1);
        _lastDevice = device >= 0 ? device : -2;
        API.LogInfo($"[SF6Access] KeyConfig active (gameMode={_gameMode}, device={device}, " +
            $"msgData={_menuMessageData != null}, settingList={_partsListSetting != null}, " +
            $"spins={Array.FindAll(_spinTexts, s => s != null).Length})");
        if (device == 1)
            Speak("Keyboard", interrupt: false);
        else if (device == 0)
            Speak("Controller", interrupt: false);

        _lastMenuPadButtons = ReadPadButtons();
        PollFocus();
    }

    // --- Focus on the left column (spins + menu list) ---

    private void PollFocus()
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

    private void AnnounceLeftGroup(int leftId, int listItemId)
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
        Speak(announcement);
    }

    private string GetLeftGroupGuide(int leftId)
    {
        var msg = FlowHelper.Call(_menuMessageData, "GetLeftGroupItemMessage", leftId) as ManagedObject;
        return FlowHelper.ResolveGuidField(msg, "Guide");
    }

    private string ResolveListItemName(int listItemId)
    {
        if (listItemId < 0) return null;

        var msg = FlowHelper.Call(_menuMessageData, "GetListItemMessage", listItemId) as ManagedObject;
        string name = FlowHelper.ResolveGuidField(msg, "Name");
        if (!string.IsNullOrEmpty(name)) return name;

        return listItemId < ListItemFallbacks.Length ? ListItemFallbacks[listItemId] : $"Item {listItemId}";
    }

    // --- Spin value changes (left/right on a spin) ---

    private void PollSpinValues()
    {
        for (int i = 0; i < _spinTexts.Length; i++)
        {
            if (_spinTexts[i] == null) continue;

            string value = FlowHelper.ReadGuiText(_spinTexts[i]);
            if (string.IsNullOrEmpty(value) || value == _lastSpinValues[i]) continue;

            bool first = _lastSpinValues[i] == null;
            _lastSpinValues[i] = value;
            if (first) continue;

            // Control type: append the type's own tooltip sentence so the user
            // hears what Classic/Modern/Dynamic means, not just the name. The
            // guide text holds one "{name}: explanation" segment per type.
            string announcement = value;
            if (i == LEFT_MODE)
            {
                string segment = FindGuideSegment(GetLeftGroupGuide(LEFT_MODE), value);
                if (segment != null) announcement = segment;
            }

            API.LogInfo($"[SF6Access] KeyConfig spin [{i}]: {announcement}");
            Speak(announcement);
        }
    }

    /// <summary>The guide sentence that starts with the given value name.</summary>
    private static string FindGuideSegment(string guide, string value)
    {
        if (string.IsNullOrEmpty(guide) || string.IsNullOrEmpty(value)) return null;
        try
        {
            foreach (var raw in guide.Split('.'))
            {
                string segment = raw.Trim();
                if (segment.StartsWith(value, StringComparison.OrdinalIgnoreCase))
                    return segment;
            }
        }
        catch { }
        return null;
    }

    // --- Setting rows (right-side list: action name + assigned input) ---

    private void PollSettingList()
    {
        if (_partsListSetting == null) return;

        int idx = FlowHelper.CallInt(_partsListSetting, "get_SelectedIndex");
        if (idx < 0) return;

        // Re-read the row each poll: reassigning a key changes the row's text
        // without moving the cursor, and that must be announced too
        string announcement = ReadSettingRow(idx);

        bool first = _lastSettingIdx == -1;
        bool rowChanged = idx != _lastSettingIdx;
        bool textChanged = !rowChanged && !string.IsNullOrEmpty(announcement)
            && _lastSettingText != null && announcement != _lastSettingText;

        _lastSettingIdx = idx;
        if (!string.IsNullOrEmpty(announcement)) _lastSettingText = announcement;

        if (first || string.IsNullOrEmpty(announcement)) return;
        if (!rowChanged && !textChanged) return;

        // Changing control type or preset on the left spins rebuilds the whole
        // list (Modern "Ataque Leve" rows become Classic "Soco Leve" rows) —
        // only announce rows while the cursor is actually in the list
        if (_lastLeftId != LEFT_LIST) return;

        API.LogInfo($"[SF6Access] KeyConfig setting [{idx}]: {announcement}");
        Speak(announcement);
    }

    private string ReadSettingName(ManagedObject settingParam)
    {
        string name = null;
        try
        {
            var method = settingParam.GetTypeDefinition()?.GetMethod("GetName(app.AppDefine.GameMode)");
            name = method?.InvokeBoxed(typeof(string), settingParam, new object[] { _gameMode }) as string;
        }
        catch { }
        if (string.IsNullOrEmpty(name))
            name = FlowHelper.Call(settingParam, "GetName") as string;
        return FlowHelper.CleanTags(name);
    }

    private string ReadSettingRow(int listIndex)
    {
        var settingParam = FlowHelper.Call(_param, "GetSettingParam", listIndex) as ManagedObject;
        if (settingParam == null) return null;

        string name = ReadSettingName(settingParam);

        // Assigned input: the screen only shows an icon glyph, so resolve the
        // raw via.hid enum value to a readable name. The concrete SettingParam
        // type tells the device apart (KeyboardDigitalSettingData etc.)
        string input = null;
        bool isKeyboard = settingParam.GetTypeDefinition()?.FullName?.Contains("Keyboard") == true;
        try
        {
            if (isKeyboard)
            {
                var key = FlowHelper.Call(settingParam, "GetKeyboardKey");
                if (key != null)
                    input = InputNameResolver.KeyboardKeyName(Convert.ToInt32(key));
            }
            else
            {
                var button = FlowHelper.Call(settingParam, "GetGamePadButton");
                if (button != null)
                    input = InputNameResolver.GamePadButtonName(Convert.ToUInt32(button));
            }
        }
        catch { }

        if (string.IsNullOrEmpty(input))
            input = FlowHelper.CleanTags(FlowHelper.Call(settingParam, "GetInputIcon") as string);

        if (string.IsNullOrEmpty(name)) return input;
        return string.IsNullOrEmpty(input) ? name : $"{name}: {input}";
    }

    // --- Save/discard confirmation popup ---

    private void PollMessageBox()
    {
        var current = FlowHelper.TrackFlowParam(MSGBOX_PARAM_TYPE, _msgBoxParam, out bool changed);
        if (current == null)
        {
            if (_msgBoxParam != null)
            {
                _msgBoxParam = null;
                _lastMsgBoxIndex = -2;
                _msgBoxLabelPending = false;
            }
            return;
        }

        if (changed)
        {
            _msgBoxParam = current;
            _lastMsgBoxIndex = ReadMsgBoxSelectedIndex();
            // The PartsList isn't constructed yet when the popup appears (the
            // on-screen label read "Cancel" instead of "Cancelar") — defer the
            // selected-button announcement until it reads localized
            _msgBoxLabelPending = true;
            _msgBoxLabelPolls = 0;

            var inParam = FlowHelper.GetObjectField(current, "In");
            string title = FlowHelper.ResolveGuidField(inParam, "Title");
            string message = FlowHelper.ResolveGuidField(inParam, "Message");

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(title)) parts.Add(title);
            if (!string.IsNullOrEmpty(message) && message != title) parts.Add(message);
            if (parts.Count == 0) return;

            string announcement = string.Join(". ", parts);
            API.LogInfo($"[SF6Access] KeyConfig message box: {announcement}");
            Speak(announcement);
            return;
        }

        int idx = ReadMsgBoxSelectedIndex();

        // Initially focused button, announced as soon as its on-screen text
        // resolves (hardcoded fallback only after ~1s of failed reads)
        if (_msgBoxLabelPending)
        {
            _msgBoxLabelPolls++;
            string focused = ReadMsgBoxButtonLabel(idx, allowFallback: _msgBoxLabelPolls > 12);
            if (string.IsNullOrEmpty(focused)) return;

            _msgBoxLabelPending = false;
            _lastMsgBoxIndex = idx;
            API.LogInfo($"[SF6Access] KeyConfig message box button [{idx}]: {focused}");
            Speak(focused, interrupt: false);
            return;
        }

        // Selection moved between Yes/No/Cancel
        if (idx < 0 || idx == _lastMsgBoxIndex) return;
        _lastMsgBoxIndex = idx;

        string label = ReadMsgBoxButtonLabel(idx, allowFallback: true);
        if (string.IsNullOrEmpty(label)) return;

        API.LogInfo($"[SF6Access] KeyConfig message box button [{idx}]: {label}");
        Speak(label);
    }

    private int ReadMsgBoxSelectedIndex()
    {
        var partsList = FlowHelper.GetObjectField(_msgBoxParam, "PartsList");
        int idx = FlowHelper.CallInt(partsList, "get_SelectedIndex", -2);
        if (idx >= 0) return idx;
        return FlowHelper.ReadIntField(_msgBoxParam, "SelectedIndex", -2);
    }

    private string ReadMsgBoxButtonLabel(int selIdx, bool allowFallback)
    {
        // On-screen text of the selected row first (localized)
        var partsList = FlowHelper.GetObjectField(_msgBoxParam, "PartsList");
        string text = FlowHelper.ReadSelectedItemText(partsList);
        if (!string.IsNullOrEmpty(text)) return text;

        // Then the popup's own Yes/No message Guids
        var inParam = FlowHelper.GetObjectField(_msgBoxParam, "In");
        string label = selIdx switch
        {
            0 => FlowHelper.ResolveGuidField(inParam, "Yes"),
            1 => FlowHelper.ResolveGuidField(inParam, "No"),
            _ => null
        };
        if (!string.IsNullOrEmpty(label)) return label;
        if (!allowFallback) return null;

        // UIKeyConfig.MessageBoxIndex: Yes=0, No=1, Cancel=2
        return selIdx switch { 0 => "Yes", 1 => "No", 2 => "Cancel", _ => null };
    }

    // --- Input test screen ("Config. de Teste") ---

    private void PollInputTest()
    {
        var current = FlowHelper.TrackFlowParam(INPUT_TEST_PARAM_TYPE, _inputTestParam, out bool changed);
        if (current == null)
        {
            if (_inputTestParam != null)
            {
                _inputTestParam = null;
                _iconNameMap = null;
                _kbActionMap = null;
                _kbHeld.Clear();
            }
            return;
        }

        if (changed)
        {
            _inputTestParam = current;
            Array.Clear(_lastButtonOn, 0, _lastButtonOn.Length);
            _lastInputFlags = 0;
            _iconNameMap = BuildIconNameMap();
            BuildTestActionMaps();
            _lastPadButtons = ReadPadButtons(); // don't announce already-held buttons
            PrefillHeldKeys();

            // Diagnostics for the widget-polling fallback path
            var parts = FlowHelper.GetObjectField(current, "Buttons");
            var arr = FlowHelper.GetObjectField(parts, "_Buttons");
            API.LogInfo($"[SF6Access] KeyConfig input test active (iconNames={_iconNameMap.Count}, " +
                $"buttonsParts={parts != null}, buttons={FlowHelper.GetListCount(arr)}, " +
                $"inputType={FlowHelper.ReadIntField(parts, "_InputType", -1)}, " +
                $"padActions={_padActionMap.Count}, kbActions={_kbActionMap.Count}, " +
                $"padState=0x{_lastPadButtons:X})");

            // Screen has no focusable items: announce its guide text on entry
            // ("Veja suas configurações de botão.") so it isn't silent
            var msg = FlowHelper.Call(_menuMessageData, "GetListItemMessage", LIST_TEST_INPUT) as ManagedObject;
            string guide = FlowHelper.ResolveGuidField(msg, "Guide");
            if (!string.IsNullOrEmpty(guide))
                Speak(guide);
            return;
        }

        // Primary path: read the physical gamepad state directly — the game's
        // own test widgets proved unreliable (_On never observed true and
        // SetInputFlags never seen firing in round 4)
        uint padButtons = ReadPadButtons();
        uint risingPad = (padButtons & ~_lastPadButtons) & PAD_ANNOUNCE_MASK;
        _lastPadButtons = padButtons;
        if (risingPad != 0) AnnouncePadButtons(risingPad);

        PollKeyboardTest();

        // Announce actions whose button just got pressed (rising edge)
        var buttonsParts = FlowHelper.GetObjectField(_inputTestParam, "Buttons");
        var buttons = FlowHelper.GetObjectField(buttonsParts, "_Buttons");
        int count = Math.Min(FlowHelper.GetListCount(buttons), _lastButtonOn.Length);

        List<string> pressed = null;
        for (int i = 0; i < count; i++)
        {
            var button = FlowHelper.GetListItem(buttons, i);
            bool on = FlowHelper.ReadBoolField(button, "_On");
            bool wasOn = _lastButtonOn[i];
            _lastButtonOn[i] = on;
            if (!on || wasOn) continue;

            string icon = FlowHelper.ReadStringField(button, "Icon")
                ?? FlowHelper.Call(button, "get_Icon") as string;
            string name = ResolveIconName(icon);
            if (string.IsNullOrEmpty(name)) continue;

            pressed ??= new List<string>();
            if (!pressed.Contains(name)) pressed.Add(name);
        }

        if (pressed == null) return;
        string announcement = string.Join(" + ", pressed);
        API.LogInfo($"[SF6Access] Input test pressed: {announcement}");
        Speak(announcement);
    }

    /// <summary>Currently pressed buttons of the merged gamepad device.</summary>
    private static uint ReadPadButtons() => ReadoutShortcut.ReadPadButtons();

    /// <summary>
    /// Physical input → assigned action name, for BOTH devices regardless of
    /// which one the menu currently shows: Menu.Param.GetSettingParams
    /// (TargetDevice) returns the rows per device. GetGamePadButton /
    /// GetKeyboardKey are the same via.hid values the devices report.
    /// </summary>
    private void BuildTestActionMaps()
    {
        _padActionMap = new Dictionary<uint, string>();
        _kbActionMap = new Dictionary<int, string>();
        try
        {
            // IObject.Call with a full signature doesn't resolve — use the TDB method
            _getSettingParamsMethod ??= _param.GetTypeDefinition()
                ?.GetMethod("GetSettingParams(app.UIKeyConfig.TargetDevice)");

            for (int device = 0; device <= 1; device++)
            {
                var rows = _getSettingParamsMethod?.InvokeBoxed(
                    typeof(object), _param, new object[] { device }) as ManagedObject;
                int count = FlowHelper.GetListCount(rows);

                for (int i = 0; i < count; i++)
                {
                    var settingParam = FlowHelper.GetListItem(rows, i);
                    if (settingParam == null) continue;

                    string name = ReadSettingName(settingParam);
                    if (string.IsNullOrEmpty(name)) continue;

                    if (device == 0)
                    {
                        var button = FlowHelper.Call(settingParam, "GetGamePadButton");
                        if (button == null) continue;
                        uint flag = Convert.ToUInt32(button);
                        if (flag != 0 && !_padActionMap.ContainsKey(flag)) _padActionMap[flag] = name;
                    }
                    else
                    {
                        var key = FlowHelper.Call(settingParam, "GetKeyboardKey");
                        if (key == null) continue;
                        int keyValue = Convert.ToInt32(key);
                        if (keyValue > 0 && !_kbActionMap.ContainsKey(keyValue)) _kbActionMap[keyValue] = name;
                    }
                }
            }

            // Fallback: rows of the currently shown device only
            if (_padActionMap.Count == 0 && _kbActionMap.Count == 0)
            {
                for (int i = 0; i < 32; i++)
                {
                    var settingParam = FlowHelper.Call(_param, "GetSettingParam", i) as ManagedObject;
                    if (settingParam == null) break;
                    string name = ReadSettingName(settingParam);
                    if (string.IsNullOrEmpty(name)) continue;
                    var button = FlowHelper.Call(settingParam, "GetGamePadButton");
                    if (button == null) continue;
                    uint flag = Convert.ToUInt32(button);
                    if (flag != 0 && !_padActionMap.ContainsKey(flag)) _padActionMap[flag] = name;
                }
            }
        }
        catch { }
    }

    /// <summary>Keyboard device of via.hid.Keyboard, null when unavailable.</summary>
    private static object GetKeyboardDevice()
    {
        try
        {
            var keyboard = API.GetNativeSingleton("via.hid.Keyboard");
            return (keyboard as IObject)?.Call("get_Device");
        }
        catch { return null; }
    }

    /// <summary>Mark keys already held when the test opens (e.g. Enter) as seen.</summary>
    private void PrefillHeldKeys()
    {
        _kbHeld.Clear();
        var device = GetKeyboardDevice();
        if (device == null || _kbActionMap == null) return;
        foreach (var key in _kbActionMap.Keys)
        {
            if (IsKeyDown(device, key)) _kbHeld.Add(key);
        }
    }

    private static bool IsKeyDown(object device, int key)
    {
        try
        {
            var result = (device as IObject)?.Call("isDown", key);
            return result != null && Convert.ToBoolean(result);
        }
        catch { return false; }
    }

    /// <summary>Announce assigned keyboard keys as they are pressed.</summary>
    private void PollKeyboardTest()
    {
        if (_kbActionMap == null || _kbActionMap.Count == 0) return;
        var device = GetKeyboardDevice();
        if (device == null) return;

        List<string> pressed = null;
        foreach (var kv in _kbActionMap)
        {
            bool down = IsKeyDown(device, kv.Key);
            if (down && _kbHeld.Add(kv.Key))
            {
                pressed ??= new List<string>();
                if (!pressed.Contains(kv.Value)) pressed.Add(kv.Value);
            }
            else if (!down)
            {
                _kbHeld.Remove(kv.Key);
            }
        }

        if (pressed == null) return;
        string announcement = string.Join(" + ", pressed);
        API.LogInfo($"[SF6Access] Input test keyboard: {announcement}");
        Speak(announcement);
    }

    private static void AnnouncePadButtons(uint rising)
    {
        var pressed = new List<string>();
        for (int bit = 0; bit < 17; bit++)
        {
            uint flag = 1u << bit;
            if ((rising & flag) == 0) continue;

            // Assigned action first (what the test screen shows), physical
            // button name when nothing is assigned
            if (_padActionMap != null && _padActionMap.TryGetValue(flag, out var action))
                pressed.Add(action);
            else
                pressed.Add(InputNameResolver.GamePadButtonName(flag));
        }
        if (pressed.Count == 0) return;

        string announcement = string.Join(" + ", pressed);
        API.LogInfo($"[SF6Access] Input test pad 0x{rising:X}: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    /// <summary>
    /// Map icon tag sequences to localized action names using the legend the
    /// key config GUI draws on screen: e_text_title holds "&lt;ICON lp&gt;"
    /// style tags and the next plain text holds the name ("Soco Leve").
    /// </summary>
    private static Dictionary<string, string> BuildIconNameMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var texts = GuiTextReader.ReadTextsByOwner("KeyConfig");
            for (int i = 0; i + 1 < texts.Count; i++)
            {
                string raw = texts[i].Raw;
                if (raw == null || !raw.Contains("<ICON")) continue;

                string name = texts[i + 1].Text?.Trim();
                if (string.IsNullOrEmpty(name) || name.Contains("<ICON")) continue;

                string key = NormalizeIconTags(raw);
                if (key.Length > 0 && !map.ContainsKey(key)) map[key] = name;
            }
        }
        catch { }
        return map;
    }

    private static string ResolveIconName(string iconTags)
    {
        if (string.IsNullOrEmpty(iconTags)) return null;
        return ResolveIconKey(NormalizeIconTags(iconTags));
    }

    private static string ResolveIconKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (_iconNameMap != null && _iconNameMap.TryGetValue(key, out var name)) return name;
        // Unknown icon: announce the raw token(s) ("lp" → "LP")
        return key.Replace('+', ' ').ToUpperInvariant();
    }

    /// <summary>"&lt;ICON lp&gt;&lt;ICON mp&gt;" → "lp+mp".</summary>
    private static string NormalizeIconTags(string raw)
    {
        var tokens = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in
            System.Text.RegularExpressions.Regex.Matches(raw, @"<ICON\s+([^>\s]+)[^>]*>"))
        {
            tokens.Add(m.Groups[1].Value.Trim().ToLowerInvariant());
        }
        return string.Join("+", tokens);
    }

    private void ResetMenu()
    {
        API.LogInfo("[SF6Access] KeyConfig ended");
        _param = null;
        _menuMessageData = null;
        _partsListSetting = null;
        for (int i = 0; i < _spinTexts.Length; i++) _spinTexts[i] = null;
        for (int i = 0; i < _lastSpinValues.Length; i++) _lastSpinValues[i] = null;
        _lastLeftId = -2;
        _lastListItemId = -2;
        _lastSettingIdx = -1;
        _lastSettingText = null;
        _lastDevice = -2;
        _inputTestParam = null;
        _iconNameMap = null;
        _padActionMap = null;
        _kbActionMap = null;
        _kbHeld.Clear();
        _getSettingParamsMethod = null;
        _r3CompareAtTick = -1;
    }
}
