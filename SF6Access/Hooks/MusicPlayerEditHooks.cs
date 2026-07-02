using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the music player playlist edit window (app.UIFlowMusicPlayer_EditWindow,
/// opened with T). It has two song lists — partsAllScroll (all tracks, to add)
/// and partsEditScroll (the playlist's current tracks) — switched via
/// partsGroupSwitch. Announces the focused side and the focused track as you move.
/// Migrated to ScreenAdapter (IsActive kept for MusicPlayerHooks).
/// </summary>
public sealed class MusicPlayerEditHooks : SingleParamScreenAdapter
{
    private static MusicPlayerEditHooks _self;

    /// <summary>Consumed by MusicPlayerHooks so the player pauses under this window.</summary>
    public static bool IsActive => _self != null && _self.Active;

    protected override string ParamType => "app.UIFlowMusicPlayer_EditWindow.Param";

    private int _lastSide = -2;
    private string _lastTrack;

    public MusicPlayerEditHooks()
    {
        _self = this;
        SearchInterval = 30;
        ReadInterval = 5;
    }

    protected override void OnBind()
    {
        _lastSide = -2;
        _lastTrack = null;
        API.LogInfo("[SF6Access] Music edit window active");
    }

    protected override void OnExit()
    {
        _lastSide = -2;
        _lastTrack = null;
        API.LogInfo("[SF6Access] Music edit window ended");
    }

    protected override void Poll()
    {
        var all = FlowHelper.GetObjectField(Param, "partsAllScroll");
        var edit = FlowHelper.GetObjectField(Param, "partsEditScroll");

        // Whichever side holds focus is the one being navigated
        int side = IsFocused(edit) ? 1 : IsFocused(all) ? 0 : -1;
        var list = side == 1 ? edit : side == 0 ? all : null;
        if (list == null) return;

        string track = FlowHelper.ReadSelectedItemText(list);
        if (string.IsNullOrEmpty(track)) return;

        bool sideChanged = side != _lastSide && _lastSide != -2;
        _lastSide = side;
        if (track == _lastTrack && !sideChanged) return;
        _lastTrack = track;

        // Switching side: prefix which list you moved to
        string announcement = sideChanged
            ? $"{(side == 1 ? "Playlist" : "All tracks")}. {track}"
            : track;

        API.LogInfo($"[SF6Access] Music edit [{side}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    private static bool IsFocused(ManagedObject parts)
    {
        if (parts == null) return false;
        var v = FlowHelper.Call(parts, "get_IsFocus");
        return v is bool b ? b : FlowHelper.ReadBoolField(parts, "_IsFocus");
    }
}
