using System.Collections.Generic;
using System.Runtime.InteropServices;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the Avatar Arcade top screen (app.UIFlowAvatarArcadeTop.Param).
/// The course list (MainList) rows are read by the generic GroupFocusHooks; this
/// hook adds what that reader can't: the selected mode's description (rendered in
/// the shared InputGuide widget) announced on entry / mode swap, and the avatar's
/// equipped style + combat stats on the G key (read from the global
/// WTPlayerManager.LocalPlayerData, since this screen has no equip param).
/// </summary>
public class AvatarArcadeTopHooks
{
    private const string PARAM_TYPE = "app.UIFlowAvatarArcadeTop.Param";
    private const string PLAYER_MANAGER = "app.worldtour.WTPlayerManager";

    // GUI owner names whose texts we read for this screen.
    private const string ARCADE_GUI = "AvatarArcadeTop";
    private const string INPUT_GUIDE_GUI = "InputGuide";

    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    private static ManagedObject _param;
    private static int _lastIndex = -2;
    private static string _lastDescription;

    // Description is announced a few frames after the selection change so it
    // follows (and isn't cut off by) the row name spoken by GroupFocusHooks.
    private static string _pendingDescription;
    private static int _pendingDescriptionFrame = -1;
    private const int DESC_DELAY_FRAMES = 12;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")]
    private static extern System.IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(System.IntPtr hWnd, out uint processId);
    private const int VK_G = 0x47;
    private static bool _lastGState;

    /// <summary>True only when the game window is the foreground app — so the G
    /// shortcut never fires while the user is typing in another window.</summary>
    private static bool IsGameForeground()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            return pid == (uint)System.Environment.ProcessId;
        }
        catch { return false; }
    }

    public static bool IsInAvatarArcadeTop => _isActive;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] AvatarArcadeTopHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (!_isActive)
        {
            if (_pollCounter % POLL_SEARCH_INTERVAL == 0) TryActivate();
            return;
        }

        // G re-reads the avatar style + stats panel on demand (game-focused only)
        bool gDown = (GetAsyncKeyState(VK_G) & 0x8000) != 0;
        if (gDown && !_lastGState && IsGameForeground()) AnnounceStatsSummary();
        _lastGState = gDown;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            _param = FlowHelper.TrackFlowParam(PARAM_TYPE, _param, out bool _);
            if (_param == null) { Reset(); return; }
        }

        if (_pollCounter % POLL_READ_INTERVAL == 0)
            PollDescription();

        FlushPendingDescription();
    }

    /// <summary>Speak the deferred description once its delay has elapsed.</summary>
    private static void FlushPendingDescription()
    {
        if (_pendingDescription == null || _pollCounter < _pendingDescriptionFrame) return;
        string desc = _pendingDescription;
        _pendingDescription = null;
        API.LogInfo($"[SF6Access] Avatar Arcade description: {desc}");
        ScreenReaderService.Speak(desc, interrupt: false);
    }

    private static void TryActivate()
    {
        var param = FlowHelper.FindFlowParam(PARAM_TYPE);
        if (param == null) return;

        _param = param;
        _lastIndex = -2;
        _lastDescription = null;
        _isActive = true;
        API.LogInfo("[SF6Access] Avatar Arcade top active");
        PollDescription();
    }

    private static void Reset()
    {
        _isActive = false;
        _param = null;
        _lastIndex = -2;
        _lastDescription = null;
        _pendingDescription = null;
    }

    /// <summary>
    /// Announce the selected mode's description when the list selection changes or
    /// the mode is swapped. The course/difficulty name itself is read by the
    /// generic focus reader, so only the description (which it can't reach) is
    /// spoken here, and only when it actually changes (to avoid repeating a static
    /// mode description across rows that share it).
    /// </summary>
    private static void PollDescription()
    {
        try
        {
            var list = FlowHelper.GetObjectField(_param, "MainList");
            int idx = list != null ? FlowHelper.CallInt(list, "get_SelectedIndex") : -1;

            bool first = _lastIndex == -2;
            if (!first && idx == _lastIndex) return;
            _lastIndex = idx;

            string description = ReadDescription();
            if (string.IsNullOrEmpty(description) || description == _lastDescription) return;
            _lastDescription = description;

            // Defer: announce after the focus reader speaks the row name so this
            // queued line isn't cut off by that interrupting announcement.
            _pendingDescription = description;
            _pendingDescriptionFrame = _pollCounter + DESC_DELAY_FRAMES;
        }
        catch { }
    }

    /// <summary>The selected mode's description text (InputGuide e_text).</summary>
    private static string ReadDescription()
    {
        foreach (var t in GuiTextReader.ReadTextsByOwner(INPUT_GUIDE_GUI))
        {
            if (t.Name == "e_text" && !string.IsNullOrWhiteSpace(t.Text))
                return t.Text.Trim();
        }
        return null;
    }

    /// <summary>Announce the equipped style (name + rank) and combat stats.</summary>
    private static void AnnounceStatsSummary()
    {
        try
        {
            var playerData = GetLocalPlayerData();
            if (playerData == null) return;

            var parts = new List<string>();

            string style = ResolveStyleText(playerData);
            if (!string.IsNullOrEmpty(style)) parts.Add(style);

            string stats = AvatarStatsReader.FormatStats(
                AvatarStatsReader.ReadStatsFromPlayerData(playerData));
            if (!string.IsNullOrEmpty(stats)) parts.Add(stats);

            if (parts.Count == 0) return;
            string summary = string.Join(". ", parts);
            API.LogInfo($"[SF6Access] Avatar Arcade stats: {summary}");
            ScreenReaderService.Speak(summary, interrupt: true);
        }
        catch { }
    }

    /// <summary>
    /// Equipped style label, e.g. "Ryu's Style: Rank 1". The master name renders
    /// as a texture (the on-screen e_text_style is just "'s Style: Rank N"), so
    /// resolve the name from the equipped style id and prepend it to the on-screen
    /// rank text.
    /// </summary>
    private static string ResolveStyleText(ManagedObject playerData)
    {
        string name = null;
        try
        {
            var styleData = FlowHelper.GetObjectField(playerData, "Style");
            int styleId = FlowHelper.ReadIntField(styleData, "StyleEquipId");
            if (styleId > 0)
            {
                string fighter = FlowHelper.ResolveStyleFighterName((uint)styleId);
                if (!string.IsNullOrWhiteSpace(fighter)) name = fighter;
            }
        }
        catch { }

        // The on-screen text carries the localized "'s Style: Rank N" suffix.
        string suffix = null;
        foreach (var t in GuiTextReader.ReadTextsByOwner(ARCADE_GUI))
        {
            if (t.Name == "e_text_style" && !string.IsNullOrWhiteSpace(t.Text))
            {
                suffix = t.Text.Trim();
                break;
            }
        }

        if (name != null && suffix != null) return name + suffix;
        if (name != null) return $"{name}'s Style";
        return suffix;
    }

    private static ManagedObject GetLocalPlayerData()
    {
        var mgr = API.GetManagedSingleton(PLAYER_MANAGER);
        return FlowHelper.GetObjectField(mgr as ManagedObject, "LocalPlayerData");
    }
}
