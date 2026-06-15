using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

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
/// </summary>
public class ArcadeResultHooks
{
    private const string RESULT_PARAM = "app.UIFlowUI11105.Param";
    private const string ENDCARD_PARAM = "app.UIFlowArcadeEndCard.Param";

    private static int _pollCounter;
    private const int POLL_INTERVAL = 10;

    // Let the victory quote (announced by ArcadeHooks) be spoken first: the
    // tally shares the screen with the quote, so wait a beat before reading it.
    private const int RESULT_DELAY = 60;

    private static bool _announcedResult;
    private static int _resultSeenFrame = -1;
    private static string _lastCard;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] ArcadeResultHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;
        if (_pollCounter % POLL_INTERVAL != 0) return;

        PollResult();
        PollEndCard();
    }

    private static void PollResult()
    {
        try
        {
            var param = FlowHelper.FindFlowParam(RESULT_PARAM);
            if (param == null)
            {
                _announcedResult = false; // screen gone — arm for the next stage
                _resultSeenFrame = -1;
                return;
            }
            if (_announcedResult) return;

            // mIsActive/mIsFadeIn stay false even while the tally is on screen, so
            // trigger on the populated total instead, after a short delay so the
            // victory quote (which shares the screen) is announced first.
            int total = FlowHelper.ReadIntField(param, "mTotalScore", 0);
            if (total == 0) return; // data not populated yet

            if (_resultSeenFrame < 0) { _resultSeenFrame = _pollCounter; return; }
            if (_pollCounter - _resultSeenFrame < RESULT_DELAY) return;

            int score = FlowHelper.ReadIntField(param, "mRewardScore", 0);
            int timeBonus = FlowHelper.ReadIntField(param, "mTimeScore", 0);
            int vitalBonus = FlowHelper.ReadIntField(param, "mLifeScore", 0);
            int finishBonus = FlowHelper.ReadIntField(param, "mFinishTypeScore", 0);
            int subtotal = FlowHelper.ReadIntField(param, "mRoundScore", 0);

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
            ScreenReaderService.Speak(text, interrupt: false);
        }
        catch { }
    }

    private static void PollEndCard()
    {
        try
        {
            var param = FlowHelper.FindFlowParam(ENDCARD_PARAM);
            if (param == null)
            {
                _lastCard = null; // gallery closed — re-announce on the next entry
                return;
            }

            string caption = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(param, "text"));
            if (string.IsNullOrWhiteSpace(caption) || caption == _lastCard) return;
            _lastCard = caption;

            API.LogInfo($"[SF6Access] Arcade end card: {caption}");
            ScreenReaderService.Speak(caption);
        }
        catch { }
    }
}
