using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the training mode pause menu (app.training.TrainingManager).
/// Polls PrimaryIndex/SecondaryIndex for navigation and announces the focused
/// item: parent label (CurrentParentData) + current value (CurrentMenuData) +
/// guide message. For spin rows CurrentMenuData is the selected VALUE child and
/// CurrentParentData is the row label. Value edits (left/right on the same row)
/// are detected by polling the resolved value text.
/// </summary>
public class TrainingMenuHooks
{
    private const string FLOW_PARAM_TYPE = "app.training.UIFlowTrainingMenu.Param";

    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;
    private const int POLL_VALUE_INTERVAL = 10;

    private static ManagedObject _manager;
    private static int _lastPrimary = -1;
    private static int _lastSecondary = -1;
    private static string _lastValueName;
    private static string _lastSectionName;
    private static int _lastSliderValue = int.MinValue;
    private static string _lastRowText;
    private static ManagedObject _flowParam; // cached UIFlowTrainingMenu.Param

    // app.training.ItemType slider variants (SLIDER, SLIDER_GUIDE, SLIDER_VITAL_1P/2P,
    // SLIDER_DRIVE, SLIDER_SA_1P/2P) — their value is numeric, not a message Guid
    private static readonly int[] SliderItemTypes = { 2, 10, 11, 12, 13, 14, 15 };

    // app.training.ItemType.REVERSAL_ITEM: a strip of reversal slot tiles whose
    // GUI repaints all tiles at once (unreadable as row text) — read the slot
    // data from TrainingManager._tData instead
    private const int ITEM_TYPE_REVERSAL = 8;
    private static bool _onReversalRow;
    private static string _lastSlotSig;

    // TrainingReversalData skill arrays indexed by app.training.ReversalType
    // (NORMAL..OTHER); RECORDING (4) has no skill array
    private static readonly string[] SkillArrayFields =
        { "NormalData", "CommandNormalData", "SpecialData", "SaData", null, "CommonData", "OtherData" };

    // OnSliderUp/Down edits: the slider's GUI text updates AFTER the handler
    // runs, so the hook only queues the slider and the read happens a few
    // frames later. ViewData/row-text polling missed edits around value 0
    // (panel repaints blew up the text diff; stale ViewData blocked the
    // fallback), so with these hooks installed the poll never announces.
    private static bool _sliderHooksInstalled;
    private static ManagedObject _pendingSlider;
    private static int _sliderReadDelay;
    private const int SLIDER_READ_DELAY_FRAMES = 3;

    public static bool IsInTrainingMenu => _isActive;

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

    /// <summary>
    /// Announce a queued slider edit from the slider's own GUI: e_text_value
    /// (+ e_text_endwords suffix, e.g. "%"). Unlike the section-wide row text,
    /// this is scoped to the edited control, so value 0 and panel repaints
    /// can't hide it. Falls back to the slider's CurrentParam float.
    /// </summary>
    private static void ProcessPendingSlider()
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
            ScreenReaderService.Speak(announcement);

