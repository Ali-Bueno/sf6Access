using REFrameworkNET;
using SF6Access.Services.Ui;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// The shared "may a hands-free field reader speak or sound right now?" gate,
/// plus "is the player actually walking?".
///
/// <para>Once the World Tour readers lost their toggle keys they became four
/// always-on voices, and each one having its own idea of when to shut up went
/// wrong immediately: the arrival radar announced "nothing nearby" over and over
/// in the MAIN MENU, and the distance reader talked across tutorial subtitles.
/// Both had passed their own gate. So the gate lives here, once.</para>
///
/// <para><b>Why <c>AvatarManager != null</c> is not enough.</b> That was the old
/// field test, and it is what let the main menu talk: the singleton survives
/// leaving the field, so it still resolves in menus. What does NOT survive is the
/// player avatar itself — no avatar, no readable position. So presence is
/// "the manager resolves AND the player has a real position", which is true in
/// the walkable field (the opening tutorial included) and false everywhere
/// else.</para>
///
/// <para><b>Movement is derived, not read.</b> Nothing in the game's managed
/// surface exposes the avatar's speed, so it is measured: the player's position
/// is sampled a few times a second and compared. Cheap, and it needs no
/// reverse-engineering to stay correct.</para>
///
/// <para>Refresh-on-demand rather than a registered callback: every reader calls
/// <see cref="Refresh"/> first, extra calls within one sample window cost
/// nothing, and there is no ordering dependency between hooks.</para>
/// </summary>
public static class FieldPresenceService
{
    // Sampling interval. Fast enough that "started walking" is noticed within a
    // step, slow enough that the avatar-list walk is not a per-frame cost.
    private const long SAMPLE_MS = 150;

    // Speed under which the player counts as standing still, in metres/second
    // (RE Engine world units are metres). Far below any walking speed and far
    // above the positional jitter of an idle animation, so it separates "walking"
    // from "breathing" without needing a real velocity from the game.
    private const float MOVING_SPEED_MPS = 0.4f;

    // Movement is held briefly after the player stops, so a reader mid-sentence
    // is not cut off by a pause at a kerb, and small stutters do not flap the
    // gate on and off.
    private const long STILL_GRACE_MS = 700;

    /// <summary>True while the player avatar is standing in a walkable World Tour
    /// field — the opening tutorial included, menus excluded.</summary>
    public static bool InField { get; private set; }

    /// <summary>True while the player is walking (or stopped only a moment ago).</summary>
    public static bool Moving { get; private set; }

    /// <summary>True while a fight is in progress — World Tour battles included,
    /// which is the case that matters, since those keep the walkable field
    /// loaded and so pass every other test.</summary>
    public static bool Fighting { get; private set; }

    /// <summary>True when a hands-free reader may make itself heard: in the
    /// field, no menu owning the screen, and no fight in progress. Callers add
    /// their own reasons on top (dialogue, priority, cadence).</summary>
    public static bool CanSpeak => InField && !Fighting && !MenuActive();

    /// <summary>Same, for readers that should only run while the player is
    /// actually moving through the world.</summary>
    public static bool CanSpeakWhileMoving => CanSpeak && Moving;

    private static long _sampledAt;
    private static float _lastX, _lastY, _lastZ;
    private static bool _havePrev;
    private static long _movedAt;

    /// <summary>Bring the presence and movement readings up to date. Safe and
    /// cheap to call from every reader every frame.</summary>
    public static void Refresh()
    {
        long now = System.Environment.TickCount64;
        long elapsed = now - _sampledAt;
        if (elapsed < SAMPLE_MS) return;
        _sampledAt = now;

        // Sampled once here rather than per reader per frame: four always-on
        // readers all ask, and the answer cannot change between them.
        Fighting = ReadFighting();

        var mgr = WorldTourStateService.GetAvatarManager();
        if (mgr == null) { Clear(); return; }

        var p = AvatarFieldReader.ReadPlayerPos(mgr);
        if (!p.ok) { Clear(); return; }

        InField = true;

        if (_havePrev)
        {
            float dx = p.x - _lastX, dy = p.y - _lastY, dz = p.z - _lastZ;
            float metres = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            // A teleport (loading into a new area) is not walking; it would
            // otherwise register as an enormous speed for one sample.
            if (metres / (elapsed / 1000f) >= MOVING_SPEED_MPS && metres < MAX_STEP_M)
                _movedAt = now;
        }

        _lastX = p.x; _lastY = p.y; _lastZ = p.z;
        _havePrev = true;
        Moving = now - _movedAt <= STILL_GRACE_MS;

        WatchForStuckGate(now);
    }

    // How long the readers may stay muted, with the field plainly loaded, before
    // that stops being a normal pause and starts being a bug.
    private const long MUTE_WARN_MS = 15000;
    private static long _mutedSince;
    private static bool _mutedWarned;

