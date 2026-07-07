using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Announces the training-mode frame data display. The in-battle HUD GUI
/// "ui11255" holds two ready-to-read localized lines (e_txt_up / e_txt_down),
/// e.g. "Início 7F/Total 25F/Vantagem 2F" (Startup/Total/Advantage). They read
/// "--" until a move is performed, then fill with numbers. Confirmed present in
/// every training autodump; no need for the uint-keyed widget dictionary.
///
/// ScreenAdapter: Locate() scans for the HUD GUI views (no flow Param owns
/// them). Registered in ScreenRegistry.
/// </summary>
public sealed class TrainingFrameDataHooks : ScreenAdapter
{
    private const string FRAME_GUI = "ui11255";
    private static readonly string[] Types = { FRAME_GUI };
    public override string[] OwnedTypes => Types;

    private static readonly string[] LineNames = { "e_txt_up", "e_txt_down" };

    public TrainingFrameDataHooks()
    {
        SearchInterval = 60;
        ReadInterval = 6;
    }

    private readonly List<(string owner, ManagedObject view)> _views = new();
    private readonly Dictionary<string, string> _lastByLine = new();

    protected override bool Locate()
    {
        _views.Clear();
        foreach (var v in GuiTextReader.FindGuiViews(FRAME_GUI))
            _views.Add(v);
        return _views.Count > 0;
    }

    protected override void OnDeactivate()
    {
        _views.Clear();
        _lastByLine.Clear();
    }

    protected override void OnPoll()
    {
        // The frame meter HUD ("ui11255") also renders while watching replays
        // (where it was leaking). Only read it during a live training session...
        if (API.GetManagedSingleton("app.training.TrainingManager") == null)
        {
            _lastByLine.Clear();
            return;
        }

        // ...and only when the Frame Meter display option is enabled. The panel
        // stays in the scene when the option is off, so it must be gated by the
        // actual setting (Is_FrameMeter_View) rather than panel presence.
        var ds = FlowHelper.GetTrainingDisplaySetting();
        if (ds != null && !FlowHelper.ReadBoolField(ds, "Is_FrameMeter_View"))
        {
            _lastByLine.Clear();
            return;
        }

        PollFrameData();
    }

    private void PollFrameData()
    {
        foreach (var (owner, view) in _views)
        {
            try
            {
                foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
                {
                    if (System.Array.IndexOf(LineNames, t.Name) < 0) continue;
                    if (string.IsNullOrWhiteSpace(t.Text)) continue;

                    string text = FlowHelper.CleanTags(t.Text).Trim();
                    // Only speak once a move filled in numbers (skip the "--" idle
                    // state and frames mid-animation that have no digits yet)
                    if (!ContainsDigit(text)) { _lastByLine[t.Name] = null; continue; }

                    _lastByLine.TryGetValue(t.Name, out string last);
                    if (text == last) continue;
                    _lastByLine[t.Name] = text;

                    API.LogInfo($"[SF6Access] Frame data [{t.Name}]: {text}");
                    Speak(text, interrupt: false);
                }
            }
            catch { }
        }
    }

    private static bool ContainsDigit(string s)
    {
        foreach (char c in s) if (c >= '0' && c <= '9') return true;
        return false;
    }
}
