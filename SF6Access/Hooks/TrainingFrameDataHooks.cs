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
/// Spoken as "You: …" / "Opponent: …" once per exchange.
///
/// ScreenAdapter: Locate() scans for the HUD GUI views (no flow Param owns
/// them). Registered in ScreenRegistry.
/// </summary>
public sealed class TrainingFrameDataHooks : ScreenAdapter
{
    private const string FRAME_GUI = "ui11255";
    private static readonly string[] Types = { FRAME_GUI };
    public override string[] OwnedTypes => Types;

    // Top row is P1 (the training player), bottom row P2 — confirmed in game
    // 2026-10-16: the top row carried the startup of the player's own attack
    private const string OWN_LINE = "e_txt_up";
    private const string OPPONENT_LINE = "e_txt_down";
    private const int SEGMENTS = 3; // startup / total / advantage

    // GameLocalData.FMActive: 0 idle, 1 acting, 3 contact, 2 closing (observed)
    private const int FM_IDLE = 0;
    // The rows are rewritten within a frame or two of the meter going idle
    private const int SETTLE_FRAMES = 3;

    public TrainingFrameDataHooks()
    {
        SearchInterval = 60;
        ReadInterval = 1; // the idle gap between two exchanges can be a few frames
    }

    private readonly List<(string owner, ManagedObject view)> _views = new();
    private bool _exchangeRunning;
    private int _idleFrames;

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
        _exchangeRunning = false;
    }

    /// <summary>
    /// One readout per exchange, when the game's frame meter goes idle
    /// (GameLocalData.FMActive back to 0) — the rows are final then. Reading
    /// the rows as they change instead spoke every stage the game fills in
    /// (startup, then total, then advantage), and a move started straight after
    /// another briefly shows the new startup beside the old total/advantage.
    /// FMActive only exists in a live training session, which also keeps the
    /// HUD copy shown in replays silent.
    /// </summary>
    protected override void OnPoll()
    {
        int meter = FlowHelper.ReadIntField(FlowHelper.GetTrainingGameData(), "FMActive", FM_IDLE);
        if (meter != FM_IDLE) { _exchangeRunning = true; _idleFrames = 0; return; }
        if (!_exchangeRunning || ++_idleFrames < SETTLE_FRAMES) return;
        _exchangeRunning = false;

        // The panel stays in the scene when the Frame Meter display option is
        // off, so gate on the setting itself (Is_FrameMeter_View)
        var ds = FlowHelper.GetTrainingDisplaySetting();
        if (ds != null && !FlowHelper.ReadBoolField(ds, "Is_FrameMeter_View")) return;

        AnnounceExchange();
    }

    /// <summary>
    /// Each attacker's row, labelled by side. The defender's row has no startup
    /// and only mirrors the attacker's advantage, so it is left out; rows with no
    /// startup at all (walking, dashing) are not announced. A whiff never gets
    /// an advantage, so its "--" is dropped.
    /// </summary>
    private void AnnounceExchange()
    {
        string own = null, opponent = null;
        foreach (var (owner, view) in _views)
        {
            try
            {
                foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
                {
                    if (t.Name == OWN_LINE) own = FlowHelper.CleanTags(t.Text).Trim();
                    else if (t.Name == OPPONENT_LINE) opponent = FlowHelper.CleanTags(t.Text).Trim();
                }
            }
            catch { }
        }

        var parts = new List<string>();
        if (HasStartup(own)) parts.Add($"{LocalizedText.You()}: {WithoutUnknownAdvantage(own)}");
        if (HasStartup(opponent)) parts.Add($"{LocalizedText.Opponent()}: {WithoutUnknownAdvantage(opponent)}");
        if (parts.Count == 0) return;

        string text = string.Join(". ", parts);
        API.LogInfo($"[SF6Access] Frame data: {text}");
        Speak(text, interrupt: false);
    }

    // Rows read "Startup 8F / Total 28F / Advantage 5F" in every language, with
    // "--" for a value not known
    private static string[] Segments(string row) => row?.Split('/') ?? System.Array.Empty<string>();

    private static bool HasStartup(string row)
    {
        var s = Segments(row);
        return s.Length == SEGMENTS && ContainsDigit(s[0]);
    }

    private static string WithoutUnknownAdvantage(string row)
    {
        var s = Segments(row);
        return ContainsDigit(s[SEGMENTS - 1]) ? row : string.Join("/", s, 0, SEGMENTS - 1).Trim();
    }

    private static bool ContainsDigit(string s)
    {
        foreach (char c in s) if (c >= '0' && c <= '9') return true;
        return false;
    }
}
