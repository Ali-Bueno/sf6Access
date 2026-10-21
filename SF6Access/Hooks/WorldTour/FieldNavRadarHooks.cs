using System.Collections.Generic;
using System.Runtime.InteropServices;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>Automatic geometry radar in exploration. Existing sounds describe clearance,
/// proximity and drops; B reads once and Shift+B optionally pauses/resumes.
/// Holds for menus, combat, dialogue and tutorial panels. Readout formatting is separate.</summary>
public partial class FieldNavRadarHooks
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>B, the radar key. A letter shortcut, so it only ever acts while
    /// SF6 owns the foreground window.</summary>
    private const int VK_B = 0x42;
    private const int VK_SHIFT = 0x10;

    /// <summary>How often the continuous mode re-reads the GAME'S OWN verdict, in
    /// milliseconds on the monotonic clock.
    ///
    /// <para>Milliseconds, not LateUpdate ticks: a frame count means a different
    /// sampling rate on every machine, and a wall approached at a run has to be met
    /// at the same distance whatever the frame rate. The interval is RE7's
    /// <see cref="FieldRadarTuning.FallbackSenseMs"/> — that mod's own rate for a
    /// channel that yields only coarse information, which is exactly what an obstacle
    /// CLASS is. The geometry radar underneath runs far faster and on its own clock;
    /// see <see cref="FieldRadarService"/>.</para></summary>
    private const long VERDICT_SENSE_MS = FieldRadarTuning.FallbackSenseMs;

    /// <summary>Consecutive identical samples before a new situation is announced.
    /// Ray hits are binary tests against real geometry, so a railing, a doorframe or
    /// a lamp post can flicker a side feeler on and off as the player walks past it,
    /// and a cue per flicker is the noise this design exists to avoid. Two samples
    /// is a third of a second of confirmation — short enough to still warn before a
    /// wall, long enough to swallow a single-sample flicker.</summary>
    internal const int CONFIRM_SAMPLES = 2;

    private static bool _keyDown;
    private static bool _continuous = true;
    private static long _nextVerdictAt;

    // The last CONFIRMED situation, and the one currently being confirmed.
    private static NavReading _announced;
    private static bool _haveBaseline;
    private static NavReading _pending;
    private static int _pendingSamples;
    private static bool _announceBlock, _announceDrop;
    private static long _lastReadingAt;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] FieldNavRadarHooks initialized (continuous on by default; Shift+B = pause/resume)");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        // The key must be sampled every frame — a short press between two polls is
        // a press the player has to repeat.
        bool down = (GetAsyncKeyState(VK_B) & 0x8000) != 0;
        bool edge = down && !_keyDown;
        _keyDown = down;

        FieldPresenceService.Refresh();

        if (edge && ReadoutShortcut.IsGameForeground()) HandlePress();

        if (!_continuous) return;
        // The shared field gate: in a walkable World Tour field, no menu owning the
        // screen, no fight. Never cast rays anywhere else.
        if (!FieldFeedback.CanOutput) { ResetContinuous(); return; }
        // Nothing may sound over the game's own dialogue voice.
        if (SF6Access.Hooks.SpTalkNovelHooks.DialogueActive || PadGuideHooks.Active) return;

        // The reactive radar — the replication of the RE7 mod's RadarService. It owns
        // its own monotonic cadence (faster while the player turns), so it is offered
        // every frame and decides for itself when to sense.
        FieldRadarService.Update();

        long clock = System.Environment.TickCount64;
        if (clock < _nextVerdictAt) return;
        _nextVerdictAt = clock + VERDICT_SENSE_MS;

        var now = FieldNavRadarService.Sample();
        if (now.Ok)
        {
            _lastReadingAt = clock;
            Confirm(now);
        }
        OfferNotices();

        var cameraForward = FieldDirectionService.GetCameraForward();
        // Automatic diagnostic: the player is blind and cannot aim F10 at a
        // specific object, so every sample where the game's own verdict is the
        // one BlockPhrase() would speak as "wall"/"blocked" (never the ray
        // ladder) also logs what is actually being hit. ContactCatalog does its
        // own identity dedupe and log-rate limiting, so this fires unconditionally
        // while blocked.
        if (now.Ok && now.Block == FrontBlock.Blocked) ContactCatalog.Note(cameraForward);
    }

    /// <summary>Shift+B toggles the continuous mode; B alone answers once. Both are
    /// refused outside the field: an explicit press deserves an answer, but there is
    /// no geometry to answer with in a menu or a battle.</summary>
    private static void HandlePress()
    {
        if (!FieldFeedback.CanOutput)
        {
            API.LogInfo("[SF6Access] Nav radar key pressed outside the World Tour field — ignored " +
                        $"(InField={FieldPresenceService.InField}, Fighting={FieldPresenceService.Fighting})");
            return;
        }

        if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) ToggleContinuous();
        else ReadOutOnce();
    }

    private static void ToggleContinuous()
    {
        _continuous = !_continuous;
        ResetContinuous();
        ScreenReaderService.Speak(_continuous ? LocalizedText.NavRadarOn() : LocalizedText.NavRadarOff(),
                                  interrupt: true);
        API.LogInfo($"[SF6Access] Nav radar continuous mode {(_continuous ? "on" : "off")}");
    }

    /// <summary>Hold a new situation for <see cref="CONFIRM_SAMPLES"/> consecutive
    /// samples, then cue the transition from the last confirmed one — exactly once,
    /// since the counter only equals the threshold on a single sample.</summary>
    private static void Confirm(NavReading now)
    {
        if (!now.SameStateAs(_pending)) { _pending = now; _pendingSamples = 1; return; }
        if (++_pendingSamples != CONFIRM_SAMPLES) return;

        var previous = _announced;
        bool hadBaseline = _haveBaseline;
        Seed(now);
        // The first confirmed reading after switching on (or after the gate closed
        // and reopened) is a BASELINE, not an event: cueing it would fire a burst of
        // sounds describing where the player was already standing.
        if (hadBaseline) Cue(previous, now);
    }

    /// <summary>Everything that changed in the game's verdict: the drop motif and
    /// the one spoken class. Proximity is not cued here at all: it is
    /// spoken once rather than also chimed.</summary>
    private static void Cue(NavReading was, NavReading now)
    {
        // The drop goes first: it is the only cue that is a safety matter, so it
        // must not queue behind a wall cue in the same sample.
        if (was.GroundSolid && !now.GroundSolid)
            _announceDrop = true;
        if (now.GroundSolid)
        { _announceDrop = false; FieldFeedback.Forget("radar-ground"); }

        // BLOCKED is the game's own verdict, never the ray ladder: a rung that hits
        // means "something of about this height is somewhere along that ray", which
        // is not the same question and was answering it wrongly in both directions.
        bool wasBlocked = was.Block == FrontBlock.Blocked;
        bool isBlocked = now.Block == FrontBlock.Blocked;
        // Without interrupting: the word only has to arrive, not to arrive first.
        if (!wasBlocked && isBlocked) _announceBlock = true;
        if (!isBlocked && now.Block != FrontBlock.WallRide)
        { _announceBlock = false; FieldFeedback.Forget("radar-block"); }

        if (isBlocked && wasBlocked && was.Front != now.Front)
        {
            // Blocked before and blocked still, but a DIFFERENT obstacle — walking
            // from a kerb up to the wall behind it. That used to be silent, and it
            // is exactly the moment the player's options change (a Step can be
            // walked over, a Wall cannot). NO sound: the cue pair means
            // "closed" / "opened" and neither happened, so re-firing one would lie.
            // The word alone carries it, and the confirmation window plus the
            // reader's duplicate filter keep a wobbling class from chattering.
            _announceBlock = true;
        }

        // A wall the avatar can run along is a route, not a dead end, so it never
        // fires the impassable cue — but arriving at one is worth a word.
        if (now.Block == FrontBlock.WallRide && was.Block != FrontBlock.WallRide)
            _announceBlock = true;
    }

    private static bool FreshReading => _haveBaseline &&
        _pending.SameStateAs(_announced) &&
        System.Environment.TickCount64 - _lastReadingAt <= 2 * VERDICT_SENSE_MS;

    private static void OfferNotices()
    {
        if (!FreshReading) return;
        if (_announceDrop)
            FieldFeedback.Say("radar-ground", LocalizedText.NavFloorDrop(), FieldSpeechPriority.Interaction,
                onSpoken: () => _announceDrop = false,
                stillValid: () => FreshReading && _announceDrop && !_announced.GroundSolid);
        if (!_announceBlock) return;
        var reading = _announced;
        string text = reading.Block == FrontBlock.WallRide ? LocalizedText.NavWallRide() : BlockPhrase(reading);
        FieldFeedback.Say("radar-block", text, FieldSpeechPriority.Route,
            onSpoken: () => _announceBlock = false,
            stillValid: () => FreshReading && _announceBlock && _announced.SameStateAs(reading));
    }

    private static void Seed(NavReading r)
    {
        _announced = r;
        _pending = r;
        _pendingSamples = CONFIRM_SAMPLES;
        _haveBaseline = true;
    }

    /// <summary>Forget everything, so switching the mode on — or walking back into
    /// the field after a menu — starts from a fresh silent baseline.</summary>
    private static void ResetContinuous()
    {
        FieldFeedback.Forget("radar-block"); FieldFeedback.Forget("radar-ground");
        _nextVerdictAt = 0;
        FieldRadarService.Reset();
        _announced = default;
        _pending = default;
        _pendingSamples = 0;
        _haveBaseline = false;
        _announceBlock = _announceDrop = false;
        _lastReadingAt = 0;
        FieldRayMetrics.Reset();
        NavMeshOpenings.Reset();
    }
}
