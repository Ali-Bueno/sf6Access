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

    // Opponent connection (WiFi/wired) announced once per match, at the moment
    // the battle profiles first become available (matchmaking confirmed)
    private static bool _connectionAnnounced;

    // "Opponent found! Accept the match?" confirm screen (UIWidget_MatchingSelect):
    // the opponent's signal bars + WiFi/wired icon are set via SetSignalStrength,
    // while the prompt + opponent name + any LP/rank are via.gui.Texts in the
    // widget's contents. Capture the widget instance + signal args in the hook,
    // then walk the whole widget on a later update (off the hook thread). The
    // texts can load a beat after the signal, so retry a few frames before
    // giving up. Antenna.Loading = -1 is the not-yet-measured placeholder.
    private static ManagedObject _signalWidget;
    private static int _pendingAntenna = int.MinValue;
    private static int _pendingInterface;
    private static int _signalReadDelay;
    private static int _signalRetries;
    private const int SIGNAL_READ_DELAY_FRAMES = 2;
    private static string _lastSignalText;

    [PluginEntryPoint]
    public static void Initialize()
    {
        HookMatchingSelectSignal();
        API.LogInfo("[SF6Access] BattleInfoHooks initialized");
    }

    /// <summary>
    /// Hook UIWidget_MatchingSelect.SetSignalStrength(Antenna, InterfaceType) —
    /// fired when the "opponent found! Accept the match?" confirm screen shows
    /// the opponent's connection quality (signal bars) and connection type
    /// (WiFi/wired), both rendered as icons with no readable text.
    /// </summary>
    private static void HookMatchingSelectSignal()
    {
        try
        {
            var td = TDB.Get().FindType("app.UIWidget_MatchingSelect");
            var method = td?.GetMethod("SetSignalStrength") ??
                         td?.GetMethod("SetSignalStrength(app.network.api.Enum.Antenna, via.network.core.InterfaceType)");
            if (method == null)
            {
                API.LogInfo("[SF6Access] UIWidget_MatchingSelect.SetSignalStrength not found, opponent connection skipped");
                return;
            }

            var hook = method.AddHook(false);
            hook.AddPre(args =>
            {
                try
                {
                    // args[1] = this (widget), args[2] = antenna, args[3] = interfaceType
                    _signalWidget = ManagedObject.ToManagedObject(args[1]);
                    _pendingAntenna = (int)(long)args[2];
                    _pendingInterface = (int)(long)args[3];
                    _signalReadDelay = SIGNAL_READ_DELAY_FRAMES;
                    _signalRetries = 6;
                }
                catch { }
                return PreHookResult.Continue;
            });
            API.LogInfo("[SF6Access] Matching opponent connection hook installed");
        }
        catch (System.Exception ex)
        {
            API.LogError($"[SF6Access] SetSignalStrength hook failed: {ex.Message}");
        }
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        if (_signalWidget != null && --_signalReadDelay <= 0) AnnounceOpponentWidget();

        _pollCounter++;
        if (_pollCounter % POLL_INTERVAL == 0)
        {
            PollVsScreen();
            PollRoundCounts();
            PollResultCounter();
            PollMatchConnection();
        }
        if (_pollCounter % SCAN_INTERVAL == 0)
            PollMatchingNotices();
    }

    // ---- Opponent-found confirm screen connection ----

    /// <summary>
    /// Speak the whole "opponent found! Accept the match?" widget captured by the
    /// SetSignalStrength hook: the prompt + opponent name + any LP/rank (via.gui
    /// texts walked from the widget contents), then the decoded connection —
    /// connection type (WiFi/wired) + signal bars (Antenna 0-5, Loading = -1
    /// skipped). Retries a few frames while the texts/signal finish loading, then
    /// announces once per distinct result (the setup calls repeat every frame).
    /// </summary>
    private static void AnnounceOpponentWidget()
    {
        var widget = _signalWidget;
        int antenna = _pendingAntenna;

        // Walk the widget's contents for all readable text (prompt, name, rank).
        var textParts = new List<string>();
        try
        {
            foreach (var field in new[] { "mCtrlContents", "mCtrlMatchiConfilm" })
            {
                var ctrl = FlowHelper.GetObjectField(widget, field);
                foreach (var t in GuiTextReader.ReadControlTexts(ctrl))
                {
                    if (string.IsNullOrWhiteSpace(t.Text)) continue;
                    string trimmed = t.Text.Trim();
                    if (!textParts.Contains(trimmed)) textParts.Add(trimmed);
                }
            }
        }
        catch { }

        // Wait for the texts and a measured signal to load before announcing.
        bool ready = textParts.Count > 0 && antenna >= 0;
        if (!ready && --_signalRetries > 0)
        {
            _signalReadDelay = SIGNAL_READ_DELAY_FRAMES;
            return;
        }
        _signalWidget = null; // done with this show (succeeded or gave up)

        try
        {
            var parts = new List<string>(textParts);
            if (antenna >= 0)
            {
                var conn = new List<string>();
                string type = InterfaceLabel(_pendingInterface);
                if (type != null) conn.Add(type);
                conn.Add($"signal {antenna} of 5");
                parts.Add(string.Join(", ", conn));
            }
            if (parts.Count == 0) { _lastSignalText = null; return; }

            string text = string.Join(". ", parts);
            if (text == _lastSignalText) return;
            _lastSignalText = text;

            API.LogInfo($"[SF6Access] Opponent confirm: {text}");
            ScreenReaderService.Speak(text, interrupt: false);
        }
        catch { }
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

            // Ranked-match rank tier + division (the on-screen rank is an icon,
            // so there is no text to read — derive it from mOnlineLP). Empty for
            // offline / casual play, where the league point struct stays zero.
            string rank = ReadRank(pd);
            if (!string.IsNullOrEmpty(rank)) bits.Add(rank);

            // Connection type (WiFi / wired) per side, online only
            string conn = ConnectionLabel(i);
            if (!string.IsNullOrEmpty(conn)) bits.Add(conn);

            if (bits.Count == 0) return null; // player data not filled yet
            sides.Add(string.Join(" ", bits));
        }

        string stage = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(param, "mTextStageName"))
            ?? FlowHelper.ReadStringField(param, "mStageCityName");

        string text = $"{sides[0]} vs {sides[1]}";
        if (!string.IsNullOrWhiteSpace(stage)) text += $". {stage.Trim()}";
        return text;
    }

    // SF6 rank tiers, five divisions each, then Master (rated separately).
    private static readonly string[] RankTiers =
        { "Rookie", "Iron", "Bronze", "Silver", "Gold", "Platinum", "Diamond" };

    /// <summary>
    /// Readable rank ("Platinum 5", "Master 1832") from a VSInfo player's
    /// mOnlineLP (app.network.MsgLeaguePoint). Null when not a ranked player
    /// (the struct is all-zero for casual / offline). UNTESTED: the league_rank
    /// → tier/division mapping needs confirmation on a real ranked face-off.
    /// </summary>
    private static string ReadRank(ManagedObject pd)
    {
        try
        {
            var lp = FlowHelper.GetObjectField(pd, "mOnlineLP");
            if (lp == null) return null;

            int masterRating = FlowHelper.ReadIntField(lp, "master_rating", 0);
            if (masterRating > 0) return $"Master {masterRating}";

            int rank = FlowHelper.ReadIntField(lp, "league_rank", 0);
            if (rank <= 0) return null;

            int idx = rank - 1;
            int tier = idx / 5;
            int division = idx % 5 + 1;
            if (tier >= RankTiers.Length) return "Master";
            return $"{RankTiers[tier]} {division}";
        }
        catch { return null; }
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

            // Fallback: hidden name elements of the round HUD, tree order. The
            // HUD lists 2P FIRST, then 1P — confirmed in every dump by
            // BattleHud_PlayerName ("Jogador 2" then "Jogador 1"), PlayerArrow
            // (J2 then J1) and the vs-CPU dump where "Você" (the human = 1P) is
            // the SECOND entry. The old code assigned fallback[0] to 1P, which
            // reversed the round winner (P1 wins → announced P2 / the CPU).
            var all = GuiTextReader.ReadControlTexts(_roundView, visibleOnly: false);
            var fallback = new List<string>();
            foreach (var t in all)
            {
                if (t.Name == "e_text_name" && !string.IsNullOrWhiteSpace(t.Text))
                    fallback.Add(t.Text.Trim());
            }
            if (fallback.Count >= 2)
            {
                _fighterName2P = fallback[0];
                _fighterName1P = fallback[1];
                API.LogInfo($"[SF6Access] Fighter sides (fallback): 1P={fallback[1]}, 2P={fallback[0]}");
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

    // ---- Connection type (WiFi / wired) ----

    // app.network.core.InterfaceType: 0 Unknown, 1 Wireless, 2 Wired
    private static string InterfaceLabel(int type) => type switch
    {
        1 => "WiFi",
        2 => "Cable",
        _ => null,
    };

    /// <summary>
    /// Connection label for a battle side (team 0 = P1, 1 = P2) from the
    /// fighter's online profile (FighterProfileDesc.InterfaceType). Source:
    /// the current BattleDesc held by the commentator info holder, falling back
    /// to the character-select flow's FighterDescArray. Null offline / when the
    /// profile isn't a remote player yet.
    /// </summary>
    private static string ConnectionLabel(int team)
    {
        var profile = GetTeamProfile(team);
        if (profile == null) return null;
        try
        {
            int type = FlowHelper.ReadIntField(profile, "InterfaceType", -1);
            if (type < 0)
            {
                var r = FlowHelper.Call(profile, "get_InterfaceType");
                if (r != null) type = System.Convert.ToInt32(r);
            }
            return InterfaceLabel(type);
        }
        catch { return null; }
    }

    private static ManagedObject GetTeamProfile(int team)
    {
        // 1. Current battle's FighterDesc.Profile via the commentator holder
        try
        {
            var holder = API.GetManagedSingleton("app.commentator.bCommentatorGlobalInfoHolder");
            var battleDesc = FlowHelper.Call(holder, "get_CurrentBattleDesc") as ManagedObject;
            var fighter = FlowHelper.Call(battleDesc, "getFighter", team, 0) as ManagedObject;
            var profile = FlowHelper.Call(fighter, "get_Profile") as ManagedObject;
            if (profile != null) return profile;
        }
        catch { }

        // 2. Character-select flow's FighterDescArray[team].Profile
        try
        {
            var flow = FlowHelper.FindFlowParam("app.UIFlowUI10501.FlowParam");
            var arr = FlowHelper.GetObjectField(flow, "FighterDescArray");
            var fighter = FlowHelper.GetListItem(arr, team);
            var profile = FlowHelper.Call(fighter, "get_Profile") as ManagedObject;
            if (profile != null) return profile;
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Announce each side's connection once when the match profiles first load
    /// (right after matchmaking, before the VS splash). The VS screen repeats it
    /// inline with the names; this earlier call is for players who want to know
    /// the opponent's connection as soon as the match is set.
    /// </summary>
    private static void PollMatchConnection()
    {
        try
        {
            string c1 = ConnectionLabel(0);
            string c2 = ConnectionLabel(1);
            if (c1 == null && c2 == null)
            {
                _connectionAnnounced = false; // no battle profiles → reset for next match
                return;
            }
            if (_connectionAnnounced) return;
            _connectionAnnounced = true;

            var parts = new List<string>();
            if (c1 != null) parts.Add($"P1 {c1}");
            if (c2 != null) parts.Add($"P2 {c2}");
            string text = string.Join(". ", parts);
            API.LogInfo($"[SF6Access] Match connection: {text}");
            ScreenReaderService.Speak(text, interrupt: false);
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

                // The "opponent found! Confirm" prompt (MatchingStandby) is
                // time-sensitive and needs an input — speak it immediately
                // instead of queuing it behind "Searching for opponent..."
                bool isConfirmPrompt = kvp.Key.IndexOf("MatchingStandby",
                    System.StringComparison.OrdinalIgnoreCase) >= 0;

                API.LogInfo($"[SF6Access] Matching notice [{kvp.Key}]: {text}");
                ScreenReaderService.Speak(text, interrupt: isConfirmPrompt);
            }
        }
        catch { }
    }
}
