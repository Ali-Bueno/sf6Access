using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Announces the Attack Data (combo damage + hit count) when a combo ends in
/// training mode. The end is signalled by the on-screen hit counter
/// (app.UIPartsHitCount): UpdateNumber marks a combo as running and the
/// FADE_OUT animation (or HideCount) marks the true end. A quiet-frames timer
/// is the fallback. Both end signals are additionally held off while the game's
/// own combo counter (app.cTeam.mComboCount) is still above zero, so a long
/// finisher animation is no longer mistaken for the end of the combo. Data
/// source: TrainingManager.DisplayFunc._gData.PlayerDatas[0] (prevComboCount /
/// comboDamage), the same data that feeds the Attack Data panel.
/// </summary>
public class TrainingAttackDataHooks
{
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 120;
    private const int POLL_READ_INTERVAL = 10;

    // Fallback when the HUD hooks miss the end: the combo is over when its
    // numbers stop moving for this many frames. Generous on purpose — super
    // freezes pause the counters for well over a second mid-combo
    private const int QUIET_FRAMES = 100;

    // The HUD also replays FADE_OUT while RESETTING the counter at combo
    // start (which announced the very first hit as a finished combo), so a
    // fade only counts as the end after the numbers stay put this long
    private const int END_CONFIRM_FRAMES = 40;

    // app.UIPartsHitCount.ANIM_STATE: FADE_IN, DEFAULT, COUNT_UP, FADE_OUT
    private const int ANIM_STATE_FADE_IN = 0;
    private const int ANIM_STATE_COUNT_UP = 2;
    private const int ANIM_STATE_FADE_OUT = 3;

    private static ManagedObject _manager;
    private static bool _isActive;

    private static int _lastCount = -1;
    private static int _lastDamage = -1;
    private static int _lastChangeFrame;
    private static bool _dirty;

    // HUD hit-counter state (set from hooks, consumed by the poll)
    private static bool _comboActive;
    private static int _hudCount;
    private static int _hudEndFrame = -1; // frame the counter faded, -1 = no end pending

    [PluginEntryPoint]
    public static void Initialize()
    {
        InstallHudHooks();
        API.LogInfo("[SF6Access] TrainingAttackDataHooks initialized");
    }

    private static void InstallHudHooks()
    {
        try
        {
            var td = TDB.Get().FindType("app.UIPartsHitCount");
            if (td == null)
            {
                API.LogInfo("[SF6Access] UIPartsHitCount type not found, HUD combo hooks skipped");
                return;
            }

            var update = td.GetMethod("UpdateNumber(System.Int32)") ?? td.GetMethod("UpdateNumber");
            if (update != null)
            {
                var hook = update.AddHook(false);
                hook.AddPre(args =>
                {
                    int count = (int)(long)args[2];
                    if (count > 0)
                    {
                        _hudCount = count;
                        _comboActive = true;
                        _hudEndFrame = -1; // the counter is alive — that fade was a reset
                    }
                    return PreHookResult.Continue;
                });
                API.LogInfo("[SF6Access] UIPartsHitCount.UpdateNumber hook installed");
            }

            var anim = td.GetMethod("PlayAnimState(app.UIPartsHitCount.ANIM_STATE)") ?? td.GetMethod("PlayAnimState");
            if (anim != null)
            {
                var hook = anim.AddHook(false);
                hook.AddPre(args =>
                {
                    int state = (int)(long)args[2];
                    if (state == ANIM_STATE_FADE_OUT && _comboActive)
                    {
                        _comboActive = false;
                        _hudEndFrame = _pollCounter;
                    }
                    else if (state == ANIM_STATE_FADE_IN || state == ANIM_STATE_COUNT_UP)
                    {
                        // Counter (re)shown — the combo is running, not ending
                        _comboActive = true;
                        _hudEndFrame = -1;
                    }
                    return PreHookResult.Continue;
                });
                API.LogInfo("[SF6Access] UIPartsHitCount.PlayAnimState hook installed");
            }

            // Backup end signal: some paths hide the counter without fading
            var hide = td.GetMethod("HideCount()") ?? td.GetMethod("HideCount");
            if (hide != null)
            {
                var hook = hide.AddHook(false);
                hook.AddPost((ref ulong retval) =>
                {
                    if (_comboActive)
                    {
                        _comboActive = false;
                        _hudEndFrame = _pollCounter;
                    }
                });
                API.LogInfo("[SF6Access] UIPartsHitCount.HideCount hook installed");
            }
        }
        catch (System.Exception ex)
        {
            API.LogError($"[SF6Access] UIPartsHitCount hooks failed: {ex.Message}");
        }
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (!_isActive)
        {
            if (_pollCounter % POLL_SEARCH_INTERVAL != 0) return;
            try { _manager = API.GetManagedSingleton("app.training.TrainingManager"); }
            catch { _manager = null; }
            if (_manager == null) return;

            _isActive = true;
            _lastCount = -1;
            _lastDamage = -1;
            _dirty = false;
            _comboActive = false;
            _hudEndFrame = -1;
            API.LogInfo("[SF6Access] Training attack data watcher active");
            return;
        }

        if (_pollCounter % POLL_READ_INTERVAL != 0) return;

        try
        {
            var playerData = FindPlayerLocalData();
            if (playerData == null)
            {
                if (_pollCounter % POLL_SEARCH_INTERVAL == 0 &&
                    API.GetManagedSingleton("app.training.TrainingManager") == null)
                {
                    _isActive = false;
                    _manager = null;
                    API.LogInfo("[SF6Access] Training attack data watcher ended");
                }
                return;
            }

            int count = FlowHelper.ReadIntField(playerData, "prevComboCount", -1);
            int damage = FlowHelper.ReadIntField(playerData, "comboDamage", -1);
            if (count < 0 && damage < 0) return;

            bool first = _lastCount == -1 && _lastDamage == -1;

            if (count != _lastCount || damage != _lastDamage)
            {
                _lastCount = count;
                _lastDamage = damage;
                _lastChangeFrame = _pollCounter;
                if (!first) _dirty = true; // a combo is in progress
                // Numbers moved after a fade — it wasn't the end (or they
                // were still settling); restart the confirmation window
                if (_hudEndFrame >= 0) _hudEndFrame = _pollCounter;
                return;
            }

            // HUD counter faded and the numbers stayed put — the combo really
            // ended. The display data is the panel's own latched result, so it
            // is announced as-is: the old equality gate against the HUD count
            // (count == _hudCount) silently DROPPED combos whenever the two
            // sources disagreed by a hit (multi-hit supers advanced one source
            // before the other), which under-reported damage to the player.
            if (_hudEndFrame >= 0)
            {
                if (_pollCounter - _hudEndFrame < END_CONFIRM_FRAMES) return;
                // The game's own combo counter is still up — a long finisher is
                // still part of the combo; wait for it to truly end
                if (TeamComboStillActive()) return;
                _hudEndFrame = -1;
                if (_dirty && count > 0)
                {
                    _dirty = false;
                    _lastChangeFrame = _pollCounter;
                    if (count != _hudCount)
                        API.LogInfo($"[SF6Access] HUD/data combo count differ (announcing data): hud={_hudCount}, data={count}");
                    if (!TrainingMenuHooks.IsInTrainingMenu)
                        AnnounceCombo(count, damage);
                    return;
                }
            }

            // Quiet-frames fallback: silent while the HUD counter is still up
            // (juggle gaps and freezes are not the end) and on stable zeros
            if (_comboActive) return;
            if (!_dirty || _pollCounter - _lastChangeFrame < QUIET_FRAMES) return;
            // Still combatting: the game combo counter has not cleared yet
            if (TeamComboStillActive()) return;
            _dirty = false;
            if (count <= 0 && damage <= 0) return;

            // No announcements while the pause menu is open (stale values)
            if (TrainingMenuHooks.IsInTrainingMenu) return;

            AnnounceCombo(count, damage);
        }
        catch { }
    }