    /// <summary>Say so when everything has gone quiet and should not have.
    ///
    /// <para>A gate that sticks on is invisible: the readers simply stop, exactly
    /// as they do when they are correctly holding their tongue, and the log shows
    /// nothing either way. That already cost a round after a World Tour battle
    /// left the field silent. This names the culprit instead of leaving it to be
    /// guessed at.</para>
    /// </summary>
    private static void WatchForStuckGate(long now)
    {
        if (CanSpeak)
        {
            _mutedSince = 0;
            _mutedWarned = false;
            return;
        }

        if (_mutedSince == 0) { _mutedSince = now; return; }
        if (_mutedWarned || now - _mutedSince < MUTE_WARN_MS) return;

        _mutedWarned = true;
        API.LogWarning($"[SF6Access] Field readers muted for {(now - _mutedSince) / 1000}s with the " +
                       $"field loaded — InField={InField}, Fighting={Fighting}, menu={MenuActive()}. " +
                       "Whichever of those is unexpectedly set is the stuck gate.");
    }

    // Distance in one sample that can only be a teleport/scene change, not a
    // step: at the sample interval above, even a sprint covers a fraction of it.
    private const float MAX_STEP_M = 5f;

    /// <summary>Whether a menu currently owns the screen. Covers the screens
    /// migrated to the adapter pattern; legacy per-screen hooks are not in it,
    /// which is why <see cref="InField"/> does the heavy lifting.</summary>
    private static bool MenuActive()
    {
        try
        {
            if (UiDispatcher.AnyAdapterActive) return true;
            // The World Tour phone, named explicitly as well: it is the menu the
            // player opens while standing in the field, so it is the one where a
            // field reader talking over the screen is most likely and most
            // annoying. Naming it costs nothing if the dispatcher already has it.
            return SF6Access.Hooks.WTMPauseHooks.IsInWTMPause;
        }
        catch { return false; }
    }

    // The battle flags. There is no single confirmed "in a fight" signal in the
    // game's managed surface, so two are OR'd and a third is logged beside them:
    // one real World Tour battle in the log then says which is authoritative.
    private const string COMMENTATOR_HOLDER = "app.commentator.bCommentatorGlobalInfoHolder";
    private const string WT_BATTLE_MANAGER = "app.worldtour.WTBattleManager";

    /// <summary>Whether a fight is on. <c>IsBattleNow</c> comes from the
    /// commentator holder (which World Tour's own contact battles drive, via
    /// <c>ContactCmdCtrlCommentatorAsset</c>); <c>WTBattleManager.IsBattle</c> is
    /// the World Tour-specific flag, kept as an OR-term because the commentator
    /// path is inferred rather than proven.
    ///
    /// <para><c>CurrentBattleDesc</c> is deliberately NOT gated on, only logged:
    /// it is the best-proven of the three (it is already read in
    /// <c>BattleInfoHooks</c>) but it is also the coarsest, and if it stays
    /// populated after a match it would silence the field readers for the rest of
    /// the session. Logging it costs nothing and settles the question.</para>
    /// </summary>
    private static bool ReadFighting()
    {
        bool commentator = Flag(COMMENTATOR_HOLDER, "get_IsBattleNow");
        bool worldTour = Flag(WT_BATTLE_MANAGER, "get_IsBattle");
        bool battleDesc = HasBattleDesc();

        string sig = $"{commentator}/{worldTour}/{battleDesc}";
        if (GameStateTracker.HasChanged("wt_battle_signals", sig))
            API.LogInfo($"[SF6Access] Battle signals — IsBattleNow={commentator}, " +
                        $"WTBattleManager.IsBattle={worldTour}, CurrentBattleDesc!=null={battleDesc}");

        // CONFIRMED in game 2026-08-14 by the log above: WTBattleManager.IsBattle
        // and CurrentBattleDesc both go true for a World Tour battle and back to
        // false when it ends — CurrentBattleDesc does NOT persist afterwards,
        // which was the only reason it was held back from the gate. IsBattleNow
        // never fired once; it is kept purely as an OR-term for non-World-Tour
        // fights, which these readers never see anyway.
        //
        // The live-Training check that used to sit here is GONE. Every reader
        // behind this gate is already restricted to the World Tour field, so
        // Training could never have been the state — and asking cost two failed
        // member probes six times a second, which REFramework logs three lines
        // apiece. That single dead check was ~3000 log lines a minute.
        return worldTour || battleDesc || commentator;
    }

    private static bool Flag(string singleton, string getter)
    {
        try
        {
            var obj = API.GetManagedSingleton(singleton) as ManagedObject;
            if (obj == null) return false;
            return FlowHelper.Call(obj, getter) is bool b && b;
        }
        catch { return false; }
    }

    private static bool HasBattleDesc()
    {
        try
        {
            var obj = API.GetManagedSingleton(COMMENTATOR_HOLDER) as ManagedObject;
            return obj != null && FlowHelper.Call(obj, "get_CurrentBattleDesc") != null;
        }
        catch { return false; }
    }


    private static void Clear()
    {
        InField = false;
        Moving = false;
        _havePrev = false;
    }
}
