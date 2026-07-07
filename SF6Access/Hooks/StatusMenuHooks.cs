using System.Collections.Generic;
using System.Runtime.InteropServices;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the avatar status menu (app.UIStatusMenu: equip / special
/// moves / super arts / skills / master tabs) opened from training or the hub.
/// Polls StatusMenuParam.TabIndex for tab changes and UIStatusMenu_Equip.Param
/// focus state for the equip tab: slot list (mTopList), item grid (mItemList,
/// item name read from mEquipItemLabel) and preset list (mPresetList).
///
/// ScreenAdapter (G-key pattern): ReadInterval = 1 so the G shortcut edge is
/// sampled every frame; the heavier reads run every READ_TICKS frames via an
/// instance counter. Child params (equip/master) are re-tracked in Locate()
/// every SearchInterval. MainMenuHooks + GuideTextHooks read IsInStatusMenu.
/// Registered in ScreenRegistry.
/// </summary>
public sealed class StatusMenuHooks : ScreenAdapter
{
    private const string STATUS_PARAM_TYPE = "app.UIStatusMenu.StatusMenuParam";
    private const string EQUIP_PARAM_TYPE = "app.UIStatusMenu_Equip.Param";
    private const string MASTER_PARAM_TYPE = "app.UIStatusMenu_Master.Param";

    private static readonly string[] Types = { STATUS_PARAM_TYPE, EQUIP_PARAM_TYPE, MASTER_PARAM_TYPE };
    public override string[] OwnedTypes => Types;

    // Heavier reads run every 5th frame (the original POLL_READ_INTERVAL).
    private const int READ_TICKS = 5;

    private static StatusMenuHooks _self;
    public static bool IsInStatusMenu => _self != null && _self.Active;

    public StatusMenuHooks()
    {
        SearchInterval = 60;
        ReadInterval = 1; // per-frame G-key edge; reads gated by READ_TICKS
        _self = this;
    }

    private ManagedObject _statusParam;
    private ManagedObject _equipParam;
    private ManagedObject _masterParam;

    private int _frame;
    private int _lastTab = -2;
    private int _lastGroupFocus = -2;
    private int _lastTopIndex = -2;
    private int _lastItemIndex = -2;
    private int _lastPresetIndex = -2;
    private string _lastItemName;
    private string _lastMasterText;

    // Stats panel (mEquipStatus): announced once on entering the equip tab and
    // re-read on the G key. The item grid announces each focused item's stats.
    private bool _statsAnnounced;

    // Unique-moves popup (UniqueMovesWindow) opened from the Style slot — a
    // move list with name / command / description that overlays the equip screen.
    private bool _uniqueWindowWasOpen;
    private int _lastUniqueIndex = -2;
    private string _lastUniqueState;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")]
    private static extern System.IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(System.IntPtr hWnd, out uint processId);
    private const int VK_G = 0x47;
    private bool _lastGState;

    /// <summary>True only when the game window is the foreground app — so the G
    /// shortcut never fires while the user is typing in another window.</summary>
    private static bool IsGameForeground()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            return pid == (uint)System.Environment.ProcessId;
        }
        catch { return false; }
    }

    // English fallbacks when on-screen tab/slot labels can't be read
    // (UIStatusMenu.MenuType and UIStatusMenu_Equip.TopFocusType enum order)
    private static readonly string[] TabNames =
        { "Equipment", "Special Moves", "Super Arts", "Skills", "Master" };
    private static readonly string[] TopFocusNames =
    {
        "Style", "Head", "Body", "Body sub", "Legs", "Shoes",
        "Accessory 1", "Accessory 2", "Accessory 3", "My set", "Equip type"
    };

    protected override bool Locate()
    {
        var current = FlowHelper.TrackFlowParam(STATUS_PARAM_TYPE, _statusParam, out bool changed);
        if (current == null)
        {
            _statusParam = null;
            return false;
        }

        if (changed)
        {
            // Menu opened or was recreated — re-bind all params + reset state
            _statusParam = current;
            var found = FlowHelper.FindFlowParams(Types);
            found.TryGetValue(EQUIP_PARAM_TYPE, out _equipParam);
            found.TryGetValue(MASTER_PARAM_TYPE, out _masterParam);
            ResetState();
            API.LogInfo($"[SF6Access] Status menu active (equip={_equipParam != null})");
            PollState();
        }
        else
        {
            // Child params come and go as the user switches tabs / reopens
            // sub-screens — track the live instances
            _equipParam = FlowHelper.TrackFlowParam(EQUIP_PARAM_TYPE, _equipParam, out bool equipChangedDummy);
            _masterParam = FlowHelper.TrackFlowParam(MASTER_PARAM_TYPE, _masterParam, out bool masterChangedDummy);
        }
        return true;
    }

    protected override void OnDeactivate()
    {
        API.LogInfo("[SF6Access] Status menu ended");
        _statusParam = null;
        _equipParam = null;
        _masterParam = null;
        ResetState();
    }

    private void ResetState()
    {
        _lastTab = -2;
        _lastGroupFocus = -2;
        _lastTopIndex = -2;
        _lastItemIndex = -2;
        _lastPresetIndex = -2;
        _lastItemName = null;
        _lastMasterText = null;
        _statsAnnounced = false;
        _uniqueWindowWasOpen = false;
        _lastUniqueIndex = -2;
        _lastUniqueState = null;
    }

    protected override void OnPoll()
    {
        // G re-reads the full stats panel on demand (only with the game focused,
        // so it never fires while the user types in another window)
        bool gDown = (GetAsyncKeyState(VK_G) & 0x8000) != 0;
        if (gDown && !_lastGState && IsGameForeground()) AnnounceStatsSummary(interrupt: true);
        _lastGState = gDown;

        if (++_frame % READ_TICKS == 0)
            PollState();
    }

    private void PollState()
    {
        AnnounceTabChange();

        // Master tab: read the focused master's detail window (its own child flow,
        // no equip param) — this is where the avatar's fighting style is equipped
        if (_masterParam != null)
            PollMasterTab();

        if (_equipParam == null) return;

        // Read the combat stats once on entering the equip tab (after the tab
        // name), so the player hears their avatar's stats without hunting for them
        if (!_statsAnnounced)
        {
            _statsAnnounced = true;
            AnnounceStatsSummary(interrupt: false);
        }

        // The unique-moves popup overlays the equip screen — when it's open read
        // it and skip the normal slot/grid polling underneath.
        if (PollUniqueMovesWindow()) return;

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

    private void AnnounceTabChange()
    {
        int tab = FlowHelper.ReadIntField(_statusParam, "TabIndex");
        if (tab < 0 || tab == _lastTab) return;

        bool first = _lastTab == -2;
        _lastTab = tab;

        var tabList = FlowHelper.GetObjectField(_statusParam, "TabList");
        string label = FlowHelper.ReadListRowText(tabList, tab);
        if (string.IsNullOrEmpty(label) && tab < TabNames.Length)
            label = LangFile.GetByText("tab", TabNames[tab]);
        if (string.IsNullOrEmpty(label)) return;

        // Announce the initial tab too: it tells the user the menu is open
        API.LogInfo($"[SF6Access] Status menu tab [{tab}]: {label}");
        Speak(label, interrupt: !first);
    }

    /// <summary>Equipment slot list (Style / Head / Body / ... / My set).</summary>
    private void PollTopList(bool groupChanged)
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
            label = LangFile.GetByText("slot", TopFocusNames[idx]);
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
        Speak(announcement);
    }

    /// <summary>Item grid: the focused item's name renders in mEquipItemLabel.</summary>
    private void PollItemGrid(bool groupChanged)
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
        Speak(name);

        // Announce only the stats this item grants (the equipped totals are
        // available on demand via G). Styles aren't gear items — skip them.
        if (!IsStyleSlot()) AnnounceItemStats(name);
    }

    /// <summary>Preset (my set) list rows.</summary>
    private void PollPresetList(bool groupChanged)
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
        Speak(label);
    }

    /// <summary>
    /// Master tab grid: navigating a master updates mMasterDetailWindow with the
    /// focused master's name, level and style detail. The master names are
    /// rendered as text here (unlike the equip slots), so read them directly.
    /// </summary>
    private void PollMasterTab()
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
        Speak(text, interrupt: !first);
    }

    private string ReadEquipItemLabel()
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
    private bool IsStyleSlot()
        => FlowHelper.ReadIntField(_equipParam, "TopFocus") == 0;

    /// <summary>
    /// Name of the currently-equipped style ("Ryu's Style"), via the player's
    /// equipped style id (WTPlayerData.Style.StyleEquipId) → master fighter name.
    /// </summary>
    private string ResolveEquippedStyleName()
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
    private string ResolveSlotEquippedItem(int focusIndex)
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
    private string ResolveStyleName(int idx)
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

    /// <summary>
    /// Read the unique-moves popup (UniqueMovesWindow) when it is shown over the
    /// equip screen: announces the focused move's name, command and description.
    /// Returns true while the popup is open so the caller skips normal polling.
    /// </summary>
    private bool PollUniqueMovesWindow()
    {
        var window = FlowHelper.GetObjectField(_equipParam, "UniqueMovesWindow");
        bool open = window != null && IsWidgetShown(window);
        if (!open)
        {
            _uniqueWindowWasOpen = false;
            _lastUniqueIndex = -2;
            _lastUniqueState = null;
            return false;
        }

        var list = FlowHelper.GetObjectField(window, "mPartsList");
        int idx = FlowHelper.CallInt(list, "get_SelectedIndex");
        string state = ReadUniqueMoveState(window, list, idx);

        bool firstOpen = !_uniqueWindowWasOpen;
        _uniqueWindowWasOpen = true;

        bool moved = idx != _lastUniqueIndex || firstOpen;
        bool toggled = !moved && state != _lastUniqueState; // enabled/disabled in place
        if (!moved && !toggled) return true;
        _lastUniqueIndex = idx;
        _lastUniqueState = state;

        // Move name (from the detail panel, already localized)
        var detail = FlowHelper.GetObjectField(window, "mPartsDetail");
        string name = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(detail, "mTextMoveName"));
        if (string.IsNullOrWhiteSpace(name))
            name = FlowHelper.ReadSelectedItemText(list); // fall back to the focused row
        if (string.IsNullOrWhiteSpace(name)) return true;

        // State goes right after the name — enabling/disabling is the window's purpose
        string head = string.IsNullOrEmpty(state) ? name.Trim() : $"{name.Trim()}, {state}";

        // On a toggle (no cursor move) announce just the name + new state
        if (toggled)
        {
            API.LogInfo($"[SF6Access] Unique move toggled [{idx}]: {head}");
            Speak(head, interrupt: true);
            return true;
        }

        var parts = new List<string> { head };
        string command = ReadUniqueMoveCommand(list);
        if (!string.IsNullOrWhiteSpace(command)) parts.Add(command);
        string desc = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(detail, "mTextDescription"));
        if (!string.IsNullOrWhiteSpace(desc)) parts.Add(desc.Trim());

        string text = string.Join(". ", parts);
        API.LogInfo($"[SF6Access] Unique move [{idx}]: {text}");
        Speak(text, interrupt: !firstOpen);
        return true;
    }

    /// <summary>"On"/"Off" for the focused unique move, via UniqueMovesListWidget.IsEquip.</summary>
    private static string ReadUniqueMoveState(ManagedObject window, ManagedObject list, int idx)
    {
        try
        {
            // GetFocusSkill is the reliable focused entry; fall back to SkillList[idx]
            var skill = FlowHelper.Call(window, "GetFocusSkill") as ManagedObject;
            if (skill == null)
                skill = FlowHelper.GetListItem(FlowHelper.GetObjectField(window, "SkillList"), idx);
            if (skill == null) return null;

            int skillId = FlowHelper.ReadIntField(skill, "SkillId", -1);
            if (skillId < 0) return null;

            var eq = FlowHelper.Call(window, "IsEquip", (uint)skillId);
            if (eq is not bool on) return null;

            return LocalizedText.OnOff(on);
        }
        catch { return null; }
    }

    /// <summary>The focused move row's command input ("M", "HH"), input tags spoken.</summary>
    private static string ReadUniqueMoveCommand(ManagedObject list)
    {
        try
        {
            var item = FlowHelper.Call(list, "get_SelectedItem") as ManagedObject;
            foreach (var t in GuiTextReader.ReadControlTexts(item))
            {
                if (t.Name == "e_text_command" && !string.IsNullOrWhiteSpace(t.Raw))
                    return FlowHelper.SpeakableIcons(t.Raw).Trim();
            }
        }
        catch { }
        return null;
    }

    /// <summary>True when the unique-moves popup is shown. Prefers the widget's
    /// get_IsShow; if that getter does not dispatch on the concrete type, falls
    /// back to detecting the popup's GUI (StatusMenu_UniqueSkill) in the scene.</summary>
    private static bool IsWidgetShown(ManagedObject widget)
    {
        var shown = FlowHelper.Call(widget, "get_IsShow");
        if (shown is bool b) return b;
        return GuiTextReader.FindGuiViews("UniqueSkill").Count > 0;
    }

    /// <summary>Speak the equipped avatar's stats summary (totals + perks).</summary>
    private void AnnounceStatsSummary(bool interrupt)
    {
        if (_equipParam == null) return;
        string summary = AvatarStatsReader.ReadSummary(_statusParam, _equipParam);
        if (string.IsNullOrWhiteSpace(summary)) return;

        API.LogInfo($"[SF6Access] Status stats: {summary}");
        Speak(summary, interrupt);
    }

    /// <summary>Announce only the stats the focused gear item grants.</summary>
    private void AnnounceItemStats(string itemName)
    {
        string text = AvatarStatsReader.FormatStats(
            AvatarStatsReader.ReadItemStats(_equipParam, itemName));
        if (string.IsNullOrWhiteSpace(text)) return;

        API.LogInfo($"[SF6Access] Status item stats: {text}");
        Speak(text, interrupt: false);
    }
}
