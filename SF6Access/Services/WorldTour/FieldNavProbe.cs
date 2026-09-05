using System;
using System.Text;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Blocks D and E of the one-shot World Tour navigation probe — the two sources of
/// "where can I actually walk" that cost no query at all:
/// <list type="bullet">
/// <item><b>D — NavMesh.</b> A confirmed dead end (every run reports
///   <c>CityResource</c> null and <c>findMapHandle()</c> NULL), kept as a bare
///   liveness check so we would notice if the city ever did publish one.</item>
/// <item><b>E — the collision the avatar already publishes every frame:</b> its
///   volatile collision info, plus the wall contacts <c>AvatarBase</c> exposes
///   directly. This is the cheap path a radar would live on.</item>
/// </list>
/// Diagnostic only; nothing engine-owned outlives the call.
/// </summary>
public static class FieldNavProbe
{
    // ---------- D. navmesh (dead end; liveness check only) ----------

    /// <summary>One line saying whether a queryable navmesh exists. The no-arg
    /// <c>queryNode()</c> is deliberately NOT called: it is an unbounded, city-wide
    /// query and this probe must never stall the game.</summary>
    public static void DumpNavMesh(StringBuilder sb)
    {
        var common = API.GetManagedSingleton("app.global.WTCommon") as ManagedObject;
        var res = FieldProbeService.Member(common, "CityResource") as ManagedObject;
        var aiMap = FieldProbeService.Member(res, "CityAIMap") as ManagedObject;
        var handle = FlowHelper.Call(aiMap, "findMapHandle") as ManagedObject;
        sb.AppendLine($"WTCommon={(common == null ? "null" : "ok")} CityResource={(res == null ? "null" : "ok")} " +
                      $"CityAIMap={(aiMap == null ? "null" : "ok")} " +
                      $"findMapHandle={handle?.GetTypeDefinition()?.GetFullName() ?? "NULL - still no queryable navmesh"}");
    }

    // ---------- E. collision the avatar already publishes ----------

    public static void DumpVolatileCollision(StringBuilder sb, ManagedObject avatar)
    {
        var vol = FlowHelper.Call(avatar, "__GetVolatileParam") as ManagedObject;
        sb.AppendLine($"__GetVolatileParam = {vol?.GetTypeDefinition()?.GetFullName() ?? "null"}");

        // The plain interface getter returned null last run; Member() also tries the
        // getter and the backing field declared on the base types.
        var col = FieldProbeService.Member(vol, "Collision") as ManagedObject;
        sb.AppendLine($"  Collision = {col?.GetTypeDefinition()?.GetFullName() ?? "null"}");
        if (col != null) DumpCollisionInfo(sb, col);

        DumpContactedWalls(sb, avatar);
    }

    private static void DumpCollisionInfo(StringBuilder sb, ManagedObject col)
    {
        sb.AppendLine($"    IsGround = {FieldProbeService.Member(col, "IsGround", typeof(bool))?.ToString() ?? "?"} " +
                      $"IsSlope = {FieldProbeService.Member(col, "IsSlope", typeof(bool))?.ToString() ?? "?"} " +
                      $"IsWallContact() = {FlowHelper.Call(col, "IsWallContact")?.ToString() ?? "?"}");
        sb.AppendLine($"    AdjustedGroundPos = {FieldProbeService.Vec(FieldProbeService.Member(col, "AdjustedGroundPos"))}");
        DumpGroundPos(sb, col);

        var walls = FieldProbeService.Member(col, "WallContactInfoList") as ManagedObject;
        int n = FlowHelper.GetListCount(walls);
        sb.AppendLine($"    WallContactInfoList count = {n}");
        for (int i = 0; i < n; i++)
        {
            var w = FlowHelper.GetListItem(walls, i);
            var contact = FieldProbeService.Member(w, "Contact");
            sb.AppendLine($"      [{i}] {FieldProbeService.Contact(contact)} " +
                          $"material={FieldProbeService.Member(w, "Material")?.ToString() ?? "null"}");
        }
    }

    /// <summary>The ground point under the avatar. <c>GetGroundPos</c> writes into a
    /// buffer the caller owns, so the buffer is shaped by the method's OWN parameter
    /// type — REFramework gives the engine that buffer's address and copies nothing
    /// back afterwards. No safe buffer means no call.</summary>
    private static void DumpGroundPos(StringBuilder sb, ManagedObject col)
    {
        var m = FindMethod(col, "GetGroundPos");
        if (m == null) { sb.AppendLine("    GetGroundPos: method not found"); return; }

        var pt = m.GetParameters()?[0].Type;
        using var gp = FieldOutBuffer.Acquire(pt);
        if (gp == null) { sb.AppendLine($"    GetGroundPos: {FieldOutBuffer.Refusal(pt)}"); return; }

        object got = null;
        try { got = m.InvokeBoxed(typeof(bool), col, new object[] { gp.View }); } catch { }
        sb.AppendLine($"    GetGroundPos ({gp}) -> {got?.ToString() ?? "?"} " +
                      $"pos={FieldProbeService.Vec(gp)}");
    }

    /// <summary>The wall contacts <c>AvatarBase</c> publishes directly. The list is
    /// caller-owned, so its element type comes from the method's OWN parameter
    /// rather than a hand-written generic name.</summary>
    private static void DumpContactedWalls(StringBuilder sb, ManagedObject avatar)
    {
        var m = FindMethod(avatar, "GetContactedWallInfos");
        if (m == null) { sb.AppendLine("  GetContactedWallInfos: method not found"); return; }

        // The list is a generic instantiation; REFramework cannot construct one, and
        // there is no safe substitute — the engine would write through whatever we
        // passed instead. An honest failure line is the correct outcome, not a
        // hand-shaped buffer. The same wall data is already available for free from
        // CollisionInfo.WallContactInfoList and CharacterController.getWallContactPoint.
        var listType = m.GetParameters()?[0].Type;
        var list = FieldProbeService.NewInstance(listType);
        if (list == null)
        {
            sb.AppendLine($"  GetContactedWallInfos: could not allocate {listType?.FullName ?? "(unknown list type)"} " +
                          "-> call SKIPPED (use WallContactInfoList / getWallContactPoint instead)");
            return;
        }

        var ok = m.InvokeBoxed(typeof(bool), avatar, new object[] { list });
        int n = FlowHelper.GetListCount(list);
        sb.AppendLine($"  GetContactedWallInfos({listType?.FullName}) -> {ok?.ToString() ?? "?"} : {n} walls");
        for (int i = 0; i < n; i++)
        {
            var w = FlowHelper.GetListItem(list, i);
            sb.AppendLine($"    [{i}] canWallRide={FieldProbeService.Member(w, "CanWallRide", typeof(bool))?.ToString() ?? "?"} " +
                          $"pos={FieldProbeService.Vec(FieldProbeService.Member(w, "ContactedPos"))} " +
                          $"n={FieldProbeService.Vec(FieldProbeService.Member(w, "ContactedNormal"))}");
        }
    }

    /// <summary>The first method of that name anywhere in the object's type chain —
    /// a derived TypeDefinition does not expose what its parent declares.</summary>
    private static Method FindMethod(ManagedObject obj, string name)
    {
        for (var td = obj?.GetTypeDefinition(); td != null; td = td.ParentType)
        {
            try
            {
                var methods = td.GetMethods();
                if (methods == null) continue;
                foreach (var m in methods) if (m.Name == name) return m;
            }
            catch { }
        }
        return null;
    }
}
