using REFrameworkNET;
using REFrameworkNET.Attributes;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the training mode pause menu (app.training.TrainingManager).
/// Polls PrimaryIndex/SecondaryIndex for navigation and announces the focused
/// item: parent label (CurrentParentData) + current value (CurrentMenuData) +
/// guide message. For spin rows CurrentMenuData is the selected VALUE child and
/// CurrentParentData is the row label. Value edits (left/right on the same row)
/// are detected by polling the resolved value text.
///
/// ScreenAdapter: the screen signal is TrainingManager.IsMenuOpening (no flow
/// param owns activity), checked every SearchInterval = 5 frames so close
/// detection stays fast (TrainingAttackDataHooks suppresses on IsInTrainingMenu).
/// ReadInterval = 1 drives the 3-frame delayed slider announce; the heavier
/// reads run every READ_TICKS frames. While the reversal / character-specific
/// submenus are up, this adapter freezes (they own announcements). The
/// OnSliderUp/Down hooks stay in the static [PluginEntryPoint]. Registered in
/// ScreenRegistry.
/// </summary>
public sealed class TrainingMenuHooks : ScreenAdapter
{
    private const string FLOW_PARAM_TYPE = "app.training.UIFlowTrainingMenu.Param";
    private static readonly string[] Types = { FLOW_PARAM_TYPE };
    public override string[] OwnedTypes => Types;

    // Navigation reads every 5th frame, value-edit reads every 10th (the
    // original POLL_READ_INTERVAL / POLL_VALUE_INTERVAL).
    private const int READ_TICKS = 5;
    private const int VALUE_TICKS = 10;

    private static TrainingMenuHooks _self;
    public static bool IsInTrainingMenu => _self != null && _self.Active;

    public TrainingMenuHooks()
    {
        SearchInterval = 5;
        ReadInterval = 1;
        _self = this;
    }

    private ManagedObject _manager;
    private ManagedObject _flowParam; // cached UIFlowTrainingMenu.Param
    private int _frame;
    private int _lastPrimary = -1;
    private int _lastSecondary = -1;
    private string _lastValueName;
    private string _lastSectionName;
    private int _lastSliderValue = int.MinValue;
    private string _lastRowText;

    // app.training.ItemType slider variants (SLIDER, SLIDER_GUIDE, SLIDER_VITAL_1P/2P,
    // SLIDER_DRIVE, SLIDER_SA_1P/2P) — their value is numeric, not a message Guid
    private static readonly int[] SliderItemTypes = { 2, 10, 11, 12, 13, 14, 15 };

    // Slot-strip rows whose GUI repaints all tiles at once (unreadable as plain
    // row text) — read the focused slot's named texts instead. app.training.ItemType:
    // PLAY_SLOT_ITEM=5, RECORD_SLOT_ITEM=6 (recording slots), REVERSAL_ITEM=8.
    // 19 is the reversal list's 2nd slot, which reports a different _Type (and a
    // bogus _SlotID) than the other reversal slots — verified via the log.
    private static readonly int[] SLOT_ITEM_TYPES = { 5, 6, 8, 19 };
    private bool _onSlotRow;
    private string _lastSlotText;
    private int _lastSlotId = int.MinValue;

    // OnSliderUp/Down edits: the slider's GUI text updates AFTER the handler
    // runs, so the hook only queues the slider and the read happens a few
    // frames later. ViewData/row-text polling missed edits around value 0
    // (panel repaints blew up the text diff; stale ViewData blocked the
    // fallback), so with these hooks installed the poll never announces.
    private static bool _sliderHooksInstalled;
    private static ManagedObject _pendingSlider;
    private static int _sliderReadDelay;
    private const int SLIDER_READ_DELAY_FRAMES = 3;

    /// <summary>The reversal move-selection / character-specific submenus open on
    /// top of the training menu and own announcements while up — freeze here.</summary>
    private static bool IsSuppressed() =>
        TrainingReversalHooks.IsActive || TrainingCharacterSpecificHooks.IsActive;

