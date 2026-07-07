using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the Extreme Battle rules/gimmicks screen
/// (app.UIFlowDeathMatchSetting, also used inside custom rooms). The generic
/// focus reader announced only the option names — each panel's description
/// lives in a separate e_text_desc element that updates with the selection,
/// so the focused option is announced together with its tooltip.
///
/// ScreenAdapter: locates by prefixes (base + custom-room variant), re-binds on
/// instance change. MainMenuHooks reads IsInDeathMatchSetting for suppression.
/// Registered in ScreenRegistry.
/// </summary>
public sealed class DeathMatchSettingHooks : ScreenAdapter
{
    private static readonly string[] ParamPrefixes =
    {
        "app.UIFlowDeathMatchSetting",
        "app.UIFlowCustomRoomDeathMatchSetting",
    };

    public override string[] OwnedTypes => ParamPrefixes;

    // Param.SettingList fields holding the two panels (rules / gimmicks)
    private static readonly string[] PanelFields = { "Rule", "Gimmick" };

    private static DeathMatchSettingHooks _self;
    public static bool IsInDeathMatchSetting => _self != null && _self.Active;

    public DeathMatchSettingHooks()
    {
        SearchInterval = 60;
        ReadInterval = 5;
        _self = this;
    }

    private ManagedObject _param;
    private readonly int[] _lastIndex = { -2, -2 };
    private readonly string[] _lastDesc = new string[2];

    protected override bool Locate()
    {
        var found = FlowHelper.FindFirstFlowParamsByPrefixes(ParamPrefixes);
        ManagedObject param = null;
        foreach (var entry in found.Values)
        {
            param = entry.param;
            break;
        }

        if (param == null)
        {
            _param = null;
            return false;
        }

        // Re-bind on first find or instance change (stale-param pattern).
        if (_param == null || FlowHelper.AddressOf(param) != FlowHelper.AddressOf(_param))
        {
            _param = param;
            ResetPanels();
            API.LogInfo("[SF6Access] DeathMatchSetting active");
        }
        return true;
    }

    protected override void OnDeactivate()
    {
        API.LogInfo("[SF6Access] DeathMatchSetting ended");
        _param = null;
        ResetPanels();
    }

    protected override void OnPoll()
    {
        for (int i = 0; i < PanelFields.Length; i++)
        {
            try
            {
                var panel = FlowHelper.GetObjectField(_param, PanelFields[i]);
                var list = FlowHelper.GetObjectField(panel, "List");
                if (list == null) continue;

                int idx = FlowHelper.CallInt(list, "get_SelectedIndex");
                if (idx < 0) continue;

                string desc = ReadPanelDesc(panel);

                bool first = _lastIndex[i] == -2;
                bool indexChanged = idx != _lastIndex[i];
                bool descChanged = !string.IsNullOrEmpty(desc) && desc != _lastDesc[i];
                _lastIndex[i] = idx;
                if (!string.IsNullOrEmpty(desc)) _lastDesc[i] = desc;

                if (first)
                {
                    // Initial row: only the panel that actually holds focus
                    var focusResult = FlowHelper.Call(list, "get_IsFocus");
                    if (!(focusResult is bool fb && fb)) continue;
                }
                else if (!indexChanged && !descChanged) continue;

                string announcement;
                if (first || indexChanged)
                {
                    string item = FlowHelper.ReadSelectedItemText(list);
                    announcement = string.IsNullOrEmpty(item)
                        ? desc
                        : string.IsNullOrEmpty(desc) ? item : $"{item}. {desc}";
                }
                else
                {
                    // Same option, tooltip loaded a beat later
                    announcement = desc;
                }
                if (string.IsNullOrEmpty(announcement)) continue;

                API.LogInfo($"[SF6Access] DeathMatch {PanelFields[i]} [{idx}]: {announcement}");
                Speak(announcement, interrupt: !first && indexChanged);
            }
            catch { }
        }
    }

    /// <summary>The panel's description text (e_text_desc) under its group control.</summary>
    private static string ReadPanelDesc(ManagedObject panel)
    {
        try
        {
            var group = FlowHelper.GetObjectField(panel, "Group");
            var control = FlowHelper.GetObjectField(group, "Control")
                ?? FlowHelper.Call(group, "get_Control") as ManagedObject;
            foreach (var t in GuiTextReader.ReadControlTexts(control))
            {
                if (t.Name == "e_text_desc" && !string.IsNullOrWhiteSpace(t.Text))
                    return t.Text.Trim();
            }
        }
        catch { }
        return null;
    }

    private void ResetPanels()
    {
        for (int i = 0; i < PanelFields.Length; i++)
        {
            _lastIndex[i] = -2;
            _lastDesc[i] = null;
        }
    }
}
