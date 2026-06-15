using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for combo trial gameplay (app.esports.UI11439.Param — the
/// recipe panel). Reads the trial title and the combo recipe when the trial
/// starts, and re-reads the recipe every time an attempt ends (progress
/// returns to zero), succeeded or not. Recipe steps render as inline icon
/// tags — read raw and converted via SpeakableIcons.
/// </summary>
public class ComboTrialHooks
{
    private const string PARAM_TYPE = "app.esports.UI11439.Param";

    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 10;

    private static ManagedObject _param;
    private static int _lastProgress;
    private static bool _lastFailed;
    private static string _lastRecipe;
    private static string _lastTitle;

    // The recipe rows go blank during the reset animation — retry the read
    // until they come back instead of announcing an empty recipe
    private static bool _pendingAnnounce;
    private static bool _pendingIncludeTitle;
    private static int _pendingSinceFrame;
    private const int PENDING_TIMEOUT_FRAMES = 240;

    // Attempt-result overlay tracking + cooldown so a trial-swap announce
    // isn't followed by a redundant re-read
    private static bool _announceFlowWasActive;
    private static int _lastAnnounceFrame = -10000;
    private const int ANNOUNCE_COOLDOWN_FRAMES = 180;

    // Game-side attempt events from app.FBattleMission (the trial judge).
    // The recipe panel shows NO change when an attempt never progresses
    // (wrong combo, dropped first hit) — the judge is the only signal there.
    // ResetProgress fires at every real attempt boundary (success, fail,
    // wrong combo) and never mid-combo — it's the primary trigger.
    private static volatile bool _gameResetEvent;
    private static volatile int _lastHitFrame = -1;
    private static volatile bool _activitySinceAnnounce;

    // Hold re-reads until hits stop landing: the judge marks success the
    // instant the last step is validated, while the final special move is
    // still animating — speaking there talks over the end of the combo.
    // Success needs a longer window: a finisher's last HIT can land 2s
    // before its animation ends (Beginner 5 read during the special)
    private const int QUIET_FRAMES = 45;
    private const int QUIET_FRAMES_SUCCESS = 120;
    private static int _pendingQuietFrames = QUIET_FRAMES;

    [PluginEntryPoint]
    public static void Initialize()
    {
        InstallBattleMissionHooks();
        API.LogInfo("[SF6Access] ComboTrialHooks initialized");
    }

    private static void InstallBattleMissionHooks()
    {
        try
        {
            var td = TDB.Get().FindType("app.FBattleMission");
            if (td == null)
            {
                API.LogInfo("[SF6Access] FBattleMission type not found");
                return;
            }

            var reset = td.GetMethod("ResetProgress(nBattle.cPlayer)") ?? td.GetMethod("ResetProgress");
            if (reset != null)
            {
                var hook = reset.AddHook(false);
                hook.AddPost((ref ulong retval) =>
                {
                    if (!_isActive) return;
                    API.LogInfo("[SF6Access] Combo trial judge ResetProgress fired");
                    _gameResetEvent = true;
                });
                API.LogInfo("[SF6Access] FBattleMission.ResetProgress hook installed");
            }

            var onAttack = td.GetMethod("TrialOnAttack(nBattle.cWork, nBattle.cPlayer)") ?? td.GetMethod("TrialOnAttack");
            if (onAttack != null)
            {
                var hook = onAttack.AddHook(false);
                hook.AddPre(args =>
                {
                    if (_isActive)
                    {
                        // Cache both fighters' teams so the poll can read the live
                        // combo counter (args[2]=attacker cWork, args[3]=defender
                        // cPlayer) and hold re-reads until the combo really ends.
                        try
                        {
                            ComboTracker.NoteTeams(
                                ManagedObject.ToManagedObject(args[2]),
                                ManagedObject.ToManagedObject(args[3]));
                        }
                        catch { }
                    }
                    return PreHookResult.Continue;
                });
                hook.AddPost((ref ulong retval) =>
                {
                    if (!_isActive) return;
                    _lastHitFrame = _pollCounter;
                    _activitySinceAnnounce = true;
                });
                API.LogInfo("[SF6Access] FBattleMission.TrialOnAttack hook installed");
            }
        }
        catch (System.Exception ex)
        {
            API.LogError($"[SF6Access] FBattleMission hooks failed: {ex.Message}");
        }
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (!_isActive)
        {
            if (_pollCounter % POLL_SEARCH_INTERVAL != 0) return;
            _param = FlowHelper.FindFlowParam(PARAM_TYPE);
            if (_param == null) return;

            _isActive = true;
            _lastProgress = 0;
            _lastFailed = false;
            _lastRecipe = null;
            _lastTitle = null;
            ResetAttemptEvents();
            API.LogInfo("[SF6Access] Combo trial active");
            RequestAnnounce(includeTitle: true);
            return;
        }

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            // Each trial creates a NEW param instance — a stale reference kept
            // reading the previous trial (second trial was never announced)
            var current = FlowHelper.FindFlowParam(PARAM_TYPE);
            if (current == null)
            {
                _isActive = false;
                _param = null;
                ComboTracker.Clear();
                API.LogInfo("[SF6Access] Combo trial ended");
                return;
            }

            bool swapped;
            try { swapped = current.GetAddress() != _param.GetAddress(); }
            catch { swapped = true; }
            if (swapped)
            {
                _param = current;
                _lastProgress = 0;
                _lastFailed = false;
                _lastTitle = null;
                ResetAttemptEvents();
                API.LogInfo("[SF6Access] Combo trial param swapped (next trial)");
                RequestAnnounce(includeTitle: true);
                return;
            }
        }

        if (_pollCounter % POLL_READ_INTERVAL != 0) return;

        // The attempt-result overlay closing marks the end of an attempt
        // (success banners and fail banners both use it) — more reliable than
        // the progress counter, which a step-zero fail never moves
        bool announceFlow = FlowTrackerHooks.IsFlowActive("FGTutorialBattleAnnounce");
        if (_announceFlowWasActive && !announceFlow)
            TryRequestAttemptAnnounce("overlay closed");
        _announceFlowWasActive = announceFlow;

        // Mid-combo fails don't show the overlay or move the progress counter:
        // the recipe rows' PlayStates (DEFAULT/CLEARED/CURRENT/FAILED) are the
        // ground truth — a FAILED state or a reset back to all-DEFAULT after
        // progress means the attempt ended
        PollRowStates();

        // The param persists between trials: a title change means the next
        // trial loaded — announce it fully (the second trial was silent)
        string title = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(_param, "TextTitle"));
        if (!string.IsNullOrEmpty(title) && _lastTitle != null && title != _lastTitle)
        {
            _lastTitle = title;
            _lastProgress = 0;
            _lastFailed = false;
            RequestAnnounce(includeTitle: true);
        }
        if (_lastTitle == null && !string.IsNullOrEmpty(title)) _lastTitle = title;

        // Attempt finished (success or fail): progress drops back (fail sets
        // it to -1) or the fail flag rises — queue a recipe re-read
        int progress = FlowHelper.ReadIntField(_param, "CurrentProgressNo", 0);
        bool failed = FlowHelper.ReadBoolField(_param, "IsFailedProgress");

        bool attemptEnded = (progress < _lastProgress) || (failed && !_lastFailed);
        if (progress != _lastProgress)
            API.LogInfo($"[SF6Access] Combo trial progress: {progress} (failed={failed})");
        if (progress > 0) _activitySinceAnnounce = true;

        _lastProgress = progress;
        _lastFailed = failed;

        if (attemptEnded)
        {
            bool isFail = failed || _rowsHadFailed;
            TryRequestAttemptAnnounce("progress dropped or fail flag",
                isFail ? QUIET_FRAMES : QUIET_FRAMES_SUCCESS);
        }

        // The judge reset the attempt. Only re-read when something actually
        // happened since the last announce (an idle reset can't loop), and
        // keep the event pending while the cooldown blocks it — a reset right
        // after the trial-start read was getting silently lost
        if (_gameResetEvent)
        {
            if (!_activitySinceAnnounce)
                _gameResetEvent = false;
            else if (TryRequestAttemptAnnounce("judge reset"))
                _gameResetEvent = false;
        }

        if (_pendingAnnounce)
            TryAnnounce();
    }

    private static void ResetAttemptEvents()
    {
        _gameResetEvent = false;
        _lastHitFrame = -1;
        _activitySinceAnnounce = false;
    }

    /// <summary>Attempt-end re-read, deduplicated across all detection paths
    /// (row states, progress counter, overlay, judge events) via one cooldown.</summary>
    private static bool TryRequestAttemptAnnounce(string reason, int quietFrames = QUIET_FRAMES)
    {
        // The example-demo viewer (UI11440) lands hits and resets the judge
        // every loop — no re-reads while it plays. Closing it fires one final
        // judge reset, which re-reads the recipe exactly once.
        if (FlowTrackerHooks.IsFlowActive("UI11440"))
            return false;

        if (_pendingAnnounce || _pollCounter - _lastAnnounceFrame <= ANNOUNCE_COOLDOWN_FRAMES)
            return false;
        API.LogInfo($"[SF6Access] Combo trial attempt ended ({reason})");
        _pendingQuietFrames = quietFrames;
        RequestAnnounce(includeTitle: false);
        return true;
    }

    private static bool _rowsHadProgress;
    private static bool _rowsHadFailed;
    private static string _lastStateSignature;

    // Exact row-state names from app.esports.UI11439.ItemPlayState statics
    private static string _stateDefault;
    private static string _stateFailed;
    private static bool _statesCached;

    private static void CacheStateNames()
    {
        if (_statesCached) return;
        _statesCached = true;
        try
        {
            var td = TDB.Get().FindType("app.esports.UI11439.ItemPlayState");
            _stateDefault = td?.GetField("Default")?.GetDataBoxed(typeof(string), 0, true) as string;
            _stateFailed = td?.GetField("Failed")?.GetDataBoxed(typeof(string), 0, true) as string;
            API.LogInfo($"[SF6Access] Combo trial states: default='{_stateDefault}', failed='{_stateFailed}'");
        }
        catch { }
    }

    private static void PollRowStates()
    {
        try
        {
            CacheStateNames();

            var list = FlowHelper.GetObjectField(_param, "PartsScrollListRecipe");
            var control = FlowHelper.GetObjectField(list, "Control")
                ?? FlowHelper.Call(list, "get_Control") as ManagedObject;
            if (control == null) return;

            var states = new System.Collections.Generic.List<string>();
            GuiTextReader.ReadPlayStates(control, states);
            if (states.Count == 0) return;

            // Log the state signature whenever it changes — ground truth for
            // tuning the fail/reset detection
            string signature = string.Join("|", states);
            if (signature != _lastStateSignature)
            {
                _lastStateSignature = signature;
                API.LogInfo($"[SF6Access] Combo trial row states: {signature}");
            }

            bool anyProgress = false;
            bool anyFailed = false;
            foreach (var state in states)
            {
                bool isFailed = (_stateFailed != null && state == _stateFailed) ||
                    state.Contains("FAIL", System.StringComparison.OrdinalIgnoreCase) ||
                    state.Contains("MISS", System.StringComparison.OrdinalIgnoreCase);
                if (isFailed) anyFailed = true;

                bool isDefault = (_stateDefault != null && state == _stateDefault) ||
                    state.Contains("DEFAULT", System.StringComparison.OrdinalIgnoreCase);
                if (!isDefault && !isFailed) anyProgress = true;
            }

            bool failedNow = anyFailed && !_rowsHadFailed;
            bool resetNow = _rowsHadProgress && !anyProgress && !anyFailed;
            _rowsHadFailed = anyFailed;
            _rowsHadProgress = anyProgress;

            if (failedNow || resetNow)
                TryRequestAttemptAnnounce($"rows failed={failedNow}, reset={resetNow}");
        }
        catch { }
    }

    private static void RequestAnnounce(bool includeTitle)
    {
        _pendingAnnounce = true;
        _pendingIncludeTitle = includeTitle;
        _pendingSinceFrame = _pollCounter;
        TryAnnounce();
    }

    private static void TryAnnounce()
    {
        try
        {
            bool timedOut = _pollCounter - _pendingSinceFrame > PENDING_TIMEOUT_FRAMES;

            // Never re-read the recipe while a combo is still live: the game keeps
            // its combo counter (cTeam.mComboCount) up through long finisher
            // animations, so this waits for the real end instead of talking over
            // a combo the player hasn't finished yet.
            if (!_pendingIncludeTitle && ComboTracker.IsComboActive())
            {
                if (timedOut) _pendingAnnounce = false;
                return;
            }

            // Attempt-end re-reads wait for the combo to actually finish:
            // no hits landed for QUIET_FRAMES. If the player just keeps
            // attacking past the timeout, drop the re-read instead of talking
            // over them — the next attempt boundary re-triggers it anyway.
            if (!_pendingIncludeTitle &&
                _lastHitFrame >= 0 && _pollCounter - _lastHitFrame <= _pendingQuietFrames)
            {
                if (timedOut) _pendingAnnounce = false;
                return;
            }

            string recipe = ReadRecipeText();
            if (string.IsNullOrEmpty(recipe) && !timedOut)
                return; // rows blank during the reset animation — retry next poll

            _pendingAnnounce = false;

            var parts = new List<string>();
            if (_pendingIncludeTitle)
            {
                string title = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(_param, "TextTitle"));
                if (!string.IsNullOrEmpty(title)) parts.Add(title);
            }
            if (!string.IsNullOrEmpty(recipe)) parts.Add(recipe);
            if (parts.Count == 0) return;

            string announcement = string.Join(". ", parts);
            _lastRecipe = recipe;
            _lastAnnounceFrame = _pollCounter;
            _activitySinceAnnounce = false;

            // Attempt-end re-reads interrupt: the player just failed and needs
            // the recipe NOW. Trial-start reads queue after banner/menu speech.
            API.LogInfo($"[SF6Access] Combo trial recipe: {announcement}");
            ScreenReaderService.Speak(announcement, interrupt: !_pendingIncludeTitle);
        }
        catch { _pendingAnnounce = false; }
    }

    private static bool? _lastOrderMode;

    // Arrow separators and sizing placeholders that pollute the recipe rows
    private static bool IsNoiseToken(string step)
    {
        if (step.Equals("next", System.StringComparison.OrdinalIgnoreCase)) return true;
        bool allM = true;
        foreach (char c in step)
        {
            if (c != 'M' && c != 'm') { allM = false; break; }
        }
        return allM;
    }

    /// <summary>The recipe steps: raw texts (icon tags) under the recipe list.</summary>
    private static string ReadRecipeText()
    {
        var list = FlowHelper.GetObjectField(_param, "PartsScrollListRecipe");
        var control = FlowHelper.GetObjectField(list, "Control")
            ?? FlowHelper.Call(list, "get_Control") as ManagedObject;
        if (control == null) return null;

        // Position-sorted: tree order reads recipes bottom-to-top
        // (Beginner 6/7/8 all announced reversed)
        var raws = GuiTextReader.ReadControlTextsByPosition(control, true, out bool positioned);
        if (raws.Count == 0)
            raws = GuiTextReader.ReadControlTextsByPosition(control, false, out positioned);

        // No positions available (get_Position yields nothing on these
        // elements): every observed tree-order recipe was EXACTLY reversed,
        // so flip it. Rows map 1:1 to texts, so this reverses rows only.
        if (!positioned) raws.Reverse();

        if (_lastOrderMode != positioned)
        {
            _lastOrderMode = positioned;
            API.LogInfo($"[SF6Access] Combo trial recipe order: {(positioned ? "positions" : "reversed tree")}");
        }

        var parts = new List<string>();
        foreach (var raw in raws)
        {
            string step = FlowHelper.CleanTags(FlowHelper.SpeakableIcons(raw));
            if (string.IsNullOrWhiteSpace(step)) continue;
            step = step.Replace('\n', ' ').Trim();

            // Strip in-row noise: "next" arrow labels and the single-letter
            // strength shorthand (M/L/H) that duplicates the icon name
            step = System.Text.RegularExpressions.Regex.Replace(step, @"\bnext\b", ",",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            step = System.Text.RegularExpressions.Regex.Replace(step, @"\b[MLH]\b", "");
            step = System.Text.RegularExpressions.Regex.Replace(step, @"\s+", " ").Trim(' ', ',');

            if (string.IsNullOrWhiteSpace(step) || IsNoiseToken(step)) continue;
            parts.Add(step);
            if (parts.Count >= 20) break;
        }
        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }
}
