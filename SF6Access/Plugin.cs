using System;
using System.Runtime.InteropServices;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using SF6Access.Services;

namespace SF6Access;

public class Plugin
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_HIDE = 0;

    /// <summary>
    /// Hide the console window REFramework.NET allocates at startup.
    /// Logs still go to re2_framework_log.txt.
    /// </summary>
    private static void HideConsole()
    {
        try
        {
            API.LogToConsole = false;

            var hwnd = GetConsoleWindow();
            if (hwnd != IntPtr.Zero)
                ShowWindow(hwnd, SW_HIDE);
        }
        catch { }
    }

    [PluginEntryPoint]
    public static void Main()
    {
        HideConsole();

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
