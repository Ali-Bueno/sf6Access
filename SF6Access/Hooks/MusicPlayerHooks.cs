using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the music player / BGM menu (app.UIFlowMusicPlayer), reachable from the
/// menu and from training. Announces the playlist tab (All / Playlist 1-5) as
/// you switch it with L/R and the focused track as you move through the list.
/// Tabs live on partsTab (UIPartsSimpleList), tracks on partsList (UIPartsScrollList).
/// </summary>
public class MusicPlayerHooks
{
    private const string PARAM_TYPE = "app.UIFlowMusicPlayer.Param";

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 30;
    private const int POLL_READ_INTERVAL = 5;

    private static bool _active;
    private static ManagedObject _param;
    private static string _lastTab;
    private static string _lastTrack;

    public static bool IsActive => _active;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] MusicPlayerHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var current = FlowHelper.TrackFlowParam(PARAM_TYPE, _param, out bool changed);
            if (changed) { _lastTab = null; _lastTrack = null; }
            if (current != null && !_active)
            {
                _active = true;
                _param = current;
                _lastTab = null;
                _lastTrack = null;
                API.LogInfo("[SF6Access] Music player active");
            }
            else if (current == null && _active)
            {
                _active = false;
                _param = null;
                _lastTab = null;
                _lastTrack = null;
                API.LogInfo("[SF6Access] Music player ended");
            }
            else if (current != null) _param = current;
        }

        if (!_active || _pollCounter % POLL_READ_INTERVAL != 0) return;
        PollTab();
        PollTrack();
    }

    private static void PollTab()
    {
        var tab = FlowHelper.GetObjectField(_param, "partsTab");
        string text = FlowHelper.ReadSelectedItemText(tab);
        if (string.IsNullOrEmpty(text) || text == _lastTab) return;
        _lastTab = text;

        // Announce the tab together with its focused track: the track poll
        // fires the same frame with interrupt and was cutting the tab off,
        // so the user only heard the song. Seed _lastTrack to avoid a repeat.
        string track = ReadTrack();
        if (!string.IsNullOrEmpty(track)) _lastTrack = track;

        string announcement = string.IsNullOrEmpty(track) ? text : $"{text}. {track}";
        API.LogInfo($"[SF6Access] Music tab: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    private static void PollTrack()
    {
        string text = ReadTrack();
        if (string.IsNullOrEmpty(text) || text == _lastTrack) return;
        _lastTrack = text;

        API.LogInfo($"[SF6Access] Music track: {text}");
        ScreenReaderService.Speak(text);
    }

    private static string ReadTrack()
    {
        var list = FlowHelper.GetObjectField(_param, "partsList");
        return FlowHelper.ReadSelectedItemText(list);
    }
}