            // Keep the poll's sync state current so it stays quiet
            _lastSliderValue = FlowHelper.ReadIntField(FindViewData(), "SliderValue", _lastSliderValue);
            _lastRowText = ReadFocusedRowText();
        }
        catch { }
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;
        ProcessPendingSlider();

        if (!_isActive)
        {
            if (_pollCounter % POLL_SEARCH_INTERVAL != 0) return;
            TryActivate();
            return;
        }

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
            _flowParam = FlowHelper.FindFlowParam(FLOW_PARAM_TYPE);

        if (_pollCounter % POLL_READ_INTERVAL != 0) return;

        if (!IsMenuOpen())
        {
            Reset();
            return;
        }

        PollNavigation();
    }

    private static void TryActivate()
    {
        try
        {
            _manager = API.GetManagedSingleton("app.training.TrainingManager");
        }
        catch { _manager = null; }

        if (_manager == null || !IsMenuOpen()) return;

        _lastPrimary = -1;
        _lastSecondary = -1;
        _lastValueName = null;
        _lastSectionName = null;
        _lastRowText = null;
        _flowParam = FlowHelper.FindFlowParam(FLOW_PARAM_TYPE);
        _isActive = true;
        API.LogInfo("[SF6Access] Training menu opened");

        PollNavigation();
    }

    private static bool IsMenuOpen()
    {
        var result = FlowHelper.Call(_manager, "get_IsMenuOpening");
        return result is bool b && b;
    }

    private static void PollNavigation()
    {
        int primary = FlowHelper.CallInt(_manager, "get_PrimaryIndex");
        int secondary = FlowHelper.CallInt(_manager, "get_SecondaryIndex");

        if (primary == _lastPrimary && secondary == _lastSecondary)
        {
            // Same row: detect value edits (left/right) via the resolved value text
            if (_pollCounter % POLL_VALUE_INTERVAL == 0)
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

    private static void PollValueChange()
    {
        // Reversal slot rows: left/right moves between tiles and in-place edits
        // (toggle, count, skill change) only show up in the slot DATA
        if (_onReversalRow)
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
                        ScreenReaderService.Speak(sliderValue.ToString());
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
            ScreenReaderService.Speak(name);
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
    private static void PollRowTextChange()
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
        ScreenReaderService.Speak(announcement);
    }

    private static int CountSegments(string text) =>
        text.Split(new[] { ". " }, System.StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>On-screen text of the focused row in the menu's secondary list.</summary>
    private static string ReadFocusedRowText()
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
    private static int ReadSliderValue(ManagedObject viewData, int itemType, ManagedObject rowData, string rowLabel)
    {
        int value = FlowHelper.ReadIntField(viewData, "SliderValue", int.MinValue);
        if (value != int.MinValue) return value;

        string guiValue = ReadSliderValueFromGui(rowLabel);
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
    private static string ReadSliderValueFromGui(string rowLabel)
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
    private static ManagedObject FindRowData()
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

    private static void AnnounceCurrentItem(bool tabChanged)
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

        _onReversalRow = FlowHelper.ReadIntField(row, "_Type", -1) == ITEM_TYPE_REVERSAL;
        _lastSlotSig = null;

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
        if (string.IsNullOrEmpty(label) && !_onReversalRow)
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

        // Reversal slot rows: append the focused slot's real data
        if (_onReversalRow)
        {
            string slotInfo = ReadReversalSlotInfo(row, out _lastSlotSig);
            API.LogInfo($"[SF6Access] Reversal row: SlotID={FlowHelper.ReadIntField(row, "_SlotID", -1)}, info={slotInfo}");
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
        ScreenReaderService.Speak(announcement);
    }

    /// <summary>
    /// Fill icon gaps in OTHER hooks' announcements (the InputGuide tooltip
    /// repeats the focused row's guide with bare " ." gaps) using the current
    /// row's icons. No-op outside the training menu.
    /// </summary>
    public static string FillGuideIcons(string text)
    {
        if (!_isActive || string.IsNullOrEmpty(text)) return text;
        try
        {
            var row = FindRowData()
                ?? FlowHelper.Call(_manager, "get_CurrentMenuData") as ManagedObject;
            return InjectGuideIcons(text, row);
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
    private static string InjectGuideIcons(string guide, ManagedObject rowData)
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
    /// Detect tile moves (left/right, via TrainingManager._CurrentSlotNo) and
    /// in-place edits on the focused reversal slot row by watching a cheap
    /// signature of the slot's data fields.
    /// </summary>
    private static void PollReversalSlot()
    {
        // Reversal rows have no value children, so CurrentMenuData is the row
        // itself when the data tree lookup fails
        var rowData = FindRowData()
            ?? FlowHelper.Call(_manager, "get_CurrentMenuData") as ManagedObject;
        if (rowData == null) return;

        string info = ReadReversalSlotInfo(rowData, out string sig);
        if (sig == null || sig == _lastSlotSig) return;

        bool first = _lastSlotSig == null;
        _lastSlotSig = sig;
        if (first || string.IsNullOrEmpty(info)) return;

        API.LogInfo($"[SF6Access] Reversal slot: {info} ({sig})");
        ScreenReaderService.Speak(info);
        _lastRowText = ReadFocusedRowText();
    }

    /// <summary>
    /// Read the focused tile of a reversal slot strip from the training save
    /// data: slot number, assigned move name, active state, count and delay.
    /// The strip's GUI repaints all tiles at once and is unreadable as text.
    /// </summary>
    private static string ReadReversalSlotInfo(ManagedObject rowData, out string signature)
    {
        signature = null;
        try
        {
            // _SlotID is the slot number within the strip (one row per slot);
            // the strip category (wakeup/block/damage) is the selected
            // reversal tab, mirrored in ReversalSetting.ReversalType
            int slotNo = FlowHelper.ReadIntField(rowData, "_SlotID", -1);
            if (slotNo < 0) return null;

            var tData = FlowHelper.GetObjectField(_manager, "_tData");
            var setting = FlowHelper.GetObjectField(tData, "ReversalSetting");

            int category = FlowHelper.ReadIntField(setting, "ReversalType", 0);
            string arrayField = category switch
            {
                0 => "DownReversalDatas",
                1 => "GuardReversalDatas",
                2 => "DamageReversalDatas",
                _ => null,
            };
            if (arrayField == null) return null;

            var fighters = FlowHelper.GetObjectField(setting, "FighterDataList");
            int fighterCount = FlowHelper.GetListCount(fighters);
            if (fighterCount == 0) return null;

            // Settings are per fighter; the entry being edited follows the
            // menu's player index (single-entry list = that one fighter)
            int playerIdx = FlowHelper.ReadIntField(_manager, "_UIPlayerIndex", 0);
            if (playerIdx < 0) playerIdx = 0;
            var fighter = FlowHelper.GetListItem(fighters, System.Math.Min(playerIdx, fighterCount - 1));

            var slot = FlowHelper.GetListItem(FlowHelper.GetObjectField(fighter, arrayField), slotNo);
            if (slot == null) return null;

            bool isValid = FlowHelper.ReadBoolField(slot, "IsValid");
            bool isActive = FlowHelper.ReadBoolField(slot, "IsActive");
            int type = FlowHelper.ReadIntField(slot, "Type", -1);
            int skillIndex = FlowHelper.ReadIntField(slot, "SkillIndex", -1);
            int count = FlowHelper.ReadIntField(slot, "Count", 0);
            int delay = FlowHelper.ReadIntField(slot, "Delay", 0);
            signature = $"{category}|{slotNo}|{isValid}|{isActive}|{type}|{skillIndex}|{count}|{delay}";

            var parts = new System.Collections.Generic.List<string> { (slotNo + 1).ToString() };
            if (isValid)
            {
                string skill = ReadReversalSkillName(playerIdx, type, skillIndex);
                if (!string.IsNullOrEmpty(skill)) parts.Add(skill);
                parts.Add(isActive ? "ON" : "OFF");
                if (count > 1) parts.Add(count.ToString());
                if (delay > 0) parts.Add(delay.ToString());
            }
            return string.Join(". ", parts);
        }
        catch { return null; }
    }

    /// <summary>Spoken names of the focused row's inline icon glyphs, in tree
    /// order (e.g. &lt;ICON KeyR&gt; → "R") — they reflect the player's device.</summary>
    private static System.Collections.Generic.List<string> ReadRowIconNames()
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

    /// <summary>Localized move name from TrainingManager._UISkillData (per
    /// player), in the skill array matching the slot's ReversalType.</summary>
    private static string ReadReversalSkillName(int playerIdx, int reversalType, int skillIndex)
    {
        try
        {
            if (reversalType < 0 || reversalType >= SkillArrayFields.Length || skillIndex < 0)
                return null;
            string field = SkillArrayFields[reversalType];
            if (field == null) return null; // RECORDING has no skill array

            var perPlayer = FlowHelper.GetListItem(
                FlowHelper.GetObjectField(_manager, "_UISkillData"), playerIdx);
            var record = FlowHelper.GetListItem(
                FlowHelper.GetObjectField(perPlayer, field), skillIndex);
            return FlowHelper.ResolveGuidField(record, "SkillMessage");
        }
        catch { return null; }
    }

    /// <summary>
    /// The focused row's ViewData entry (Index == SecondaryIndex). The list
    /// accumulates per-redraw entries with stale ones FIRST — keep the last
    /// match (the newest), or slider values never updated after edits.
    /// Returns null for rows that were never redrawn.
    /// </summary>
    private static ManagedObject FindViewData()
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

    private static void Reset()
    {
        API.LogInfo("[SF6Access] Training menu closed");
        _isActive = false;
        _lastPrimary = -1;
        _lastSecondary = -1;
        _lastValueName = null;
        _lastSectionName = null;
        _lastRowText = null;
        _flowParam = null;
        _onReversalRow = false;
        _lastSlotSig = null;
        _pendingSlider = null;
    }
}