    [PluginEntryPoint]
    public static void Initialize()
    {
        InstallSliderHooks();
        API.LogInfo("[SF6Access] TrainingMenuHooks initialized");
    }

    private static void InstallSliderHooks()
    {
        try
        {
            var td = TDB.Get().FindType(FLOW_PARAM_TYPE);
            if (td == null)
            {
                API.LogInfo("[SF6Access] UIFlowTrainingMenu.Param type not found, slider hooks skipped");
                return;
            }

            foreach (var name in new[] { "OnSliderUp", "OnSliderDown" })
            {
                var method = td.GetMethod($"{name}(app.UIPartsSlider)") ?? td.GetMethod(name);
                if (method == null)
                {
                    API.LogInfo($"[SF6Access] {name} method not found");
                    continue;
                }

                var hook = method.AddHook(false);
                hook.AddPre(args =>
                {
                    try
                    {
                        _pendingSlider = ManagedObject.ToManagedObject(args[2]);
                        _sliderReadDelay = SLIDER_READ_DELAY_FRAMES;
                    }
                    catch { }
                    return PreHookResult.Continue;
                });
                _sliderHooksInstalled = true;
                API.LogInfo($"[SF6Access] Training {name} hook installed");
            }
        }
        catch (System.Exception ex)
        {
            API.LogError($"[SF6Access] Training slider hooks failed: {ex.Message}");
        }
    }

    protected override bool Locate()
    {
        // While a submenu owns the screen, freeze the current state — neither
        // activate nor deactivate (the parent menu is still open underneath).
        if (IsSuppressed()) return Active;

        if (_manager == null)
        {
            try { _manager = API.GetManagedSingleton("app.training.TrainingManager"); }
            catch { _manager = null; }
        }
        if (_manager == null || !IsMenuOpen()) return false;

        if (Active)
            _flowParam = FlowHelper.FindFlowParam(FLOW_PARAM_TYPE);
        return true;
    }

    protected override void OnActivate()
    {
        _lastPrimary = -1;
        _lastSecondary = -1;
        _lastValueName = null;
        _lastSectionName = null;
        _lastRowText = null;
        _flowParam = FlowHelper.FindFlowParam(FLOW_PARAM_TYPE);
        API.LogInfo("[SF6Access] Training menu opened");

        PollNavigation(valueTick: false);
    }

    protected override void OnDeactivate()
    {
        API.LogInfo("[SF6Access] Training menu closed");
        _lastPrimary = -1;
        _lastSecondary = -1;
        _lastValueName = null;
        _lastSectionName = null;
        _lastRowText = null;
        _flowParam = null;
        _onSlotRow = false;
        _lastSlotText = null;
        _lastSlotId = int.MinValue;
        _pendingSlider = null;
    }

    protected override void OnPoll()
    {
        _frame++;
        ProcessPendingSlider();

        if (IsSuppressed()) return;
        if (_frame % READ_TICKS != 0) return;

        PollNavigation(valueTick: _frame % VALUE_TICKS == 0);
    }

    private bool IsMenuOpen()
    {
        var result = FlowHelper.Call(_manager, "get_IsMenuOpening");
        return result is bool b && b;
    }

