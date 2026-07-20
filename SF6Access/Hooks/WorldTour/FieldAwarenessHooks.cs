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
/// <para>NOTE (first-live calibration): the nearby list is ordered by the game's
/// <c>Distance</c> (ordering is reliable regardless of unit) but does not yet
/// speak a metric distance or clock direction — those need the units/reference
/// frame of <c>Distance</c>/<c>Angle</c> confirmed against an F8/F9 dump before
/// they can be phrased without guessing. That is the immediate WT-1 follow-up.</para>
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

        if (wantsNearby) { AnnounceNearby(); LogAvatarPositionsDiag(mgr); }

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

        // DIAGNOSTIC (WT-1 bring-up): report AvatarManager reachability + the
        // counts of ALL THREE access lists, only when they change, so one field
        // walk tells us which list actually populates near an interactable
        // (walk-through zones vs press-to-interact targets may live in
        // different lists). Remove once the radar list is confirmed.
        if (GameStateTracker.HasChanged("wt_diag_avatarmgr", mgr == null ? "null" : "ok"))
            API.LogInfo($"[SF6Access] WT radar: AvatarManager = {(mgr == null ? "NULL (GetManagedSingleton failed)" : "resolved")}");
        if (mgr == null) return result;

        var list = FlowHelper.GetObjectField(mgr, "CurrentAccessInfoList")
                   ?? FlowHelper.Call(mgr, "get_CurrentAccessInfoList") as ManagedObject;
        int count = FlowHelper.GetListCount(list);
        LogListCounts(mgr, count);
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

            // DIAGNOSTIC (WT-1 bring-up): a target in range (cur:1) that never
            // announced means one of these reads returned empty — log the raw
            // per-entry reads so the next in-range tick pinpoints which. Only
            // fires when the list is non-empty (i.e. standing at an interactable),
            // so no spam. Remove once the announce is confirmed.
            API.LogInfo($"[SF6Access] radar entry[{i}]: access={(access!=null)} info={(info!=null)} target={(target!=null)} name=[{name}] type={contactType} dist={distance}");

            if (string.IsNullOrEmpty(name)) continue;
            result.Add(new Interactable(name, contactType, distance));
        }
        return result;
    }

    /// <summary>DIAGNOSTIC (WT-1 "walk to Luke" bring-up): on the on-demand key,
    /// log every avatar in <c>AvatarManager.AvatarList</c> with its concrete type,
    /// name and WORLD position (DrawObj → Transform → Position). One field walk
    /// then reveals which entry is the player vs Luke and their coordinates — the
    /// inputs for the future clock+distance guidance to a DISTANT objective (the
    /// access list only covers targets already in range). Remove once built.</summary>
    private static void LogAvatarPositionsDiag(ManagedObject mgr)
    {
        var avatars = GetAvatarList(mgr);
        int n = FlowHelper.GetListCount(avatars);
        API.LogInfo($"[SF6Access] pos-diag: avatar list type=[{avatars?.GetTypeDefinition()?.GetFullName() ?? "null"}] count={n}");

        for (int i = 0; i < n && i < 24; i++)
        {
            try
            {
                var av = FlowHelper.GetListItem(avatars, i);
                if (av == null) { API.LogInfo($"[SF6Access] pos-diag[{i}] = null"); continue; }
                string type = av.GetTypeDefinition()?.GetFullName() ?? "?";
                // One-shot member dump of the NPC avatar type: GetDispName is NOT
                // on AvatarBase, so this reveals where the NPC's name/id actually
                // lives for the future named readout.
                if (type.Contains("AvatarNpc")) DumpMembersOnce(av);
                string name = FlowHelper.CleanTags(FlowHelper.Call(av, "GetDispName") as string);
                var (x, y, z, ok) = ReadAvatarWorldPos(av);
                // Invariant formatting: under a Spanish locale the decimal comma
                // collided with the separators and made the log unreadable.
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                API.LogInfo($"[SF6Access] pos-diag[{i}] type=[{type}] name=[{name}] " +
                            $"pos=(x={x.ToString("0.00", inv)} y={y.ToString("0.00", inv)} z={z.ToString("0.00", inv)}) posOk={ok}");
            }
            catch (System.Exception ex) { API.LogInfo($"[SF6Access] pos-diag[{i}] threw {ex.Message}"); }
        }
    }

    /// <summary>The field's avatar list, unwrapped: <c>AvatarList</c> is a
    /// <c>SafeList&lt;T&gt;</c> wrapper whose <c>get_Count</c> isn't the standard
    /// accessor, so fall back to its inner <c>System...List</c> field. On an
    /// unreachable list the owner's members are dumped once so the log names the
    /// real accessor (stale pointer vs runtime-renamed member).</summary>
    private static ManagedObject GetAvatarList(ManagedObject mgr)
    {
        var avatars = FlowHelper.GetObjectField(mgr, "AvatarList")
                      ?? FlowHelper.Call(mgr, "get_AvatarList") as ManagedObject;
        if (avatars == null) { DumpMembersOnce(mgr); return null; }
        if (FlowHelper.GetListCount(avatars) == 0)
        {
            var inner = FindInnerList(avatars);
            if (inner != null) return inner;
            DumpMembersOnce(avatars);
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

        var others = new List<float>();
        foreach (var (av, type) in entries)
        {
            if (type.Contains("AvatarPlayer")) continue;
            var (x, y, z, ok) = ReadAvatarWorldPos(av);
            if (!ok || (x == 0f && y == 0f && z == 0f)) continue;
            float dx = x - player.x, dy = y - player.y, dz = z - player.z;
            others.Add((float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz));
        }
        if (others.Count == 0) return false;

        others.Sort();
        var parts = new List<string>(others.Count);
        foreach (float dist in others)
            parts.Add(LocalizedText.AtMeters(LocalizedText.ContactPerson(), (int)System.Math.Round(dist)));

        ScreenReaderService.Speak(
            $"{LocalizedText.NearbyCount(parts.Count)}: {string.Join(", ", parts)}", interrupt: true);
        return true;
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

    /// <summary>One-shot PER TYPE: log an object's fields and interesting methods
    /// so the correct accessor / name source can be identified from a single run.</summary>
    private static readonly HashSet<string> _dumpedTypes = new();
    private static void DumpMembersOnce(ManagedObject obj)
    {
        try
        {
            var td = obj?.GetTypeDefinition();
            string typeName = td?.GetFullName() ?? "?";
            if (!_dumpedTypes.Add(typeName)) return;
            API.LogInfo($"[SF6Access] members of [{typeName}]");
            var fields = td?.GetFields();
            if (fields != null)
                foreach (var f in fields)
                    try { API.LogInfo($"[SF6Access]   field [{f.Type?.GetFullName()}] {f.Name}"); } catch { }
            var methods = td?.GetMethods();
            if (methods != null)
            {
                int c = 0;
                foreach (var m in methods)
                {
                    try
                    {
                        string mn = m.Name;
                        if (mn != null && (mn.Contains("Count") || mn.Contains("Item") ||
                            mn.Contains("List") || mn.Contains("Enumerat") || mn == "get_Length" ||
                            mn.Contains("Avatar") || mn.Contains("Access") ||
                            mn.Contains("Name") || mn.Contains("Npc") || mn.Contains("Disp") ||
                            mn.Contains("Id")))
                        {
                            API.LogInfo($"[SF6Access]   method {mn}");
                            if (++c > 30) break;
                        }
                    }
                    catch { }
                }
            }
        }
        catch (System.Exception ex) { API.LogInfo($"[SF6Access] member dump threw {ex.Message}"); }
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

            // DIAGNOSTIC (one-shot per vec type): if components still read wrong,
            // this names the returned struct type and its real field layout, so
            // the next log says whether the read strategy or the source is at
            // fault. Remove once positions are confirmed sane.
            if (pos is ManagedObject vecObj) DumpMembersOnce(vecObj);

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

    /// <summary>DIAGNOSTIC (WT-1 bring-up): log the counts of the three
    /// AvatarManager access lists whenever they change, so a single field walk
    /// reveals which list populates near an interactable. Remove once confirmed.</summary>
    private static void LogListCounts(ManagedObject mgr, int currentCount)
    {
        // NOTE: CurrentFailedMostNearInfoList was dropped — the runtime type has
        // no such member ("Method not found" in the 2026-07-19 log); it existed
        // only in the decompiled source.
        int def = ListCount(mgr, "CurrentDefaultAccessInfoList");
        string sig = $"cur:{currentCount} def:{def}";
        if (GameStateTracker.HasChanged("wt_diag_lists", sig))
            API.LogInfo($"[SF6Access] WT radar lists → {sig}");
    }

    private static int ListCount(ManagedObject mgr, string field)
    {
        var list = FlowHelper.GetObjectField(mgr, field)
                   ?? FlowHelper.Call(mgr, "get_" + field) as ManagedObject;
        return FlowHelper.GetListCount(list);
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
