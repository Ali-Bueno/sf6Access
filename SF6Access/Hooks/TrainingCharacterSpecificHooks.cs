using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the training "character-specific" (unique) settings list
/// (app.training.UIFlowTrainingMenu_All), opened on top of the training menu.
/// It inherits the menu's _SecondaryList (UIPartsGroupScroll); each focused row
/// is a character setting (e_txt_chara + e_txt_name + e_txt_0 value) read via the
/// shared row formatter. While active the parent TrainingMenuHooks pauses so the
/// stale main-menu row isn't announced on top.
/// </summary>
public class TrainingCharacterSpecificHooks
{
    private const string PARAM_TYPE = "app.training.UIFlowTrainingMenu_All.Param";

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 30;
    private const int POLL_READ_INTERVAL = 5;

    private static bool _active;
    private static ManagedObject _param;
    private static int _lastFocus = int.MinValue;
    private static string _lastText;

    public static bool IsActive => _active;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] TrainingCharacterSpecificHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var current = FlowHelper.TrackFlowParam(PARAM_TYPE, _param, out bool changed);
            if (changed) { _lastFocus = int.MinValue; _lastText = null; }
            if (current != null && !_active)
            {
                _active = true;
                _param = current;
                _lastFocus = int.MinValue;
                _lastText = null;
                API.LogInfo("[SF6Access] Character-specific settings active");
            }
            else if (current == null && _active)
            {
                _active = false;
                _param = null;
                _lastFocus = int.MinValue;
                _lastText = null;
                API.LogInfo("[SF6Access] Character-specific settings ended");
            }
            else if (current != null) _param = current;
        }

        if (!_active || _pollCounter % POLL_READ_INTERVAL != 0) return;
        PollRow();
    }

    private static void PollRow()
    {
        var list = FlowHelper.GetObjectField(_param, "_SecondaryList");
        if (list == null) return;

        int focus = FlowHelper.ReadIntField(list, "_FocusIndex", int.MinValue);
        var child = FlowHelper.Call(list, "GetFocusChild") as ManagedObject;
        var control = FlowHelper.GetObjectField(child, "Control")
            ?? FlowHelper.Call(child, "get_Control") as ManagedObject;
        // Character rows reorder to "RYU. Sun Crest. 2. Standard" via FormatRowTexts
        string text = FlowHelper.FormatRowTexts(GuiTextReader.ReadControlTexts(control), 6);
        if (string.IsNullOrEmpty(text)) return;

        bool first = _lastFocus == int.MinValue;
        bool rowChanged = focus != _lastFocus;
        bool textChanged = text != _lastText;
        string previous = _lastText;
        _lastFocus = focus;
        _lastText = text;
        if (!rowChanged && !textChanged) return;

        // Up/down (row move) reads the whole row; left/right (value edit on the
        // same row) reads only the changed value segment, not the full setting again
        string announcement = (!first && !rowChanged)
            ? FlowHelper.DiffSegments(previous, text)
            : text;
        if (string.IsNullOrEmpty(announcement)) return;

        API.LogInfo($"[SF6Access] Character-specific [{focus}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }
}
