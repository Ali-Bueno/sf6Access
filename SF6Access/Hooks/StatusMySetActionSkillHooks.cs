using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the "Move Set" assignment screen
/// (app.UIStatusMenu_MySetActionSkill) — where directional special-move slots
/// (neutral/forward/back/down + special) are filled from a preset. It is a
/// multi-pane screen the generic reader handled badly: a preset list on the
/// left (empty names render as "－－－－" dashes → silent), a Grounded/Aerial/SA
/// set-type tab switched with Tab (image labels → silent), and the slot panels
/// on the right (separate Modern grid / Classic list). Read here from the typed
/// widgets instead.
/// </summary>
public class StatusMySetActionSkillHooks
{
    private const string PARAM_TYPE = "app.UIStatusMenu_MySetActionSkill.Param";

    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    private static ManagedObject _param;
    private static int _lastSetType = int.MinValue;
    private static int _lastPresetIdx = int.MinValue;
    private static int _lastSlotIdx = int.MinValue;
    private static string _lastText;

    public static bool IsActive => _isActive;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] StatusMySetActionSkillHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (!_isActive)
        {
            if (_pollCounter % POLL_SEARCH_INTERVAL == 0) TryActivate();
            return;
        }

        if (_pollCounter % POLL_READ_INTERVAL != 0) return;

        var current = FlowHelper.FindFlowParam(PARAM_TYPE);
        if (current == null) { Reset(); return; }
        if (FlowHelper.AddressOf(current) != FlowHelper.AddressOf(_param))
        {
            _param = current;
            ResetState();
        }

        Poll();
    }

    private static void TryActivate()
    {
        var p = FlowHelper.FindFlowParam(PARAM_TYPE);
        if (p == null) return;

        _param = p;
        ResetState();
        _isActive = true;
        API.LogInfo("[SF6Access] My Set action-skill screen active");
        Poll();
    }

    private static void Poll()
    {
        if (_param == null) return;

        // 1) Set-type tab (Grounded / Aerial / SA) — switched with Tab. It changes
        // nothing in the slot/preset indices, so announce it on its own.
        int setType = FlowHelper.ReadIntField(_param, "SetType", -1);
        bool setTypeChanged = setType != _lastSetType && _lastSetType != int.MinValue;
        bool first = _lastSetType == int.MinValue;
        _lastSetType = setType;
        if (setTypeChanged)
        {
            string setName = ReadSetTypeName(setType);
            if (!string.IsNullOrEmpty(setName))
            {
                _lastText = setName;
                API.LogInfo($"[SF6Access] My Set set-type: {setName}");
                ScreenReaderService.Speak(setName);
            }
            // The slot list re-lays out on tab change; re-baseline so the next
            // navigation announces the focused slot afresh
            _lastSlotIdx = int.MinValue;
            return;
        }

        // 2) Focused slot in the active panel (Modern grid preferred, else Classic).
        var slotList = ActiveSlotList();
        int slotIdx = FlowHelper.CallInt(slotList, "get_SelectedIndex");

        // 3) Focused preset in the left list.
        var presetList = FlowHelper.GetObjectField(_param, "mPresetList");
        int presetIdx = FlowHelper.CallInt(presetList, "get_SelectedIndex");

        if (slotIdx != _lastSlotIdx || presetIdx != _lastPresetIdx)
            API.LogInfo($"[SF6Access] My Set idx: preset={presetIdx} slot={slotIdx}");

        // Announce whichever pane's cursor moved. The index gate alone prevents
        // re-reading the same item — do NOT also dedup on text, or a run of
        // identical empty slots/presets goes silent after the first.
        if (slotIdx >= 0 && slotIdx != _lastSlotIdx)
        {
            bool firstSlot = _lastSlotIdx == int.MinValue;
            _lastSlotIdx = slotIdx;
            if (!firstSlot || first) { AnnounceSlot(slotList); return; }
        }

        if (presetIdx >= 0 && presetIdx != _lastPresetIdx)
        {
            bool firstPreset = _lastPresetIdx == int.MinValue;
            _lastPresetIdx = presetIdx;
            if (!firstPreset || first) AnnouncePreset(presetList);
        }
    }

    /// <summary>
    /// The slot list in use for the player's control type: the Modern grid when it
    /// holds the focus, otherwise the Classic list.
    /// </summary>
    private static ManagedObject ActiveSlotList()
    {
        var modern = FlowHelper.GetObjectField(_param, "mSkillPanelList_Modern");
        if (modern != null && FlowHelper.CallInt(modern, "get_SelectedIndex") >= 0)
            return modern;
        return FlowHelper.GetObjectField(_param, "mSkillPanelList_Classic") ?? modern;
    }

    /// <summary>
    /// Announce the focused move-set slot: its trigger input (neutral/forward/back
    /// + special) and the assigned move, or "Empty" when the slot is unfilled.
    /// </summary>
    private static void AnnounceSlot(ManagedObject slotList)
    {
        var item = FlowHelper.Call(slotList, "get_SelectedItem") as ManagedObject;
        if (item == null) return;

        var texts = GuiTextReader.ReadControlTexts(item);
        string command = null, name = null;
        foreach (var t in texts)
        {
            if (t.Name == "e_text_command" && command == null && !string.IsNullOrWhiteSpace(t.Raw))
                command = FlowHelper.SpeakableIcons(t.Raw)?.Trim();
            else if (t.Name == "e_text_name" && name == null && !string.IsNullOrWhiteSpace(t.Text))
                name = t.Text.Trim();
        }

        var parts = new List<string>();
        // The trigger (neutral/forward/back + special) is the slot's identity. When
        // it can't be read, fall back to the slot position so each slot is distinct.
        if (!string.IsNullOrEmpty(command)) parts.Add(command);
        else parts.Add($"{SlotWord()} {_lastSlotIdx + 1}");
        // Empty slots render the name as a dash placeholder ("－－－－"): say "Empty"
        parts.Add(IsPlaceholder(name) ? EmptyWord() : name);

        string text = string.Join(". ", parts);
        if (string.IsNullOrWhiteSpace(text)) return;
        _lastText = text;
        API.LogInfo($"[SF6Access] My Set slot [{_lastSlotIdx}]: {text}");
        ScreenReaderService.Speak(text);
    }

    private static void AnnouncePreset(ManagedObject presetList)
    {
        var item = FlowHelper.Call(presetList, "get_SelectedItem") as ManagedObject;
        if (item == null) return;

        string name = null;
        foreach (var t in GuiTextReader.ReadControlTexts(item))
        {
            if (t.Name == "e_text_name" && !string.IsNullOrWhiteSpace(t.Text)) { name = t.Text.Trim(); break; }
        }

        // Always lead with the preset number so identical empty presets stay distinct.
        string label = $"{PresetWord()} {_lastPresetIdx + 1}";
        string text = IsPlaceholder(name) ? $"{label}. {EmptyWord()}" : $"{label}. {name}";
        _lastText = text;
        API.LogInfo($"[SF6Access] My Set preset [{_lastPresetIdx}]: {text}");
        ScreenReaderService.Speak(text);
    }

    /// <summary>An empty-slot/preset placeholder rendered as repeated dash glyphs.</summary>
    private static bool IsPlaceholder(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        foreach (char c in name)
            if (c != '－' && c != '-' && c != 'ー' && c != ' ') return false;
        return true;
    }

    /// <summary>
    /// Name of the current set-type tab (Grounded / Aerial / SA). Prefer the game's
    /// own localized tab label; fall back to the WTActionSkillSetType enum
    /// (Ground=1, Air=2, SuperArts=3).
    /// </summary>
    private static string ReadSetTypeName(int setType)
    {
        var tab = FlowHelper.GetObjectField(_param, "mSetTypeTab");
        var item = FlowHelper.Call(tab, "get_SelectedItem") as ManagedObject;
        if (item != null)
        {
            foreach (var t in GuiTextReader.ReadControlTexts(item, visibleOnly: true))
                if (!string.IsNullOrWhiteSpace(t.Text)) return t.Text.Trim();
        }
        return setType switch
        {
            1 => "Grounded",
            2 => "Aerial",
            3 => "SA",
            _ => null,
        };
    }

    private static string SlotWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "Ranura",
            FlowHelper.UiLang.Pt => "Espaço",
            _ => "Slot",
        };

    private static string PresetWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "Preajuste",
            FlowHelper.UiLang.Pt => "Predefinição",
            _ => "Preset",
        };

    private static string EmptyWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "Vacío",
            FlowHelper.UiLang.Pt => "Vazio",
            _ => "Empty",
        };

    private static void ResetState()
    {
        _lastSetType = int.MinValue;
        _lastPresetIdx = int.MinValue;
        _lastSlotIdx = int.MinValue;
        _lastText = null;
    }

    private static void Reset()
    {
        API.LogInfo("[SF6Access] My Set action-skill screen ended");
        _isActive = false;
        _param = null;
        ResetState();
    }
}
