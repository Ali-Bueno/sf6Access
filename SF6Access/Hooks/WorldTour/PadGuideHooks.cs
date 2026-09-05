using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// Sequential guide to the World Tour tutorial's step-on panels: sound the
/// nearest one that has not been walked over, and move on to the next as each is
/// cleared, until they are all done.
///
/// <para>Different mechanic from the ambient <see cref="FieldBeaconHooks"/>: one
/// target at a time, and it ENDS. The cue is the panel's own sound through the
/// panel's own emitter, so it arrives in real 3D from the panel itself.</para>
///
/// <para><b>No toggle key.</b> The panels ARE the tutorial — a player who has to
/// know a shortcut exists in order to be told where to walk has already been left
/// behind. So the guide arms itself whenever panels are present and disarms when
/// they are gone, and the presence of the panels is the only switch.</para>
///
/// <para><b>Sound alone is not enough, and that is the point of the speech.</b>
/// Wwise attenuates each sound over its own authored distance, so a panel far
/// enough away is simply inaudible — a player who hears nothing has no way to
/// tell "no panels left" from "the next one is 30 m behind you". So every change
/// of target is also announced with the camera-relative clock direction and the
/// distance, which have no range limit at all. The rhythm gets you the last few
/// metres; the sentence gets you into range in the first place.</para>
///
/// <para>Silence rules match the beacons — the guide must never talk over the
/// game: nothing while a dialogue is on screen, nothing while an interaction
/// prompt is up (which is where tutorial text lives), and nothing right after the
/// screen reader speaks.</para>
/// </summary>
public class PadGuideHooks
{
    // Scan cadence in LateUpdate ticks at 60 fps. Enumerating the scene by type
    // is far too heavy to run every frame, and with no toggle key there is no
    // longer a press to pay for it — so it is throttled, and throttled harder
    // while there is nothing to guide to. 0.25 s while active is fast enough to
    // catch the instant a panel is stepped on.
    private const int SCAN_ACTIVE_TICKS = 15;
    private const int SCAN_IDLE_TICKS = 60;

    // Cue cadence in ticks: the quickening as the player closes in is itself the
    // "getting warmer" signal.
    private const int CUE_NEAR_TICKS = 45;    // 0.75 s on top of it
    private const int CUE_FAR_TICKS = 120;    // 2 s further out
    private const float CUE_NEAR_M = 4f;

    // Ground-plane distance at which the player counts as standing on a panel.
    // MEASURED in game 2026-08-14, not guessed: the two panels confirmed stepped
    // on bottomed out at 0.41 m and 0.54 m in 3D, i.e. roughly 0 m and 0.35 m on
    // the ground plane once the panels' fixed height offset is removed.
    private const float CLEAR_FLAT_M = 0.6f;

    // Re-announce the same target this often while walking to it, so a player who
    // drifted off course is corrected instead of hunting in silence.
    private const int REANNOUNCE_TICKS = 300;   // 5 s

    // Hold after the reader speaks, so a cue never lands on an announcement.
    private const long READER_HOLD_MS = 1200;

    /// <summary>True while panels are present and at least one is still unwalked.
    /// With every reader now always-on, something has to arbitrate: during this
    /// tutorial the panels ARE the objective, so the continuous tracker and the
    /// arrival radar stand down rather than talk over the guide.</summary>
    public static bool Active { get; private set; }

    // Consecutive empty scans before the walked-panel record is thrown away. At
    // the idle scan rate this is a few seconds — far longer than any hiccup, far
    // shorter than a session.
    private const int FORGET_AFTER_EMPTY_SCANS = 3;

