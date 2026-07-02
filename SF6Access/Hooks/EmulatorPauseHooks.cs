using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the retro game (Game Center / emulator) pause menu —
/// app.UIFlowEmulatorPauseMenu.Param. The param exposes only outSelectedIndex
/// (no UIParts lists), so the focused option's text is read from the pause
/// menu's own GUI; "Option N" is the last-resort fallback so blind players can
/// at least count their way to the exit entry. Migrated to ScreenAdapter.
/// </summary>
public sealed class EmulatorPauseHooks : SingleParamScreenAdapter
{
    protected override string ParamType => "app.UIFlowEmulatorPauseMenu.Param";

    public EmulatorPauseHooks()
    {
        SearchInterval = 30;
        ReadInterval = 5;
    }

    private int _lastIndex = -2;

    protected override void OnBind()
    {
        _lastIndex = -2;
        API.LogInfo("[SF6Access] Emulator pause menu opened");
        ScreenReaderService.Speak("Pause menu");
    }

    protected override void OnExit()
    {
        _lastIndex = -2;
        API.LogInfo("[SF6Access] Emulator pause menu closed");
    }

    protected override void Poll()
    {
        int idx = FlowHelper.ReadIntField(Param, "outSelectedIndex");
        if (idx < 0 || idx == _lastIndex) return;
        _lastIndex = idx;

        string label = ReadOptionText(idx) ?? $"Option {idx + 1}";
        API.LogInfo($"[SF6Access] Emulator pause [{idx}]: {label}");
        ScreenReaderService.Speak(label);
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
}
