using System.Collections.Generic;
using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Shared readers over the World Tour avatar field (<c>AvatarManager</c>) used
/// by both the on-demand radar (<c>FieldAwarenessHooks</c>) and the continuous
/// tracker (<c>FieldTrackingHooks</c>): the in-range access list, the global
/// avatar list with world positions, and avatar naming.
///
/// <para>All findings are runtime-confirmed 2026-07-20 — see
/// <c>docs/sf6-screens.md</c> § World Tour — field awareness for the full
/// reference (SafeList unwrapping, sibling access-target component, etc.).</para>
/// </summary>
public static class AvatarFieldReader
{
    // app.HudDef.ContactUIType — the kind of interactable (source of the enum
    // values, not magic numbers): None = -1, NPC = 0, Legendary = 1, OM = 2,
    // OtherPlayer = 3. "Legendary" is a Master; "OM" is an object/gimmick.
    public const int CONTACT_NPC = 0;
    public const int CONTACT_LEGENDARY = 1;
    public const int CONTACT_OM = 2;
    public const int CONTACT_OTHER_PLAYER = 3;

    /// <summary>Another avatar in the field, with its offset from the player
    /// (dx/dz on the ground plane) and full 3D distance in meters.</summary>
    public readonly struct Other
    {
        public readonly ManagedObject Avatar;
        public readonly float Dist;
        public readonly float Dx;
        public readonly float Dz;
        public Other(ManagedObject avatar, float dist, float dx, float dz)
        {
            Avatar = avatar; Dist = dist; Dx = dx; Dz = dz;
        }
    }

    /// <summary>The in-range access list (<c>CurrentAccessInfoList</c>) — the
    /// arm's-length "what can I interact with right now" list.</summary>
    public static ManagedObject GetAccessInfoList(ManagedObject mgr)
        => FlowHelper.GetObjectField(mgr, "CurrentAccessInfoList")
           ?? FlowHelper.Call(mgr, "get_CurrentAccessInfoList") as ManagedObject;

    /// <summary>Number of in-range interactables (0 when far from everything).</summary>
    public static int GetAccessInfoCount(ManagedObject mgr)
        => mgr == null ? 0 : FlowHelper.GetListCount(GetAccessInfoList(mgr));

    /// <summary>Every OTHER avatar in the field with real distances from the
    /// player's own avatar (|otherPos − playerPos|; RE Engine world units are
    /// meters), sorted nearest first. Empty when the field can't be read (no
    /// manager, no player position). An EXACT (0,0,0) world position means the
    /// component read failed (nothing stands at the exact origin), so those
    /// avatars are skipped rather than announced with garbage distances.</summary>
    public static List<Other> ReadOthers(ManagedObject mgr)
    {
        var result = new List<Other>();
        if (mgr == null) return result;
        var avatars = GetAvatarList(mgr);
        int n = FlowHelper.GetListCount(avatars);
        if (n == 0) return result;

        var entries = new List<(ManagedObject av, string type)>(n);
        for (int i = 0; i < n && i < MAX_AVATARS; i++)
        {
            var av = FlowHelper.GetListItem(avatars, i);
            if (av != null) entries.Add((av, av.GetTypeDefinition()?.GetFullName() ?? ""));
        }

        (float x, float y, float z) player = default;
        bool playerOk = false;
        foreach (var (av, type) in entries)
        {
            if (!type.Contains("AvatarPlayer")) continue;
            var (x, y, z, ok) = ReadWorldPos(av);
            if (ok && (x != 0f || y != 0f || z != 0f)) { player = (x, y, z); playerOk = true; }
            break;
        }
        if (!playerOk) return result;

        foreach (var (av, type) in entries)
        {
            if (type.Contains("AvatarPlayer")) continue;
            var (x, y, z, ok) = ReadWorldPos(av);
            if (!ok || (x == 0f && y == 0f && z == 0f)) continue;
            float dx = x - player.x, dy = y - player.y, dz = z - player.z;
            result.Add(new Other(av, (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz), dx, dz));
        }
        result.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        return result;
    }

    // Sanity cap on how many avatar entries to walk per read (crowded hubs);
    // avatars beyond it are simply not announced this press/tick.
    private const int MAX_AVATARS = 24;

    /// <summary>Name + kind of an avatar ("Luke, master"), or null.
    /// <c>GetDispName</c> is not on <c>AvatarBase</c>, but the access-target
    /// component that carries it (<c>WTNpcAccessTarget</c> etc.) is a SIBLING
    /// component on the avatar's own GameObject, so walk the GameObject's
    /// component array — works at ANY range. <c>WTNpcContext.NpcName</c> is the
    /// fallback name source (crowd NPCs may have no access target).</summary>
    public static string DescribeAvatar(ManagedObject avatar)
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

    /// <summary>World position of an avatar via its GameObject's Transform.
    /// <c>DrawObj</c> is NOT a member of AvatarBase (in the decompiled source it
    /// only exists inside the nested per-body-part <c>WTBodyDisp</c> struct), so
    /// the avatar's own <c>Component.GameObject</c> is the entry point.</summary>
    public static (float x, float y, float z, bool ok) ReadWorldPos(ManagedObject avatar)
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

    /// <summary>Localized kind word for a HudDef.ContactUIType.</summary>
    public static string KindWord(int contactType) => contactType switch
    {
        CONTACT_NPC => LocalizedText.ContactPerson(),
        CONTACT_LEGENDARY => LocalizedText.ContactMaster(),
        CONTACT_OM => LocalizedText.ContactObject(),
        CONTACT_OTHER_PLAYER => LocalizedText.ContactPlayer(),
        _ => null,
    };

    /// <summary>The target's HudDef.ContactUIType, dispatched per concrete
    /// subtype (don't cache the Method across instances).</summary>
    public static int ReadContactType(ManagedObject target)
    {
        var boxed = FlowHelper.Call(target, "GetContactUIType");
        return boxed != null ? System.Convert.ToInt32(boxed) : -1;
    }

    /// <summary>Read a getter-only property: field (incl. backing field) first,
    /// then the <c>get_</c> accessor — the WT access structs expose these as
    /// properties with no plain field.</summary>
    public static ManagedObject GetProp(ManagedObject obj, string name)
    {
        if (obj == null) return null;
        return FlowHelper.GetObjectField(obj, name)
               ?? FlowHelper.Call(obj, "get_" + name) as ManagedObject;
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
}
