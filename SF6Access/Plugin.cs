using System;
using System.Runtime.InteropServices;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access;

public class Plugin
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private const int SW_HIDE = 0;

    private static int _frameCounter;
    private static bool _focusRestored;

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

    /// <summary>
    /// The console steals focus at startup and hiding it leaves the screen
    /// reader stranded — move focus back to the game window so the user
    /// doesn't have to alt-tab.
    /// </summary>
    private static void RestoreGameFocus()
    {
        try
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var gameWindow = process.MainWindowHandle;
            var console = GetConsoleWindow();
            if (gameWindow == IntPtr.Zero || gameWindow == console) return;

            // Only steal focus when it is on the (hidden) console or lost to
            // no window of ours — never yank it from an app the user chose
            var foreground = GetForegroundWindow();
            if (foreground == gameWindow) return;
            GetWindowThreadProcessId(foreground, out uint foregroundPid);
            if (foreground != IntPtr.Zero && foreground != console &&
                foregroundPid != (uint)process.Id) return;

            SetForegroundWindow(gameWindow);
            API.LogInfo("[SF6Access] Restored focus to the game window");
        }
        catch { }
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        if (_focusRestored) return;

        // Give the game window time to exist, then fix focus once
        if (++_frameCounter < 180) return;
        _focusRestored = true;
        HideConsole();
        RestoreGameFocus();
    }

    [PluginEntryPoint]
    public static void Main()
    {
        HideConsole();
        RestoreGameFocus();

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
