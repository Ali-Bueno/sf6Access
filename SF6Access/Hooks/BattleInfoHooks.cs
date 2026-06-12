using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Battle-flow announcements that have no menu focus to hook:
/// - VS screen (who fights whom + stage) via app.esports.VSInfo* flow params,
///   reading the VSInfo GUI texts (player names, fighter names, LP, stage).
/// - Round wins during battle: BattleAnnounceHud_Round GUI keeps per-side
///   round-win counters (e_txt_count_1P/2P) and the fighter names — announce
///   "{winner} {c1} - {c2}" whenever a counter increases.
/// - Match result via app.esports.UIFlowResultCounter.Param (mTeam = EWinTeam
///   winner, mP1WinCount/mP2WinCount, localized streak text).
/// - Matchmaking notices (ranked/casual match found while in other menus) by
///   watching Matching* widget GUI texts for changes.
/// </summary>
public class BattleInfoHooks
{
    private const string VSINFO_PREFIX = "app.esports.VSInfo";
    private const string RESULT_COUNTER = "app.esports.UIFlowResultCounter.Param";
    private static readonly string[] ResultCounterTypes = { RESULT_COUNTER };

    // GameObject-name filters of matchmaking banner widgets ("Wating" is the
    // game's own typo in BattleHubMatchWating; Resident_Cmn_OnlineStandby holds
    // the ranked/casual "searching for opponent" text). Keep these SPECIFIC: a
    // broad "Matching" filter matched the MatchingSetting screens and read them
    // as notices on every value change.
    private static readonly string[] MatchingOwnerFilters =
        { "MatchingStandby", "MatchWating", "OnlineStandby" };

    private static int _pollCounter;
    private const int POLL_INTERVAL = 30;
    private const int SCAN_INTERVAL = 60;
    private const int MAX_VS_SEGMENTS = 12;

    private static bool _vsAnnounced;
    private static string _vsPendingText;

    private static ManagedObject _roundView;
    // Persist across view re-finds: resetting them on every re-acquire
    // swallowed round announcements (only the first round of a ranked match
    // was read). A counter DECREASE is the new-match signal.
    private static int _lastCount1P = -1;
    private static int _lastCount2P = -1;
    private static string _fighterName1P;
    private static string _fighterName2P;

    private static bool _resultAnnounced;