    private static int _emptyScans;
    private static int _scanCountdown;
    private static int _sinceCue;
    private static int _sinceAnnounce;
    private static string _target;
    private static bool _announcedDone;
    private static readonly System.Collections.Generic.HashSet<string> Cleared =
        new System.Collections.Generic.HashSet<string>();

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] PadGuideHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        if (--_scanCountdown > 0) return;

        // Ticks actually elapsed since the last pass, so the cue and announcement
        // cadences stay in real time however the scan rate changes underneath.
        int elapsed = _lastScanPeriod;
        Schedule(SCAN_IDLE_TICKS);   // tightened below once there is something to guide to

        FieldPresenceService.Refresh();

        // Not gated on movement, unlike the beacons and the distance reader: the
        // cue is what tells a standing player which way to set off, so requiring
        // movement first would be a deadlock. Menus and non-field states are
        // still excluded — but a state we cannot read is a PAUSE, not an ending:
        // forgetting which panels are done because of one unreadable frame is
        // what made the guide keep sounding panels already walked over.
        var mgr = FieldPresenceService.CanSpeak ? WorldTourStateService.GetAvatarManager() : null;
        if (mgr == null) { Pause(); return; }

        var pads = FieldPadService.ReadPads(mgr);
        if (pads.Count == 0)
        {
            // One empty read is a hiccup — ReadPads also comes back empty when the
            // player position momentarily cannot be read. Only a SUSTAINED absence
            // means the tutorial is over, which is also what lets it be replayed.
            if (++_emptyScans >= FORGET_AFTER_EMPTY_SCANS) Reset();
            else Active = false;
            return;
        }
        _emptyScans = 0;

        Schedule(SCAN_ACTIVE_TICKS);
        _sinceCue += elapsed;
        _sinceAnnounce += elapsed;

        // BOOKKEEPING FIRST, before any gate that only governs SPEAKING. Marking a
        // panel as walked is not an announcement, and running it after the gates
        // meant that stepping on a panel which registers as an interaction target
        // silenced the hook before it could notice — so the panel stayed "unwalked"
        // and kept sounding forever.
        ClearUnderfoot(pads);

        int next = NextIndex(pads);
        Active = next >= 0;

        if (next < 0)
        {
            // All walked. Say so once, then stay quiet until the panels themselves
            // go away — a guide that keeps talking after it is finished is worse
            // than no guide.
            if (!_announcedDone)
            {
                _announcedDone = true;
                ScreenReaderService.Speak(LocalizedText.PadsDone(), interrupt: true);
            }
            return;
        }

        // From here on it is output, so the speech gates apply.
        if (SF6Access.Hooks.SpTalkNovelHooks.DialogueActive) return;
        if (System.Environment.TickCount64 - ScreenReaderService.LastInterruptTick < READER_HOLD_MS) return;
        // In interaction range the arrival reader owns the moment, and this is
        // where prompts and tutorial text appear.
        if (AvatarFieldReader.GetAccessInfoCount(mgr) > 0) return;

        var target = pads[next];
        Announce(target);

        int cueEvery = target.Dist <= CUE_NEAR_M ? CUE_NEAR_TICKS : CUE_FAR_TICKS;
        if (_sinceCue >= cueEvery)
        {
            _sinceCue = 0;
            // Behind the player gets the panel's other sound. A source dead ahead
            // and one dead behind arrive almost identically in 3D audio, so
            // panning and distance alone cannot separate them — and walking the
            // wrong way is the one mistake this guide exists to prevent.
            var fwd = FieldDirectionService.GetCameraForward();
            bool behind = fwd.Ok && (target.Dx * fwd.X + target.Dz * fwd.Z) < 0f;
            bool ok = FieldPadService.Cue(target, behind);
            // Logged because a cue is otherwise unobservable: a silent failure and
            // a cue the player did not notice look identical from the log.
            API.LogInfo($"[SF6Access] Panel cue '{target.Name}' {target.Dist:0.0}m " +
                        $"{(behind ? "behind" : "ahead")} {(ok ? "played" : "FAILED")}");
        }
    }

    // Ticks the last scan period was set to, so elapsed real time can be counted
    // without a per-frame counter of its own.
    private static int _lastScanPeriod = SCAN_IDLE_TICKS;

    private static void Schedule(int ticks)
    {
        _scanCountdown = ticks;
        _lastScanPeriod = ticks;
    }

    /// <summary>Announce the target when it changes, and periodically while it
    /// stays the same. The name is never spoken — a player does not care that it
    /// is called <c>vi_020000_09</c>; direction and distance are the whole
    /// message, so the terse form is used for repeats.</summary>
    private static void Announce(FieldPadService.Pad target)
    {
        bool changed = target.Name != _target;
        if (!changed && _sinceAnnounce < REANNOUNCE_TICKS) return;

        _target = target.Name;
        _sinceAnnounce = 0;

        int meters = (int)System.Math.Round(target.Dist);
        int hour = FieldDirectionService.ClockHour(
            FieldDirectionService.GetCameraForward(), target.Dx, target.Dz);

        // Clock direction needs a readable camera frame; distance alone is the
        // fallback rather than saying nothing.
        string text = hour > 0
            ? (changed ? LocalizedText.AtClockMeters(LocalizedText.PadWord(), hour, meters)
                       : LocalizedText.ClockShort(hour, meters))
            : LocalizedText.AtMeters(LocalizedText.PadWord(), meters);

        ScreenReaderService.Speak(text, interrupt: true);
    }

    /// <summary>Mark and silence every panel the player is standing on.</summary>
    private static void ClearUnderfoot(System.Collections.Generic.List<FieldPadService.Pad> pads)
    {
        foreach (var pad in pads)
        {
            if (pad.Flat > CLEAR_FLAT_M || Cleared.Contains(pad.Name)) continue;
            Cleared.Add(pad.Name);
            FieldPadService.Silence(pad);
            API.LogInfo($"[SF6Access] Panel cleared '{pad.Name}' at flat {pad.Flat:0.00}m " +
                        $"({Cleared.Count} of {pads.Count})");
            // Let the next target announce itself immediately.
            _target = null;
            _sinceAnnounce = REANNOUNCE_TICKS;
            _sinceCue = CUE_FAR_TICKS;
        }
    }

    /// <summary>Index of the nearest panel not yet walked over, or -1.</summary>
    private static int NextIndex(System.Collections.Generic.List<FieldPadService.Pad> pads)
    {
        for (int i = 0; i < pads.Count; i++)
            if (!Cleared.Contains(pads[i].Name)) return i;
        return -1;
    }

    /// <summary>Stand down without forgetting: the guide is not running right
    /// now, but the panels walked so far are still walked.</summary>
    private static void Pause()
    {
        Active = false;
        _target = null;
    }

    /// <summary>Forget everything — only for a genuine end of the tutorial, so a
    /// later run starts clean instead of thinking its panels are already done.</summary>
    private static void Reset()
    {
        Pause();
        _sinceCue = 0;
        _sinceAnnounce = 0;
        _announcedDone = false;
        _emptyScans = 0;
        _lastScanPeriod = SCAN_IDLE_TICKS;
        Cleared.Clear();
    }
}
