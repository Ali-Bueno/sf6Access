using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Generic value-change reader for menus without dedicated hooks (custom rooms,
/// lobby creation forms, etc.). MainMenuHooks registers the focused GUI control
/// here; this hook re-reads its subtree text and announces changes — which covers
/// spin values edited with left/right that don't fire FocusChanged.
/// </summary>
public class FocusValueHooks
{
    private static ManagedObject _control;
    private static string _lastText;
    private static int _pollCounter;
    private static int _lastAnnounceFrame;

    private const int POLL_INTERVAL = 10;
    private const int MIN_FRAMES_BETWEEN_ANNOUNCEMENTS = 20;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] FocusValueHooks initialized");
    }

    /// <summary>Start watching a focused control's text for changes.</summary>
    public static void Track(ManagedObject control)
    {
        _control = control;
        _lastText = control != null ? GuiTextReader.ReadControlTextJoined(control) : null;
    }

    public static void Clear()
    {
        _control = null;
        _lastText = null;
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;
        if (_control == null || _pollCounter % POLL_INTERVAL != 0) return;

        string text;
        try { text = GuiTextReader.ReadControlTextJoined(_control); }
        catch
        {
            Clear(); // Control is dead
            return;
        }

        if (string.IsNullOrEmpty(text) || text == _lastText)
            return;

        string previous = _lastText;
        _lastText = text;
        if (previous == null) return; // First successful read, don't announce

        if (_pollCounter - _lastAnnounceFrame < MIN_FRAMES_BETWEEN_ANNOUNCEMENTS) return;
        _lastAnnounceFrame = _pollCounter;

        API.LogInfo($"[SF6Access] Focus value changed: {text}");
        ScreenReaderService.Speak(text);
    }
}
