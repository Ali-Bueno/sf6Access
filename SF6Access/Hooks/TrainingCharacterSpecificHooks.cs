using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the training "character-specific" (unique) settings list
/// (app.training.UIFlowTrainingMenu_All), opened on top of the training menu.
/// It inherits the menu's _SecondaryList (UIPartsGroupScroll); each focused row
/// is a character setting (e_txt_chara + e_txt_name + e_txt_0 value) read via the
/// shared row formatter. While active the parent TrainingMenuHooks pauses so the
/// stale main-menu row isn't announced on top. Migrated to ScreenAdapter
/// (IsActive kept for TrainingMenuHooks).
/// </summary>
public sealed class TrainingCharacterSpecificHooks : SingleParamScreenAdapter
{
    private static TrainingCharacterSpecificHooks _self;

    /// <summary>Consumed by TrainingMenuHooks to pause its own row reads.</summary>
    public static bool IsActive => _self != null && _self.Active;

    protected override string ParamType => "app.training.UIFlowTrainingMenu_All.Param";

    private int _lastFocus = int.MinValue;
    private string _lastText;

    public TrainingCharacterSpecificHooks()
    {
        _self = this;
        SearchInterval = 30;
        ReadInterval = 5;
    }

    protected override void OnBind()
    {
        _lastFocus = int.MinValue;
        _lastText = null;
        API.LogInfo("[SF6Access] Character-specific settings active");
    }

    protected override void OnExit()
    {
        _lastFocus = int.MinValue;
        _lastText = null;
        API.LogInfo("[SF6Access] Character-specific settings ended");
    }

    protected override void Poll()
    {
        var list = FlowHelper.GetObjectField(Param, "_SecondaryList");
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
