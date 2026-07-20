using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// World Tour field-awareness reader (WT-1). An always-on monitor (no menu focus
/// to hook): while the player is in the World Tour field
/// (<see cref="WorldTourStateService.IsInWorldTour"/>) it
/// <list type="bullet">
/// <item>announces the CURRENT interaction target when it changes — the nearest
///   thing the player can talk to / examine, by name and kind; and</item>
/// <item>on an on-demand key, lists the nearby interactables sorted by the game's
///   own distance.</item>
/// </list>
/// Both come from <c>AvatarManager.CurrentAccessInfoList</c>, whose entries carry
/// the target plus the game's already-computed <c>Distance</c>/<c>Angle</c> — so
/// nothing positional is recomputed here.
///
/// <para>The distant-avatar readout speaks a camera-relative clock hour
/// ("Luke, master at 12 o'clock, 5 meters") via
/// <see cref="FieldDirectionService"/> — fully calibrated in game 2026-07-20
/// (forward axis, left/right handedness, live update under camera rotation).</para>
/// </summary>
public class FieldAwarenessHooks
{
    // app.HudDef.ContactUIType — the kind of interactable (source of the enum
    // values, not magic numbers): None = -1, NPC = 0, Legendary = 1, OM = 2,
    // OtherPlayer = 3. "Legendary" is a Master; "OM" is an object/gimmick.
    private const int CONTACT_NPC = 0;
    private const int CONTACT_LEGENDARY = 1;
    private const int CONTACT_OM = 2;
    private const int CONTACT_OTHER_PLAYER = 3;

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

