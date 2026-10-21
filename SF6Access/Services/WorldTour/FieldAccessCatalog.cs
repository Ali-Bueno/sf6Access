using System;
using System.Collections.Generic;
using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>What kind of way through an access is.</summary>
internal enum AccessKind { Ladder, Door }

/// <summary>Nearby ladder/door geometry, independent of the selected goal. Copied world positions only.</summary>
internal static class FieldAccessCatalog
{
    internal readonly record struct Access(string Key, string Text, float X, float Y, float Z, long SeenAt, AccessKind Kind, ulong Target);
    private static readonly string[] Types = {
        "app.worldtour.om.Om011000", "app.worldtour.om.Om016000", "app.worldtour.om.Om017000"
    };
    // One family per half-second, at most 32 objects. Spread large scenes across passes.
    private const long ScanMs = 500;
    private const int BatchSize = 32;
    internal const float RangeM = 12f; // Local discovery range, not a claim of walkability.
    // Kept (not spoken) out to the beacons' felt range, for the access sonar and the look sweep.
    private static float KeepRangeM => Hooks.WorldTour.FieldBeaconHooks.HOME_RANGE_M;
    private const long LifetimeMs = 6000;
    private static readonly int[] Cursors = new int[Types.Length];
    private static readonly Dictionary<string, Access> Entries = new();
    private static readonly List<string> Expired = new();
    private static readonly HashSet<string> Logged = new();
    private static int _family;
    private static long _nextScan;

    internal static IEnumerable<Access> Nearby(float px, float py, float pz, long now)
    {
        if (now >= _nextScan)
        {
            _nextScan = now + ScanMs;
            Scan(px, py, pz, now);
            _family = (_family + 1) % Types.Length;
        }
        Expired.Clear();
        foreach (var pair in Entries)
            if (now - pair.Value.SeenAt > LifetimeMs) Expired.Add(pair.Key);
        foreach (string key in Expired) Entries.Remove(key);
        return Entries.Values;
    }

    private static void Scan(float px, float py, float pz, long now)
    {
        var list = FieldProbeService.SceneComponents(Types[_family]);
        int count = FlowHelper.GetListCount(list);
        if (count == 0) return;
        int start = Cursors[_family] % count;
        int amount = Math.Min(count, BatchSize);
        for (int j = 0; j < amount; j++)
        {
            var om = FlowHelper.GetListItem(list, (start + j) % count);
            if (om == null) continue;
            string key = Types[_family] + ":" + om.GetAddress();
            Entries.Remove(key); // A fresh negative observation invalidates the old position/state.
            if (!IsLive(om)) continue;
            float x, y, z;
            string text;
            var access = MemberAccess.ReadFieldsFirst(om, "AccessTarget") as ManagedObject;
            if (_family == 0)
            {
                if (!LadderCatalog.TryReadEnds(om, out var low, out var high)) continue;
                bool upper = DistanceSq(high.X, high.Y, high.Z, px, py, pz) < DistanceSq(low.X, low.Y, low.Z, px, py, pz);
                (x, y, z) = upper ? (high.X, high.Y, high.Z) : (low.X, low.Y, low.Z);
                text = upper ? ExplorationText.Get("ladder_down", "Ladder leading down")
                             : ExplorationText.Get("ladder_up", "Ladder leading up");
            }
            else
            {
                var go = FlowHelper.Call(om, "get_GameObject") as ManagedObject;
                var transform = FlowHelper.Call(go, "get_Transform");
                if (!Vector(transform, "Position", out x, out y, out z)) continue;
                string member = _family == 1 ? "State" : "CurrentState";
                string state = FlowHelper.ResolveEnumName(Types[_family] + (_family == 1 ? ".OpenState" : ".StateType"),
                    FlowHelper.ReadIntField(om, member, -1));
                if (state == null) continue;
                bool opened = state == "Open" || state == "Opened" || state == "OpenedReverse";
                text = opened ? ExplorationText.Get("door_open", "Open door")
                              : ExplorationText.Get("door_closed", "Closed door");
                if (!opened && _family == 1)
                {
                    int lockType = FlowHelper.ReadByteField(om, "Lock", -1);
                    foreach (var (lockName, value) in LockKinds)
                        if (value == lockType && lockName == "AlwaysLock")
                            text = ExplorationText.Get("door_locked", "Blocked door");
                }
                string name = FlowHelper.Call(access, "GetDispName") as string;
                if (!string.IsNullOrWhiteSpace(name)) text += ", " + FlowHelper.CleanTags(name);
            }
            if (DistanceSq(x, y, z, px, py, pz) > KeepRangeM * KeepRangeM) continue;
            Entries[key] = new Access(key, text, x, y, z, now, _family == 0 ? AccessKind.Ladder : AccessKind.Door,
                access?.GetAddress() ?? 0);
            if (Logged.Add(Types[_family]))
                API.LogInfo(FormattableString.Invariant($"[SF6Access] Exploration access: {Types[_family]}, {text}, ({x:F2}, {y:F2}, {z:F2})"));
        }
        Cursors[_family] = (start + amount) % count;
    }

    /// <summary>An enabled component whose GameObject is drawn.</summary>
    internal static bool IsLive(ManagedObject om)
    {
        if (MemberAccess.ReadFieldsFirst(om, "Enabled", typeof(bool)) is false) return false;
        var go = FlowHelper.Call(om, "get_GameObject") as ManagedObject;
        return MemberAccess.ReadFieldsFirst(go, "DrawSelf", typeof(bool)) is not false;
    }

    internal static bool Vector(object owner, string member, out float x, out float y, out float z)
    {
        x = y = z = 0;
        var value = MemberAccess.ReadFieldsFirst(owner, member);
        if (value == null) return false;
        x = FlowHelper.ReadVecComponent(value, "x"); y = FlowHelper.ReadVecComponent(value, "y");
        z = FlowHelper.ReadVecComponent(value, "z");
        return float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z) && (x != 0 || y != 0 || z != 0);
    }

    internal static float DistanceSq(float x, float y, float z, float px, float py, float pz)
        => (x - px) * (x - px) + (y - py) * (y - py) + (z - pz) * (z - pz);

    internal static void Reset()
    { Entries.Clear(); Array.Clear(Cursors); _family = 0; _nextScan = 0; }

    private static List<(string name, int value)> _lockKinds;
    private static List<(string name, int value)> LockKinds => _lockKinds ??=
        FieldProbeService.ReadEnum(Types[1] + ".LockType", byteWidth: true);
}
