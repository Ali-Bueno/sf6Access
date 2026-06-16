using System.Collections.Generic;
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

        var banner = FlowHelper.Call(rooms, "get_SelectedItem") as ManagedObject;
        if (banner == null) return;

        // Room name/comment + who hosts/invited (RoomMasterName) + entrants + rules
        string comment = ReadScrollText(FlowHelper.GetObjectField(banner, "Comment"));
        string master = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(banner, "RoomMasterName"));
        string count = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(banner, "PlayerCount"));
        string shortId = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(banner, "ShortId"));
        string setting = ReadScrollText(FlowHelper.GetObjectField(banner, "Setting"));

        var parts = new List<string>();
        void Add(string s) { if (!string.IsNullOrWhiteSpace(s)) { s = s.Trim(); if (!parts.Contains(s)) parts.Add(s); } }
        Add(comment);
        Add(master);
        Add(count);
        Add(shortId);
        Add(setting);
        if (parts.Count == 0) return;

        string text = string.Join(". ", parts);
        if (text == _lastRoom) return;
        _lastRoom = text;

        API.LogInfo($"[SF6Access] Custom room row: {text}");
        ScreenReaderService.Speak(text);
    }

    /// <summary>Read a UIPartsScrollText (its inner mTextScroll), falling back to the object itself.</summary>
    private static string ReadScrollText(ManagedObject scrollText)
    {
        if (scrollText == null) return null;
        return FlowHelper.ReadGuiText(FlowHelper.GetObjectField(scrollText, "mTextScroll"))
            ?? FlowHelper.ReadGuiText(scrollText);
    }
}
