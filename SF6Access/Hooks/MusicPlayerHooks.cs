using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the music player / BGM menu (app.UIFlowMusicPlayer), reachable from the
/// menu and from training. Announces the playlist tab (All / Playlist 1-5) as
/// you switch it with L/R and the focused track as you move through the list.
/// Tabs live on partsTab (UIPartsSimpleList), tracks on partsList (UIPartsScrollList).
/// Migrated to ScreenAdapter (IsActive kept for MainMenuHooks).
/// </summary>
public sealed class MusicPlayerHooks : SingleParamScreenAdapter
{
    private static MusicPlayerHooks _self;

    /// <summary>Consumed by MainMenuHooks to suppress the generic focus reader.</summary>
    public static bool IsActive => _self != null && _self.Active;

    protected override string ParamType => "app.UIFlowMusicPlayer.Param";

    private string _lastTab;
    private string _lastTrack;

    public MusicPlayerHooks()
    {
        _self = this;
        SearchInterval = 30;
        ReadInterval = 5;
    }

    protected override void OnBind()
    {
        _lastTab = null;
        _lastTrack = null;
        API.LogInfo("[SF6Access] Music player active");
    }

    protected override void OnExit()
    {
        _lastTab = null;
        _lastTrack = null;
        API.LogInfo("[SF6Access] Music player ended");
    }

    protected override void Poll()
    {
        // The edit window (T) opens on top with its own lists — let it own
        // announcements so the player's track poll doesn't read underneath it
        if (MusicPlayerEditHooks.IsActive) return;
        PollTab();
        PollTrack();
    }

    private void PollTab()
    {
        var tab = FlowHelper.GetObjectField(Param, "partsTab");
        string text = FlowHelper.ReadSelectedItemText(tab);
        if (string.IsNullOrEmpty(text) || text == _lastTab) return;
        _lastTab = text;

        // Announce only the tab name. Seed _lastTrack with the current
        // selection so the track poll stays quiet — on a tab switch the list
        // still reports the loaded/playing song (stale), and reading it just
        // repeated "Not On The Sidelines"; the real song reads as you navigate.
        string track = ReadTrack();
        if (!string.IsNullOrEmpty(track)) _lastTrack = track;

        API.LogInfo($"[SF6Access] Music tab: {text}");
        ScreenReaderService.Speak(text);
    }

    private void PollTrack()
    {
        string text = ReadTrack();
        if (string.IsNullOrEmpty(text) || text == _lastTrack) return;
        _lastTrack = text;

        API.LogInfo($"[SF6Access] Music track: {text}");
        ScreenReaderService.Speak(text);
    }

    private string ReadTrack()
    {
        var list = FlowHelper.GetObjectField(Param, "partsList");
        return FlowHelper.ReadSelectedItemText(list);
    }
}
