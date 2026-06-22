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
    private const string MASTER_PARAM_TYPE = "app.UIStatusMenu_Master.Param";

    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    private static ManagedObject _statusParam;
    private static ManagedObject _equipParam;
    private static ManagedObject _masterParam;

    private static int _lastTab = -2;
    private static int _lastGroupFocus = -2;
    private static int _lastTopIndex = -2;
    private static int _lastItemIndex = -2;
    private static int _lastPresetIndex = -2;
    private static string _lastItemName;
    private static string _lastMasterText;

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
        var found = FlowHelper.FindFlowParams(new[] { STATUS_PARAM_TYPE, EQUIP_PARAM_TYPE, MASTER_PARAM_TYPE });
        if (!found.TryGetValue(STATUS_PARAM_TYPE, out var statusParam)) return;

        _statusParam = statusParam;
        found.TryGetValue(EQUIP_PARAM_TYPE, out _equipParam);
        found.TryGetValue(MASTER_PARAM_TYPE, out _masterParam);

        _lastTab = -2;
        _lastGroupFocus = -2;
        _lastTopIndex = -2;
        _lastItemIndex = -2;
        _lastPresetIndex = -2;
        _lastItemName = null;
        _lastMasterText = null;
        _isActive = true;

        API.LogInfo($"[SF6Access] Status menu active (equip={_equipParam != null})");
        PollState();
    }

    private static void PollState()
    {
        AnnounceTabChange();

        // Child params come and go as the user switches tabs / reopens
        // sub-screens — track the live instances
        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            _equipParam = FlowHelper.TrackFlowParam(EQUIP_PARAM_TYPE, _equipParam, out bool _equipChanged);
            _masterParam = FlowHelper.TrackFlowParam(MASTER_PARAM_TYPE, _masterParam, out bool _masterChanged);
        }

        // Master tab: read the focused master's detail window (its own child flow,
        // no equip param) — this is where the avatar's fighting style is equipped
        if (_masterParam != null)
            PollMasterTab();

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

        // Name what's equipped in the focused slot so the player knows it without
        // entering: the Style slot resolves the equipped style, gear slots resolve
        // their equipped item (or "Empty"). The row's item name is a hidden GUI
        // element, so both come from the equip data.
        if (IsStyleSlot())
        {
            string equipped = ResolveEquippedStyleName();
            if (!string.IsNullOrWhiteSpace(equipped)) label = equipped;
        }
        else
        {
            string item = ResolveSlotEquippedItem(idx);
            if (!string.IsNullOrWhiteSpace(item)) label = $"{label}. {item}";
        }

        // Append the focused slot's contextual help (InputGuide), e.g. the Style
        // slot's "Change your battle style... commands are for facing right". The
        // equipped-item label (mEquipItemLabel) is NOT appended — it reflects the
        // last grid item viewed, not this slot, so it produced stale text
        // ("Head. 's Style"); items are read when the slot's grid is entered.
        string tooltip = ReadInputGuideTooltip();
        string announcement = string.IsNullOrWhiteSpace(tooltip) ? label : $"{label}. {tooltip}";

        API.LogInfo($"[SF6Access] Status menu slot [{idx}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    /// <summary>Item grid: the focused item's name renders in mEquipItemLabel.</summary>
    private static void PollItemGrid(bool groupChanged)
    {
        var itemList = FlowHelper.GetObjectField(_equipParam, "mItemList");
        int idx = FlowHelper.CallInt(itemList, "get_SelectedIndex");

        // The Style slot's label renders the master's name as a portrait, so the
        // text alone is "'s Style". Resolve the localized name from style data.
        string name = IsStyleSlot() ? ResolveStyleName(idx) : null;
        if (string.IsNullOrEmpty(name)) name = ReadEquipItemLabel();

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

    /// <summary>
    /// Master tab grid: navigating a master updates mMasterDetailWindow with the
    /// focused master's name, level and style detail. The master names are
    /// rendered as text here (unlike the equip slots), so read them directly.
    /// </summary>
    private static void PollMasterTab()
    {
        var detail = FlowHelper.GetObjectField(_masterParam, "mMasterDetailWindow");
        if (detail == null) return;

        string name = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(detail, "mTextName"));
        if (string.IsNullOrEmpty(name)) return;

        string level = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(detail, "mTextLevel"));
        string styleDetail = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(detail, "mTextStyleDetail"));

        string text = name;
        if (!string.IsNullOrEmpty(level)) text += $". Level {level}";
        if (!string.IsNullOrEmpty(styleDetail)) text += $". {styleDetail}";

        if (text == _lastMasterText) return;
        bool first = _lastMasterText == null;
        _lastMasterText = text;

        API.LogInfo($"[SF6Access] Status master: {text}");
        ScreenReaderService.Speak(text, interrupt: !first);
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

    /// <summary>True when the focused equip slot is Style (TopFocusType.STYLE = 0).</summary>
    private static bool IsStyleSlot()
        => FlowHelper.ReadIntField(_equipParam, "TopFocus") == 0;

    /// <summary>
    /// Name of the currently-equipped style ("Ryu's Style"), via the player's
    /// equipped style id (WTPlayerData.Style.StyleEquipId) → master fighter name.
    /// </summary>
    private static string ResolveEquippedStyleName()
    {
        try
        {
            var playerData = FlowHelper.GetObjectField(_statusParam, "PlayerData");
            var styleData = FlowHelper.GetObjectField(playerData, "Style");
            if (styleData == null) return null;

            int styleId = FlowHelper.ReadIntField(styleData, "StyleEquipId");
            if (styleId <= 0) return null;

            string fighter = FlowHelper.ResolveStyleFighterName((uint)styleId);
            return string.IsNullOrWhiteSpace(fighter) ? null : $"{fighter}'s Style";
        }
        catch { return null; }
    }

    // TopFocusType index → WTEquipItemSlot enum value (EquipParamMap key).
    // STYLE(0) is handled separately. HEAD/BODY/BODY_SUB/LEG/SHOES/ACC1-3 →
    // EquipSlot_01/02, EquipSubSlot_02, EquipSlot_03/04, AccessorySlot_01-03.
    private static readonly int[] FocusToEquipSlot =
        { 0, 1, 2, 8, 3, 4, 5, 6, 7 };

    /// <summary>
    /// The item equipped in a focused gear slot ("Breakneck Brawler Cap"), or
    /// "Empty" when nothing is equipped. The slot row shows it as a hidden GUI
    /// element, so read it from the equip data (EquipParamMap, keyed by the
    /// WTEquipItemSlot the focus index maps to).
    /// </summary>
    private static string ResolveSlotEquippedItem(int focusIndex)
    {
        try
        {
            var map = FlowHelper.GetObjectField(_equipParam, "EquipParamMap");
            if (map == null || focusIndex < 0 || focusIndex >= FocusToEquipSlot.Length) return null;

            // GetEquipSlotBySelectItemFocus (interface method) doesn't dispatch on
            // the concrete type, so map TopFocus → WTEquipItemSlot directly.
            int slot = FocusToEquipSlot[focusIndex];
            var slotParam = FlowHelper.GetListItem(map, slot);
            if (slotParam == null) return null;

            int itemId = FlowHelper.ReadIntField(slotParam, "EquipItemId", 0);
            var itemParam = FlowHelper.GetObjectField(slotParam, "ItemParam");
            string name = itemParam == null ? null : FlowHelper.Call(itemParam, "GetNameWithLevel") as string;
            name = string.IsNullOrWhiteSpace(name) ? null : FlowHelper.CleanTags(name).Trim();

            if (!string.IsNullOrWhiteSpace(name)) return name;
            return itemId > 0 ? null : "Empty";
        }
        catch { return null; }
    }

    /// <summary>The focused slot's contextual help from the InputGuide widget's
    /// "e_text" element (e.g. "Change your battle style... facing right").</summary>
    private static string ReadInputGuideTooltip()
    {
        try
        {
            foreach (var (owner, view) in GuiTextReader.FindGuiViews("InputGuide"))
            {
                foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
                {
                    if (t.Name == "e_text" && !string.IsNullOrWhiteSpace(t.Text))
                        return t.Text.Replace('\n', ' ').Trim();
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// On the Style slot the name comes back as "&lt;WLTAG CmdNo="2" Arg0="2"
    /// Arg1="N"&gt;'s Style" — the master's name is a texture-rendered command tag
    /// (Arg1 is the master id). Resolve the real localized style name
    /// ("Luke's Style") from the master profile table instead.
    /// </summary>
    private static string ResolveStyleName(int idx)
    {
        if (idx < 0) return null;
        try
        {
            var styleList = FlowHelper.GetObjectField(_equipParam, "EquipStyleList");
            var styleData = FlowHelper.GetListItem(styleList, idx);
            if (styleData == null) return null;

            string raw = FlowHelper.Call(styleData, "GetUIName") as string
                      ?? FlowHelper.Call(styleData, "GetName") as string;

            // Recover the master id from the WLTAG (Arg1) and look up the master's
            // localized name, then form "Ryu's Style" (the "'s Style" suffix is the
            // on-screen template; the master itself is a texture with no text).
            uint masterId = ParseWltagMasterId(raw);
            if (masterId > 0)
            {
                // The master's name message is a texture WLTAG; resolve the name via
                // the underlying fighter instead. Append the style flavor/description.
                string master = FlowHelper.ResolveMasterFighterName(masterId);
                string desc = NormalizeLines(FlowHelper.ResolveMasterMessage(masterId, "StyleNameID"));

                if (!string.IsNullOrWhiteSpace(master))
                    return string.IsNullOrWhiteSpace(desc) ? $"{master}'s Style"
                                                           : $"{master}'s Style. {desc}";
                if (!string.IsNullOrWhiteSpace(desc)) return desc;
            }

            // Last resort: stripped label (loses the master, but better than the tag)
            string clean = FlowHelper.CleanTags(raw);
            return string.IsNullOrWhiteSpace(clean) ? null : clean.Trim();
        }
        catch { return null; }
    }

    /// <summary>Collapse newlines into ". " so multi-line descriptions speak cleanly.</summary>
    private static string NormalizeLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s*[\r\n]+\s*", ". ");
        return text;
    }

    /// <summary>Extract the master id from a "&lt;WLTAG ... Arg1="N"&gt;" command tag.</summary>
    private static uint ParseWltagMasterId(string raw)
    {
        if (string.IsNullOrEmpty(raw) || !raw.Contains("WLTAG")) return 0;
        var m = System.Text.RegularExpressions.Regex.Match(raw, "Arg1\\s*=\\s*\"(\\d+)\"");
        return m.Success && uint.TryParse(m.Groups[1].Value, out uint id) ? id : 0;
    }

    private static void Reset()
    {
        API.LogInfo("[SF6Access] Status menu ended");
        _isActive = false;
        _statusParam = null;
        _equipParam = null;
        _masterParam = null;
        _lastItemName = null;
        _lastMasterText = null;
    }
}
