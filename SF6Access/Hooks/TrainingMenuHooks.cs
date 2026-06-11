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

    // app.training.ItemType slider variants (SLIDER, SLIDER_GUIDE, SLIDER_VITAL_1P/2P,
    // SLIDER_DRIVE, SLIDER_SA_1P/2P) — their value is numeric, not a message Guid
    private static readonly int[] SliderItemTypes = { 2, 10, 11, 12, 13, 14, 15 };

    public static bool IsInTrainingMenu => _isActive;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] TrainingMenuHooks initialized");
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
        // Slider rows (drive gauge, vitality...): the value is a number on the
        // ViewData, not a message Guid — left/right changed nothing audible
        var viewData = FindViewData();
        if (viewData != null && IsSliderRow(viewData))
        {
            int sliderValue = FlowHelper.ReadIntField(viewData, "SliderValue", int.MinValue);
            if (sliderValue != int.MinValue && sliderValue != _lastSliderValue)
            {
                bool firstSlider = _lastSliderValue == int.MinValue;
                _lastSliderValue = sliderValue;
                if (!firstSlider)
                {
                    API.LogInfo($"[SF6Access] Training slider changed: {sliderValue}");
                    ScreenReaderService.Speak(sliderValue.ToString());
                }
            }
            return;
        }

        var data = FlowHelper.Call(_manager, "get_CurrentMenuData") as ManagedObject;
        if (data == null) return;

        string name = FlowHelper.ResolveGuidField(data, "_MessageID");
        if (string.IsNullOrEmpty(name) || _lastValueName == null || name == _lastValueName)
        {
            if (!string.IsNullOrEmpty(name) && _lastValueName == null) _lastValueName = name;
            return;
        }

        _lastValueName = name;
        API.LogInfo($"[SF6Access] Training value changed: {name}");
        ScreenReaderService.Speak(name);
    }

    private static bool IsSliderRow(ManagedObject viewData)
    {
        var rowData = FlowHelper.GetObjectField(viewData, "Data");
        int itemType = FlowHelper.ReadIntField(rowData, "_Type", -1);
        return System.Array.IndexOf(SliderItemTypes, itemType) >= 0;
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
        var rowData = FlowHelper.GetObjectField(viewData, "Data");

        string value = FlowHelper.ResolveGuidField(data, "_MessageID");
        _lastValueName = value;

        string label = FlowHelper.ResolveGuidField(rowData, "_MessageID");
        string sub = FlowHelper.ResolveGuidField(rowData ?? data, "_SubMessageID");
        string guide = FlowHelper.ResolveGuidField(rowData ?? data, "_GuideMessage")
                    ?? FlowHelper.ResolveGuidField(rowData ?? data, "_GuideMessageID");

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

        if (!string.IsNullOrEmpty(label))
            parts.Add(label);
        if (!string.IsNullOrEmpty(value) && value != label)
            parts.Add(value);

        // Slider rows carry a numeric value (drive gauge, vitality...)
        _lastSliderValue = int.MinValue;
        if (viewData != null && IsSliderRow(viewData))
        {
            int sliderValue = FlowHelper.ReadIntField(viewData, "SliderValue", int.MinValue);
            _lastSliderValue = sliderValue;
            if (sliderValue != int.MinValue)
                parts.Add(sliderValue.ToString());
        }

        if (!string.IsNullOrEmpty(sub) && sub != label && sub != value)
            parts.Add(sub);
        if (!string.IsNullOrEmpty(guide) && guide != label)
            parts.Add(guide);

        if (parts.Count == 0) return;

        string announcement = string.Join(". ", parts);
        API.LogInfo($"[SF6Access] Training menu [{_lastPrimary},{_lastSecondary}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    /// <summary>The focused row's ViewData entry (Index == SecondaryIndex).</summary>
    private static ManagedObject FindViewData()
    {
        try
        {
            var param = FlowHelper.FindFlowParam(FLOW_PARAM_TYPE);
            var list = FlowHelper.GetObjectField(param, "_ViewDataList");
            if (list == null) return null;

            int count = FlowHelper.GetListCount(list);
            for (int i = 0; i < count; i++)
            {
                var vd = FlowHelper.GetListItem(list, i);
                if (vd == null) continue;
                if (FlowHelper.ReadIntField(vd, "Index") != _lastSecondary) continue;
                return vd;
            }
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
    }
}
