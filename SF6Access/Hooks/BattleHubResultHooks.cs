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
///
/// app.UIFlowRankGauge.Param (post-match LP) is read DATA-DRIVEN: the on-screen LP
/// text animates (counts up), but the param already holds the final result as data
/// in RankInfoAfter (league_point / league_rank / master_rating) plus RankInfoBefore
/// for the delta. So each gauge is announced once, as soon as that data is populated,
/// with no dependency on the count-up animation. The rank name is resolved through
/// the shared LeagueRankResolver (no hardcoded tiers).
/// </summary>
public class BattleHubResultHooks
{
    // Params whose announcement is the joined text of the listed via.gui.Text fields.
    private static readonly Dictionary<string, string[]> TextParams = new()
    {
        { "app.esports.UIFlowWinMessage.Param", new[] { "mText" } },
        { "app.UIFlowRivalAISuggestion.Param", new[] { "mTextSuggestion", "mCompleteText" } },
        // app.UIFlowRankGauge.Param is handled separately (settle-once, see PollRankGauges).
    };

    private const string COUNTER_PARAM = "app.esports.UIFlowResultCounter.Param";

    private const string RANK_GAUGE = "app.UIFlowRankGauge.Param";

    private static int _pollCounter;
    private const int POLL_INTERVAL = 12;

    // Last announced text per param type, so a display is read once until it changes.
    private static readonly Dictionary<string, string> _lastText = new();

    // RankGauge instances already announced (keyed by object address): both
    // players' gauges are announced once each; cleared when a gauge disappears.
    private static readonly HashSet<ulong> _rankAnnounced = new();

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

        PollRankGauges();
    }

    /// <summary>
    /// Announce each post-match LP gauge once from its final DATA (RankInfoAfter),
    /// not the animating on-screen text — so it reads the finished value with no
    /// count-up wait. Both players' gauges are announced once each; the announced
    /// set is pruned when a gauge disappears so the next match announces again.
    /// </summary>
    private static void PollRankGauges()
    {
        var present = new HashSet<ulong>();
        foreach (var (_, param) in FlowHelper.FindFlowParamsByPrefix(RANK_GAUGE))
        {
            ulong addr = FlowHelper.AddressOf(param);
            if (addr == 0) continue;
            present.Add(addr);
            if (_rankAnnounced.Contains(addr)) continue;

            // Data may populate a poll or two after the gauge appears — retry until ready.
            string text = BuildRankResult(param);
            if (string.IsNullOrEmpty(text)) continue;

            _rankAnnounced.Add(addr);
            API.LogInfo($"[SF6Access] Rank gauge: {text}");
            ScreenReaderService.Speak(text, interrupt: false);
        }

        if (_rankAnnounced.Count == 0) return;
        var stale = new List<ulong>();
        foreach (var key in _rankAnnounced)
            if (!present.Contains(key)) stale.Add(key);
        foreach (var key in stale) _rankAnnounced.Remove(key);
    }

    /// <summary>
    /// "{name}. {rank}. {value} LP/MR. {+/-delta}" from the gauge's final data
    /// (RankInfoAfter, with RankInfoBefore for the change). Null until populated.
    /// </summary>
    private static string BuildRankResult(ManagedObject param)
    {
        var after = FlowHelper.GetObjectField(param, "RankInfoAfter");
        if (after == null) return null;

        int mr = FlowHelper.ReadIntField(after, "master_rating", 0);
        int lp = FlowHelper.ReadIntField(after, "league_point", int.MinValue);
        bool master = mr > 0;
        if (!master && lp == int.MinValue) return null; // data not ready yet

        var parts = new List<string>();

        string name = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(param, "TextName"));
        if (!string.IsNullOrWhiteSpace(name)) parts.Add(name.Trim());

        string rank = LeagueRankResolver.Resolve(FlowHelper.ReadIntField(after, "league_rank", 0), tierOnly: false);
        if (!string.IsNullOrEmpty(rank)) parts.Add(rank);

        int afterVal = master ? mr : lp;
        parts.Add($"{afterVal} {(master ? "MR" : "LP")}");

        // Change vs before the match.
        var before = FlowHelper.GetObjectField(param, "RankInfoBefore");
        int beforeVal = FlowHelper.ReadIntField(before, master ? "master_rating" : "league_point", afterVal);
        int delta = afterVal - beforeVal;
        if (delta != 0) parts.Add(delta > 0 ? $"+{delta}" : delta.ToString());

        return parts.Count > 0 ? string.Join(". ", parts) : null;
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
