using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Announces the training Attack Data once per hit or combo, with the numbers
/// the panel shows when the exchange is over. The end is the game's own combo
/// counter (app.cTeam.mComboCount, per side via ComboTracker.BattleTeam)
/// dropping back to zero: it stays up through hitstun, juggles and long
/// finishers, and rises and falls for every single hit — so repeated identical
/// hits each get a readout. (The previous detection waited for the panel numbers
/// to CHANGE and then go quiet, which never fired for identical or rapid single
/// hits.) Values: TrainingManager.DisplayFunc._gData.PlayerDatas[side], the
/// panel's own latched record: comboDamage, prevComboCount, correction (the
/// damage scaling percent shown as "270 (30%)") and attackInfo (attack type).
/// </summary>
public class TrainingAttackDataHooks
{
    private const int POLL_SEARCH_INTERVAL = 120;
    // Hitstun of the lightest hit lasts far longer than this
    private const int POLL_READ_INTERVAL = 2;
    // Frames between the counter clearing and reading the panel record, so the
    // last hit's numbers have latched
    private const int SETTLE_FRAMES = 6;
    private const int SIDES = 2;
    // Whether a combo counts on the attacker's own cTeam (vs the defender's)
    private const bool COUNTER_ON_ATTACKER = true;
    // PlayerDatas[0] / the panel's left column is the training player (P1)
    private const int OWN_SIDE = 0;
    // Damage scaling is a percent; a full-damage hit reads 100
    private const int NO_SCALING = 100;

    private static int _pollCounter;
    private static ManagedObject _manager;
    private static bool _isActive;

    private static int _attacker = -1;  // side whose hit/combo is running, -1 = none
    private static int _endFrame = -1;  // frame the counter cleared, -1 = no end pending

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
            _attacker = -1;
            _endFrame = -1;
            API.LogInfo("[SF6Access] Training attack data watcher active");
            return;
        }

        if (_pollCounter % POLL_READ_INTERVAL != 0) return;

        try
        {
            var players = PlayerDatas();
            if (players == null)
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

            int attacker = RunningComboSide();
            if (attacker >= 0)
            {
                _attacker = attacker;
                _endFrame = -1;
                return;
            }

            if (_attacker < 0) return;
            if (_endFrame < 0) _endFrame = _pollCounter;
            if (_pollCounter - _endFrame < SETTLE_FRAMES) return;

            int side = _attacker;
            _attacker = -1;
            _endFrame = -1;
            if (!TrainingMenuHooks.IsInTrainingMenu)
                Announce(FlowHelper.GetListItem(players, side), side);
        }
        catch { }
    }

    /// <summary>The attacking side of the hit or combo in progress, -1 if none.</summary>
    private static int RunningComboSide()
    {
        for (int side = 0; side < SIDES; side++)
        {
            if (ComboTracker.CountOf(ComboTracker.BattleTeam(side), out int _damage) > 0)
                return COUNTER_ON_ATTACKER ? side : SIDES - 1 - side;
        }
        return -1;
    }

    private static void Announce(ManagedObject data, int side)
    {
        // Combo trials give their own recipe/progress feedback — the tester asked
        // to silence the damage readout there
        if (data == null || ComboTrialHooks.IsActive) return;

        // "Attack Data" is the master toggle for the whole panel (Is_AttackAllView);
        // the panel record stays populated while it is hidden
        var ds = FlowHelper.GetTrainingDisplaySetting();
        if (ds != null && !FlowHelper.ReadBoolField(ds, "Is_AttackAllView")) return;

        int count = FlowHelper.ReadIntField(data, "prevComboCount", 0);
        int damage = FlowHelper.ReadIntField(data, "comboDamage", 0);
        if (count <= 0 && damage <= 0) return;

        var parts = new List<string>();
        if (side != OWN_SIDE) parts.Add(LocalizedText.Opponent());
        parts.Add(damage.ToString());
        if (count > 1)
        {
            parts.Add($"{count} {LocalizedText.Hits()}");
            // correction is a long; the percent fits the low 32 bits
            int scaling = FlowHelper.ReadIntField(data, "correction", NO_SCALING);
            if (scaling > 0 && scaling < NO_SCALING) parts.Add($"{LocalizedText.Scaling()} {scaling}%");
        }
        else
        {
            // A lone hit: its attack type (High / Low / Throw…), as the panel shows
            string type = FlowHelper.ResolveGuidField(data, "attackInfo");
            if (!string.IsNullOrWhiteSpace(type)) parts.Add(type);
        }

        string announcement = string.Join(". ", parts);
        API.LogInfo($"[SF6Access] Attack data: {announcement}");
        ScreenReaderService.Speak(announcement, interrupt: false);
    }

    /// <summary>Both sides' display-state records (P1 first).</summary>
    private static ManagedObject PlayerDatas() =>
        FlowHelper.GetObjectField(FlowHelper.GetTrainingGameData(), "PlayerDatas");
}
