using System;
using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Every breakable street prop near the player that is still INTACT — crates,
/// balls, signs, barrels — as plain world points a beacon can home on.
///
/// <para><b>Why it exists.</b> Some World Tour missions cannot be finished without
/// smashing things, and a blind player has no way to find them at all. Reported by
/// the tester 2026-09-09: "para avanzar necesito romper unos carteles".</para>
///
/// <para><b>What is scanned.</b> The World Tour smashable is
/// <c>app.worldtour.om.Om001000 : WTCityOM : Behavior</c> (<c>Om001000.cs:10</c>) —
/// it carries <c>Vital</c> (<c>:150</c>), <c>EventDestroy</c> (<c>:254</c>) and the
/// state below. Asking <c>via.Scene.findComponents</c> for that CONCRETE type
/// returns only breakables, so unlike the tutorial panels no name filter is needed.
/// Four sibling families drop items and break too and are scanned alongside it; see
/// <see cref="Families"/>.</para>
///
/// <para><b>"Already broken" is read, and read at the right WIDTH.</b>
/// <c>Om001000.CurrentState</c> is <c>State : byte { Default, Destroyed }</c>
/// (<c>Om001000.cs:115-119,158</c>) — ONE BYTE. Reading it as an int would drag in
/// the neighbouring bytes and hand back garbage, which is a documented trap in this
/// mod. The <c>Destroyed</c> value is resolved from the TDB BY NAME at byte width,
/// never written as a literal. <c>IsDefaultState</c> (<c>:238</c>) is deliberately
/// not used: it is a getter-only property, and those do not dispatch on concrete
/// IL2CPP types.</para>
///
/// <para><b>A family whose state cannot be determined is DROPPED, not guessed.</b>
/// A beacon over rubble is worse than no beacon: it would send the player to smash
/// something that is not there, repeatedly, with no way to tell the mod is wrong.
/// Each family binds its own intact-test at startup and logs the verdict; anything
/// that fails simply never produces a cue.</para>
///
/// <para><b>Nothing engine-owned is kept.</b> The scan runs on a slow timer and
/// copies each object straight into a plain struct. The obvious optimisation —
/// holding the component handles and re-reading them every frame — is exactly the
/// wrapper-lifetime crash this mod has already paid for twice, so it is not done.
/// Props do not move, so a cached position stays true, and a broken one disappears
/// on the next scan.</para>
/// </summary>
public static class BreakableCatalog
{
    /// <summary>How the intact/broken state of a family is expressed. Each of these
    /// exists in the game; none is a guess.</summary>
    private enum StateKind
    {
        /// <summary>A byte-backed enum with a <c>Destroyed</c> member
        /// (<c>Om001000.CurrentState</c>, <c>Om039000.State</c>).</summary>
        ByteEnum,
        /// <summary>A bool that is true once the break animation finished
        /// (<c>Om021000</c>/<c>Om038000.IsDestroyEnd</c>).</summary>
        DoneFlag,
        /// <summary>Remaining health; intact while above zero (<c>Om028000.Vital</c>).</summary>
        Vital,
    }

    private readonly struct Family
    {
        public readonly string TypeName;
        public readonly string Member;
        public readonly StateKind Kind;
        public readonly string EnumName;

        public Family(string typeName, string member, StateKind kind, string enumName = null)
        {
            TypeName = typeName; Member = member; Kind = kind; EnumName = enumName;
        }
    }

    /// <summary>The enum member that means "this one is gone". Matched by NAME, so a
    /// renumbered enum still reads correctly.</summary>
    private const string DESTROYED_MEMBER = "Destroyed";

    /// <summary>The destructible OM families, each with the member that says whether
    /// it is still there. <c>Om001000</c> is the main street prop; the rest are the
    /// other item-dropping destructibles found in the same namespace.</summary>
    private static readonly Family[] Families =
    {
        new("app.worldtour.om.Om001000", "CurrentState", StateKind.ByteEnum,
            "app.worldtour.om.Om001000.State"),
        new("app.worldtour.om.Om039000", "State", StateKind.ByteEnum,
            "app.worldtour.om.Om039000.State"),
        new("app.worldtour.om.Om021000", "IsDestroyEnd", StateKind.DoneFlag),
        new("app.worldtour.om.Om038000", "IsDestroyEnd", StateKind.DoneFlag),
        new("app.worldtour.om.Om028000", "Vital", StateKind.Vital),
    };

    /// <summary>How often the scene is re-scanned. Breakables are static scenery, so
    /// the only thing that changes is one of them disappearing — and a second and a
    /// half between smashing something and the beacon moving on is not a wait
    /// anybody notices. A scene walk per family is far too expensive to do per
    /// frame; this is what keeps the cost bounded.
    ///
    /// <para><b>Raised from 1500 ms once the real scale was measured in game:</b> 250
    /// props in one Metro City district, at roughly seven engine calls each, is about
    /// 1,750 calls per sweep. At 1.5 s that was over a thousand calls a second, which
    /// is the order of magnitude that has crashed this mod before. Four seconds keeps
    /// it near 400/s, and the only cost is that a smashed crate can keep pulsing for a
    /// few seconds longer.</para></summary>
    private const long RESCAN_MS = 4000;

