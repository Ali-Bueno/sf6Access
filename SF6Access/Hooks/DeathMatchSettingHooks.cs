using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the Extreme Battle rules/gimmicks screen
/// (app.UIFlowDeathMatchSetting, also used inside custom rooms). The generic
/// focus reader announced only the option names — each panel's description
/// lives in a separate e_text_desc element that updates with the selection,
/// so the focused option is announced together with its tooltip.
/// </summary>
public class DeathMatchSettingHooks
{
    private static readonly string[] ParamPrefixes =
    {
        "app.UIFlowDeathMatchSetting",
        "app.UIFlowCustomRoomDeathMatchSetting",
    };

    // Param.SettingList fields holding the two panels (rules / gimmicks)
    private static readonly string[] PanelFields = { "Rule", "Gimmick" };

    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    private static ManagedObject _param;
    private static readonly int[] _lastIndex = { -2, -2 };
    private static readonly string[] _lastDesc = new string[2];

    public static bool IsInDeathMatchSetting => _isActive;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] DeathMatchSettingHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
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
                if (_isActive) Reset();
            }
            else if (!_isActive || FlowHelper.AddressOf(param) != FlowHelper.AddressOf(_param))
            {
                _param = param;
                _isActive = true;
                for (int i = 0; i < 2; i++)
                {
                    _lastIndex[i] = -2;
                    _lastDesc[i] = null;
                }
                API.LogInfo("[SF6Access] DeathMatchSetting active");
            }
        }

        if (_isActive && _pollCounter % POLL_READ_INTERVAL == 0)
            PollPanels();
    }

    private static void PollPanels()
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
                ScreenReaderService.Speak(announcement, interrupt: !first && indexChanged);
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

    private static void Reset()
    {
        API.LogInfo("[SF6Access] DeathMatchSetting ended");
        _isActive = false;
        _param = null;
        for (int i = 0; i < 2; i++)
        {
            _lastIndex[i] = -2;
            _lastDesc[i] = null;
        }
    }
}
