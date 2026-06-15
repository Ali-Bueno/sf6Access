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

    // app.UICFNReplayInfo.MenuType values (GetFocusChild does NOT track focus on
    // this mediator — GetFocusMenuType is the authoritative focused item).
    private const int MENU_COMMENTATOR = 1;

    // English last resort if the localized GetMenuTypeMessage call can't be made.
    private static readonly string[] FallbackLabels =
    {
        "Watch replay",            // REPLAY
        "Commentary",              // COMMENTATOR
        "Add to favorites",        // REGIST
        "Remove from favorites",   // UNREGIST
        "Player 1 details",        // PROFILE1
        "Player 2 details",        // PROFILE2
        "Search by player 1",      // SEARCH1
        "Search by player 2",      // SEARCH2
        "Round results",           // RESULT_LIST
    };

    private static void PollFocusedItem()
    {
        try
        {
            int type = FlowHelper.CallInt(_list, "GetFocusMenuType", int.MinValue);
            if (type == int.MinValue || type < 0) return;

            string label = FlowHelper.CleanTags(
                FlowHelper.Call(_list, "GetMenuTypeMessage", type) as string);
            if (string.IsNullOrEmpty(label) && type < FallbackLabels.Length)
                label = FallbackLabels[type];
            if (string.IsNullOrEmpty(label)) return;

            // Comentário is an on/off toggle — append its current value.
            if (type == MENU_COMMENTATOR)
            {
                string toggle = ReadCommentToggle();
                if (!string.IsNullOrEmpty(toggle)) label = $"{label}. {toggle}";
            }

            if (label == _lastText) return;
            bool first = _lastText == null;
            _lastText = label;
            API.LogInfo($"[SF6Access] Replay menu [{type}]: {label}");
            ScreenReaderService.Speak(label, interrupt: !first);
        }
        catch { }
    }

    /// <summary>The Comentário on/off value ("Lig." / "Desl.") from the GUI.</summary>
    private static string ReadCommentToggle()
    {
        try
        {
            foreach (var (owner, view) in GuiTextReader.FindGuiViews("CFNReplayInfo"))
                foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
                    if (t.Name == "e_text" && !string.IsNullOrWhiteSpace(t.Text))
                        return t.Text.Trim();
        }
        catch { }
        return null;
    }
}