        var list = FlowHelper.GetObjectField(mgr, "CurrentAccessInfoList")
                   ?? FlowHelper.Call(mgr, "get_CurrentAccessInfoList") as ManagedObject;
        int count = FlowHelper.GetListCount(list);
        for (int i = 0; i < count; i++)
        {
            var access = FlowHelper.GetListItem(list, i);          // AvatarManager.AccessInfo
            var info = GetProp(access, "TargetInfo");              // IAccessTargetSearcher.AccessTargetInfo
            var target = GetProp(info, "Target");                  // AvatarAccessTargetBase

            // GetDispName / GetContactUIType are interface methods on differing
            // concrete subtypes (WTNpcAccessTarget, WTOmAccessTargetSimple, ...);
            // FlowHelper.Call dispatches correctly per instance (don't cache the
            // Method — a cache from one subtype misfires on another).
            string name = FlowHelper.CleanTags(FlowHelper.Call(target, "GetDispName") as string)?.Trim();
            int contactType = target != null ? ReadContactType(target) : -1;
            float distance = FlowHelper.ReadFloatField(info, "Distance", 0f);

            if (string.IsNullOrEmpty(name)) continue;
            result.Add(new Interactable(name, contactType, distance));
        }
        return result;
    }

    /// <summary>The field's avatar list, unwrapped: <c>AvatarList</c> is a
    /// <c>SafeList&lt;T&gt;</c> wrapper whose <c>get_Count</c> isn't the standard
    /// accessor, so fall back to its inner <c>System...List</c> field.</summary>
    private static ManagedObject GetAvatarList(ManagedObject mgr)
    {
        var avatars = FlowHelper.GetObjectField(mgr, "AvatarList")
                      ?? FlowHelper.Call(mgr, "get_AvatarList") as ManagedObject;
        if (avatars == null) return null;
        if (FlowHelper.GetListCount(avatars) == 0)
        {
            var inner = FindInnerList(avatars);
            if (inner != null) return inner;
        }
        return avatars;
    }

    /// <summary>Fallback radar for the on-demand key: the OTHER avatars in the
    /// field by real distance from the player's own avatar
    /// (|otherPos − playerPos|; RE Engine world units are meters). This is the
    /// hot/cold guidance toward a DISTANT NPC — walk, press again, hear the
    /// distance shrink. Returns false when nothing could be read, so the caller
    /// falls back to "nothing nearby".</summary>
    private static bool AnnounceNearbyFromAvatarList()
    {
        var mgr = WorldTourStateService.GetAvatarManager();
        if (mgr == null) return false;
        var avatars = GetAvatarList(mgr);
        int n = FlowHelper.GetListCount(avatars);
        if (n == 0) return false;

        var entries = new List<(ManagedObject av, string type)>(n);
        for (int i = 0; i < n && i < 24; i++)
        {
            var av = FlowHelper.GetListItem(avatars, i);
            if (av != null) entries.Add((av, av.GetTypeDefinition()?.GetFullName() ?? ""));
        }

        // The reference point is the player's own avatar. An EXACT (0,0,0) world
        // position means the component read failed (nothing stands at the exact
        // origin), so treat it as unreadable rather than announce garbage.
        (float x, float y, float z) player = default;
        bool playerOk = false;
        foreach (var (av, type) in entries)
        {
            if (!type.Contains("AvatarPlayer")) continue;
            var (x, y, z, ok) = ReadAvatarWorldPos(av);
            if (ok && (x != 0f || y != 0f || z != 0f)) { player = (x, y, z); playerOk = true; }
            break;
        }
        if (!playerOk) return false;

        // Clock frame: camera forward (stick-relative "12").
        var camFwd = FieldDirectionService.GetCameraForward();

        var others = new List<(float dist, float dx, float dz, ManagedObject av)>();
        foreach (var (av, type) in entries)
        {
            if (type.Contains("AvatarPlayer")) continue;
            var (x, y, z, ok) = ReadAvatarWorldPos(av);
            if (!ok || (x == 0f && y == 0f && z == 0f)) continue;
            float dx = x - player.x, dy = y - player.y, dz = z - player.z;
            others.Add(((float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz), dx, dz, av));
        }
        if (others.Count == 0) return false;

        others.Sort((a, b) => a.dist.CompareTo(b.dist));
        var parts = new List<string>(others.Count);
        foreach (var (dist, dx, dz, av) in others)
        {
            int meters = (int)System.Math.Round(dist);
            int hour = FieldDirectionService.ClockHour(camFwd, dx, dz);
            string what = DescribeDistantAvatar(av) ?? LocalizedText.ContactPerson();
            parts.Add(hour > 0
                ? LocalizedText.AtClockMeters(what, hour, meters)
                : LocalizedText.AtMeters(what, meters));
        }

        ScreenReaderService.Speak(
            $"{LocalizedText.NearbyCount(parts.Count)}: {string.Join(", ", parts)}", interrupt: true);
        return true;
    }

    /// <summary>Name + kind of a DISTANT avatar, or null. <c>GetDispName</c> is
    /// not on <c>AvatarBase</c>, but the access-target component that carries it
    /// (<c>WTNpcAccessTarget</c> etc. — the very component the in-range list
    /// hands us) is a SIBLING component on the avatar's own GameObject, so walk
    /// the GameObject's component array. <c>WTNpcContext.NpcName</c> is the
    /// fallback name source (crowd NPCs may have no access target).</summary>
    private static string DescribeDistantAvatar(ManagedObject avatar)
    {
        try
        {
            var go = FlowHelper.Call(avatar, "get_GameObject") as ManagedObject;
            var comps = FlowHelper.Call(go, "get_Components") as ManagedObject;
            int n = FlowHelper.GetListCount(comps);
            ManagedObject npcContext = null;
            for (int i = 0; i < n; i++)
            {
                var c = FlowHelper.GetListItem(comps, i);
                string t = c?.GetTypeDefinition()?.GetFullName() ?? "";
                if (t.Contains("AccessTarget"))
                {
                    // Searcher components also match the substring but have no
                    // GetDispName — the empty-name check skips them naturally.
                    string name = FlowHelper.CleanTags(FlowHelper.Call(c, "GetDispName") as string)?.Trim();
                    if (!string.IsNullOrEmpty(name))
                    {
                        string kind = KindWord(ReadContactType(c));
                        return string.IsNullOrEmpty(kind) ? name : $"{name}, {kind}";
                    }
                }
                else if (t.EndsWith("WTNpcContext"))
                {
                    npcContext = c;
                }
            }
            if (npcContext != null)
            {
                string name = FlowHelper.CleanTags(FlowHelper.Call(npcContext, "get_NpcName") as string)?.Trim();
                if (!string.IsNullOrEmpty(name)) return name;
            }
        }
        catch { }
        return null;
    }

    /// <summary>An inner <c>System...List`1</c> field of a wrapper object (e.g.
    /// SafeList), read so it can be iterated with the standard list helpers.</summary>
    private static ManagedObject FindInnerList(ManagedObject wrapper)
    {
        try
        {
            var fields = wrapper.GetTypeDefinition()?.GetFields();
            if (fields == null) return null;
            foreach (var f in fields)
            {
                string ft = f.Type?.GetFullName();
                if (ft != null && ft.Contains("System.Collections.Generic.List"))
                {
                    var inner = FlowHelper.GetObjectField(wrapper, f.Name);
                    if (inner != null) return inner;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>World position of an avatar via its GameObject's Transform.
    /// <c>DrawObj</c> is NOT a member of AvatarBase (in the decompiled source it
    /// only exists inside the nested per-body-part <c>WTBodyDisp</c> struct), so
    /// the avatar's own <c>Component.GameObject</c> is the entry point.</summary>
    private static (float x, float y, float z, bool ok) ReadAvatarWorldPos(ManagedObject avatar)
    {
        try
        {
            var go = FlowHelper.Call(avatar, "get_GameObject") as ManagedObject;
            var tr = FlowHelper.Call(go, "get_Transform") as ManagedObject;
            var pos = FlowHelper.Call(tr, "get_Position");
            if (pos == null) return (0f, 0f, 0f, false);

            float px = FlowHelper.ReadVecComponent(pos, "x");
            float py = FlowHelper.ReadVecComponent(pos, "y");
            float pz = FlowHelper.ReadVecComponent(pos, "z");
            bool ok = float.IsFinite(px) && float.IsFinite(py) && float.IsFinite(pz);
            return (px, py, pz, ok);
        }
        catch { }
        return (0f, 0f, 0f, false);
    }

    /// <summary>"Ryu, person" — the interactable's name plus its kind word.</summary>
    private static string Describe(Interactable it)
    {
        string kind = KindWord(it.ContactType);
        return string.IsNullOrEmpty(kind) ? it.Name : $"{it.Name}, {kind}";
    }

    private static Interactable Nearest(List<Interactable> list)
    {
        var best = list[0];
        foreach (var it in list)
            if (it.Distance < best.Distance) best = it;
        return best;
    }

    /// <summary>Localized kind word for a HudDef.ContactUIType.</summary>
    private static string KindWord(int contactType) => contactType switch
    {
        CONTACT_NPC => LocalizedText.ContactPerson(),
        CONTACT_LEGENDARY => LocalizedText.ContactMaster(),
        CONTACT_OM => LocalizedText.ContactObject(),
        CONTACT_OTHER_PLAYER => LocalizedText.ContactPlayer(),
        _ => null,
    };

    private static int ReadContactType(ManagedObject target)
    {
        var boxed = FlowHelper.Call(target, "GetContactUIType");
        return boxed != null ? System.Convert.ToInt32(boxed) : -1;
    }

    /// <summary>Read a getter-only property: field (incl. backing field) first,
    /// then the <c>get_</c> accessor — the WT access structs expose these as
    /// properties with no plain field.</summary>
    private static ManagedObject GetProp(ManagedObject obj, string name)
    {
        if (obj == null) return null;
        return FlowHelper.GetObjectField(obj, name)
               ?? FlowHelper.Call(obj, "get_" + name) as ManagedObject;
    }
}
