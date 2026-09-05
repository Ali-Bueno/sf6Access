using System;
using System.Runtime.InteropServices;
using System.Text;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// <b>F10 — one-shot World Tour navigation probe (RESEARCH TOOL, not a feature).</b>
/// Dumps, to <c>reframework/data/sf6access_fieldprobe_*.txt</c>, everything needed
/// to decide how a World Tour navigation radar should be built:
/// <list type="bullet">
/// <item>A — the avatar's real collision capsule, its cached contacts and its
///   transform (here);</item>
/// <item>B — the <c>eFilterInfo</c> collision-filter table with engine-resolved
///   layer/mask names, plus the filter the player capsule actually uses
///   (<see cref="FieldFilterProbe"/>);</item>
/// <item>C — <b>the avatar's own sensing rays</b>, cast through the game's
///   <c>AvatarState_FieldBase</c> API (<see cref="FieldRayProbe"/>);</item>
/// <item>D — is there a queryable NavMesh? (<see cref="FieldNavProbe"/>);</item>
/// <item>E — the collision the avatar already publishes every frame, free
///   (<see cref="FieldNavProbe"/>);</item>
/// <item>F — fast-travel points and section state (here).</item>
/// </list>
///
/// <para>Each block is independently guarded: a block that throws is recorded in
/// the dump and the probe carries on, so half a working answer is never lost to
/// the other half failing. The avatar is resolved BEFORE the blocks so that no
/// block depends on another one having succeeded — block C in particular must
/// always run, capsule or no capsule. Nothing engine-owned is cached between
/// frames: everything is resolved inside the keypress handler.</para>
/// </summary>
public class FieldProbeHooks
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_F10 = 0x79;
    private static bool _lastKeyState;
    private static bool _running;

    /// <summary>What the probe resolved, shared between blocks. Lives for one
    /// keypress only — engine objects must never outlive the frame that read them.</summary>
    private sealed class Probe
    {
        public ManagedObject Avatar;
        public ManagedObject CharaController;
        public string ControllerRoute;
        public float Px, Py, Pz;
        public bool HasPos;
        public float EffRadius, EffHeight;
    }

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] FieldProbeHooks initialized (F10 = World Tour navigation probe)");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        bool down = (GetAsyncKeyState(VK_F10) & 0x8000) != 0;
        bool edge = down && !_lastKeyState;
        _lastKeyState = down;
        if (!edge || _running) return;

        // The probe reads World Tour field data exclusively; outside the field
        // every block would just record nulls.
        if (WorldTourStateService.GetAvatarManager() == null)
        {
            API.LogInfo("[SF6Access] Field probe: not in the World Tour field (AvatarManager null)");
            ScreenReaderService.Speak("Field probe needs World Tour");
            return;
        }

        _running = true;
        try
        {
            string path = RunProbe();
            API.LogInfo($"[SF6Access] Field probe written to {path}");
            ScreenReaderService.Speak("Field probe complete");
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] Field probe failed: {ex.Message}");
            ScreenReaderService.Speak("Field probe failed");
        }
        finally { _running = false; }
    }

    private static string RunProbe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== SF6 WORLD TOUR NAVIGATION PROBE - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        sb.AppendLine($"city={WorldTourStateService.CityId} situation={WorldTourStateService.SituationId} " +
                      $"section={WorldTourStateService.CurrentSectionId}");

        var p = new Probe();
        // Resolved outside the blocks: every block below wants the avatar, and none
        // of them should be lost because an earlier one threw.
        try
        {
            var pm = API.GetManagedSingleton("app.worldtour.WTPlayerManager") as ManagedObject;
            p.Avatar = FlowHelper.Call(pm, "GetAvatarPlayer") as ManagedObject;
        }
        catch (Exception ex) { sb.AppendLine($"[avatar lookup failed: {ex.Message}]"); }
        sb.AppendLine($"GetAvatarPlayer = {p.Avatar?.GetTypeDefinition()?.GetFullName() ?? "null"}");
        sb.AppendLine();

        Block(sb, "A. AVATAR & CAPSULE", () => DumpAvatar(sb, p));
        Block(sb, "B. COLLISION FILTER TABLE", () => FieldFilterProbe.DumpFilterTable(sb, p.CharaController));
        Block(sb, "C. AVATAR CAST RAYS", () => FieldRayProbe.DumpRays(sb, p.Avatar));
        Block(sb, "D. NAVMESH", () => FieldNavProbe.DumpNavMesh(sb));
        Block(sb, "E. AVATAR-PUBLISHED COLLISION", () => FieldNavProbe.DumpVolatileCollision(sb, p.Avatar));
        Block(sb, "F. TRANSIT POINTS & SECTIONS", () => DumpTransit(sb, p));

        string path = System.IO.Path.Combine(ObjectDumper.DumpDir,
            $"sf6access_fieldprobe_{DateTime.Now:HHmmss}.txt");
        System.IO.File.WriteAllText(path, sb.ToString());
        return path;
    }

    /// <summary>Run one probe block under its own guard: a block that throws is
    /// reported in place and the rest of the dump still happens.</summary>
    private static void Block(StringBuilder sb, string title, Action body)
    {
        sb.AppendLine($"========== {title} ==========");
        try { body(); }
        catch (Exception ex) { sb.AppendLine($"[BLOCK FAILED: {ex.GetType().Name}: {ex.Message}]"); }
        sb.AppendLine();
    }

    // ---------- A. avatar & capsule ----------

    private static void DumpAvatar(StringBuilder sb, Probe p)
    {
        ResolveController(sb, p);
        sb.AppendLine($"CharacterController = {p.CharaController?.GetTypeDefinition()?.GetFullName() ?? "null"} " +
                      $"(via {p.ControllerRoute ?? "no route worked"})");

        if (p.CharaController != null)
        {
            foreach (string prop in new[] { "Radius", "Height", "SlopeLimit", "Ground", "Wall", "Ceiling",
                                            "NumGroundContactPoints", "NumWallContactPoints", "NumCeilingContactPoints" })
                sb.AppendLine($"  {prop} = {FieldProbeService.Member(p.CharaController, prop)?.ToString() ?? "null"}");
            sb.AppendLine($"  Position = {FieldProbeService.Vec(FieldProbeService.Member(p.CharaController, "Position"))}");
            DumpWallContacts(sb, p.CharaController);
        }

        DumpCapsuleSize(sb, p);
        sb.AppendLine($"  GetVelocity = {FieldProbeService.Vec(FlowHelper.Call(p.Avatar, "GetVelocity"))}");

        var go = FlowHelper.Call(p.Avatar, "get_GameObject") as ManagedObject;
        var tr = FlowHelper.Call(go, "get_Transform") as ManagedObject;
        foreach (string prop in new[] { "Position", "Rotation", "EulerAngle", "AxisZ", "AxisX" })
            sb.AppendLine($"  Transform.{prop} = {FieldProbeService.Vec(FlowHelper.Call(tr, "get_" + prop))}");

        var pos = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());
        p.Px = pos.x; p.Py = pos.y; p.Pz = pos.z; p.HasPos = pos.ok;
        sb.AppendLine(FormattableString.Invariant(
            $"  player world pos (shared reader) = ({p.Px:F3}, {p.Py:F3}, {p.Pz:F3}) ok={p.HasPos}"));
    }

    /// <summary>Two documented routes to the same controller: <c>AvatarBase.Components</c>
    /// is a SINGLE <c>AvatarComponent</c> (not a collection) that owns a
    /// <c>CharacterController</c>; failing that, the field state's
    /// <c>AvatarCollisionManager</c> publishes it as <c>CharaController</c>.</summary>
    private static void ResolveController(StringBuilder sb, Probe p)
    {
        var comps = FieldProbeService.Member(p.Avatar, "Components") as ManagedObject;
        p.CharaController = FieldProbeService.Member(comps, "CharacterController") as ManagedObject;
        sb.AppendLine($"  route 1: Components={comps?.GetTypeDefinition()?.GetFullName() ?? "null"} -> " +
                      $"CharacterController {(p.CharaController == null ? "null" : "ok")}");
        if (p.CharaController != null) { p.ControllerRoute = "Components.CharacterController"; return; }

        var state = FieldProbeService.FieldState(p.Avatar);
        var acm = FieldProbeService.Member(state, "CollisionManager") as ManagedObject;
        p.CharaController = FieldProbeService.Member(acm, "CharaController") as ManagedObject;
        sb.AppendLine($"  route 2: GetFieldState()={(state == null ? "null" : "ok")} " +
                      $"CollisionManager={acm?.GetTypeDefinition()?.GetFullName() ?? "null"} -> " +
                      $"CharaController {(p.CharaController == null ? "null" : "ok")}");
        if (p.CharaController != null) p.ControllerRoute = "GetFieldState().CollisionManager.CharaController";
    }

    /// <summary>Every wall the controller is already touching — the contacts the
    /// engine computes each frame anyway, so a radar could read them for free.</summary>
    private static void DumpWallContacts(StringBuilder sb, ManagedObject cc)
    {
        int n = 0;
        try { n = Convert.ToInt32(FieldProbeService.Member(cc, "NumWallContactPoints", typeof(int)) ?? 0); }
        catch { }
        if (n <= 0) { sb.AppendLine("  wall contacts: none"); return; }

        var getPoint = cc.GetTypeDefinition()?.GetMethod("getWallContactPoint(System.Int32)");
        for (int i = 0; i < n; i++)
        {
            object cp = null;
            // VALUE-TYPE RETURN: REFramework boxes it from the method's own TDB
            // return type. Naming the generated ContactPoint INTERFACE as the target
            // type only wraps that in a dispatch proxy, which reads as all zeros.
            try { cp = getPoint?.InvokeBoxed(typeof(object), cc, new object[] { i }); } catch { }
            sb.AppendLine($"    wall[{i}] {FieldProbeService.Contact(cp)} " +
                          $"obj='{FieldProbeService.GameObjectName(FlowHelper.Call(cc, "getWallGameObject", i))}'");
        }
    }

    /// <summary>The EFFECTIVE capsule: <c>Radius * widthRatio</c> by <c>Height *
    /// heightRatio</c> — the controller is scaled by the avatar's current state.</summary>
    private static void DumpCapsuleSize(StringBuilder sb, Probe p)
    {
        float wr = 1f, hr = 1f;
        // ref-float out-params: REFramework copies NOTHING back into the boxed argument
        // array and a boxed float is passed by value, so the only way to receive one is a
        // caller-owned one-float buffer whose address the engine writes through, shaped by
        // the method's OWN parameter type. No buffer that can be shown safe, no call.
        var ratioM = p.Avatar?.GetTypeDefinition()?.GetMethod("GetCurrentCharacterControllerSizeRatio");
        var ps = ratioM?.GetParameters();
        using var wBuf = FieldOutBuffer.Acquire(ps?[0].Type);
        using var hBuf = FieldOutBuffer.Acquire(ps?[1].Type);
        if (ratioM == null) sb.AppendLine("  GetCurrentCharacterControllerSizeRatio: method not found");
        else if (wBuf == null || hBuf == null)
            sb.AppendLine($"  GetCurrentCharacterControllerSizeRatio: {FieldOutBuffer.Refusal(ps?[0].Type)}");
        else
        {
            object ok = null;
            try { ok = ratioM.InvokeBoxed(typeof(bool), p.Avatar, new object[] { wBuf.View, hBuf.View }); }
            catch { }
            float w = FieldProbeService.OutFloat(wBuf), h = FieldProbeService.OutFloat(hBuf);
            sb.AppendLine($"  GetCurrentCharacterControllerSizeRatio ({wBuf}) -> {ok?.ToString() ?? "?"} " +
                          FormattableString.Invariant($"width={w:F3} height={h:F3}") +
                          (w <= 0f || h <= 0f ? "  [no ratio written -> assuming 1.0]" : ""));
            if (w > 0f) wr = w;
            if (h > 0f) hr = h;
        }

        p.EffRadius = FieldProbeService.ToFloat(FieldProbeService.Member(p.CharaController, "Radius", typeof(float))) * wr;
        p.EffHeight = FieldProbeService.ToFloat(FieldProbeService.Member(p.CharaController, "Height", typeof(float))) * hr;
        sb.AppendLine(FormattableString.Invariant(
            $"  EFFECTIVE capsule: radius={p.EffRadius:F3} height={p.EffHeight:F3}"));
    }

    // ---------- F. transit points & sections ----------

    private static void DumpTransit(StringBuilder sb, Probe p)
    {
        var city = WorldTourStateService.GetCityManager();
        // City / situation ids come from the manager's own state, never invented.
        uint cityId = WorldTourStateService.CityId;
        uint situationId = WorldTourStateService.SituationId;
        var list = FlowHelper.Call(city, "GetFastTravelPointList",
            cityId, situationId, false, false) as ManagedObject;
        int n = FlowHelper.GetListCount(list);
        sb.AppendLine($"GetFastTravelPointList(city={cityId}, situation={situationId}, " +
                      $"releasedOnly=false, sort=false) -> {n} points");

        for (int i = 0; i < n; i++)
        {
            var pt = FlowHelper.GetListItem(list, i);
            // The elements are PointDataFastTravelInfo, but the id, position and
            // rotation are declared on the BASE type CityPointDataInfoBase — asking
            // the element's own type for them finds nothing. Position/Rotation are
            // structs, so they name no target type: doing so returns a proxy that
            // reads as (0,0,0).
            object pos = FieldProbeService.Member(pt, "Position");
            sb.AppendLine($"  [{i}] mPointId={FieldProbeService.Member(pt, "mPointId", typeof(int))?.ToString() ?? "?"} " +
                          $"pos={FieldProbeService.Vec(pos)} " +
                          $"rot={FieldProbeService.Vec(FieldProbeService.Member(pt, "Rotation"))} " +
                          $"dist={DistanceTo(pos, p)} name='{FastTravelName(pt) ?? "?"}'");
        }

        var sect = WorldTourStateService.GetSectionManager();
        var sections = FlowHelper.Call(sect, "get_SectionInfoList") as ManagedObject;
        sb.AppendLine($"CurrentSectionId = {WorldTourStateService.CurrentSectionId} " +
                      $"SectionInfoList count = {FlowHelper.GetListCount(sections)}");
    }

    /// <summary>The point's localized name: the attached user-data record carries a
    /// message Guid, resolved through the shared message resolver.</summary>
    private static string FastTravelName(ManagedObject pt)
    {
        var rec = FieldProbeService.Member(pt, "mAttachedData") as ManagedObject;
        var msg = FieldProbeService.Member(rec, "PointNameID") as ManagedObject;
        return FlowHelper.ResolveGuidField(msg, "GUID");
    }

    private static string DistanceTo(object pos, Probe p)
    {
        if (!p.HasPos || pos == null) return "?";
        float dx = FlowHelper.ReadVecComponent(pos, "x") - p.Px;
        float dy = FlowHelper.ReadVecComponent(pos, "y") - p.Py;
        float dz = FlowHelper.ReadVecComponent(pos, "z") - p.Pz;
        return FormattableString.Invariant($"{Math.Sqrt(dx * dx + dy * dy + dz * dz):F2}m");
    }
}
