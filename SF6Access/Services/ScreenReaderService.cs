using DavyKager;
using REFrameworkNET;

namespace SF6Access.Services;

public static class ScreenReaderService
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;

        try
        {
            Tolk.Load();
            _initialized = true;

            var screenReader = Tolk.DetectScreenReader();
            if (!string.IsNullOrEmpty(screenReader))
            {
                API.LogInfo($"[SF6Access] Screen reader detected: {screenReader}");
            }
            else
            {
                API.LogWarning("[SF6Access] No screen reader detected");
            }
        }
        catch (System.Exception ex)
        {
            API.LogError($"[SF6Access] Failed to initialize Tolk: {ex.Message}");
        }
    }

    public static void Shutdown()
    {
        if (!_initialized) return;

        try
        {
            Tolk.Unload();
            _initialized = false;
        }
        catch (System.Exception ex)
        {
            API.LogError($"[SF6Access] Failed to unload Tolk: {ex.Message}");
        }
    }

    /// <summary>Tick of the last interrupting announcement — lets low-priority
    /// readers (tooltips) wait so they queue after the element name.</summary>
    public static long LastInterruptTick { get; private set; }
    public static long LastExternalTick { get; private set; }
    public static long ExternalSequence { get; private set; }
    public static long ProtectedUntil { get; private set; }
    private static bool _lastWasAutomatic;

    public static bool ExternalSpeaking
    {
        get
        {
            if (!_initialized || _lastWasAutomatic) return false;
            try { return Tolk.IsSpeaking(); }
            catch { return false; }
        }
    }

    // Several hooks can resolve the same on-screen text (focused item subtree
    // walk + guide text poll) — drop identical announcements fired back-to-back.
    // Keep the window SHORT: real double-fires land within a frame or two,
    // while 600ms also swallowed rows the user re-visited by navigating fast
    private static string _lastText;
    private static long _lastTextTick;
    private const long DUPLICATE_WINDOW_MS = 250;

    public static void Speak(string text, bool interrupt = true)
        => Output(text, interrupt, automatic: false);

    internal static void SpeakAutomatic(string text) => Output(text, true, automatic: true);

    private static void Output(string text, bool interrupt, bool automatic)
    {
        if (!_initialized || string.IsNullOrEmpty(text)) return;

        try
        {
            long now = System.Environment.TickCount64;
            // Also drop a text CONTAINED in what was just spoken: the menu
            // announces "Profile. {description}" and the guide watcher then
            // re-announces the same description on its own
            if (_lastText != null && now - _lastTextTick < DUPLICATE_WINDOW_MS &&
                (text == _lastText || _lastText.Contains(text)))
            {
                API.LogInfo($"[SF6Access] Speak skipped (duplicate): {text}");
                return;
            }
            _lastText = text;
            _lastTextTick = now;

            // Ground truth of everything sent to the reader — hooks log their
            // own announces, but duplicates can come from paths that don't
            API.LogInfo($"[SF6Access] Speak({(interrupt ? "interrupt" : "queue")}): {text}");

            if (interrupt) LastInterruptTick = now;
            if (!automatic)
            {
                LastExternalTick = now;
                ExternalSequence++;
                ProtectedUntil = WorldTour.FieldSpeechPolicy.ProtectUntil(now, ProtectedUntil, text, interrupt);
            }
            _lastWasAutomatic = automatic;
            Tolk.Output(text, interrupt);
        }
        catch (System.Exception ex)
        {
            API.LogError($"[SF6Access] Tolk.Output failed: {ex.Message}");
        }
    }
}
