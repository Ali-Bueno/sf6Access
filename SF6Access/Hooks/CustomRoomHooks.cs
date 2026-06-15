using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the custom room top menu (app.UIFlowCustomRoomTop:
/// search / create / invite). The focused entry's localized description comes
/// from Param.GuideMessage(); the FunctionList SelectedIndex drives navigation.
/// </summary>
public class CustomRoomHooks
{
    private const string PARAM_TYPE = "app.UIFlowCustomRoomTop.Param";

    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    private static ManagedObject _param;
    private static ManagedObject _functionList;
    private static int _lastIndex = -2;

    public static bool IsInCustomRoomTop => _isActive;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] CustomRoomHooks initialized");
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
            var current = FlowHelper.TrackFlowParam(PARAM_TYPE, _param, out bool changed);
            if (current == null)
            {
                Reset();
                return;
            }
            if (changed)
                TryActivate(); // menu was recreated — re-bind param and child caches
        }

        if (_pollCounter % POLL_READ_INTERVAL == 0)
            PollSelection();
    }

    private static void TryActivate()
    {
        var param = FlowHelper.FindFlowParam(PARAM_TYPE);
        if (param == null) return;

        _param = param;
        _functionList = FlowHelper.GetObjectField(param, "FunctionList");
        _lastIndex = -2;
        _isActive = true;

        API.LogInfo($"[SF6Access] CustomRoomTop active (functionList={_functionList != null})");
        PollSelection();
    }

    private static void PollSelection()
    {
        if (_functionList == null) return;

        int idx = FlowHelper.CallInt(_functionList, "get_SelectedIndex");
        if (idx < 0 || idx == _lastIndex) return;

        bool first = _lastIndex == -2;
        _lastIndex = idx;
        if (first) return;

        // Localized description of the focused entry
        string guide = FlowHelper.CleanTags(FlowHelper.Call(_param, "GuideMessage") as string);

        // The entry's on-screen label. The per-child control has no text here,
        // and indexing the flat control text list by SelectedIndex came out in
        // the wrong order (names were swapped). get_SelectedItem returns the
        // focused item directly, independent of order.
        string label = FlowHelper.ReadSelectedItemText(_functionList);
        if (string.IsNullOrEmpty(label))
        {
            try
            {
                var control = FlowHelper.GetObjectField(_functionList, "Control")
                    ?? FlowHelper.Call(_functionList, "get_Control") as ManagedObject;
                var texts = GuiTextReader.ReadControlTexts(control);
                if (idx < texts.Count) label = texts[idx].Text;
            }
            catch { }
        }

        string announcement = !string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(guide)
            ? $"{label}. {guide}"
            : !string.IsNullOrEmpty(label) ? label : guide;

        if (string.IsNullOrEmpty(announcement)) announcement = $"Option {idx + 1}";

        API.LogInfo($"[SF6Access] CustomRoomTop [{idx}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    private static void Reset()
    {
        API.LogInfo("[SF6Access] CustomRoomTop ended");
        _isActive = false;
        _param = null;
        _functionList = null;
        _lastIndex = -2;
    }
}
