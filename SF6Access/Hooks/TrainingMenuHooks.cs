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

    private static void AnnounceCurrentItem(bool tabChanged)
    {
        var data = FlowHelper.Call(_manager, "get_CurrentMenuData") as ManagedObject;
        if (data == null) return;

        string name = FlowHelper.ResolveGuidField(data, "_MessageID");
        string sub = FlowHelper.ResolveGuidField(data, "_SubMessageID");
        string guide = FlowHelper.ResolveGuidField(data, "_GuideMessage")
                    ?? FlowHelper.ResolveGuidField(data, "_GuideMessageID");

        // Row label: for spin rows CurrentMenuData is the value child and the
        // parent holds the actual option name ("Recovery behavior" etc.)
        string parentName = null;
        var parentData = FlowHelper.Call(_manager, "get_CurrentParentData") as ManagedObject;
        if (parentData != null)
            parentName = FlowHelper.ResolveGuidField(parentData, "_MessageID");

        _lastValueName = name;

        string announcement = name;
        if (!string.IsNullOrEmpty(parentName) && parentName != name)
            announcement = string.IsNullOrEmpty(announcement) ? parentName : $"{parentName}. {announcement}";
        if (!string.IsNullOrEmpty(sub) && sub != name)
            announcement = string.IsNullOrEmpty(announcement) ? sub : $"{announcement} {sub}";
        if (!string.IsNullOrEmpty(guide) && guide != announcement)
            announcement = string.IsNullOrEmpty(announcement) ? guide : $"{announcement}. {guide}";

        if (string.IsNullOrEmpty(announcement)) return;

        // Prepend the localized tab title when switching tabs
        if (tabChanged)
        {
            string tabTitle = ReadTabTitle(_lastPrimary);
            if (!string.IsNullOrEmpty(tabTitle) && !announcement.StartsWith(tabTitle))
                announcement = $"{tabTitle}. {announcement}";
        }

        API.LogInfo($"[SF6Access] Training menu [{_lastPrimary},{_lastSecondary}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    /// <summary>Localized tab title from the flow param's primary tab list.</summary>
    private static string ReadTabTitle(int tabIndex)
    {
        try
        {
            var param = FlowHelper.FindFlowParam(FLOW_PARAM_TYPE);
            if (param == null) return null;

            var tabList = FlowHelper.GetObjectField(param, "_PrimaryTabList");
            string title = FlowHelper.ReadListRowText(tabList, tabIndex);
            if (!string.IsNullOrEmpty(title)) return title;

            // Fallback: Nth visible text under the tab list control
            var control = FlowHelper.GetObjectField(tabList, "Control")
                ?? FlowHelper.Call(tabList, "get_Control") as ManagedObject;
            var texts = GuiTextReader.ReadControlTexts(control);
            if (tabIndex >= 0 && tabIndex < texts.Count) return texts[tabIndex].Text;
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
    }
}