    private static readonly Dictionary<string, string> _lastMatchingTexts = new();

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] BattleInfoHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;
        if (_pollCounter % POLL_INTERVAL == 0)
        {
            PollVsScreen();
            PollRoundCounts();
            PollResultCounter();
        }
        if (_pollCounter % SCAN_INTERVAL == 0)
            PollMatchingNotices();
    }

    // ---- VS screen ----

    private static void PollVsScreen()
    {
        try
        {
            var found = FlowHelper.FindFlowParamsByPrefix(VSINFO_PREFIX);
            if (found.Count == 0)
            {
                _vsAnnounced = false;
                return;
            }
            if (_vsAnnounced) return;

            // Structured read first: the GUI tree enumerates P2 BEFORE P1, so
            // the text-window approach announced the sides reversed
            string text = null;
            foreach (var (unused, param) in found)
            {
                text = BuildVsAnnouncement(param);
                if (text != null) break;
            }
            text ??= ReadVsGuiTexts();
            if (text == null) return; // not filled yet, retry next poll

            // Wait for two consecutive equal reads: the opponent's name loads
            // a beat after the rest and a too-early announcement missed it
            if (text != _vsPendingText)
            {
                _vsPendingText = text;
                return;
            }

            _vsAnnounced = true;
            _vsPendingText = null;
            API.LogInfo($"[SF6Access] VS screen: {text}");
            ScreenReaderService.Speak(text);
        }
        catch { }
    }

    /// <summary>
    /// "{P1} vs {P2}. {stage}" from the VSInfo param's player data array
    /// (fighter name + online id + CPU level per side). Null when the param
    /// doesn't carry player data or it isn't filled yet.
    /// </summary>
    private static string BuildVsAnnouncement(ManagedObject param)
    {
        var players = FlowHelper.GetObjectField(param, "mPlayerData");
        if (FlowHelper.GetListCount(players) < 2) return null;

        var sides = new List<string>();
        for (int i = 0; i < 2; i++)
        {
            var pd = FlowHelper.GetListItem(players, i);
            var bits = new List<string>();

            string chara = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(pd, "mTextCharaName"));
            if (!string.IsNullOrWhiteSpace(chara)) bits.Add(chara.Trim());

            string online = FlowHelper.ReadStringField(pd, "mOnlineIdName");
            if (!string.IsNullOrWhiteSpace(online) && online.Trim() != chara?.Trim())
                bits.Add(online.Trim());

            string cpuLevel = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(pd, "mTextCPULevel"));
            if (!string.IsNullOrWhiteSpace(cpuLevel)) bits.Add($"CPU {cpuLevel.Trim()}");

            if (bits.Count == 0) return null; // player data not filled yet
            sides.Add(string.Join(" ", bits));
        }

        string stage = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(param, "mTextStageName"))
            ?? FlowHelper.ReadStringField(param, "mStageCityName");

        string text = $"{sides[0]} vs {sides[1]}";
        if (!string.IsNullOrWhiteSpace(stage)) text += $". {stage.Trim()}";
        return text;
    }

    /// <summary>Fallback: window of visible VSInfo GUI texts in tree order.</summary>
    private static string ReadVsGuiTexts()
    {
        var segments = new List<string>();
        foreach (var (owner, view) in GuiTextReader.FindGuiViews("VSInfo"))
        {
            foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
            {
                if (string.IsNullOrWhiteSpace(t.Text)) continue;
                string trimmed = t.Text.Trim();
                if (segments.Contains(trimmed)) continue;
                segments.Add(trimmed);
                if (segments.Count >= MAX_VS_SEGMENTS) break;
            }
        }
        return segments.Count > 0 ? string.Join(". ", segments) : null;
    }

    // ---- Round-win counters (battle HUD) ----

    private static void PollRoundCounts()
    {
        try
        {
            if (_roundView == null)
            {
                if (_pollCounter % SCAN_INTERVAL != 0) return;
                foreach (var (owner, view) in GuiTextReader.FindGuiViews("BattleAnnounceHud_Round"))
                {
                    _roundView = view;
                    break;
                }
                if (_roundView == null) return;
            }

            // Counters are visible; fighter names sit hidden in the same GUI
            var texts = GuiTextReader.ReadViewTexts(_roundView, null);
            int c1 = -1, c2 = -1;
            foreach (var t in texts)
            {
                if (t.Name == "e_txt_count_1P") int.TryParse(t.Text, out c1);
                else if (t.Name == "e_txt_count_2P") int.TryParse(t.Text, out c2);
            }
            if (c1 < 0 || c2 < 0)
            {
                // Counters gone — battle ended, view may be stale
                _roundView = null;
                return;
            }

            if (_lastCount1P < 0 || c1 < _lastCount1P || c2 < _lastCount2P)
            {
                // First read or counter reset (new match) — sync silently
                _lastCount1P = c1;
                _lastCount2P = c2;
                ReadFighterNames();
                return;
            }
            if (c1 == _lastCount1P && c2 == _lastCount2P) return;

            ReadFighterNames();
            string winner = c1 > _lastCount1P
                ? (_fighterName1P ?? "P1")
                : (_fighterName2P ?? "P2");
            _lastCount1P = c1;
            _lastCount2P = c2;

            // Names + numbers only — language-neutral round result
            string announcement = $"{winner} {c1} - {c2}";
            API.LogInfo($"[SF6Access] Round result: {announcement}");
            ScreenReaderService.Speak(announcement);
        }
        catch
        {
            _roundView = null;
        }
    }

    private static void ReadFighterNames()
    {
        try
        {
            // Tree order of the round HUD's hidden name elements does NOT
            // reliably match the 1P/2P sides (a ranked round announced the
            // wrong winner). The HUD character thumbnails are side-anchored:
            // sort their visible name texts by on-screen position — leftmost
            // is always 1P in SF6.
            foreach (var (owner, view) in GuiTextReader.FindGuiViews("BattleHud_CharaThumbnail"))
            {
                var ordered = GuiTextReader.ReadControlTextsByPosition(view,
                    visibleOnly: true, out bool positionsResolved);
                if (!positionsResolved) break;

                var names = new List<string>();
                foreach (var raw in ordered)
                {
                    string clean = FlowHelper.CleanTags(raw)?.Trim();
                    if (string.IsNullOrEmpty(clean) || names.Contains(clean)) continue;
                    names.Add(clean);
                }
                if (names.Count >= 2)
                {
                    if (_fighterName1P != names[0] || _fighterName2P != names[1])
                        API.LogInfo($"[SF6Access] Fighter sides: 1P={names[0]}, 2P={names[1]}");
                    _fighterName1P = names[0];
                    _fighterName2P = names[1];
                    return;
                }
                break;
            }

            // Fallback: hidden name elements of the round HUD, tree order
            var all = GuiTextReader.ReadControlTexts(_roundView, visibleOnly: false);
            var fallback = new List<string>();
            foreach (var t in all)
            {
                if (t.Name == "e_text_name" && !string.IsNullOrWhiteSpace(t.Text))
                    fallback.Add(t.Text.Trim());
            }
            if (fallback.Count >= 2)
            {
                _fighterName1P = fallback[0];
                _fighterName2P = fallback[1];
            }
        }
        catch { }
    }

    // ---- Match result ----

    private static void PollResultCounter()
    {
        try
        {
            var found = FlowHelper.FindFlowParams(ResultCounterTypes);
            if (!found.TryGetValue(RESULT_COUNTER, out var param))
            {
                _resultAnnounced = false;
                return;
            }
            if (_resultAnnounced) return;

            int team = FlowHelper.ReadIntField(param, "mTeam", 0); // EWinTeam
            if (team <= 0) return; // not decided yet, retry next poll

            int c1 = FlowHelper.ReadIntField(param, "mP1WinCount", -1);
            int c2 = FlowHelper.ReadIntField(param, "mP2WinCount", -1);
            string streak = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(param, "mTextStreak"));

            string winner = team switch
            {
                1 => _fighterName1P ?? "P1",
                2 => _fighterName2P ?? "P2",
                _ => null, // TEAM_DRAW
            };

            var parts = new List<string>();
            if (winner != null) parts.Add(winner);
            if (c1 >= 0 && c2 >= 0) parts.Add($"{c1} - {c2}");
            if (!string.IsNullOrWhiteSpace(streak)) parts.Add(streak.Trim());
            if (parts.Count == 0) return;

            _resultAnnounced = true;
            string announcement = string.Join(". ", parts);
            API.LogInfo($"[SF6Access] Match result: {announcement}");
            ScreenReaderService.Speak(announcement, interrupt: false);
        }
        catch { }
    }

    // ---- Matchmaking notices ----

    private static void PollMatchingNotices()
    {
        try
        {
            // Aggregate texts across ALL same-name GUI instances: the scene
            // keeps ~10 copies of these widgets and the first one found is
            // often an empty duplicate — keeping only it missed the notice
            var partsByOwner = new Dictionary<string, List<string>>();
            foreach (var filter in MatchingOwnerFilters)
            {
                foreach (var (owner, view) in GuiTextReader.FindGuiViews(filter))
                {
                    if (owner == null) continue;
                    if (!partsByOwner.TryGetValue(owner, out var parts))
                        partsByOwner[owner] = parts = new List<string>();
                    if (parts.Count >= 6) continue;

                    foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
                    {
                        if (string.IsNullOrWhiteSpace(t.Text)) continue;
                        string trimmed = t.Text.Trim();
                        if (!parts.Contains(trimmed)) parts.Add(trimmed);
                        if (parts.Count >= 6) break;
                    }
                }
            }

            foreach (var kvp in partsByOwner)
            {
                string text = kvp.Value.Count > 0 ? string.Join(". ", kvp.Value) : null;

                _lastMatchingTexts.TryGetValue(kvp.Key, out var last);
                if (text == last) continue;
                _lastMatchingTexts[kvp.Key] = text;
                if (string.IsNullOrEmpty(text)) continue;
                if (last == null && _pollCounter < SCAN_INTERVAL * 2) continue; // skip startup state

                API.LogInfo($"[SF6Access] Matching notice [{kvp.Key}]: {text}");
                ScreenReaderService.Speak(text, interrupt: false);
            }
        }
        catch { }
    }
}
