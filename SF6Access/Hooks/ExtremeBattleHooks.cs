using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the Extreme Battle "Rules &amp; Regulations" gimmick. Two HUD GUIs:
/// - "ExtremeHud_RuleAnnounce": the rule banner (e_text_rule = name,
///   e_text_desc = "Perform the specified actions first!").
/// - "ExtremeHud_Challange": the objective list — pairs of e_txt_value then
///   e_txt_title in tree order (e.g. "Drive Impact: 2 golpes"). Announced once
///   when the set of objectives appears; the live counters don't re-trigger it.
/// </summary>
public class ExtremeBattleHooks
{
    private const string RULE_GUI = "ExtremeHud_RuleAnnounce";
    private const string CHALLENGE_GUI = "ExtremeHud_Challange"; // game's own spelling

    private static int _pollCounter;
    private const int POLL_INTERVAL = 20;
    private const int SCAN_INTERVAL = 120;

    private static readonly List<(string owner, ManagedObject view)> _ruleViews = new();
    private static readonly List<(string owner, ManagedObject view)> _challengeViews = new();
    private static string _lastRule;
    private static string _lastChallengeKey;
    // Per-objective values, so a step completing (counter changing) is announced
    // as progress instead of staying silent after the first full read.
    private static readonly Dictionary<string, string> _objectiveValues = new();

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] ExtremeBattleHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % SCAN_INTERVAL == 0 || (_ruleViews.Count == 0 && _challengeViews.Count == 0 && _pollCounter % 60 == 0))
        {
            _ruleViews.Clear();
            _ruleViews.AddRange(GuiTextReader.FindGuiViews(RULE_GUI));
            _challengeViews.Clear();
            _challengeViews.AddRange(GuiTextReader.FindGuiViews(CHALLENGE_GUI));
        }

        if (_pollCounter % POLL_INTERVAL != 0) return;
        PollRuleBanner();
        PollChallengeList();
    }

    private static void PollRuleBanner()
    {
        string rule = null, desc = null;
        foreach (var (owner, view) in _ruleViews)
        {
            foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
            {
                if (t.Name == "e_text_rule" && !string.IsNullOrWhiteSpace(t.Text)) rule ??= t.Text.Trim();
                else if (t.Name == "e_text_desc" && !string.IsNullOrWhiteSpace(t.Text)) desc ??= t.Text.Trim();
            }
        }
        if (string.IsNullOrEmpty(rule) && string.IsNullOrEmpty(desc)) { _lastRule = null; return; }

        string announcement = string.IsNullOrEmpty(desc) ? rule
            : string.IsNullOrEmpty(rule) ? desc : $"{rule}. {desc}";
        if (announcement == _lastRule) return;
        _lastRule = announcement;

        API.LogInfo($"[SF6Access] Extreme rule: {announcement}");
        ScreenReaderService.Speak(announcement, interrupt: false);
    }

    private static void PollChallengeList()
    {
        var pairs = new List<string>();
        var titles = new List<string>();
        var values = new Dictionary<string, string>();
        foreach (var (owner, view) in _challengeViews)
        {
            string pendingValue = null;
            foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
            {
                if (string.IsNullOrWhiteSpace(t.Text)) continue;
                if (t.Name == "e_txt_value") pendingValue = t.Text.Trim();
                else if (t.Name == "e_txt_title")
                {
                    string title = t.Text.Trim();
                    titles.Add(title);
                    string value = pendingValue;
                    pairs.Add(value != null ? $"{title}: {value}" : title);
                    if (value != null) values[title] = value;
                    pendingValue = null;
                }
            }
        }
        if (pairs.Count == 0) { _lastChallengeKey = null; _objectiveValues.Clear(); return; }

        string key = string.Join("|", titles);
        if (key != _lastChallengeKey)
        {
            // New objective set: read the whole list once.
            _lastChallengeKey = key;
            _objectiveValues.Clear();
            foreach (var kv in values) _objectiveValues[kv.Key] = kv.Value;

            string announcement = "Objetivos. " + string.Join(". ", pairs);
            API.LogInfo($"[SF6Access] Extreme objectives: {announcement}");
            ScreenReaderService.Speak(announcement, interrupt: false);
            return;
        }

        // Same set: a counter changed = a step progressed — announce just that
        // objective so the player can track remaining progress.
        foreach (var kv in values)
        {
            if (_objectiveValues.TryGetValue(kv.Key, out string old) && old == kv.Value) continue;
            _objectiveValues[kv.Key] = kv.Value;
            string progress = $"{kv.Key}: {kv.Value}";
            API.LogInfo($"[SF6Access] Extreme progress: {progress}");
            ScreenReaderService.Speak(progress, interrupt: false);
        }
    }
}
