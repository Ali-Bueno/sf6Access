using REFrameworkNET;
using REFrameworkNET.Attributes;
using SF6Access.Services;

namespace SF6Access;

public class Plugin
{
    [PluginEntryPoint]
    public static void Main()
    {
        API.LogInfo("[SF6Access] Initializing SF6 Accessibility Plugin...");

        ScreenReaderService.Initialize();
        ScreenReaderService.Speak("SF6 Access loaded");

        API.LogInfo("[SF6Access] Plugin initialized successfully");
    }

    [PluginExitPoint]
    public static void Unload()
    {
        API.LogInfo("[SF6Access] Shutting down...");

        GameStateTracker.Clear();
        ScreenReaderService.Shutdown();
    }
}