    /// <summary>
    /// Announce a queued slider edit from the slider's own GUI: e_text_value
    /// (+ e_text_endwords suffix, e.g. "%"). Unlike the section-wide row text,
    /// this is scoped to the edited control, so value 0 and panel repaints
    /// can't hide it. Falls back to the slider's CurrentParam float.
    /// </summary>
    private void ProcessPendingSlider()
    {
        if (_pendingSlider == null) return;
        if (--_sliderReadDelay > 0) return;

        var slider = _pendingSlider;
        _pendingSlider = null;
        try
        {
            string value = null, suffix = null;
            var control = FlowHelper.GetObjectField(slider, "Control")
                ?? FlowHelper.Call(slider, "get_Control") as ManagedObject;
            foreach (var t in GuiTextReader.ReadControlTexts(control))
            {
                if (t.Name == "e_text_value" && value == null) value = t.Text?.Trim();
                else if (t.Name == "e_text_endwords" && suffix == null) suffix = t.Text?.Trim();
            }

            if (string.IsNullOrEmpty(value))
            {
                float current = FlowHelper.ReadFloatField(slider, "CurrentParam");
                if (!float.IsNaN(current))
                    value = ((int)System.Math.Round(current)).ToString();
            }
            if (string.IsNullOrEmpty(value)) return;

            string announcement = string.IsNullOrEmpty(suffix) ? value : value + suffix;
            API.LogInfo($"[SF6Access] Training slider edit: {announcement}");
            Speak(announcement);

            // Keep the poll's sync state current so it stays quiet
            _lastSliderValue = FlowHelper.ReadIntField(FindViewData(), "SliderValue", _lastSliderValue);
            _lastRowText = ReadFocusedRowText();
        }
        catch { }
    }

    private void PollNavigation(bool valueTick)
    {
        int primary = FlowHelper.CallInt(_manager, "get_PrimaryIndex");
        int secondary = FlowHelper.CallInt(_manager, "get_SecondaryIndex");

        if (primary == _lastPrimary && secondary == _lastSecondary)
        {
            // Same row: detect value edits (left/right) via the resolved value text
            if (valueTick)
                PollValueChange();
            return;
        }

        bool first = _lastPrimary == -1 && _lastSecondary == -1;
        bool tabChanged = primary != _lastPrimary;
        _lastPrimary = primary;
        _lastSecondary = secondary;
        if (first) return;

        AnnounceCurrentItem(tabChanged);
    }

    private void PollValueChange()
    {
        // Reversal slot rows: left/right moves between tiles and in-place edits
        // (toggle, count, skill change) only show up in the slot DATA
        if (_onSlotRow)
        {
            PollReversalSlot();
            return;
        }

        // Slider rows (drive gauge, vitality, SA...): the OnSliderUp/Down
        // hooks announce edits from the slider's own GUI. The poll only keeps
        // the sync state fresh — announcing from here used stale ViewData
        // entries and a section-wide text diff that both went silent around
        // value 0 (drive bar muted on 0/1)
        var sliderRow = FindRowData();
        int sliderType = FlowHelper.ReadIntField(sliderRow, "_Type", -1);
        if (IsSliderType(sliderType))
        {
            int sliderValue = FlowHelper.ReadIntField(FindViewData(), "SliderValue", int.MinValue);
            if (_sliderHooksInstalled)
            {
                if (sliderValue != int.MinValue) _lastSliderValue = sliderValue;
                return;
            }

            // Hook install failed: keep the old ViewData-driven announce
            if (sliderValue != int.MinValue)
            {
                if (sliderValue != _lastSliderValue)
                {
                    bool firstSlider = _lastSliderValue == int.MinValue;
                    _lastSliderValue = sliderValue;
                    if (!firstSlider)
                    {
                        API.LogInfo($"[SF6Access] Training slider changed: {sliderValue}");
                        Speak(sliderValue.ToString());
                        _lastRowText = ReadFocusedRowText();
                    }
                }
                return;
            }
            // No UI entry this tick — fall through to the on-screen text poll
        }

        var data = FlowHelper.Call(_manager, "get_CurrentMenuData") as ManagedObject;
        string name = data != null ? FlowHelper.ResolveGuidField(data, "_MessageID") : null;
        if (!string.IsNullOrEmpty(name) && _lastValueName != null && name != _lastValueName)
        {
            _lastValueName = name;
            API.LogInfo($"[SF6Access] Training value changed: {name}");
            Speak(name);
            _lastRowText = ReadFocusedRowText();
            return;
        }
        if (!string.IsNullOrEmpty(name) && _lastValueName == null) _lastValueName = name;

        // Fallback for rows whose value is neither a slider field nor a message
        // change (drive/SA gauge percentages, per-character unique stocks...):
        // announce what changed in the focused row's on-screen text
        PollRowTextChange();
    }

