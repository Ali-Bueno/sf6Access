using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for training menu sub-screens that open their own flow
/// (character-specific settings and similar spin/text lists). They use
/// app.training.UIFlowTrainingMenu_SpinList/_TextList.Param with their own
/// FocusIndex + ViewDataList; TrainingManager's Primary/SecondaryIndex does
/// not move there, so TrainingMenuHooks stays silent.
/// </summary>
public class TrainingSubListHooks
{
    private static readonly string[] WatchedTypes =
    {
        "app.training.UIFlowTrainingMenu_All.Param", // character-specific settings
        "app.training.UIFlowTrainingMenu_SpinList.Param",
        "app.training.UIFlowTrainingMenu_TextList.Param",
    };

    // The reversal "Delay Settings" picker (opened with R) is a SpinList whose
    // values (0F/1F/2F...) live in _MenuList, not the parent's _SecondaryList
    private const string SPINLIST_TYPE = "app.training.UIFlowTrainingMenu_SpinList.Param";

    // app.training.ItemType slider variants — value is numeric, not a Guid
    private static readonly int[] SliderItemTypes = { 2, 10, 11, 12, 13, 14, 15 };

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 30;
    private const int POLL_READ_INTERVAL = 5;
    private const int POLL_VALUE_INTERVAL = 10;

