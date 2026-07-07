using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Announces the arcade post-game screens:
/// - Score tally (app.UIFlowUI11105.Param): the breakdown lives in named int
///   fields, verified by the on-screen arithmetic (Score + the three bonuses =
///   Subtotal; Total is the running arcade total). The winning quote is read
///   first by ArcadeHooks, so this waits until the tally is shown
///   (mIsActive / mIsFadeIn) before announcing once.
/// - Ending artwork cards (app.UIFlowArcadeEndCard.Param, ui11108): a gallery
///   the player advances through; each card's caption ("Special Artwork: ...",
///   "SF Legacy: ...") lives in the `text` element and is announced as it
///   changes.
///
/// ScreenAdapter (multi-Param): either screen being present keeps the adapter
/// active; each poll handles its own param independently. Registered in
/// ScreenRegistry.
/// </summary>
public sealed class ArcadeResultHooks : ScreenAdapter
{
    private const string RESULT_PARAM = "app.UIFlowUI11105.Param";
    private const string ENDCARD_PARAM = "app.UIFlowArcadeEndCard.Param";
    private static readonly string[] Types = { RESULT_PARAM, ENDCARD_PARAM };

    public override string[] OwnedTypes => Types;

    // Let the victory quote (announced by ArcadeHooks) be spoken first: the
    // tally shares the screen with the quote, so wait a beat before reading it.
    // 6 read ticks at the 10-frame interval = the original 60-frame delay.
    private const int RESULT_DELAY_TICKS = 6;

    public ArcadeResultHooks()
    {
        SearchInterval = 10;
        ReadInterval = 10;
    }

    private ManagedObject _resultParam;
    private ManagedObject _endcardParam;
    private int _tick;
    private bool _announcedResult;
    private int _resultSeenTick = -1;
    private string _lastCard;

    protected override bool Locate()
    {
        var found = FlowHelper.FindFlowParams(Types);
        found.TryGetValue(RESULT_PARAM, out _resultParam);
        found.TryGetValue(ENDCARD_PARAM, out _endcardParam);
        return _resultParam != null || _endcardParam != null;
    }

    protected override void OnDeactivate()
    {
        _resultParam = null;
        _endcardParam = null;
        _announcedResult = false;
        _resultSeenTick = -1;
        _lastCard = null;
    }

    protected override void OnPoll()
    {
        _tick++;
        PollResult();
        PollEndCard();
    }

    private void PollResult()
    {
        try
        {
            if (_resultParam == null)
            {
                _announcedResult = false; // screen gone — arm for the next stage
                _resultSeenTick = -1;
                return;
            }
            if (_announcedResult) return;

            // mIsActive/mIsFadeIn stay false even while the tally is on screen, so
            // trigger on the populated total instead, after a short delay so the
            // victory quote (which shares the screen) is announced first.
            int total = FlowHelper.ReadIntField(_resultParam, "mTotalScore", 0);
            if (total == 0) return; // data not populated yet

            if (_resultSeenTick < 0) { _resultSeenTick = _tick; return; }
            if (_tick - _resultSeenTick < RESULT_DELAY_TICKS) return;

            int score = FlowHelper.ReadIntField(_resultParam, "mRewardScore", 0);
            int timeBonus = FlowHelper.ReadIntField(_resultParam, "mTimeScore", 0);
            int vitalBonus = FlowHelper.ReadIntField(_resultParam, "mLifeScore", 0);
            int finishBonus = FlowHelper.ReadIntField(_resultParam, "mFinishTypeScore", 0);
            int subtotal = FlowHelper.ReadIntField(_resultParam, "mRoundScore", 0);

            // The final stage shows only the running total (no breakdown), so omit
            // the zero rows instead of reading "Score 0. Time bonus 0...".
            var parts = new List<string>();
            if (score > 0) parts.Add($"Score {score}");
            if (timeBonus > 0) parts.Add($"Time bonus {timeBonus}");
            if (vitalBonus > 0) parts.Add($"Vitality bonus {vitalBonus}");
            if (finishBonus > 0) parts.Add($"Finish bonus {finishBonus}");
            if (subtotal > 0 && subtotal != total) parts.Add($"Subtotal {subtotal}");
            parts.Add($"Total {total}");

            _announcedResult = true;
            string text = string.Join(". ", parts) + ".";
            API.LogInfo($"[SF6Access] Arcade result: {text}");
            Speak(text, interrupt: false);
        }
        catch { }
    }

    private void PollEndCard()
    {
        try
        {
            if (_endcardParam == null)
            {
                _lastCard = null; // gallery closed — re-announce on the next entry
                return;
            }

            string caption = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(_endcardParam, "text"));
            if (string.IsNullOrWhiteSpace(caption) || caption == _lastCard) return;
            _lastCard = caption;

            API.LogInfo($"[SF6Access] Arcade end card: {caption}");
            Speak(caption);
        }
        catch { }
    }
}
