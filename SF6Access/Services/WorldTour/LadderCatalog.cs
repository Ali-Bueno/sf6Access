using System;
using System.Collections.Generic;
using REFrameworkNET;
using static SF6Access.Services.WorldTour.LevelRoutePlanner;

namespace SF6Access.Services.WorldTour;

/// <summary>Ladder ends in the loaded scene, copied as positions and read a batch per frame.</summary>
internal static class LadderCatalog
{
    /// <summary>What the game needs to offer a ladder: its access target (<c>WTCityOM.AccessTarget</c>, matched
    /// against the avatar's contact list) and the height of its lowest rung above the floor it serves.</summary>
    internal readonly record struct Grip(ulong Access, float BottomRungM);

    internal const string TypeName = "app.worldtour.om.Om011000";
    private const int BatchSize = 32; // Same per-pass object bound as FieldAccessCatalog.
    private static readonly List<Link> Found = new();
    private static readonly List<Grip> Grips = new();
    private static readonly HashSet<ulong> Seen = new();
    private static readonly List<string> Unread = new();
    private static int _cursor = -1;

    internal static IReadOnlyList<Link> Links => Found;

    /// <summary>Per link, same order as <see cref="Links"/>; default when the index is stale.</summary>
    internal static Grip GripOf(int link) => link >= 0 && link < Grips.Count ? Grips[link] : default;

    /// <summary>Identifies the set of ladders from the last complete sweep; changes when streaming adds or removes one.</summary>
    internal static ulong Signature { get; private set; }

    internal static void BeginSweep()
    {
        Found.Clear();
        Grips.Clear();
        Seen.Clear();
        Unread.Clear();
        _cursor = 0;
    }

    /// <summary>Reads the next batch; true once every ladder in the scene has been read.</summary>
    internal static bool Sweep()
    {
        if (_cursor < 0) return true;
        var list = FieldProbeService.SceneComponents(TypeName);
        int count = FlowHelper.GetListCount(list);
        int end = Math.Min(count, _cursor + BatchSize);
        for (; _cursor < end; _cursor++)
        {
            var om = FlowHelper.GetListItem(list, _cursor);
            if (om == null) continue;
            if (!FieldAccessCatalog.IsLive(om)) { Unread.Add("hidden " + Where(om)); continue; }
            if (!TryReadEnds(om, out var low, out var high)) { Unread.Add("uninitialised " + Where(om)); continue; }
            if (!Seen.Add(om.GetAddress())) continue;
            Found.Add(new Link(low, high));
            Grips.Add(ReadGrip(om, low));
        }
        if (_cursor < count) return false;
        ulong signature = (ulong)Seen.Count;
        foreach (ulong address in Seen) signature ^= address;
        if (signature != Signature)
            API.LogInfo($"[SF6Access] Level route: {Found.Count} of {count} ladders readable" +
                        (Unread.Count > 0 ? "; unread: " + string.Join(", ", Unread) : "") +
                        "; lowest rung above the floor: " + string.Join(", ", Grips.ConvertAll(g =>
                            FormattableString.Invariant($"{g.BottomRungM:+0.0;-0.0} m{(g.Access == 0 ? " (no access target)" : "")}"))));
        Signature = signature;
        _cursor = -1;
        return true;
    }

    private static string Where(ManagedObject om)
    {
        var go = FlowHelper.Call(om, "get_GameObject") as ManagedObject;
        var transform = FlowHelper.Call(go, "get_Transform");
        return FieldAccessCatalog.Vector(transform, "Position", out float x, out float y, out float z)
            ? FormattableString.Invariant($"({x:F1}, {y:F1}, {z:F1})") : "(?)";
    }

    /// <summary>The access target and the lowest rung: <c>StartPoint</c> is where the climb begins, which on a
    /// fire-escape ladder hangs above <c>GroundPoint</c> and has to be jumped for.</summary>
    private static Grip ReadGrip(ManagedObject om, Spot low)
    {
        var access = MemberAccess.ReadFieldsFirst(om, "AccessTarget") as ManagedObject;
        float rung = FieldAccessCatalog.Vector(om, "StartPoint", out var _, out float sy, out var _) ? sy - low.Y : 0f;
        return new Grip(access?.GetAddress() ?? 0, float.IsFinite(rung) ? rung : 0f);
    }

    /// <summary>Standing points at the bottom and top of an initialised ladder.</summary>
    internal static bool TryReadEnds(ManagedObject om, out Spot low, out Spot high)
    {
        low = high = default;
        if (MemberAccess.ReadFieldsFirst(om, "IsInitializeParam", typeof(bool)) is not true) return false;
        if (!FieldAccessCatalog.Vector(om, "GroundPoint", out float gx, out float gy, out float gz)
            || !FieldAccessCatalog.Vector(om, "EndPoint", out float ex, out float ey, out float ez)) return false;
        var ground = new Spot(gx, gy, gz);
        var end = new Spot(ex, ey, ez);
        (low, high) = gy <= ey ? (ground, end) : (end, ground);
        return true;
    }
}
