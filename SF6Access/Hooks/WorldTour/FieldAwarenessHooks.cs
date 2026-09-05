using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// World Tour field-awareness reader (WT-1). An always-on monitor (no menu focus
/// to hook): while the World Tour avatar field is loaded it
/// <list type="bullet">
/// <item>announces the CURRENT interaction target when it changes — the nearest
///   thing the player can talk to / examine, by name and kind; and</item>
/// <item>on an on-demand key, lists the nearby interactables — in-range targets
///   from the game's own access list, else every avatar in the field by name,
///   camera-relative clock hour and metric distance ("Luke, master at 12
///   o'clock, 5 meters" — fully calibrated in game 2026-07-20).</item>
/// </list>
/// The continuous companion (auto-announcing the nearest avatar while walking)
/// is <see cref="FieldTrackingHooks"/>; the shared field readers live in
/// <see cref="AvatarFieldReader"/>.
/// </summary>
public class FieldAwarenessHooks
{
    private const string CURRENT_TARGET_KEY = "wt_field_current_target";
    private const string SECTION_KEY = "wt_field_section";

    // Set when the player arrives somewhere new, cleared once the surroundings
    // have actually been read out. It is a flag rather than an immediate call
    // because arrival is exactly when the game is most likely to be talking.
    private static bool _arrivalPending;

    // Hold after the reader speaks, so the arrival readout does not cut off an
    // announcement that is still being spoken.
    private const long READER_HOLD_MS = 1200;

    // Consecutive polls the nearest target must stay the same before it is worth
    // announcing. At the poll interval below this is well under a second — long
    // enough to swallow crowd flicker, short enough to feel immediate.
    private const int TARGET_STABLE_POLLS = 3;
    private static string _pendingTarget;
    private static int _pendingPolls;

    // How many neighbours the on-demand readout names before summarising the
    // rest. In a busy hub the full list is unusable; the remainder is always
    // counted out loud rather than silently dropped.
    private const int MAX_NEARBY_SPOKEN = 8;

    // Floor between automatic arrival announcements, independent of what the
    // section id does.
    private const long ARRIVAL_MIN_GAP_MS = 20000;
    private static long _lastArrivalTick;

    // Poll the target roughly 4x/second; the on-demand key is checked every frame.
    private const int POLL_INTERVAL = 15;
    private static int _pollCounter;

    // On-demand "list nearby interactables" key. Provisional binding (keyboard N
    // / gamepad Start) — the tester confirms a non-conflicting choice in-game, as
    // was done for the shop readouts (Start was picked after other buttons turned
    // out to be field actions).
    private const int VK_N = 0x4E;
    private static readonly ReadoutShortcut NearbyKey = new(VK_N, ReadoutShortcut.PAD_START);

