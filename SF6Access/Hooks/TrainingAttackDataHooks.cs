using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Announces the Attack Data (combo damage + hit count) when a combo ends in
/// training mode. The end is signalled by the on-screen hit counter
/// (app.UIPartsHitCount): UpdateNumber marks a combo as running and the
/// FADE_OUT animation (or HideCount) marks the true end — a quiet-frames
/// timer alone announced mid-combo during super freezes. Data source:
/// TrainingManager.DisplayFunc._gData.PlayerDatas[0] (prevComboCount /
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

    // app.UIPartsHitCount.ANIM_STATE: FADE_IN, DEFAULT, COUNT_UP, FADE_OUT
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
    private static bool _hudEndPending;

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
                    if ((int)(long)args[2] == ANIM_STATE_FADE_OUT && _comboActive)
                    {
                        _comboActive = false;
                        _hudEndPending = true;
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
                        _hudEndPending = true;
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
            _hudEndPending = false;
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

            // HUD counter faded out — the combo really ended. Announce only
            // when the HUD count matches our tracked player's data (the
            // counter exists per side); on a mismatch leave _dirty set so the
            // quiet-frames fallback picks it up
            if (_hudEndPending)
            {
                _hudEndPending = false;
                if (count > 0 && count == _hudCount)
                {
                    _lastCount = count;
                    _lastDamage = damage;
                    _lastChangeFrame = _pollCounter;
                    _dirty = false;
                    if (!TrainingMenuHooks.IsInTrainingMenu)
                        AnnounceCombo(count, damage);
                    return;
                }
                API.LogInfo($"[SF6Access] HUD combo end mismatch: hud={_hudCount}, data={count}");
            }

            if (count != _lastCount || damage != _lastDamage)
            {
                _lastCount = count;
                _lastDamage = damage;
                _lastChangeFrame = _pollCounter;
                if (!first) _dirty = true; // a combo is in progress
                return;
            }

            // Quiet-frames fallback: silent while the HUD counter is still up
            // (juggle gaps and freezes are not the end) and on stable zeros
            if (_comboActive) return;
            if (!_dirty || _pollCounter - _lastChangeFrame < QUIET_FRAMES) return;
            _dirty = false;
            if (count <= 0 && damage <= 0) return;

            // No announcements while the pause menu is open (stale values)
            if (TrainingMenuHooks.IsInTrainingMenu) return;

            AnnounceCombo(count, damage);
        }
        catch { }
    }

    /// <summary>
    /// Damage first, then hit count, no "Combo" prefix — the tester asked for
    /// the bare numbers, and "hits" is the word he uses in any language.
    /// </summary>
    private static void AnnounceCombo(int count, int damage)
    {
        string announcement = damage > 0
            ? (count > 0 ? $"{damage}. {count} hits" : damage.ToString())
            : $"{count} hits";
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
