using System.Collections.Generic;
using System.Runtime.InteropServices;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// <b>World Tour navigation radar</b> — echolocation of GEOMETRY: walls, openings
/// and drops, read from the avatar's own sensing rays through
/// <see cref="FieldNavRadarService"/>. It is the complement of
/// <see cref="FieldAwarenessHooks"/> (N), which names PEOPLE; the two never talk
/// about the same thing and neither knows about the other.
///
/// <list type="bullet">
/// <item><b>B</b> — one-shot readout: the obstacle in front and how far, whether
///   each side is open or blocked, and whether there is floor ahead.</item>
/// <item><b>Shift+B</b> — toggles the continuous reactive mode. While on, the
///   radar samples periodically and speaks/sounds ONLY when the situation
///   CHANGES. This is the core of the design ported from the RE7 mod: a radar
///   that beeps continuously is noise the player learns to filter out, so the
///   silence is what makes the cues mean something.</item>
/// </list>
///
/// <para><b>Cue vocabulary</b> — sounds carry direction, speech carries the
/// class: <c>impassable.mp3</c> for something closing, <c>exit.mp3</c> for
/// something opening, panned to the side it happened on (centred for the front),
/// and a descending three-note motif for a drop, which is the only cue that is a
/// safety matter rather than navigation.</para>
///
/// <para>The mode is OPT-IN, so it deliberately does not stand down for the panel
/// guide the way the always-on readers do: geometry is orthogonal to whatever
/// those are guiding towards. It does hold for dialogue — nothing may talk over
/// the game's own voice — and for the shared field gate, so it never samples in a
/// menu or a battle.</para>
/// </summary>
public class FieldNavRadarHooks
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>B, the radar key. A letter shortcut, so it only ever acts while
    /// SF6 owns the foreground window.</summary>
    private const int VK_B = 0x42;
    private const int VK_SHIFT = 0x10;

    /// <summary>How often the continuous mode casts, in LateUpdate ticks at 60 fps
    /// (the same frame-tick convention as the other World Tour readers).
    ///
    /// <para>A sixth of a second is a UX pacing choice, not a game value, and it is
    /// bounded on both sides. Faster buys nothing a player can act on and multiplies
    /// a nine-cast sweep by the frame rate; slower and a wall approached at a run
    /// (a few metres per second) would arrive between two samples, which is exactly
    /// the failure a navigation radar exists to prevent — the forward height stack
    /// only reaches a metre or so, so it has to be re-read several times inside that
    /// reach. At this rate the radar costs about 54 casts a second spread across
    /// frames, well under the ~40 the F10 probe performs inside a SINGLE frame.</para></summary>
    private const int SAMPLE_INTERVAL_TICKS = 10;

    /// <summary>Consecutive identical samples before a new situation is announced.
    /// Ray hits are binary tests against real geometry, so a railing, a doorframe or
    /// a lamp post can flicker a side feeler on and off as the player walks past it,
    /// and a cue per flicker is the noise this design exists to avoid. Two samples
    /// is a third of a second of confirmation — short enough to still warn before a
    /// wall, long enough to swallow a single-sample flicker.</summary>
    private const int CONFIRM_SAMPLES = 2;

    /// <summary>How hard a side cue is pushed into one ear. Short of full pan, which
    /// collapses the cue into a single speaker and makes it easy to miss on the
    /// wrong side of a headset.</summary>
    private const float SIDE_PAN = 0.8f;

    /// <summary>Something closed / something opened. The mod's own cue files, the
    /// same pair the RE7 radar used for the same two events.</summary>
    private const string CUE_BLOCKED = "impassable.mp3";
    private const string CUE_OPEN = "exit.mp3";

    /// <summary>The drop warning: a DESCENDING motif, so the shape of the sound is
    /// the shape of the hazard. Built from AudioService's equal-temperament note
    /// constants rather than raw frequencies.</summary>
    private static readonly float[] DropMotif =
        { AudioService.NoteLaHigh, AudioService.NoteMi, AudioService.NoteLa };

    private static bool _keyDown;
    private static bool _continuous;
    private static int _tick;

    // The last CONFIRMED situation, and the one currently being confirmed.
    private static NavReading _announced;
    private static bool _haveBaseline;
    private static NavReading _pending;
    private static int _pendingSamples;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] FieldNavRadarHooks initialized (B = navigation radar, Shift+B = continuous)");
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
        if (!FieldPresenceService.CanSpeak) { ResetContinuous(); return; }
        // Nothing may sound over the game's own dialogue voice.
        if (SF6Access.Hooks.SpTalkNovelHooks.DialogueActive) return;

        if (++_tick < SAMPLE_INTERVAL_TICKS) return;
        _tick = 0;

        var now = FieldNavRadarService.Sample();
        if (now.Ok) Confirm(now);
    }

    /// <summary>Shift+B toggles the continuous mode; B alone answers once. Both are
    /// refused outside the field: an explicit press deserves an answer, but there is
    /// no geometry to answer with in a menu or a battle.</summary>
    private static void HandlePress()
    {
        if (!FieldPresenceService.CanSpeak)
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

    /// <summary>The full situation, in answer to a press. Spoken with an interrupt:
    /// a request must always be answered, ahead of whatever was being said.</summary>
    private static void ReadOutOnce()
    {
        var r = FieldNavRadarService.Sample();
        if (!r.Ok)
        {
            API.LogInfo("[SF6Access] Nav radar: no reading (avatar field state or ray API unreachable)");
            return;
        }
        ScreenReaderService.Speak(Describe(r), interrupt: true);
        // The continuous mode's baseline is now stale relative to what the player
        // has just been told; re-seed it so the next change is measured from here.
        if (_continuous) Seed(r);
    }

    private static string Describe(NavReading r)
    {
        var parts = new List<string>(4)
        {
            FrontPhrase(r),
            r.LeftBlocked ? LocalizedText.NavLeftBlocked() : LocalizedText.NavLeftOpen(),
            r.RightBlocked ? LocalizedText.NavRightBlocked() : LocalizedText.NavRightOpen(),
            r.GroundSolid ? LocalizedText.NavFloorSolid() : LocalizedText.NavFloorDrop(),
        };
        return string.Join(", ", parts);
    }

    /// <summary>The obstacle class with its distance. When the height stack is open
    /// the distance still matters — it is how far the long forward ray reached
    /// before finding something — but it is a different sentence, because "clear
    /// ahead at 1.9 meters" would read as an obstruction.</summary>
    private static string FrontPhrase(NavReading r)
    {
        if (r.Front == FrontProfile.Open)
            return r.HasDistance ? LocalizedText.NavClearFor(r.Distance) : LocalizedText.NavFront(r.Front);
        string cls = LocalizedText.NavFront(r.Front);
        return r.HasDistance ? LocalizedText.NavObstacleAt(cls, r.Distance) : cls;
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

    /// <summary>Everything that changed, as sounds plus the one spoken class.</summary>
    private static void Cue(NavReading was, NavReading now)
    {
        // The drop goes first: it is the only cue that is a safety matter, so it
        // must not queue behind a wall cue in the same sample.
        if (was.GroundSolid && !now.GroundSolid) AudioService.PlayTone(DropMotif);

        bool wasBlocked = was.Front != FrontProfile.Open;
        bool isBlocked = now.Front != FrontProfile.Open;
        if (!wasBlocked && isBlocked)
        {
            AudioService.PlaySound(CUE_BLOCKED);
            // Without interrupting: the sound has already said "something is there",
            // and the word only has to arrive, not to arrive first.
            ScreenReaderService.Speak(LocalizedText.NavFront(now.Front), interrupt: false);
        }
        else if (wasBlocked && !isBlocked)
        {
            AudioService.PlaySound(CUE_OPEN);
        }
        else if (isBlocked && was.Front != now.Front)
        {
            // Blocked before and blocked still, but a DIFFERENT obstacle — walking
            // from a kerb up to the wall behind it. That used to be silent, and it
            // is exactly the moment the player's options change (a Step can be
            // walked over, a Wall cannot). NO sound: the cue pair means
            // "closed" / "opened" and neither happened, so re-firing one would lie.
            // The word alone carries it, and the confirmation window plus the
            // reader's duplicate filter keep a wobbling class from chattering.
            ScreenReaderService.Speak(LocalizedText.NavFront(now.Front), interrupt: false);
        }

        SideCue(was.LeftBlocked, now.LeftBlocked, -SIDE_PAN);
        SideCue(was.RightBlocked, now.RightBlocked, SIDE_PAN);
    }

    private static void SideCue(bool was, bool now, float pan)
    {
        if (was == now) return;
        AudioService.PlaySound(now ? CUE_BLOCKED : CUE_OPEN, pan);
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
        _tick = 0;
        _announced = default;
        _pending = default;
        _pendingSamples = 0;
        _haveBaseline = false;
    }
}
