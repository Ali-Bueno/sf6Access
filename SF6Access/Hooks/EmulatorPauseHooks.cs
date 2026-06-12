using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the retro game (Game Center / emulator) pause menu —
/// app.UIFlowEmulatorPauseMenu.Param. The param exposes only outSelectedIndex
/// (no UIParts lists), so the focused option's text is read from the pause
/// menu's own GUI; "Option N" is the last-resort fallback so blind players
/// can at least count their way to the exit entry.
/// </summary>
public class EmulatorPauseHooks
{
    private const string PARAM_TYPE = "app.UIFlowEmulatorPauseMenu.Param";

    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 30;
    private const int POLL_READ_INTERVAL = 5;

    private static ManagedObject _param;
    private static int _lastIndex = -2;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] EmulatorPauseHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (!_isActive)
        {
            if (_pollCounter % POLL_SEARCH_INTERVAL != 0) return;
            TryActivate();
            return;
        }

        // Re-bind when the game recreated the Param (stale instance → silent)
        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var current = FlowHelper.TrackFlowParam(PARAM_TYPE, _param, out bool changed);
            if (current == null)
            {
                Reset();
                return;
            }
            if (changed)
            {
                _param = current;
                _lastIndex = -2;
            }
        }

        if (_pollCounter % POLL_READ_INTERVAL != 0) return;

        int idx = FlowHelper.ReadIntField(_param, "outSelectedIndex");
        if (idx < 0) return;
        if (idx == _lastIndex) return;
        _lastIndex = idx;

        string label = ReadOptionText(idx) ?? $"Option {idx + 1}";
        API.LogInfo($"[SF6Access] Emulator pause [{idx}]: {label}");
        ScreenReaderService.Speak(label);
    }

    private static void TryActivate()
    {
        var param = FlowHelper.FindFlowParam(PARAM_TYPE);
        if (param == null) return;

        _param = param;
        _lastIndex = -2;
        _isActive = true;

        API.LogInfo("[SF6Access] Emulator pause menu opened");
        ScreenReaderService.Speak("Pause menu");
    }

    /// <summary>The pause menu's on-screen option texts, by index.</summary>
    private static string ReadOptionText(int idx)
    {
        try
        {
            var candidates = new List<string>();
            foreach (var t in GuiTextReader.ReadTextsByOwner("Pause"))
            {
                if (string.IsNullOrWhiteSpace(t.Text)) continue;
                candidates.Add(t.Text.Trim());
            }
            if (idx >= 0 && idx < candidates.Count) return candidates[idx];
        }
        catch { }
        return null;
    }

    private static void Reset()
    {
        API.LogInfo("[SF6Access] Emulator pause menu closed");
        _isActive = false;
        _param = null;
        _lastIndex = -2;
    }
}