    private static ManagedObject _param;
    private static string _activeType;
    private static int _lastFocus = -2;
    private static string _lastValue;
    private static string _lastRowText;
    private static int _lastSliderValue = int.MinValue;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] TrainingSubListHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            // Re-bind every search — the game recreates Params per visit
            var found = FlowHelper.FindFlowParams(WatchedTypes);
            string type = null;
            ManagedObject param = null;
            foreach (var t in WatchedTypes)
            {
                if (found.TryGetValue(t, out param)) { type = t; break; }
            }

            if (type != _activeType)
            {
                _activeType = type;
                _lastFocus = -2;
                _lastValue = null;
                _lastRowText = null;
                _lastSliderValue = int.MinValue;
                if (type != null)
                    API.LogInfo($"[SF6Access] Training sub-list active: {type}");
            }
            _param = param;
        }

        if (_param == null || _pollCounter % POLL_READ_INTERVAL != 0) return;
        PollFocus();
    }

    private static void PollFocus()
    {
        try
        {
            int focus = FlowHelper.ReadIntField(_param, "FocusIndex", int.MinValue);
            if (focus == int.MinValue)
                focus = FlowHelper.ReadIntField(_param, "_SelectChildIndex", int.MinValue);
            if (focus == int.MinValue)
                focus = FlowHelper.ReadIntField(_param, "ItemIndex", int.MinValue);
            if (focus < 0) return;

            if (focus == _lastFocus)
            {
                if (_pollCounter % POLL_VALUE_INTERVAL == 0) PollValueChange();
                return;
            }

            bool first = _lastFocus == -2;
            _lastFocus = focus;
            AnnounceRow(focus, interrupt: !first);
        }
        catch { }
    }

    private static void AnnounceRow(int focus, bool interrupt)
    {
        var viewData = FindViewData(focus);
        var rowData = FlowHelper.GetObjectField(viewData, "Data");

        string label = FlowHelper.ResolveGuidField(rowData, "_MessageID");
        string sub = FlowHelper.ResolveGuidField(rowData, "_SubMessageID");
        string guide = FlowHelper.ResolveGuidField(rowData, "_GuideMessage")
                    ?? FlowHelper.ResolveGuidField(rowData, "_GuideMessageID");
        string value = ReadCurrentValue(viewData, rowData);
        _lastValue = value;

        // SpinList (Delay Settings) has no ViewData label — use the picker's
        // own title (e_txt_name) so the entry announce keeps its context
        if (_activeType == SPINLIST_TYPE && string.IsNullOrEmpty(label))
            label = ReadMenuListText("e_txt_name");

        // SpinList value picker (Delay Settings): on entry read the setting +
        // value once; every later left/right move is just a value change, so
        // announce only the value (interrupt == not the first read).
        if (_activeType == SPINLIST_TYPE && interrupt && !string.IsNullOrEmpty(value))
        {
            _lastRowText = ReadRowGuiText();
            API.LogInfo($"[SF6Access] Training sub-list value: {value}");
            ScreenReaderService.Speak(value, interrupt);
            return;
        }

        var parts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(label)) parts.Add(label);
        if (!string.IsNullOrEmpty(value) && value != label) parts.Add(value);
        if (!string.IsNullOrEmpty(sub) && sub != label && sub != value) parts.Add(sub);
        if (!string.IsNullOrEmpty(guide) && guide != label) parts.Add(guide);

        // The character-specific list (_All) has no usable ViewData — read the
        // focused row's GUI text (character + skill + values) instead
        if (parts.Count == 0)
        {
            string rowText = ReadRowGuiText();
            _lastRowText = rowText;
            if (string.IsNullOrEmpty(rowText)) return;
            parts.Add(rowText);
        }
        else
        {
            _lastRowText = ReadRowGuiText();
        }

        string announcement = string.Join(". ", parts);
        API.LogInfo($"[SF6Access] Training sub-list [{focus}]: {announcement}");
        ScreenReaderService.Speak(announcement, interrupt);
    }

    /// <summary>Left/right edits on the focused row.</summary>
    private static void PollValueChange()
    {
        var viewData = FindViewData(_lastFocus);
        var rowData = FlowHelper.GetObjectField(viewData, "Data");
        string value = ReadCurrentValue(viewData, rowData);
        if (!string.IsNullOrEmpty(value))
        {
            if (value == _lastValue) return;
            bool firstValue = _lastValue == null;
            _lastValue = value;
            if (firstValue) return;

            API.LogInfo($"[SF6Access] Training sub-list value: {value}");
            ScreenReaderService.Speak(value);
            return;
        }

        // No readable data value (the _All screen): announce what changed in
        // the focused row's GUI text
        string rowText = ReadRowGuiText();
        if (string.IsNullOrEmpty(rowText) || rowText == _lastRowText) return;
        string previous = _lastRowText;
        _lastRowText = rowText;
        if (previous == null) return;

        string diff = FlowHelper.DiffSegments(previous, rowText);
        if (string.IsNullOrEmpty(diff)) return;
        API.LogInfo($"[SF6Access] Training sub-list row changed: {diff}");
        ScreenReaderService.Speak(diff);
    }

    /// <summary>
    /// The SpinList's focused item carries the setting title (e_txt_name) AND
    /// the value (e_txt_0). Return only the requested element so left/right
    /// edits announce the frame value alone, not "Delay Settings" each time.
    /// </summary>
    private static string ReadMenuListText(string elementName)
    {
        try
        {
            var list = FlowHelper.GetObjectField(_param, "_MenuList");
            var child = FlowHelper.Call(list, "GetFocusChild") as ManagedObject;
            var control = FlowHelper.GetObjectField(child, "Control")
                ?? FlowHelper.Call(child, "get_Control") as ManagedObject;
            if (control == null) return null;
            foreach (var t in GuiTextReader.ReadControlTexts(control))
            {
                if (t.Name == elementName && !string.IsNullOrWhiteSpace(t.Text))
                    return t.Text.Trim();
            }
        }
        catch { }
        return null;
    }

    private static string ReadMenuListValue() => ReadMenuListText("e_txt_0");

    /// <summary>Focused row GUI text via the inherited secondary list.</summary>
    private static string ReadRowGuiText()
    {
        try
        {
            var list = FlowHelper.GetObjectField(_param, "_MenuList")
                ?? FlowHelper.GetObjectField(_param, "_SecondaryList");
            var child = FlowHelper.Call(list, "GetFocusChild") as ManagedObject;
            var control = FlowHelper.GetObjectField(child, "Control")
                ?? FlowHelper.Call(child, "get_Control") as ManagedObject;
            return FlowHelper.FormatRowTexts(GuiTextReader.ReadControlTexts(control), 8);
        }
        catch { return null; }
    }

    /// <summary>Current value of a row: slider number, or the manager's
    /// currently-selected value child message.</summary>
    private static string ReadCurrentValue(ManagedObject viewData, ManagedObject rowData)
    {
        // SpinList value picker (Delay Settings): the selected frame value is
        // the focused child of _MenuList (e_txt_0 = "1F"), not in ViewData or
        // the parent's secondary list
        if (_activeType == SPINLIST_TYPE)
        {
            string frame = ReadMenuListValue();
            if (!string.IsNullOrEmpty(frame)) return frame;
        }

        // No row data (the _All screen): the manager's CurrentMenuData points
        // at the parent menu row, not these rows — read nothing here so the
        // GUI-text path takes over
        if (rowData == null) return null;

        int itemType = FlowHelper.ReadIntField(rowData, "_Type", -1);
        if (System.Array.IndexOf(SliderItemTypes, itemType) >= 0)
        {
            int slider = FlowHelper.ReadIntField(viewData, "SliderValue", int.MinValue);
            _lastSliderValue = slider;
            if (slider != int.MinValue) return slider.ToString();
        }

        try
        {
            var manager = API.GetManagedSingleton("app.training.TrainingManager");
            var data = FlowHelper.Call(manager, "get_CurrentMenuData") as ManagedObject;
            return FlowHelper.ResolveGuidField(data, "_MessageID");
        }
        catch { return null; }
    }

    private static ManagedObject FindViewData(int focus)
    {
        try
        {
            var list = FlowHelper.GetObjectField(_param, "_ViewDataList")
                ?? FlowHelper.GetObjectField(_param, "ViewDataList");
            int count = FlowHelper.GetListCount(list);
            for (int i = 0; i < count; i++)
            {
                var vd = FlowHelper.GetListItem(list, i);
                if (vd != null && FlowHelper.ReadIntField(vd, "Index") == focus)
                    return vd;
            }
        }
        catch { }
        return null;
    }
}
