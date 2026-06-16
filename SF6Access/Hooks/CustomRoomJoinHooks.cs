using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the custom room join / invitations screen (app.UIFlowCustomRoomJoin):
/// the tab (Rooms with Friends / Rooms You've Been Invited To) and each room row
/// as you navigate it. The generic GroupFocus reader announced the tabs but not
/// the room rows (room name + who invited you), which live on UIPartsCustomRoomBanner
/// fields (RoomMasterName, Comment, PlayerCount, ShortId, Setting).
/// </summary>
public class CustomRoomJoinHooks
{
    private const string PARAM_TYPE = "app.UIFlowCustomRoomJoin.Param";

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 30;
    private const int POLL_READ_INTERVAL = 5;

    private static bool _active;
    private static ManagedObject _param;
    private static string _lastTab;
    private static string _lastRoom;

    public static bool IsActive => _active;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] CustomRoomJoinHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var current = FlowHelper.TrackFlowParam(PARAM_TYPE, _param, out bool changed);
            if (changed) { _lastTab = null; _lastRoom = null; } // menu recreated — re-read
            if (current != null && !_active)
            {
                _active = true;
                _param = current;
                _lastTab = null;
                _lastRoom = null;
                API.LogInfo("[SF6Access] Custom room join/invitations active");
            }
            else if (current == null && _active)
            {
                _active = false;
                _param = null;
                _lastTab = null;
                _lastRoom = null;
                API.LogInfo("[SF6Access] Custom room join/invitations ended");
            }
            else if (current != null) _param = current;
        }

        if (!_active || _pollCounter % POLL_READ_INTERVAL != 0) return;
        PollTab();
        PollRoom();
    }

    private static void PollTab()
    {
        var tab = FlowHelper.GetObjectField(_param, "Tab");
        string text = FlowHelper.ReadSelectedItemText(tab);
        if (string.IsNullOrEmpty(text) || text == _lastTab) return;
        _lastTab = text;
        _lastRoom = null; // switching tab changes the room list — re-read it

        API.LogInfo($"[SF6Access] Custom room tab: {text}");
        ScreenReaderService.Speak(text);
    }

    private static void PollRoom()
    {
        var rooms = FlowHelper.GetObjectField(_param, "Rooms");
        if (rooms == null) return;

        // Rooms.get_SelectedItem returns a via.gui.SelectItem, NOT a
        // UIPartsCustomRoomBanner — reading banner fields (RoomMasterName etc.)
        // off it always came back null, so nothing was announced. Reading the
        // selected item's on-screen texts works the same as the Tab and the
        // custom-room tables: the banner exposes room name, ShortId code,
        // entrant count (e_txt_num) and the rule string as visible gui texts.
        string text = FlowHelper.ReadSelectedItemText(rooms);
        if (string.IsNullOrEmpty(text) || text == _lastRoom) return;
        _lastRoom = text;

        API.LogInfo($"[SF6Access] Custom room row: {text}");
        ScreenReaderService.Speak(text);
    }
}