    /// <summary>
    /// Whether the game's own combo counter (app.cTeam.mComboCount, reached via
    /// the player shells) is still above zero — i.e. a combo is genuinely in
    /// progress. Holds the end-detection off through long finisher animations.
    /// Returns false (does not block) when the team data can't be read, so a
    /// broken path falls back to the timer/HUD heuristics rather than going mute.
    /// </summary>
    private static bool TeamComboStillActive()
    {
        try
        {
            var displayFunc = FlowHelper.GetObjectField(_manager, "DisplayFunc");
            var gameData = FlowHelper.GetObjectField(displayFunc, "_gData");
            var players = FlowHelper.GetObjectField(gameData, "PlayerDatas");
            if (players == null) return false;

            int best = 0;
            for (int i = 0; i < 2; i++)
            {
                var pl = FlowHelper.GetListItem(players, i);
                var shell = FlowHelper.GetObjectField(pl, "shell");
                var team = ComboTracker.TeamOf(shell);
                if (team == null) continue;
                int c = ComboTracker.CountOf(team, out int _d);
                if (c > best) best = c;
            }
            return best > 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Damage first, then hit count, no "Combo" prefix — the tester asked for
    /// the bare numbers, and "hits" is the word he uses in any language.
    /// </summary>
    private static void AnnounceCombo(int count, int damage)
    {
        // Combo trials show their own recipe/progress feedback — the combo damage
        // readout is noise there and the tester asked to silence it.
        if (ComboTrialHooks.IsActive) return;

        // Respect the in-game toggle: the "Attack Data" menu item is the master
        // toggle for the whole panel (Is_AttackAllView); the Is_DS_*_View flags
        // are sub-views that stay on. The combo data stays populated even when
        // the panel is hidden, so the setting is the reliable gate.
        var ds = FlowHelper.GetTrainingDisplaySetting();
        if (ds != null && !FlowHelper.ReadBoolField(ds, "Is_AttackAllView")) return;

        string hits = LocalizedText.Hits();
        string announcement = damage > 0
            ? (count > 0 ? $"{damage}. {count} {hits}" : damage.ToString())
            : $"{count} {hits}";
        API.LogInfo($"[SF6Access] Attack data: {announcement}");
        ScreenReaderService.Speak(announcement, interrupt: false);
    }

    /// <summary>P1's display-state record (combo counters live there).</summary>
    private static ManagedObject FindPlayerLocalData()
    {
        var displayFunc = FlowHelper.GetObjectField(_manager, "DisplayFunc");
        var gameData = FlowHelper.GetObjectField(displayFunc, "_gData");
        var players = FlowHelper.GetObjectField(gameData, "PlayerDatas");
        return FlowHelper.GetListItem(players, 0);
    }
}
