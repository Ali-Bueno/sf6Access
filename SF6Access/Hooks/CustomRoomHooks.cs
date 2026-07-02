using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the custom room top menu (app.UIFlowCustomRoomTop:
/// search / create / invite). The focused entry's localized description comes
/// from Param.GuideMessage(); the FunctionList SelectedIndex drives navigation.
/// Migrated to ScreenAdapter (IsInCustomRoomTop kept for MainMenuHooks).
/// </summary>
public sealed class CustomRoomHooks : SingleParamScreenAdapter
{
    private static CustomRoomHooks _self;

    /// <summary>Consumed by MainMenuHooks to suppress the generic focus reader.</summary>
    public static bool IsInCustomRoomTop => _self != null && _self.Active;

    protected override string ParamType => "app.UIFlowCustomRoomTop.Param";

    private ManagedObject _functionList;
    private int _lastIndex = -2;

    public CustomRoomHooks()
    {
        _self = this;
        SearchInterval = 60;
        ReadInterval = 5;
    }

    protected override void OnBind()
    {
        _functionList = FlowHelper.GetObjectField(Param, "FunctionList");
        _lastIndex = -2;
        API.LogInfo($"[SF6Access] CustomRoomTop active (functionList={_functionList != null})");
        PollSelection(); // baseline the current selection without announcing
    }

    protected override void OnExit()
    {
        API.LogInfo("[SF6Access] CustomRoomTop ended");
        _functionList = null;
        _lastIndex = -2;
    }

    protected override void Poll() => PollSelection();

    private void PollSelection()
    {
        if (_functionList == null) return;

        int idx = FlowHelper.CallInt(_functionList, "get_SelectedIndex");
        if (idx < 0 || idx == _lastIndex) return;

        bool first = _lastIndex == -2;
        _lastIndex = idx;
        if (first) return;

        // Localized description of the focused entry
        string guide = FlowHelper.CleanTags(FlowHelper.Call(Param, "GuideMessage") as string);

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
}
