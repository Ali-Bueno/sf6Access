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
            // Reset so re-entering the field re-announces the first target.
            GameStateTracker.Remove(CURRENT_TARGET_KEY);
            _pollCounter = 0;
            return;
        }

        if (wantsNearby) AnnounceNearby();

        if (++_pollCounter < POLL_INTERVAL) return;
        _pollCounter = 0;
        AnnounceCurrentTargetChange();
    }

    /// <summary>Announce the nearest interactable's name+kind when it changes.</summary>
    private static void AnnounceCurrentTargetChange()
    {
        var list = ReadInteractables();
        if (list.Count == 0)
        {
            GameStateTracker.Remove(CURRENT_TARGET_KEY);
            return;
        }

        var nearest = Nearest(list);
        string spoken = Describe(nearest);
        if (string.IsNullOrEmpty(spoken)) return;

        if (GameStateTracker.HasChanged(CURRENT_TARGET_KEY, spoken))
            ScreenReaderService.Speak(spoken, interrupt: false);
    }

    /// <summary>On-demand: list the nearby interactables, nearest first. The
    /// access list only covers arm's-length targets, so when it's empty fall
    /// back to the field's avatar list with real distances — hot/cold guidance
    /// toward a DISTANT NPC (the "walk to Luke" case).</summary>
    private static void AnnounceNearby()
    {
        var list = ReadInteractables();
        if (list.Count == 0)
        {
            if (!AnnounceNearbyFromAvatarList())
                ScreenReaderService.Speak(LocalizedText.NothingNearby(), interrupt: true);
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
        ScreenReaderService.Speak($"{header}: {string.Join(", ", parts)}", interrupt: true);
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
            $"{LocalizedText.NearbyCount(parts.Count)}: {string.Join(", ", parts)}", interrupt: true);
        return true;
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
