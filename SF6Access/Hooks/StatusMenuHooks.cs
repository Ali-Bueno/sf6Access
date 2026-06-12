using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the avatar status menu (app.UIStatusMenu: equip / special
/// moves / super arts / skills / master tabs) opened from training or the hub.
/// Polls StatusMenuParam.TabIndex for tab changes and UIStatusMenu_Equip.Param
/// focus state for the equip tab: slot list (mTopList), item grid (mItemList,
/// item name read from mEquipItemLabel) and preset list (mPresetList).
/// </summary>
public class StatusMenuHooks
{
    private const string STATUS_PARAM_TYPE = "app.UIStatusMenu.StatusMenuParam";
    private const string EQUIP_PARAM_TYPE = "app.UIStatusMenu_Equip.Param";

    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    private static ManagedObject _statusParam;
    private static ManagedObject _equipParam;

    private static int _lastTab = -2;
    private static int _lastGroupFocus = -2;
    private static int _lastTopIndex = -2;
    private static int _lastItemIndex = -2;
    private static int _lastPresetIndex = -2;
    private static string _lastItemName;

    // English fallbacks when on-screen tab/slot labels can't be read
    // (UIStatusMenu.MenuType and UIStatusMenu_Equip.TopFocusType enum order)
    private static readonly string[] TabNames =
        { "Equipment", "Special Moves", "Super Arts", "Skills", "Master" };
    private static readonly string[] TopFocusNames =
    {
        "Style", "Head", "Body", "Body sub", "Legs", "Shoes",
        "Accessory 1", "Accessory 2", "Accessory 3", "My set", "Equip type"
    };

    public static bool IsInStatusMenu => _isActive;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] StatusMenuHooks initialized");
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
            var current = FlowHelper.TrackFlowParam(STATUS_PARAM_TYPE, _statusParam, out bool changed);
            if (current == null)
            {
                Reset();
                return;
            }
            if (changed)
                TryActivate(); // menu was recreated — re-bind params
        }

        if (_pollCounter % POLL_READ_INTERVAL == 0)
            PollState();
    }

    private static void TryActivate()
    {
        var found = FlowHelper.FindFlowParams(new[] { STATUS_PARAM_TYPE, EQUIP_PARAM_TYPE });
        if (!found.TryGetValue(STATUS_PARAM_TYPE, out var statusParam)) return;

        _statusParam = statusParam;
        found.TryGetValue(EQUIP_PARAM_TYPE, out _equipParam);

        _lastTab = -2;
        _lastGroupFocus = -2;
        _lastTopIndex = -2;
        _lastItemIndex = -2;
        _lastPresetIndex = -2;
        _lastItemName = null;
        _isActive = true;

        API.LogInfo($"[SF6Access] Status menu active (equip={_equipParam != null})");
        PollState();
    }

    private static void PollState()
    {
        AnnounceTabChange();

        // The equip child param may appear after the menu opens, and is
        // recreated when its sub-screen is reopened — track the live instance
        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
            _equipParam = FlowHelper.TrackFlowParam(EQUIP_PARAM_TYPE, _equipParam, out bool _equipChanged);

        if (_equipParam == null) return;

        int groupFocus = FlowHelper.ReadIntField(_equipParam, "GroupFocus");
        bool groupChanged = groupFocus != _lastGroupFocus && _lastGroupFocus != -2;
        _lastGroupFocus = groupFocus;

        switch (groupFocus)
        {
            case 0: PollPresetList(groupChanged); break;
            case 1: PollTopList(groupChanged); break;
            case 2: PollItemGrid(groupChanged); break;
        }
    }

    private static void AnnounceTabChange()
    {
        int tab = FlowHelper.ReadIntField(_statusParam, "TabIndex");
        if (tab < 0 || tab == _lastTab) return;

        bool first = _lastTab == -2;
        _lastTab = tab;

        var tabList = FlowHelper.GetObjectField(_statusParam, "TabList");
        string label = FlowHelper.ReadListRowText(tabList, tab);
        if (string.IsNullOrEmpty(label) && tab < TabNames.Length)
            label = TabNames[tab];
        if (string.IsNullOrEmpty(label)) return;

        // Announce the initial tab too: it tells the user the menu is open
        API.LogInfo($"[SF6Access] Status menu tab [{tab}]: {label}");
        ScreenReaderService.Speak(label, interrupt: !first);
    }

    /// <summary>Equipment slot list (Style / Head / Body / ... / My set).</summary>
    private static void PollTopList(bool groupChanged)
    {
        var topList = FlowHelper.GetObjectField(_equipParam, "mTopList");
        int idx = FlowHelper.CallInt(topList, "get_SelectedIndex");
        if (idx < 0) idx = FlowHelper.ReadIntField(_equipParam, "TopFocus");
        if (idx < 0 || (idx == _lastTopIndex && !groupChanged)) return;

        bool first = _lastTopIndex == -2;
        _lastTopIndex = idx;
        if (first && !groupChanged) return;

        string label = FlowHelper.ReadListRowText(topList, idx);
        if (string.IsNullOrEmpty(label) && idx < TopFocusNames.Length)
            label = TopFocusNames[idx];
        if (string.IsNullOrEmpty(label)) return;

        // Append the equipped item's name shown in the label widget
        string itemName = ReadEquipItemLabel();
        if (!string.IsNullOrEmpty(itemName) && itemName != label)
            label = $"{label}. {itemName}";

        API.LogInfo($"[SF6Access] Status menu slot [{idx}]: {label}");
        ScreenReaderService.Speak(label);
    }

    /// <summary>Item grid: the focused item's name renders in mEquipItemLabel.</summary>
    private static void PollItemGrid(bool groupChanged)
    {
        var itemList = FlowHelper.GetObjectField(_equipParam, "mItemList");
        int idx = FlowHelper.CallInt(itemList, "get_SelectedIndex");
        string name = ReadEquipItemLabel();

        bool indexChanged = idx >= 0 && idx != _lastItemIndex;
        bool nameChanged = !string.IsNullOrEmpty(name) && name != _lastItemName;

        bool first = _lastItemIndex == -2 && _lastItemName == null;
        if (idx >= 0) _lastItemIndex = idx;
        if (!string.IsNullOrEmpty(name)) _lastItemName = name;

        if (first && !groupChanged) return;
        if (!indexChanged && !nameChanged && !groupChanged) return;
        if (string.IsNullOrEmpty(name))
        {
            // No label text: fall back to the grid cell's own texts
            name = FlowHelper.ReadListRowText(itemList, idx);
            if (string.IsNullOrEmpty(name)) return;
        }

        API.LogInfo($"[SF6Access] Status menu item [{idx}]: {name}");
        ScreenReaderService.Speak(name);
    }

    /// <summary>Preset (my set) list rows.</summary>
    private static void PollPresetList(bool groupChanged)
    {
        var presetList = FlowHelper.GetObjectField(_equipParam, "mPresetList");
        int idx = FlowHelper.CallInt(presetList, "get_SelectedIndex");
        if (idx < 0 || (idx == _lastPresetIndex && !groupChanged)) return;

        bool first = _lastPresetIndex == -2;
        _lastPresetIndex = idx;
        if (first && !groupChanged) return;

        string label = FlowHelper.ReadListRowText(presetList, idx);
        if (string.IsNullOrEmpty(label)) return;

        API.LogInfo($"[SF6Access] Status menu preset [{idx}]: {label}");
        ScreenReaderService.Speak(label);
    }

    private static string ReadEquipItemLabel()
    {
        try
        {
            var label = FlowHelper.GetObjectField(_equipParam, "mEquipItemLabel");
            var nameText = FlowHelper.GetObjectField(label, "_nameText")
                ?? FlowHelper.GetObjectField(label, "NameText");
            return FlowHelper.ReadGuiText(nameText);
        }
        catch { return null; }
    }

    private static void Reset()
    {
        API.LogInfo("[SF6Access] Status menu ended");
        _isActive = false;
        _statusParam = null;
        _equipParam = null;
        _lastItemName = null;
    }
}
