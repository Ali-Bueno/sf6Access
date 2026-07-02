using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the custom room join / invitations screen (app.UIFlowCustomRoomJoin):
/// the tab (Rooms with Friends / Rooms You've Been Invited To) and each room row
/// as you navigate it. The generic GroupFocus reader announced the tabs but not
/// the room rows (room name + who invited you), which live on UIPartsCustomRoomBanner
/// fields (RoomMasterName, Comment, PlayerCount, ShortId, Setting).
/// Migrated to ScreenAdapter (IsActive kept for MainMenuHooks suppression).
/// </summary>
public sealed class CustomRoomJoinHooks : SingleParamScreenAdapter
{
    private static CustomRoomJoinHooks _self;

    /// <summary>Consumed by MainMenuHooks to suppress the generic focus reader.</summary>
    public static bool IsActive => _self != null && _self.Active;

    protected override string ParamType => "app.UIFlowCustomRoomJoin.Param";

    private string _lastTab;
    private string _lastRoom;

    public CustomRoomJoinHooks()
    {
        _self = this;
        SearchInterval = 30;
        ReadInterval = 5;
    }

    protected override void OnBind()
    {
        _lastTab = null;
        _lastRoom = null;
        API.LogInfo("[SF6Access] Custom room join/invitations active");
    }

    protected override void OnExit()
    {
        _lastTab = null;
        _lastRoom = null;
        API.LogInfo("[SF6Access] Custom room join/invitations ended");
    }

    protected override void Poll()
    {
        PollTab();
        PollRoom();
    }

    private void PollTab()
    {
        var tab = FlowHelper.GetObjectField(Param, "Tab");
        string text = FlowHelper.ReadSelectedItemText(tab);
        if (string.IsNullOrEmpty(text) || text == _lastTab) return;
        _lastTab = text;
        _lastRoom = null; // switching tab changes the room list — re-read it

        API.LogInfo($"[SF6Access] Custom room tab: {text}");
        ScreenReaderService.Speak(text);
    }

    private void PollRoom()
    {
        var rooms = FlowHelper.GetObjectField(Param, "Rooms");
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
