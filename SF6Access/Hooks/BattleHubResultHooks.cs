using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the auto-shown post-match / Battle Hub INFO displays that appear on
/// their own (not navigable lists, so the focus reader does not cover them).
/// Each is a flow Param holding via.gui.Text fields; this polls them and
/// announces the text once when it appears or changes:
/// - app.esports.UIFlowWinMessage.Param  → mText (the winner's victory quote)
/// - app.UIFlowRivalAISuggestion.Param    → mTextSuggestion / mCompleteText (the
///   Rival AI training task popup)
/// - app.esports.UIFlowResultCounter.Param→ win counts + win-rate per player
///
/// app.esports.UIFlowResultTitle / app.UIFlowResultTimer / app.UIFlowResultRivalAi
/// expose no via.gui.Text fields (rendered as images), so they are not read here.
/// app.UIFlowRankGauge.Param is intentionally NOT read: its LP value animates and it
/// is shown per player, so it re-announced on every poll (see TextParams comment).
/// </summary>
public class BattleHubResultHooks
{
    // Params whose announcement is the joined text of the listed via.gui.Text fields.
    private static readonly Dictionary<string, string[]> TextParams = new()
    {
        { "app.esports.UIFlowWinMessage.Param", new[] { "mText" } },
        { "app.UIFlowRivalAISuggestion.Param", new[] { "mTextSuggestion", "mCompleteText" } },
        // app.UIFlowRankGauge.Param removed: the LP value animates (counts up) after
        // a match and the gauge is read for BOTH players, so the joined text changed
        // every poll and was re-announced many times ("yarkidius. 0th. 1370 LP. 1400. 30"
        // repeated). The LP-after-match readout needs a settle-then-announce-once design;
        // dropped for now to kill the spam.
    };

    private const string COUNTER_PARAM = "app.esports.UIFlowResultCounter.Param";

    private static int _pollCounter;
    private const int POLL_INTERVAL = 12;

    // Last announced text per param type, so a display is read once until it changes.
    private static readonly Dictionary<string, string> _lastText = new();

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] BattleHubResultHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;
        if (_pollCounter % POLL_INTERVAL != 0) return;

        var targets = new List<string>(TextParams.Keys) { COUNTER_PARAM };
        var found = FlowHelper.FindFlowParams(targets.ToArray());

        foreach (var kvp in TextParams)
        {
            found.TryGetValue(kvp.Key, out var param);
            Announce(kvp.Key, param == null ? null : JoinTextFields(param, kvp.Value));
        }

        found.TryGetValue(COUNTER_PARAM, out var counter);
        Announce(COUNTER_PARAM, counter == null ? null : BuildCounter(counter));
    }

    /// <summary>Announce a param's text once; reset its memory when it disappears.</summary>
    private static void Announce(string typeName, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            _lastText.Remove(typeName);
            return;
        }
        if (_lastText.TryGetValue(typeName, out var last) && last == text) return;

        _lastText[typeName] = text;
        API.LogInfo($"[SF6Access] {typeName}: {text}");
        ScreenReaderService.Speak(text, interrupt: false);
    }

    /// <summary>Join the messages of the named via.gui.Text fields, skipping empties and duplicates.</summary>
    private static string JoinTextFields(ManagedObject param, string[] fieldNames)
    {
        var parts = new List<string>();
        foreach (var name in fieldNames)
        {
            string msg = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(param, name));
            if (string.IsNullOrWhiteSpace(msg)) continue;
            msg = msg.Trim();
            if (!parts.Contains(msg)) parts.Add(msg);
        }
        return parts.Count > 0 ? string.Join(". ", parts) : null;
    }

    /// <summary>
    /// Win counts (mP1WinCount / mP2WinCount) plus each player's on-screen win-rate
    /// text (PlayerObj.TextRatio) for the post-match counter screen.
    /// </summary>
    private static string BuildCounter(ManagedObject param)
    {
        int p1 = FlowHelper.ReadIntField(param, "mP1WinCount", -1);
        int p2 = FlowHelper.ReadIntField(param, "mP2WinCount", -1);
        if (p1 < 0 && p2 < 0) return null;

        string r1 = null, r2 = null;
        var players = FlowHelper.GetObjectField(param, "mArrPlayerObj");
        int count = FlowHelper.GetListCount(players);
        if (count > 0) r1 = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(FlowHelper.GetListItem(players, 0), "TextRatio"));
        if (count > 1) r2 = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(FlowHelper.GetListItem(players, 1), "TextRatio"));

        string s1 = $"Player 1: {(p1 < 0 ? 0 : p1)} wins" + (string.IsNullOrWhiteSpace(r1) ? "" : $", {r1.Trim()}");
        string s2 = $"Player 2: {(p2 < 0 ? 0 : p2)} wins" + (string.IsNullOrWhiteSpace(r2) ? "" : $", {r2.Trim()}");
        return $"{s1}. {s2}";
    }
}