    /// <summary>One intact breakable, copied out of the engine.</summary>
    public readonly struct Breakable
    {
        public readonly float X, Y, Z;
        public Breakable(float x, float y, float z) { X = x; Y = y; Z = z; }

        public float DistanceFrom(float px, float py, float pz)
        {
            float dx = X - px, dy = Y - py, dz = Z - pz;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    private static readonly List<Breakable> Intact = new();
    private static readonly Dictionary<string, int> DestroyedValue = new();
    private static long _scannedAt;
    private static bool _logged;

    /// <summary>Every intact breakable currently in the scene. The list is rebuilt at
    /// most every <see cref="RESCAN_MS"/>; callers may poll as often as they like.</summary>
    public static IReadOnlyList<Breakable> All()
    {
        long now = Environment.TickCount64;
        if (now - _scannedAt >= RESCAN_MS)
        {
            _scannedAt = now;
            Rescan();
        }
        return Intact;
    }

    /// <summary>The nearest intact breakable within <paramref name="rangeM"/>, or
    /// null. Distance is 3D: a crate on a balcony above is not the same place as one
    /// at your feet.</summary>
    public static Breakable? Nearest(float px, float py, float pz, float rangeM)
    {
        Breakable? best = null;
        float bestDist = rangeM;
        foreach (var b in All())
        {
            float d = b.DistanceFrom(px, py, pz);
            if (d > bestDist) continue;
            bestDist = d;
            best = b;
        }
        return best;
    }

    /// <summary>Drop everything. A new city has its own props.</summary>
    public static void Reset()
    {
        Intact.Clear();
        DestroyedValue.Clear();
        _scannedAt = 0;
        _logged = false;
    }

    private static void Rescan()
    {
        Intact.Clear();
        int scanned = 0, families = 0;

        foreach (var family in Families)
        {
            var list = FieldProbeService.SceneComponents(family.TypeName);
            int n = FlowHelper.GetListCount(list);
            if (n == 0) continue;
            families++;

            for (int i = 0; i < n; i++)
            {
                var om = FlowHelper.GetListItem(list, i);
                if (om == null) continue;
                scanned++;

                // Unknown state means DROP IT. A cue over rubble would send the
                // player to smash something that is no longer there.
                if (!IsIntact(family, om)) continue;
                if (!ReadPosition(om, out float x, out float y, out float z)) continue;
                Intact.Add(new Breakable(x, y, z));
            }
        }

        if (!_logged)
        {
            _logged = true;
            API.LogInfo($"[SF6Access] Breakables: {Intact.Count} intact of {scanned} scanned " +
                        $"across {families} of {Families.Length} families");
        }
    }

    /// <summary>Is this one still there? False whenever the answer cannot be
    /// established — the safe direction.</summary>
    private static bool IsIntact(Family family, ManagedObject om)
    {
        try
        {
            switch (family.Kind)
            {
                case StateKind.ByteEnum:
                {
                    int gone = Destroyed(family.EnumName);
                    if (gone < 0) return false;          // enum unreadable: drop the family
                    int state = FlowHelper.ReadByteField(om, family.Member, -1);
                    return state >= 0 && state != gone;
                }
                case StateKind.DoneFlag:
                    return FieldProbeService.Member(om, family.Member, typeof(bool)) is bool done && !done;

                case StateKind.Vital:
                {
                    float vital = FieldProbeService.ToFloat(
                        FieldProbeService.Member(om, family.Member, typeof(float)));
                    return float.IsFinite(vital) && vital > 0f;
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>The value of the family's <c>Destroyed</c> member, read at BYTE
    /// width because these enums declare <c>: byte</c> — an int read would pick up
    /// the neighbouring constant's bytes. -1 when the enum is not in the TDB, which
    /// takes the whole family out of the scan.</summary>
    private static int Destroyed(string enumName)
    {
        if (DestroyedValue.TryGetValue(enumName, out int cached)) return cached;

        int value = -1;
        foreach (var (name, v) in FieldProbeService.ReadEnum(enumName, byteWidth: true))
            if (name == DESTROYED_MEMBER) { value = v; break; }

        DestroyedValue[enumName] = value;
        API.LogInfo(value >= 0
            ? $"[SF6Access] Breakables: {enumName}.{DESTROYED_MEMBER} = {value} (byte)"
            : $"[SF6Access] Breakables: {enumName}.{DESTROYED_MEMBER} not found; that family is skipped");
        return value;
    }

    /// <summary>The prop's world position, the same walk the tutorial panels use.</summary>
    private static bool ReadPosition(ManagedObject om, out float x, out float y, out float z)
    {
        x = y = z = 0f;
        var go = FlowHelper.Call(om, "get_GameObject") as ManagedObject;
        var transform = go == null ? null : FlowHelper.Call(go, "get_Transform") as ManagedObject;
        var pos = transform == null ? null : FlowHelper.Call(transform, "get_Position");
        if (pos == null) return false;

        x = FlowHelper.ReadVecComponent(pos, "x");
        y = FlowHelper.ReadVecComponent(pos, "y");
        z = FlowHelper.ReadVecComponent(pos, "z");
        // An exact origin means the read failed, not a crate at the world origin —
        // the same rule every other field reader in this mod applies.
        return !(x == 0f && y == 0f && z == 0f);
    }
}