    /// <summary>
    /// Detect left/right value edits by re-reading the focused row's GUI text
    /// and announcing only the changed segment.
    /// </summary>
    private void PollRowTextChange()
    {
        string text = ReadFocusedRowText();
        if (string.IsNullOrEmpty(text)) return;

        string previous = _lastRowText;
        if (text == previous) return;
        _lastRowText = text;
        if (previous == null) return; // First read after a focus change

        string announcement = FlowHelper.DiffSegments(previous, text);
        if (string.IsNullOrEmpty(announcement)) return;

        // A huge diff is a panel redraw (the reversal slot grid repaints all
        // its tiles), not a value edit — reading it all is just confusing
        if (CountSegments(announcement) > 5)
        {
            API.LogInfo($"[SF6Access] Training row diff skipped (panel redraw, {CountSegments(announcement)} segments)");
            return;
        }

        API.LogInfo($"[SF6Access] Training row text changed: {announcement}");
        Speak(announcement);
    }

    private static int CountSegments(string text) =>
        text.Split(new[] { ". " }, System.StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>On-screen text of the focused row in the menu's secondary list.</summary>
    private string ReadFocusedRowText()
    {
        try
        {
            var param = _flowParam ??= FlowHelper.FindFlowParam(FLOW_PARAM_TYPE);
            var list = FlowHelper.GetObjectField(param, "_SecondaryList");
            var child = FlowHelper.Call(list, "GetFocusChild") as ManagedObject;
            var control = FlowHelper.GetObjectField(child, "Control")
                ?? FlowHelper.Call(child, "get_Control") as ManagedObject;
            return GuiTextReader.ReadControlTextJoined(control);
        }
        catch { return null; }
    }

    private static bool IsSliderType(int itemType) =>
        System.Array.IndexOf(SliderItemTypes, itemType) >= 0;

    /// <summary>
    /// Current value of a slider row: the ViewData UI state when the row was
    /// recently redrawn, then the value displayed on screen next to the row's
    /// label, and only then the training parameter settings — they can be
    /// stale (the drive bar announced 0 while the screen showed 2).
    /// </summary>
    private int ReadSliderValue(ManagedObject viewData, int itemType, ManagedObject rowData, string rowLabel)
    {
        // Vitality (11/12) and Super Art gauge (14/15) rows: the ViewData
        // SliderValue is frequently a stale 0 for these (1P vitality, 1P/2P
        // super art announced 0 regardless of the real setting). The on-screen
        // value next to the row label is authoritative — the Dummy Settings
        // dump shows e_text_value=100 immediately before "Barra de vida do J1"
        // — so read it FIRST for these types. SLIDER_DRIVE (13) keeps ViewData
        // first; it was verified correct there.
        bool gaugePreferGui = itemType is 11 or 12 or 14 or 15;
        if (gaugePreferGui)
        {
            string guiFirst = ReadSliderValueFromGui(rowLabel);
            if (guiFirst != null && int.TryParse(guiFirst, out int gp)) return gp;
        }

        int value = FlowHelper.ReadIntField(viewData, "SliderValue", int.MinValue);
        if (value != int.MinValue) return value;

        string guiValue = gaugePreferGui ? null : ReadSliderValueFromGui(rowLabel);
        if (guiValue != null && int.TryParse(guiValue, out int parsed)) return parsed;

        // ItemType → (PlayerData field, player index); SLIDER_DRIVE is one
        // type for both players, distinguished by the row's _SlotID
        (string field, int player) = itemType switch
        {
            11 => ("Vital_Point", 0),  // SLIDER_VITAL_1P
            12 => ("Vital_Point", 1),  // SLIDER_VITAL_2P
            13 => ("DG_Point", FlowHelper.ReadIntField(rowData, "_SlotID", 0)), // SLIDER_DRIVE
            14 => ("SA_Point", 0),     // SLIDER_SA_1P
            15 => ("SA_Point", 1),     // SLIDER_SA_2P
            _ => (null, 0),
        };
        if (field == null) return int.MinValue;

        try
        {
            var players = FlowHelper.GetObjectField(
                FlowHelper.GetObjectField(FlowHelper.GetObjectField(_manager, "_tData"), "ParameterSetting"),
                "PlayerDatas");
            if (player < 0) player = 0;
            if (player > 1) player = 1;
            var playerData = FlowHelper.GetListItem(players, player);
            return FlowHelper.ReadIntField(playerData, field, int.MinValue);
        }
        catch { return int.MinValue; }
    }

    /// <summary>
    /// On-screen value of the focused slider row: in the focused section's
    /// texts (the focus child is the whole section group), find the
    /// e_text_name matching the row label and return the nearest preceding
    /// visible e_text_value — that pairing matches the on-screen layout.
    /// </summary>
    private string ReadSliderValueFromGui(string rowLabel)
    {
        if (string.IsNullOrEmpty(rowLabel)) return null;
        try
        {
            var param = _flowParam ??= FlowHelper.FindFlowParam(FLOW_PARAM_TYPE);
            var list = FlowHelper.GetObjectField(param, "_SecondaryList");
            var child = FlowHelper.Call(list, "GetFocusChild") as ManagedObject;
            var control = FlowHelper.GetObjectField(child, "Control")
                ?? FlowHelper.Call(child, "get_Control") as ManagedObject;

            string label = rowLabel.Trim();
            string lastValue = null;
            foreach (var t in GuiTextReader.ReadControlTexts(control))
            {
                if (t.Name == "e_text_value") lastValue = t.Text?.Trim();
                else if (t.Name == "e_text_name" && t.Text?.Trim() == label) return lastValue;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// The focused row's TrainingMenuData from the menu data tree
    /// (_UIData._MenuData[tab]._ChildData[row]) — deterministic, unlike the
    /// dirty per-redraw _ViewDataList.
    /// </summary>
    private ManagedObject FindRowData()
    {
        try
        {
            var uiData = FlowHelper.GetObjectField(_manager, "_UIData");
            var tabs = FlowHelper.GetObjectField(uiData, "_MenuData");
            var tab = FlowHelper.GetListItem(tabs, _lastPrimary);
            var rows = FlowHelper.GetObjectField(tab, "_ChildData");
            return FlowHelper.GetListItem(rows, _lastSecondary);
        }
        catch { return null; }
    }

    private void AnnounceCurrentItem(bool tabChanged)
    {
        var data = FlowHelper.Call(_manager, "get_CurrentMenuData") as ManagedObject;
        if (data == null) return;

        // For spin rows CurrentMenuData is the selected VALUE child; the row's
        // own data (label + tooltip) lives in the flow param's _ViewDataList.
        // CurrentParentData is the TAB, not the row.
        var viewData = FindViewData();
        var sectionData = FlowHelper.GetObjectField(viewData, "ParentData");

        // The menu data tree gives the row deterministically — _ViewDataList
        // only has entries for recently-redrawn rows, with stale duplicates
        var rowData = FindRowData() ?? FlowHelper.GetObjectField(viewData, "Data");
        var row = rowData ?? data;

        _onSlotRow = System.Array.IndexOf(SLOT_ITEM_TYPES, FlowHelper.ReadIntField(row, "_Type", -1)) >= 0;
        _lastSlotText = null;

        string value = FlowHelper.ResolveGuidField(data, "_MessageID");
        _lastValueName = value;

        string label = FlowHelper.ResolveGuidField(rowData, "_MessageID");
        string sub = FlowHelper.ResolveGuidField(row, "_SubMessageID");
        string guide = FlowHelper.ResolveGuidField(row, "_GuideMessage")
                    ?? FlowHelper.ResolveGuidField(row, "_GuideMessageID");

        // The buttons guides refer to render as inline glyphs with no text
        // ("Ative um espaço com .") — fill the gaps with the assigned inputs
        guide = InjectGuideIcons(guide, row);

        // Section/tab header ("Dummy settings", "Special settings"...):
        // announce whenever it changes, not only on tab switches
        string section = FlowHelper.ResolveGuidField(sectionData, "_MessageID");
        if (string.IsNullOrEmpty(section))
        {
            var parentData = FlowHelper.Call(_manager, "get_CurrentParentData") as ManagedObject;
            section = FlowHelper.ResolveGuidField(parentData, "_MessageID");
        }

        var parts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(section) && (tabChanged || section != _lastSectionName))
            parts.Add(section);
        _lastSectionName = section;

        // No ViewData (row never redrawn): the on-screen row text is the only
        // place that has label AND displayed value together
        string rowText = null;
        if (string.IsNullOrEmpty(label) && !_onSlotRow)
        {
            rowText = ReadFocusedRowText();
            if (!string.IsNullOrEmpty(rowText) && CountSegments(rowText) > 5)
                rowText = null; // repaint panels (slot strips) are unreadable
        }

        if (!string.IsNullOrEmpty(rowText))
        {
            parts.Add(rowText);
        }
        else
        {
            if (!string.IsNullOrEmpty(label))
                parts.Add(label);
            if (!string.IsNullOrEmpty(value) && value != label)
                parts.Add(value);
        }

        // Slider rows carry a numeric value (drive gauge, vitality, SA...)
        _lastSliderValue = int.MinValue;
        int itemType = FlowHelper.ReadIntField(row, "_Type", -1);
        if (IsSliderType(itemType))
        {
            int sliderValue = ReadSliderValue(viewData, itemType, row, label);
            _lastSliderValue = sliderValue;
            if (sliderValue != int.MinValue)
                parts.Add(sliderValue.ToString());
        }

        // Reversal slot rows: append the focused slot's on-screen data
        // (_tData is null at runtime, so read the GUI: slot name, assigned
        // move, on/off state, delay)
        if (_onSlotRow)
        {
            // ReadReversalRowGui reads the slot number from the on-screen tile;
            // _SlotID + 1 is only a fallback (it is bogus on the reversal 2nd slot).
            int slotId = FlowHelper.ReadIntField(row, "_SlotID", -1);
            string slotInfo = ReadReversalRowGui(slotId >= 0 ? slotId + 1 : -1);
            _lastSlotText = slotInfo;
            _lastSlotId = slotId; // baseline so the edit poll doesn't re-announce on navigation
            API.LogInfo($"[SF6Access] Slot row: SlotID={slotId}, info={slotInfo}");
            if (!string.IsNullOrEmpty(slotInfo)) parts.Add(slotInfo);
        }

        if (!string.IsNullOrEmpty(sub) && sub != label && sub != value)
            parts.Add(sub);
        if (!string.IsNullOrEmpty(guide) && guide != label)
            parts.Add(guide);

        // Snapshot the row's on-screen text so the value-edit poll only
        // reacts to later changes on this same row
        _lastRowText = rowText ?? ReadFocusedRowText();

        if (parts.Count == 0) return;

        string announcement = string.Join(". ", parts);
        API.LogInfo($"[SF6Access] Training menu [{_lastPrimary},{_lastSecondary}]: {announcement}");
        Speak(announcement);
    }

    /// <summary>
    /// Fill icon gaps in OTHER hooks' announcements (the InputGuide tooltip
    /// repeats the focused row's guide with bare " ." gaps) using the current
    /// row's icons. No-op outside the training menu.
    /// </summary>
    public static string FillGuideIcons(string text)
    {
        var self = _self;
        if (self == null || !self.Active || string.IsNullOrEmpty(text)) return text;
        try
        {
            var row = self.FindRowData()
                ?? FlowHelper.Call(self._manager, "get_CurrentMenuData") as ManagedObject;
            return self.InjectGuideIcons(text, row);
        }
        catch { return text; }
    }

    /// <summary>
    /// Guides mention buttons as inline glyphs the screen reader can't see:
    /// the stripped text keeps a lone " ." or a double space where each glyph
    /// sat. Fill the gaps with the row's on-screen icon glyphs (they reflect
    /// the player's actual device: &lt;ICON KeyR&gt; for keyboard), falling
    /// back to the row data's assigned inputs (_GuidIcon/_GuidAddIcon).
    /// </summary>
    private string InjectGuideIcons(string guide, ManagedObject rowData)
    {
        if (string.IsNullOrEmpty(guide) || rowData == null) return guide;
        try
        {
            var icons = ReadRowIconNames();
            int searchFrom = 0;
            for (int i = 0; i < 2; i++)
            {
                string spoken = i < icons.Count ? icons[i] : null;
                if (spoken == null)
                {
                    int id = FlowHelper.ReadIntField(rowData, i == 0 ? "_GuidIcon" : "_GuidAddIcon", 0);
                    if (id <= 0) continue;
                    string token = FlowHelper.ResolveEnumName("app.InputAssign.Digital.Id", id);
                    spoken = token != null ? FlowHelper.SpeakInputToken(token) : null;
                }
                if (string.IsNullOrEmpty(spoken)) continue;

                var gap = System.Text.RegularExpressions.Regex.Match(
                    guide.Substring(searchFrom), @"  | \.");
                // No glyph gap to fill: leave the guide alone — appending the
                // icons at the end read as a confusing "R T" tail
                if (!gap.Success) break;

                int pos = searchFrom + gap.Index;
                string replacement = gap.Value == " ." ? $" {spoken}." : $" {spoken} ";
                guide = guide.Substring(0, pos) + replacement + guide.Substring(pos + gap.Value.Length);
                searchFrom = pos + replacement.Length;
            }
        }
        catch { }
        return guide;
    }

    /// <summary>
    /// Detect in-place edits on the focused reversal slot row (the T toggle
    /// for on/off, the R delay/timing config, count changes) by re-reading the
    /// focused slot's on-screen text and announcing the changed segment.
    /// </summary>
    private void PollReversalSlot()
    {
        int slotId = FlowHelper.ReadIntField(FindRowData(), "_SlotID", -1);
        string info = ReadReversalRowGui(slotId >= 0 ? slotId + 1 : -1);
        if (string.IsNullOrEmpty(info)) return;

        // Moving between slots is navigation — AnnounceCurrentItem reads that.
        // Only announce in-place edits on the SAME slot (toggle/count/move),
        // otherwise the GUI lag made this fire a second "Slot N" while moving.
        if (slotId != _lastSlotId)
        {
            _lastSlotId = slotId;
            _lastSlotText = info;
            return;
        }

        string previous = _lastSlotText;
        if (info == previous) return;
        _lastSlotText = info;
        if (previous == null) return; // baseline from the focus-change read

        // Same slot, a field was edited: announce only what changed
        string announcement = FlowHelper.DiffSegments(previous, info);
        if (string.IsNullOrEmpty(announcement)) announcement = info;

        API.LogInfo($"[SF6Access] Reversal slot: {announcement}");
        Speak(announcement);
    }

    /// <summary>
    /// On-screen data of the focused reversal slot row: slot name, assigned
    /// move (or "Empty"), on/off state, count and delay. Read from the GUI
    /// because TrainingManager._tData is null at runtime — the named text
    /// elements (e_txt_name/e_txt_center/e_txt_sub/e_txt_right/e_txt_east)
    /// carry everything the sighted player sees.
    /// </summary>
    private string ReadReversalRowGui(int slotNumber = -1)
    {
        try
        {
            var param = _flowParam ??= FlowHelper.FindFlowParam(FLOW_PARAM_TYPE);
            var list = FlowHelper.GetObjectField(param, "_SecondaryList");
            var child = FlowHelper.Call(list, "GetFocusChild") as ManagedObject;
            var control = FlowHelper.GetObjectField(child, "Control")
                ?? FlowHelper.Call(child, "get_Control") as ManagedObject;
            if (control == null) return null;

            // Prefer the on-screen tile label (e_txt_name): the log confirmed it
            // matches the visible slot for every slot, including the reversal 2nd
            // slot whose row data reports a bogus _SlotID. Fall back to the row's
            // _SlotID + 1 only when the GUI label is missing.
            string name = null;
            string move = null, state = null, count = null, delay = null;
            foreach (var t in GuiTextReader.ReadControlTexts(control))
            {
                string text = t.Text?.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                switch (t.Name)
                {
                    case "e_txt_name": name ??= text; break;   // "Slot 1"
                    case "e_txt_center": move ??= text; break; // move name or "Empty"
                    case "e_txt_sub": state ??= text; break;   // "On" / "Off"
                    case "e_txt_right": count ??= text; break; // "Count: 1"
                    case "e_txt_east": delay ??= text; break;  // "Delay: 0F"
                }
            }
            if (string.IsNullOrEmpty(name) && slotNumber > 0) name = $"Slot {slotNumber}";

            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(name)) parts.Add(name);
            if (!string.IsNullOrEmpty(move)) parts.Add(move);
            if (!string.IsNullOrEmpty(state)) parts.Add(state);
            if (!string.IsNullOrEmpty(count)) parts.Add(count);
            if (!string.IsNullOrEmpty(delay)) parts.Add(delay);
            return parts.Count > 0 ? string.Join(". ", parts) : null;
        }
        catch { return null; }
    }

    /// <summary>Spoken names of the focused row's inline icon glyphs, in tree
    /// order (e.g. &lt;ICON KeyR&gt; → "R") — they reflect the player's device.</summary>
    private System.Collections.Generic.List<string> ReadRowIconNames()
    {
        var names = new System.Collections.Generic.List<string>();
        try
        {
            var param = _flowParam ??= FlowHelper.FindFlowParam(FLOW_PARAM_TYPE);
            var list = FlowHelper.GetObjectField(param, "_SecondaryList");
            var child = FlowHelper.Call(list, "GetFocusChild") as ManagedObject;
            var control = FlowHelper.GetObjectField(child, "Control")
                ?? FlowHelper.Call(child, "get_Control") as ManagedObject;
            foreach (var t in GuiTextReader.ReadControlTexts(control, visibleOnly: false))
            {
                if (t.Raw == null || !t.Raw.Contains("<ICON")) continue;
                string spoken = FlowHelper.CleanTags(FlowHelper.SpeakableIcons(t.Raw))?.Trim();
                if (!string.IsNullOrEmpty(spoken) && names.Count < 4) names.Add(spoken);
            }
        }
        catch { }
        return names;
    }

    /// <summary>
    /// The focused row's ViewData entry (Index == SecondaryIndex). The list
    /// accumulates per-redraw entries with stale ones FIRST — keep the last
    /// match (the newest), or slider values never updated after edits.
    /// Returns null for rows that were never redrawn.
    /// </summary>
    private ManagedObject FindViewData()
    {
        try
        {
            var param = _flowParam ??= FlowHelper.FindFlowParam(FLOW_PARAM_TYPE);
            var list = FlowHelper.GetObjectField(param, "_ViewDataList");
            if (list == null) return null;

            ManagedObject newest = null;
            int count = FlowHelper.GetListCount(list);
            for (int i = 0; i < count; i++)
            {
                var vd = FlowHelper.GetListItem(list, i);
                if (vd == null) continue;
                if (FlowHelper.ReadIntField(vd, "Index") != _lastSecondary) continue;
                newest = vd;
            }
            return newest;
        }
        catch { }
        return null;
    }
}
