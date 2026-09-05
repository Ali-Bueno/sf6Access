using System;
using System.Text;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Block B of the one-shot World Tour navigation probe: the collision-filter
/// table. Answers "which collision preset does the player capsule actually run
/// with, and what does each preset let a ray see", entirely in the engine's own
/// vocabulary — every layer id, mask bit and filter id is resolved by NAME out of
/// the TDB, so nothing here hardcodes a collision constant.
///
/// <para>Split out of <see cref="FieldProbeService"/> (which keeps the shared
/// value-type and enum plumbing) to stay inside the file-size rule, matching the
/// one-file-per-block layout of blocks C, D and E.</para>
/// </summary>
public static class FieldFilterProbe
{
    private const string LAYER_ENUM = "app.gCollision.LayerId";

    /// <summary>The whole <c>eFilterInfo</c> table with engine-resolved layer and
    /// mask NAMES, plus the layer-index table and the filter the player's own
    /// character controller actually runs with — the comparison that answers
    /// "which preset does the player use".</summary>
    public static void DumpFilterTable(StringBuilder sb, ManagedObject charaController)
    {
        // GetFilterInfo is an INSTANCE method on app.CollisionSystem (a Behaviour,
        // not a static helper), so the table needs the live component.
        var cs = API.GetManagedSingleton("app.CollisionSystem") as ManagedObject;
        sb.AppendLine($"app.CollisionSystem singleton: {(cs == null ? "NOT FOUND (GetFilterInfo is an instance method -> table unavailable)" : "ok")}");

        foreach (var (name, value) in FieldProbeService.ReadEnum(FieldProbeService.FILTER_ENUM, false))
        {
            object fi = cs == null ? null : FlowHelper.Call(cs, "GetFilterInfo", value);
            sb.AppendLine($"  eFilterInfo.{name} = {value}  ->  {DescribeFilter(fi)}");
        }

        var own = charaController == null ? null : FlowHelper.Call(charaController, "get_FilterInfo");
        sb.AppendLine($"  PLAYER CharacterController.FilterInfo -> {DescribeFilter(own)}");

        sb.AppendLine();
        sb.AppendLine("--- app.gCollision.LayerId -> GetLayerIndex ---");
        var getLayerIndex = TDB.Get().FindType("app.gCollision")
            ?.GetMethod("GetLayerIndex(app.gCollision.LayerId)");
        foreach (var (name, value) in FieldProbeService.ReadEnum(LAYER_ENUM, byteWidth: true))
        {
            object idx = null;
            try { idx = getLayerIndex?.InvokeBoxed(typeof(uint), null, new object[] { (byte)value }); }
            catch { }
            sb.AppendLine($"  LayerId.{name} = {value}  ->  index {idx?.ToString() ?? "(unreadable)"}");
        }
    }

    /// <summary>A <c>via.physics.FilterInfo</c> in the engine's own vocabulary:
    /// numeric layer/group/subgroup plus every set mask bit resolved through
    /// <c>via.physics.System.getLayerName / getMaskName</c>. This is the RE7
    /// <c>DescribeFilter()</c>, ported.</summary>
    public static string DescribeFilter(object filter)
    {
        if (filter == null) return "null";
        try
        {
            EnsurePhysicsSystem();
            var fType = TDB.Get().FindType("via.physics.FilterInfo");
            uint layer = ReadUIntProp(fType, filter, "get_Layer");
            uint group = ReadUIntProp(fType, filter, "get_Group");
            uint sub = ReadUIntProp(fType, filter, "get_SubGroup");
            uint mask = ReadUIntProp(fType, filter, "get_MaskBits");

            string layerName = _getLayerName?.InvokeBoxed(typeof(string), null, new object[] { layer }) as string;

            var bits = new StringBuilder();
            for (int bit = 0; bit < 32; bit++)
            {
                if ((mask & (1u << bit)) == 0) continue;
                string mn = _getMaskName?.InvokeBoxed(typeof(string), null,
                    new object[] { layer, (uint)bit }) as string;
                if (bits.Length > 0) bits.Append('|');
                bits.Append(string.IsNullOrEmpty(mn) ? bit.ToString() : mn);
            }
            return $"layer={layer}:{layerName ?? "?"} group={group} subgroup={sub} mask=0x{mask:X}[{bits}]";
        }
        catch (Exception ex) { return $"unreadable ({ex.Message})"; }
    }

    private static uint ReadUIntProp(TypeDefinition td, object owner, string getter)
    {
        try
        {
            var v = td?.GetMethod(getter)?.InvokeBoxed(typeof(uint), owner, null);
            return v == null ? 0u : Convert.ToUInt32(v);
        }
        catch { return 0u; }
    }

    private static Method _getLayerName, _getMaskName;
    private static bool _physCached;

    private static void EnsurePhysicsSystem()
    {
        if (_physCached) return;
        _physCached = true;
        var ps = TDB.Get().FindType("via.physics.System");
        _getLayerName = ps?.GetMethod("getLayerName(System.UInt32)");
        _getMaskName = ps?.GetMethod("getMaskName(System.UInt32, System.UInt32)");
    }
}