    /// <summary>One nearby interactable, as read from the access list.</summary>
    private readonly struct Interactable
    {
        public readonly string Name;
        public readonly int ContactType;   // HudDef.ContactUIType
        public readonly float Distance;
        public Interactable(string name, int contactType, float distance)
        {
            Name = name; ContactType = contactType; Distance = distance;
        }
    }

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] FieldAwarenessHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        // The on-demand key must be sampled every frame (short presses).
        bool wantsNearby = NearbyKey.Pressed();

        // Gate on the WT AVATAR SYSTEM being loaded (AvatarManager resolves), NOT
        // on WTCityManager.IsActivated(): the opening tutorial is a real avatar
        // field where the player walks to Luke, but it is NOT an "activated city",
        // so an IsActivated() gate wrongly silenced the radar there — exactly when
        // a blind player most needs to find Luke. AvatarManager is null outside
        // World Tour, and its access list is empty in WT menus, so the radar stays
        // silent everywhere it should: the list itself is the real gate.
        var mgr = WorldTourStateService.GetAvatarManager();
        // The gate signature includes the instance ADDRESS: the game recreates
        // AvatarManager on scene load, so an address change in the log confirms a
        // re-bind happened (the old pointer would have read null/0 forever).
        string gateSig = mgr == null ? "out" : $"in@{mgr.GetAddress():X}";
        if (GameStateTracker.HasChanged("wt_field_gate", gateSig))
            API.LogInfo($"[SF6Access] WT field gate: {(mgr == null ? "out (AvatarManager not loaded)" : $"in (avatar field, instance {gateSig})")}");

        if (mgr == null)
        {
            if (wantsNearby)
                API.LogInfo("[SF6Access] Nearby key pressed but AvatarManager not loaded (not in World Tour)");
            // Reset so re-entering the field re-announces the first target and
            // reads the surroundings out again.
            GameStateTracker.Remove(CURRENT_TARGET_KEY);
            GameStateTracker.Remove(SECTION_KEY);
            _arrivalPending = false;
            _pollCounter = 0;
            return;
        }

        // Arriving somewhere new reads out the surroundings by itself (user rule
        // 2026-08-14). The section id is the game's own notion of "where am I",
        // so it changes exactly on arrival and not while walking around inside a
        // place. The readout is DEFERRED rather than fired here: arrival often
        // coincides with a dialogue or a prompt, and consuming the change while
        // muted would lose the announcement entirely.
        // Section id 0 means "unavailable", not "section zero" — treating it as a
        // real value makes the id flap between 0 and the true one and fires an
        // arrival on every flap.
        uint section = WorldTourStateService.CurrentSectionId;
        if (section != 0 && GameStateTracker.HasChanged(SECTION_KEY, section.ToString()))
            _arrivalPending = true;

        if (wantsNearby)
        {
            _arrivalPending = false;   // asked for it manually; no need to repeat
            AnnounceNearby(manual: true);
        }
        else if (_arrivalPending && ArrivalReadoutAllowed(mgr))
        {
            _arrivalPending = false;
            AnnounceArrival(mgr);
        }

        if (++_pollCounter < POLL_INTERVAL) return;
        _pollCounter = 0;
        AnnounceCurrentTargetChange();
    }

    /// <summary>What arriving somewhere says: HOW MANY people are around, and
    /// nothing else.
    ///
    /// <para>It used to read the whole list, and in a busy district that is
    /// eighteen names with bearings and distances that nobody asked for and
    /// nobody can hold in their head. A count tells the player what kind of place
    /// they have walked into, which is the entire point of an arrival
    /// announcement; the detail is one keypress away on the on-demand radar,
    /// where it was requested.</para>
    /// </summary>
    private static void AnnounceArrival(ManagedObject mgr)
    {
        int count = ReadInteractables().Count;
        if (count == 0) count = AvatarFieldReader.ReadOthers(mgr).Count;
        // Nothing around is not worth saying out of the blue — only in answer to
        // the key.
        if (count == 0) return;

        _lastArrivalTick = System.Environment.TickCount64;
        ScreenReaderService.Speak(LocalizedText.NearbyCount(count), interrupt: true);
    }

    /// <summary>Whether the automatic arrival readout may speak right now. The
    /// on-demand key deliberately bypasses all of this: an explicit press is a
    /// request, and a request must always be answered.</summary>
    private static bool ArrivalReadoutAllowed(ManagedObject mgr)
    {
        // Field-and-menu gate first: AvatarManager alone survives leaving the
        // field, which is what let this speak in the main menu.
        FieldPresenceService.Refresh();
        if (!FieldPresenceService.CanSpeak) return false;
        // A floor on how often arrival can speak at all, whatever the section id
        // does. Belt and braces against the flapping that produced the spam.
        if (System.Environment.TickCount64 - _lastArrivalTick < ARRIVAL_MIN_GAP_MS) return false;
        if (SF6Access.Hooks.SpTalkNovelHooks.DialogueActive) return false;
        // The panel guide owns the objective and the mic during its tutorial.
        if (PadGuideHooks.Active) return false;
        if (System.Environment.TickCount64 - ScreenReaderService.LastInterruptTick < READER_HOLD_MS) return false;
        // Standing on top of something: the target-change reader below is already
        // announcing it, and a full list would just repeat it with extras.
        return AvatarFieldReader.GetAccessInfoCount(mgr) == 0;
    }

    /// <summary>Announce the nearest interactable's name+kind when it changes.</summary>
    private static void AnnounceCurrentTargetChange()
    {
        // This reader had NO gate at all — it predates the others and only ever
        // checked that the field was loaded, which is why it kept saying
        // "…, persona" from inside menus and during battles.
        FieldPresenceService.Refresh();
        if (!FieldPresenceService.CanSpeak) return;

        var list = ReadInteractables();
        if (list.Count == 0)
        {
            GameStateTracker.Remove(CURRENT_TARGET_KEY);
            return;
        }

        var nearest = Nearest(list);
        string spoken = Describe(nearest);
        if (string.IsNullOrEmpty(spoken)) return;

        // A CROWD makes the nearest interactable flicker between people on every
        // poll, and announcing each flip turns a walk down a busy street into a
        // running commentary. Requiring the same target across several
        // consecutive polls turns it back into "you walked up to somebody".
        if (spoken != _pendingTarget)
        {
            _pendingTarget = spoken;
            _pendingPolls = 1;
            return;
        }
        if (_pendingPolls < TARGET_STABLE_POLLS) { _pendingPolls++; return; }

        if (GameStateTracker.HasChanged(CURRENT_TARGET_KEY, spoken))
            ScreenReaderService.Speak(spoken, interrupt: false);
    }

    /// <summary>On-demand: list the nearby interactables, nearest first. The
    /// access list only covers arm's-length targets, so when it's empty fall
    /// back to the field's avatar list with real distances — hot/cold guidance
    /// toward a DISTANT NPC (the "walk to Luke" case).</summary>
    private static void AnnounceNearby(bool manual)
    {
        var list = ReadInteractables();
        if (list.Count == 0)
        {
            if (AnnounceNearbyFromAvatarList()) return;
            // "Nothing nearby" is an ANSWER, so it is only ever spoken to someone
            // who asked. The automatic arrival readout stays silent instead: it
            // fires wherever the section id moves, and announcing emptiness there
            // is how the main menu ended up repeating "nada cerca".
            if (manual) ScreenReaderService.Speak(LocalizedText.NothingNearby(), interrupt: true);
            return;
        }

        list.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        var parts = new List<string>(list.Count);
        foreach (var it in list)
        {
            string d = Describe(it);
            if (!string.IsNullOrEmpty(d)) parts.Add(d);
        }
        if (parts.Count == 0) return;

        string header = LocalizedText.NearbyCount(parts.Count);
        ScreenReaderService.Speak($"{header}: {Join(parts)}", interrupt: true);
    }

    /// <summary>Read every entry of <c>AvatarManager.CurrentAccessInfoList</c>
    /// into name/kind/distance tuples.</summary>
    private static List<Interactable> ReadInteractables()
    {
        var result = new List<Interactable>();
        var mgr = WorldTourStateService.GetAvatarManager();
        if (mgr == null) return result;

        var list = AvatarFieldReader.GetAccessInfoList(mgr);
        int count = FlowHelper.GetListCount(list);
        for (int i = 0; i < count; i++)
        {
            var access = FlowHelper.GetListItem(list, i);                    // AvatarManager.AccessInfo
            var info = AvatarFieldReader.GetProp(access, "TargetInfo");      // IAccessTargetSearcher.AccessTargetInfo
            var target = AvatarFieldReader.GetProp(info, "Target");          // AvatarAccessTargetBase

            // GetDispName / GetContactUIType are interface methods on differing
            // concrete subtypes (WTNpcAccessTarget, WTOmAccessTargetSimple, ...);
            // FlowHelper.Call dispatches correctly per instance (don't cache the
            // Method — a cache from one subtype misfires on another).
            string name = FlowHelper.CleanTags(FlowHelper.Call(target, "GetDispName") as string)?.Trim();
            int contactType = target != null ? AvatarFieldReader.ReadContactType(target) : -1;
            float distance = FlowHelper.ReadFloatField(info, "Distance", 0f);

            if (string.IsNullOrEmpty(name)) continue;
            result.Add(new Interactable(name, contactType, distance));
        }
        return result;
    }

    /// <summary>Fallback radar for the on-demand key: every OTHER avatar in the
    /// field by name, camera-relative clock hour and metric distance, nearest
    /// first. Returns false when nothing could be read, so the caller falls
    /// back to "nothing nearby".</summary>
    private static bool AnnounceNearbyFromAvatarList()
    {
        var mgr = WorldTourStateService.GetAvatarManager();
        var others = AvatarFieldReader.ReadOthers(mgr);
        if (others.Count == 0) return false;

        // Clock frame: camera forward (stick-relative "12").
        var camFwd = FieldDirectionService.GetCameraForward();

        var parts = new List<string>(others.Count);
        foreach (var o in others)
        {
            int meters = (int)System.Math.Round(o.Dist);
            int hour = FieldDirectionService.ClockHour(camFwd, o.Dx, o.Dz);
            string what = AvatarFieldReader.DescribeAvatar(o.Avatar) ?? LocalizedText.ContactPerson();
            parts.Add(hour > 0
                ? LocalizedText.AtClockMeters(what, hour, meters)
                : LocalizedText.AtMeters(what, meters));
        }

        ScreenReaderService.Speak(
            $"{LocalizedText.NearbyCount(parts.Count)}: {Join(parts)}", interrupt: true);
        return true;
    }

    /// <summary>The nearest few, then a count of the rest. A crowded hub can put
    /// dozens of people in range, and a readout that names all of them is a wall
    /// of speech nobody can hold in their head — but silently dropping the tail
    /// would misreport the place as emptier than it is, so the remainder is
    /// spoken as a number.</summary>
    private static string Join(List<string> parts)
    {
        if (parts.Count <= MAX_NEARBY_SPOKEN) return string.Join(", ", parts);
        var head = parts.GetRange(0, MAX_NEARBY_SPOKEN);
        return $"{string.Join(", ", head)}, {LocalizedText.AndMore(parts.Count - MAX_NEARBY_SPOKEN)}";
    }

    /// <summary>"Ryu, person" — the interactable's name plus its kind word.</summary>
    private static string Describe(Interactable it)
    {
        string kind = AvatarFieldReader.KindWord(it.ContactType);
        return string.IsNullOrEmpty(kind) ? it.Name : $"{it.Name}, {kind}";
    }

    private static Interactable Nearest(List<Interactable> list)
    {
        var best = list[0];
        foreach (var it in list)
            if (it.Distance < best.Distance) best = it;
        return best;
    }
}
