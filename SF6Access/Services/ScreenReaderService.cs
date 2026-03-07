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

    public static void Speak(string text, bool interrupt = true)
    {
        if (!_initialized || string.IsNullOrEmpty(text)) return;

        try
        {
            Tolk.Output(text, interrupt);
        }
        catch (System.Exception ex)
        {
            API.LogError($"[SF6Access] Tolk.Output failed: {ex.Message}");
        }
    }
}
