using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the music player playlist edit window (app.UIFlowMusicPlayer_EditWindow,
/// opened with T). It has two song lists — partsAllScroll (all tracks, to add)
/// and partsEditScroll (the playlist's current tracks) — switched via
/// partsGroupSwitch. Announces the focused side and the focused track as you move.
/// </summary>
public class MusicPlayerEditHooks
{
    private const string PARAM_TYPE = "app.UIFlowMusicPlayer_EditWindow.Param";

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 30;
    private const int POLL_READ_INTERVAL = 5;

    private static bool _active;
    private static ManagedObject _param;
    private static int _lastSide = -2;
    private static string _lastTrack;

    public static bool IsActive => _active;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] MusicPlayerEditHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var current = FlowHelper.TrackFlowParam(PARAM_TYPE, _param, out bool changed);
            if (changed) { _lastSide = -2; _lastTrack = null; }
            if (current != null && !_active)
            {
                _active = true;
                _param = current;
                _lastSide = -2;
                _lastTrack = null;
                API.LogInfo("[SF6Access] Music edit window active");
            }
            else if (current == null && _active)
            {
                _active = false;
                _param = null;
                _lastSide = -2;
                _lastTrack = null;
                API.LogInfo("[SF6Access] Music edit window ended");
            }
            else if (current != null) _param = current;
        }

        if (!_active || _pollCounter % POLL_READ_INTERVAL != 0) return;
        PollRow();
    }

    private static void PollRow()
    {
        var all = FlowHelper.GetObjectField(_param, "partsAllScroll");
        var edit = FlowHelper.GetObjectField(_param, "partsEditScroll");

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
