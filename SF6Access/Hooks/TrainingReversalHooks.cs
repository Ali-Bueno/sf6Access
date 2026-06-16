using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the training reversal move selection lists (opened from
/// the reversal rows of the training menu). One flow Param exists per move
/// category (Normal/CommandNormal/Special/SA/Recording/Common), each with a
/// focus index ("FoucsIndex" — game's own typo; "FocusIndex" on Special) and a
/// TrainingSkillData[] whose SkillMessage Guid is the localized move name.
/// </summary>
public class TrainingReversalHooks
{
    private const string PARAM_PREFIX = "app.training.UIFlowTrainingMenu_Reversal";
    private const string MAIN_PARAM = "app.training.UIFlowTrainingMenu_Reversal.Param";

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 30;
    private const int POLL_READ_INTERVAL = 5;

    private static bool _isActive;
    private static readonly List<(string typeName, ManagedObject param)> _params = new();
    private static readonly Dictionary<string, int> _lastFocus = new();
    private static readonly Dictionary<string, string> _lastText = new();
    private static int _lastTabIndex = -2;

    /// <summary>True while the reversal move-selection submenu is open, so the
    /// parent TrainingMenuHooks pauses its polling (avoids the index-out-of-
    /// range spam and cursor fight that made the reversal menu unstable).</summary>
    public static bool IsActive => _isActive;

    // Category list order, matched to the parent's TabIndex. The child flow
    // for the focused tab is the only one whose rows should be read; without
    // this every still-loaded category list announced its row at once.
    private static int CategoryIndex(string typeName) => typeName switch
    {
        "app.training.UIFlowTrainingMenu_Reversal_Normal.Param" => 0,
        "app.training.UIFlowTrainingMenu_Reversal_CommandNormal.Param" => 1,
        "app.training.UIFlowTrainingMenu_Reversal_Special.Param" => 2,
        "app.training.UIFlowTrainingMenu_Reversal_SA.Param" => 3,
        "app.training.UIFlowTrainingMenu_Reversal_Recording.Param" => 4,
        "app.training.UIFlowTrainingMenu_Reversal_Common.Param" => 5,
        _ => -1,
    };

    // Readable fallback per tab — the parent Param's PrimaryTitle array is
    // absent at runtime (logged "Member not found: PrimaryTitle"), so "Tab N"
    // was all that came out. TODO: localize from game text once a dump of the
    // reversal screen's tab widget is available.
    private static readonly string[] CategoryNames =
        { "Normal", "Command Normal", "Special", "Super Art", "Recording", "Common" };

    // Set on entry and tab change: the next readable focused row announces
    // even without a focus change (the initial row was never read otherwise)
    private static bool _pendingRowAnnounce;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] TrainingReversalHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            _params.Clear();
            _params.AddRange(FlowHelper.FindFlowParamsByPrefix(PARAM_PREFIX));

            bool active = _params.Count > 0;
            if (active && !_isActive)
            {
                _isActive = true;
                _lastFocus.Clear();
                _lastText.Clear();
                _lastTabIndex = -2;
                _pendingRowAnnounce = true;
                API.LogInfo($"[SF6Access] Reversal menu active ({_params.Count} params)");
            }
            else if (!active && _isActive)
            {
                _isActive = false;
                _lastFocus.Clear();
                _lastText.Clear();
                API.LogInfo("[SF6Access] Reversal menu ended");
            }
        }

        if (!_isActive || _pollCounter % POLL_READ_INTERVAL != 0) return;

        // Resolve the focused tab from the parent first so the move-list pass
        // can ignore the non-visible category lists regardless of handle order.
        int currentTab = -1;
        foreach (var (typeName, param) in _params)
        {
            if (typeName != MAIN_PARAM) continue;
            PollTab(param);
            currentTab = FlowHelper.ReadIntField(param, "TabIndex", -1);
            break;
        }

        foreach (var (typeName, param) in _params)
        {
            if (typeName == MAIN_PARAM) continue;
            int cat = CategoryIndex(typeName);
            // Only the child list matching the focused tab is on screen
            if (cat >= 0 && currentTab >= 0 && cat != currentTab) continue;
            PollMoveList(typeName, param);
        }
    }

    /// <summary>Category tab of the reversal screen (Normal/Special/SA...).</summary>
    private static void PollTab(ManagedObject param)
    {
        int tab = FlowHelper.ReadIntField(param, "TabIndex");
        if (tab < 0 || tab == _lastTabIndex) return;

        bool first = _lastTabIndex == -2;
        _lastTabIndex = tab;

        // A new tab shows a new list: read its focused row too, even when
        // that list's focus index didn't move
        _lastFocus.Clear();
        _lastText.Clear();
        _pendingRowAnnounce = true;

        string title = null;
        try
        {
            var titles = FlowHelper.GetObjectField(param, "PrimaryTitle");
            title = FlowHelper.Call(titles, "Get", tab) as string;
        }
        catch { }

        string announcement = !string.IsNullOrEmpty(title)
            ? FlowHelper.CleanTags(title)
            : (tab >= 0 && tab < CategoryNames.Length ? CategoryNames[tab] : $"Tab {tab + 1}");
        API.LogInfo($"[SF6Access] Reversal tab [{tab}]: {announcement}");
        ScreenReaderService.Speak(announcement, interrupt: !first);
    }

    /// <summary>
    /// Move rows of one category list. Most child Params expose _pGroupScroll
    /// (a UIPartsGroup → _FocusIndex + GetFocusChild). The Super Art tab uses
    /// _pScrollList (a UIPartsScrollList → SelectedIndex + SelectedItem)
    /// instead — confirmed in the decompiled UIFlowTrainingMenu_Reversal_SA.
    /// Read the focused row's name and announce on change.
    /// </summary>
    private static void PollMoveList(string typeName, ManagedObject param)
    {
        int focus;
        string row;

        var group = FlowHelper.GetObjectField(param, "_pGroupScroll");
        if (group != null)
        {
            focus = FlowHelper.ReadIntField(group, "_FocusIndex", int.MinValue);
            row = ReadGroupRow(group);
        }
        else
        {
            var list = FlowHelper.GetObjectField(param, "_pScrollList");
            if (list == null) return;
            focus = FlowHelper.CallInt(list, "get_SelectedIndex", int.MinValue);
            row = ReadListRow(list);
        }
        if (string.IsNullOrEmpty(row)) return;

        bool firstIdx = !_lastFocus.TryGetValue(typeName, out int lastFocus);
        bool idxChanged = firstIdx || focus != lastFocus;
        bool textChanged = !_lastText.TryGetValue(typeName, out string lastText) || row != lastText;
        _lastFocus[typeName] = focus;
        _lastText[typeName] = row;

        // Entry / tab change: read the current row once even though nothing moved
        if (_pendingRowAnnounce)
        {
            _pendingRowAnnounce = false;
            API.LogInfo($"[SF6Access] Reversal row (initial) [{typeName.Substring(PARAM_PREFIX.Length)},{focus}]: {row}");
            ScreenReaderService.Speak(row, interrupt: false);
            return;
        }

        if (!idxChanged && !textChanged) return;

        API.LogInfo($"[SF6Access] Reversal move [{typeName.Substring(PARAM_PREFIX.Length)},{focus}]: {row}");
        ScreenReaderService.Speak(row);
    }

    /// <summary>Focused move name from a UIPartsGroup (GetFocusChild).</summary>
    private static string ReadGroupRow(ManagedObject group)
    {
        try
        {
            var child = FlowHelper.Call(group, "GetFocusChild") as ManagedObject;
            var control = FlowHelper.GetObjectField(child, "Control")
                ?? FlowHelper.Call(child, "get_Control") as ManagedObject;
            return ReadNameOrJoined(control);
        }
        catch { return null; }
    }

    /// <summary>Selected move name from a UIPartsScrollList (Super Art tab).</summary>
    private static string ReadListRow(ManagedObject list)
    {
        try
        {
            var item = FlowHelper.Call(list, "get_SelectedItem") as ManagedObject;
            if (item == null) return null;
            string name = ReadNameOrJoined(item);
            return !string.IsNullOrEmpty(name) ? name : FlowHelper.ReadSelectedItemText(list);
        }
        catch { return null; }
    }

    // Strength variant of a reversal special move lives in e_txt_0 (L/M/H/OD),
    // confirmed via log. Spoken in full so changing it (left/right) is clear.
    private static readonly Dictionary<string, string> StrengthWords = new()
    {
        { "L", "Light" }, { "M", "Medium" }, { "H", "Heavy" }, { "OD", "Overdrive" },
    };

    /// <summary>Move items carry their label in e_txt_name; append the strength
    /// variant (e_txt_0: Light/Medium/Heavy/Overdrive) when present so changing
    /// it re-announces.</summary>
    private static string ReadNameOrJoined(ManagedObject control)
    {
        if (control == null) return null;

        string name = null;
        string strength = null;
        foreach (var t in GuiTextReader.ReadControlTexts(control))
        {
            if (string.IsNullOrWhiteSpace(t.Text)) continue;
            string text = t.Text.Trim();
            if (t.Name == "e_txt_name") name ??= text;
            else if (t.Name == "e_txt_0") strength ??= text;
        }

        if (!string.IsNullOrEmpty(strength) && StrengthWords.TryGetValue(strength, out var word))
            strength = word;

        if (!string.IsNullOrEmpty(name))
            return string.IsNullOrEmpty(strength) ? name : $"{name}. {strength}";
        return GuiTextReader.ReadControlTextJoined(control);
    }
}
