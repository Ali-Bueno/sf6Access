using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the replay-info option menu shown after picking a replay
/// (app.UICFNReplayInfoOnline, GUI "CFNReplayInfo"): view each player's details,
/// add to favorites, Comentário (a Lig./Desl. toggle) and Assistir a replay.
/// The options live on the UI class's _DetailedMenu (a UIPartsCFNReplayInfoList
/// mediator), not on the flow Param, so GroupFocus found "0 fields" and the menu
/// was silent. Capture the mediator from its StartInput and announce the focused
/// row's text (label + toggle value), like the mediator path in GroupFocus.
/// </summary>
public class ReplayInfoMenuHooks
{
    private const string FLOW_TYPE = "app.UICFNReplayInfoOnline.FlowParam";

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 20;
    private const int POLL_READ_INTERVAL = 5;

    private static ManagedObject _list;   // captured UIPartsCFNReplayInfoList
    private static bool _active;
    private static string _lastText;

    [PluginEntryPoint]
    public static void Initialize()
    {
        try
        {
            var td = TDB.Get().FindType("app.UIPartsCFNReplayInfoList");
            // StartInput is called when the option list becomes interactive.
            var method = td?.GetMethod("StartInput") ?? td?.GetMethod("SetupList");
            if (method != null)
            {
                var hook = method.AddHook(false);
                hook.AddPre(args =>
                {
                    try { _list = ManagedObject.ToManagedObject(args[1]); _lastText = null; }
                    catch { }
                    return PreHookResult.Continue;
                });
                API.LogInfo("[SF6Access] ReplayInfoMenuHooks initialized");
            }
            else
            {
                API.LogInfo("[SF6Access] UIPartsCFNReplayInfoList.StartInput not found; replay menu skipped");
            }
        }
        catch (System.Exception ex)
        {
            API.LogError($"[SF6Access] ReplayInfoMenuHooks init failed: {ex.Message}");
        }
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            bool present = FlowHelper.FindFlowParam(FLOW_TYPE) != null;
            if (present && !_active) { _active = true; _lastText = null; API.LogInfo("[SF6Access] Replay info menu opened"); }
            else if (!present && _active) { _active = false; _lastText = null; _list = null; API.LogInfo("[SF6Access] Replay info menu closed"); }
        }

        if (!_active || _list == null || _pollCounter % POLL_READ_INTERVAL != 0) return;
        PollFocusedItem();
    }

    private static void PollFocusedItem()
    {
        try
        {
            string text = ReadFocusedItem();
            if (string.IsNullOrEmpty(text) || text == _lastText) return;

            bool first = _lastText == null;
            _lastText = text;
            API.LogInfo($"[SF6Access] Replay menu: {text}");
            ScreenReaderService.Speak(text, interrupt: !first);
        }
        catch { }
    }

    /// <summary>Focused option's on-screen text: the label (e_itemtext) plus the
    /// toggle value (e_text, e.g. "Desl." / "Lig." for Comentário).</summary>
    private static string ReadFocusedItem()
    {
        var child = FlowHelper.Call(_list, "GetFocusChild") as ManagedObject;
        if (child == null) return null;

        var control = FlowHelper.GetObjectField(child, "Control")
            ?? FlowHelper.Call(child, "get_Control") as ManagedObject;

        string label = null, value = null;
        foreach (var t in GuiTextReader.ReadControlTexts(control))
        {
            if (string.IsNullOrWhiteSpace(t.Text)) continue;
            if (t.Name == "e_itemtext") label ??= t.Text.Trim();
            else if (t.Name == "e_text") value ??= t.Text.Trim();
        }

        if (label == null && value == null) return null;
        if (label == null) return value;
        return value != null ? $"{label}. {value}" : label;
    }
}
