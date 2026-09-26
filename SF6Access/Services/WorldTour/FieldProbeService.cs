using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Shared plumbing for the one-shot World Tour navigation probe
/// (<see cref="SF6Access.Hooks.WorldTour.FieldProbeHooks"/>): value-type and enum
/// helpers used by every block. Block B (the collision filter table) lives in
/// <see cref="FieldFilterProbe"/>.
///
/// <para>This is DIAGNOSTIC code, not a radar. It runs once per F10 press,
/// resolves everything in-frame, and caches only TDB metadata handles
/// (<c>Method</c> / <c>TypeDefinition</c>), never engine objects.</para>
///
/// <para><b>VALUE TYPES — the rule the probe learned the hard way.</b> The
/// generated <c>via.vec3</c> / <c>via.Quaternion</c> / <c>via.physics.ContactPoint</c>
/// C# types are INTERFACES, not structs. Handing one of them to
/// <c>InvokeBoxed</c> / <c>GetDataBoxed</c> as the target return type makes
/// REFramework wrap the (already correctly boxed) result in a dispatch proxy, and
/// a proxy is not a <c>REFrameworkNET.ValueType</c> — every component read off it
/// comes back 0. REFramework always boxes from the member's OWN TDB type, so no
/// target type is needed for correctness: that is exactly why the proven
/// production reader <see cref="AvatarFieldReader.ReadWorldPos"/> works — it calls
/// the getter untyped and reads components off the returned
/// <c>REFrameworkNET.ValueType</c>. Never pass a generated engine interface here;
/// for a struct member pass nothing.</para>
///
/// <para><b>OUT PARAMETERS.</b> REFramework copies nothing back into the
/// <c>object[]</c> after a call; every argument that is an <c>IObject</c> is passed
/// as its own address. So an <c>out</c> struct — and a <c>ref float</c>, since a
/// boxed float is passed by value — can only be received through a caller-owned
/// buffer: <see cref="FieldOutBuffer"/>, which is unmanaged, over-reserved and
/// aligned precisely because the engine writes into it with no bounds of its own.
/// A by-ref parameter whose type is a REFERENCE type gets NO buffer and NO call —
/// that is the rule that stopped the probe crashing the game.</para>
///
/// <para>Every enum value comes out of the TDB by NAME (the generate-enum
/// pattern), so nothing here hardcodes a filter id or a layer id.</para>
/// </summary>
public static class FieldProbeService
{
    public const string FILTER_ENUM = "app.CollisionSystem.eFilterInfo";

    // ---------- value-type helpers ----------

    /// <summary>Format any via vector/quaternion value for the dump. Anything that
    /// is NOT a raw value-type buffer is named as such instead of being printed as
    /// zeros: that is the dispatch-proxy failure described above, and it must never
    /// be mistaken for an object sitting at the world origin.</summary>
    public static string Vec(object v)
    {
        if (v == null) return "null";
        if (v is FieldOutBuffer ob)
            return Format(ob.Component("x"), ob.Component("y"), ob.Component("z"), ob.Component("w"));
        if (!(v is REFrameworkNET.ValueType) && !(v is NativeObject))
            return $"[not a value-type buffer: {v.GetType().Name}]";
        float x = FlowHelper.ReadVecComponent(v, "x");
        float y = FlowHelper.ReadVecComponent(v, "y");
        float z = FlowHelper.ReadVecComponent(v, "z");
        float w = FlowHelper.ReadVecComponent(v, "w");
        return Format(x, y, z, w);
    }

    private static string Format(float x, float y, float z, float w) => w != 0f
        ? FormattableString.Invariant($"({x:F3}, {y:F3}, {z:F3}, {w:F3})")
        : FormattableString.Invariant($"({x:F3}, {y:F3}, {z:F3})");

    /// <summary>A real instance of a REFERENCE type, to be handed over as an ordinary
    /// BY-VALUE argument that the engine mutates in place — the shape of
    /// <c>CastRayAll(type, CastRayResult result, filter)</c>, whose TDB signature
    /// carries no by-ref marker on <c>result</c> (the same signature DOES mark its
    /// <c>vec3</c> parameters, so the absence is meaningful). That is the same call
    /// shape as every other engine method taking an object, and the object is sized
    /// and laid out by the engine's own allocator, never by us.
    ///
    /// <para><b>What comes back is a LOCAL object, and that is the whole problem.</b>
    /// <c>CreateInstance</c> invokes the game's own <c>System.Activator</c> on the
    /// CALLING thread's VM context, and RE Engine's object model — Capcom's own
    /// "Achieve Rapid Iteration: RE ENGINE Design" — says a local object "can only be
    /// referenced by the spawned thread", is "registered in the local table for each
    /// thread", carries a NEGATIVE reference count that is an index into that table,
    /// and that "all objects created from C# will be local objects". The engine's frame
    /// GC reclaims it. Nothing AddRefs it, so nothing keeps it alive.</para>
    ///
    /// <para>Two consequences, both paid for in play on 2026-09-09. Let one live past
    /// the frame that made it and the engine reclaims it underneath you — the next cast
    /// writes into freed memory. Let it die instead and REFramework's finalizer
    /// dereferences the reclaimed object from the GC THREAD, which is
    /// <c>AccessViolationException at ManagedObject.Internal_Finalize</c>, uncatchable
    /// and instantly fatal. Rate only changes how long you last.</para>
    ///
    /// <para>So a caller that needs a container across frames — every result buffer
    /// here does — must use <see cref="SharedInstance"/>, which globalizes. This method
    /// is for an object genuinely consumed inside the frame that made it.</para></summary>
    public static ManagedObject NewInstance(TypeDefinition td)
    {
        // REFramework's CreateInstance has NO guard: it asks the game's Activator and
        // wraps whatever qword comes back. For an interface, an abstract class or a
        // generic instantiation that is a wrapper over a non-object and a delayed
        // AccessViolationException on the finalizer thread (seen 2026-09-06). Only a
        // concrete, non-generic reference type is constructible; the game's own
        // System.Type answers the rest.
        if (td == null || td.IsValueType() || td.IsGenericType()) return null;
        var runtimeType = td.GetRuntimeType();
        if (runtimeType == null) return null;
        if (FlowHelper.Call(runtimeType, "get_IsInterface") is bool isInterface && isInterface) return null;
        if (FlowHelper.Call(runtimeType, "get_IsAbstract") is bool isAbstract && isAbstract) return null;
        try { return td.CreateInstance(0); }
        catch { return null; }
    }

    /// <summary>One GLOBALIZED instance of a type, created on first use and held for
    /// the process — the safe way to own an engine-written result container.
    ///
    /// <para><see cref="NewInstance"/> yields a local object the engine's frame GC
    /// reclaims (see its remarks). <c>Globalize</c> is REFramework's answer to exactly
    /// this: its own documentation says it "should only need to be called" when "you
    /// are manually creating an instance of a managed object", which is precisely this
    /// call. It AddRefs, taking the reference count positive, which in RE Engine's model
    /// is what promotes a per-thread local object to one that every thread may hold —
    /// so the engine stops reclaiming it and the container survives the frame.</para>
    ///
    /// <para>Held in this table forever, deliberately. A rooted object is not a leak
    /// when there is exactly one per type and it is reused for the life of the process;
    /// it is the difference between thirty allocations a second and none. Because it is
    /// never collected, its finalizer never runs, so the GC-thread dereference that has
    /// been killing the game cannot happen at all — the crash surface is removed, not
    /// narrowed.</para>
    ///
    /// <para>Callers must still reset the container themselves (the cast API's own
    /// <c>clear</c>) before each use: it is shared across every call for that type.
    /// A wrapper whose reference count has gone non-positive is re-acquired rather than
    /// used, which should never happen to a globalized object and is the cheap check
    /// that says so if it does.</para></summary>
    public static ManagedObject SharedInstance(TypeDefinition td)
    {
        string key = td?.FullName;
        if (key == null) return null;
        if (_shared.TryGetValue(key, out var held) && held != null && Globalized(held)) return held;

        var made = NewInstance(td);
        if (made == null) return null;
        try { made.Globalize(); }
        catch (Exception ex)
        {
            API.LogWarning($"[SF6Access] Could not globalize {key}: {ex.GetType().Name}. " +
                           "Refusing it — an engine object that cannot be rooted is not safe to keep.");
            return null;
        }
        if (!Globalized(made))
        {
            API.LogWarning($"[SF6Access] Globalize({key}) left the reference count non-positive — refused.");
            return null;
        }
        _shared[key] = made;
        API.LogInfo($"[SF6Access] Shared engine container {key} created and globalized (one for the process).");
        return made;
    }

    /// <summary>A positive reference count is what "global" means in RE Engine's model;
    /// a negative one is an index into a per-thread local table.</summary>
    private static bool Globalized(ManagedObject obj)
    {
        try { return obj.GetReferenceCount() > 0; }
        catch { return false; }
    }

    private static readonly Dictionary<string, ManagedObject> _shared = new();

    /// <summary>The float the engine wrote into a one-float out buffer. The
    /// primitive's own TDB entry names its storage field, so read that; the direct
    /// read is a fallback and is refused unless the buffer really is float-sized
    /// (again from the TDB, never assumed).</summary>
    public static float OutFloat(FieldOutBuffer buf)
    {
        if (buf == null || buf.Address == 0) return 0f;
        float v = buf.Component("m_value");
        if (v != 0f) return v;
        try
        {
            if (buf.Bytes >= sizeof(float))
                return Marshal.PtrToStructure<float>((IntPtr)(long)buf.Address);
        }
        catch { }
        return 0f;
    }

    public static float ToFloat(object boxed)
    {
        try { return boxed == null ? 0f : Convert.ToSingle(boxed); }
        catch { return 0f; }
    }

    /// <summary>Read a member of an engine object the way IL2CPP actually allows:
    /// the UNTYPED getter first (REFramework boxes it from the member's own TDB
    /// type, which is the proven production path), then the getter or backing FIELD
    /// declared on any BASE type. Both fallbacks matter here — interface-declared
    /// getters do not dispatch on concrete types, and a derived
    /// <c>TypeDefinition</c> does not expose members its parent declares (that is
    /// why the fast-travel points, whose id and position live on
    /// <c>CityPointDataInfoBase</c>, read as nothing when asked of
    /// <c>PointDataFastTravelInfo</c>).
    ///
    /// <para>Works for every container kind: a <c>ManagedObject</c>, a native
    /// object, or a raw <c>ValueType</c> buffer — <c>GetDataBoxed</c>'s flag
    /// describes the CONTAINER, and a value-type container has no managed header in
    /// front of its fields.</para>
    ///
    /// <para><paramref name="expected"/> is a PRIMITIVE CLR type, or nothing. It
    /// never reinterprets the bytes — REFramework has already boxed them at the
    /// member's own width — it only picks the final conversion. Passing a generated
    /// engine interface (<c>via.vec3</c>) instead yields a dispatch proxy that reads
    /// as all zeros, so STRUCT MEMBERS PASS NOTHING.</para></summary>
    public static object Member(object owner, string name, System.Type expected = null)
    {
        if (owner == null) return null;
        // A struct's bytes must not move while the engine reads them (see PinnedValue).
        return owner is REFrameworkNET.ValueType vt
            ? PinnedValue.Use(vt, _ => Lookup(owner, name, expected))
            : Lookup(owner, name, expected);
    }

    private static object Lookup(object owner, string name, System.Type expected)
    {
        // The untyped getter, but only when the CONCRETE type declares it: asking
        // IObject.Call for a method a type does not have costs a logged
        // "Method not found" line on every single read, and these members are read
        // several times a second (measured 606 lines in 89 s from the wall and
        // collision members alone). The miss is not reported here — the hierarchy
        // walk below is the rest of the search and returns the verdict.
        if (MemberAccess.FindMethod((owner as IObject)?.GetTypeDefinition(), "get_" + name, logMiss: false) != null)
        {
            try { var v = (owner as IObject)?.Call("get_" + name); if (v != null) return v; }
            catch { }
        }

        if (!(owner is UnifiedObject uo)) return null;
        bool valueContainer = owner is REFrameworkNET.ValueType;
        System.Type want = expected ?? typeof(object);
        ulong address = uo.GetAddress();
        foreach (var accessor in MemberAccess.InheritedMember(uo.GetTypeDefinition(), name))
        {
            try { var v = accessor.Read(uo, address, want, valueContainer); if (v != null) return v; }
            catch { }
        }
        return null;
    }

    /// <summary>The avatar's current field state — the object that owns the game's
    /// own cast-ray API and the collision manager holding the character controller.</summary>
    public static ManagedObject FieldState(ManagedObject avatar) =>
        FlowHelper.Call(avatar, "GetFieldState") as ManagedObject;

    /// <summary>How many contacts the engine wrote into a <c>CastRayAll</c> result.
    /// Shared so the probe, the forward stack and the sideways rays all ask the same
    /// question the same way — the count is the hit test, and reading it through the
    /// member walk (rather than the interface getter) is what makes it answer at
    /// all.</summary>
    public static int ContactCount(ManagedObject result)
    {
        var n = Member(result, "NumContactPoints", typeof(uint));
        return n == null ? 0 : (int)Convert.ToUInt32(n);
    }

    /// <summary>A <c>via.physics.ContactPoint</c> in full: the engine's own distance
    /// and time-of-impact alongside the contact position and surface normal.</summary>
    public static string Contact(object cp)
    {
        if (cp == null) return "null";
        return $"pos={Vec(Member(cp, "Position"))} n={Vec(Member(cp, "Normal"))} " +
               FormattableString.Invariant(
                   $"dist={ToFloat(Member(cp, "Distance", typeof(float))):F3} toi={ToFloat(Member(cp, "TimeOfImpact", typeof(float))):F3}");
    }

    /// <summary>The name of a <c>via.GameObject</c>. Static level geometry carries no
    /// GameObject at all, so a null here is information, not a failure.</summary>
    public static string GameObjectName(object go)
    {
        if (go == null) return "(level geometry / no GameObject)";
        string n = FlowHelper.Call(go as ManagedObject, "get_Name") as string;
        return string.IsNullOrEmpty(n) ? "(unnamed)" : n;
    }

    /// <summary>via.physics.System.getLayerName(uint), resolved once and cached as a
    /// TDB method handle (never an engine object) — the same lookup
    /// <see cref="FieldFilterProbe"/> makes for the filter table, kept separate
    /// because this probe reads a <c>Collidable</c>'s OWN <c>FilterInfo</c> rather
    /// than the collision system's per-<c>eFilterInfo</c> table.</summary>
    private static Method _getLayerName;
    private static bool _getLayerNameResolved;

    private static Method GetLayerNameMethod()
    {
        if (_getLayerNameResolved) return _getLayerName;
        _getLayerNameResolved = true;
        try { _getLayerName = TDB.Get().FindType("via.physics.System")?.GetMethod("getLayerName(System.UInt32)"); }
        catch { _getLayerName = null; }
        return _getLayerName;
    }

    /// <summary>A <c>via.physics.Collidable</c>'s filter, in one line: numeric
    /// <c>Layer</c>/<c>Group</c>/<c>SubGroup</c> plus the layer's own NAME from
    /// <c>via.physics.System.getLayerName(uint)</c> — never guessed at by id, and
    /// never printed at all when the collidable itself is null (a contact against
    /// static level geometry with no <c>Collidable</c>).</summary>
    public static string Collidable(object coll)
    {
        if (coll == null) return "";
        try
        {
            var filter = Member(coll, "FilterInfo");
            if (filter == null) return " layer=? grp=?/?";
            uint layer = ToUInt(Member(filter, "Layer", typeof(uint)));
            uint group = ToUInt(Member(filter, "Group", typeof(uint)));
            uint sub = ToUInt(Member(filter, "SubGroup", typeof(uint)));
            string layerName = "?";
            try { layerName = GetLayerNameMethod()?.InvokeBoxed(typeof(string), null, new object[] { layer }) as string ?? "?"; }
            catch { }
            return FormattableString.Invariant($" layer={layer}({layerName}) grp={group}/{sub}");
        }
        catch { return " layer=? grp=?/?"; }
    }

    /// <summary>A contact's <c>Material</c> (<c>via.physics.MaterialInfo</c>): the
    /// surface id plus its three attribute bytes, read individually so one missing
    /// member never blanks the rest.</summary>
    public static string MaterialText(object cp)
    {
        if (cp == null) return "";
        object mat = null;
        try { mat = Member(cp, "Material"); } catch { }
        if (mat == null) return " mat=? attr=?/?/?";
        uint id = ToUInt(SafeMember(mat, "Id"));
        uint a1 = ToUInt(SafeMember(mat, "Attribute1"));
        uint a2 = ToUInt(SafeMember(mat, "Attribute2"));
        uint a3 = ToUInt(SafeMember(mat, "Attribute3"));
        return FormattableString.Invariant($" mat={id} attr={a1}/{a2}/{a3}");
    }

    /// <summary>The GameObject's <c>Folder</c> name and <c>Tag</c> string — empty
    /// string, not a placeholder, when the GameObject itself is null (level
    /// geometry with no GameObject at all).</summary>
    public static string GameObjectFolderAndTag(object go)
    {
        if (go == null) return "";
        string folder = "?", tag = "?";
        try
        {
            var folderObj = Member(go, "Folder");
            folder = folderObj == null ? "(none)" : (FlowHelper.Call(folderObj as ManagedObject, "get_Name") as string ?? "?");
        }
        catch { }
        try { tag = (SafeMember(go, "Tag") as string) ?? ""; } catch { }
        return $" folder='{folder}' tag='{tag}'";
    }

    /// <summary>Up to <see cref="MAX_COMPONENTS_LISTED"/> component type FullNames off
    /// a GameObject's <c>Components</c> array — this is what tells an interactable
    /// (an <c>app.worldtour.om.*</c> component) apart from plain geometry.</summary>
    private const int MAX_COMPONENTS_LISTED = 8;

    public static string GameObjectComponents(object go)
    {
        if (go == null) return "";
        try
        {
            var comps = Member(go, "Components") as System.Collections.IEnumerable;
            if (comps == null) return " comps=[?]";
            var names = new List<string>();
            foreach (var c in comps)
            {
                if (names.Count >= MAX_COMPONENTS_LISTED) break;
                try { names.Add((c as UnifiedObject)?.GetTypeDefinition()?.GetFullName() ?? "?"); }
                catch { names.Add("?"); }
            }
            return $" comps=[{string.Join(", ", names)}]";
        }
        catch { return " comps=[?]"; }
    }

    /// <summary>A member read as <c>object</c> rather than a primitive — used for
    /// string/reference members (<c>Tag</c>, <c>Folder</c>) where forcing a CLR
    /// primitive type on <see cref="Member"/> would be wrong.</summary>
    private static object SafeMember(object owner, string name)
    {
        try { return Member(owner, name); } catch { return null; }
    }

    private static uint ToUInt(object boxed)
    {
        try { return boxed == null ? 0u : Convert.ToUInt32(boxed); }
        catch { return 0u; }
    }

    // ---------- overload selection ----------

    /// <summary>Pick an overload by SHAPE rather than by a hand-written signature
    /// string (a formatting mismatch would silently select the wrong one), walking
    /// up from a concrete type to wherever the method is declared.
    ///
    /// <para>Both <c>CastRay</c> and <c>CastRayAll</c> have a by-type overload and a
    /// by-segment one, and the first parameter's type is what tells them apart —
    /// which of the two is safe to call is the single most expensive thing this
    /// codebase has learned, so the probe and the navigation radar resolve it
    /// through the same function rather than each keeping its own copy.</para></summary>
    public static Method FindByShape(TypeDefinition start, string name, int paramCount, string firstParamSuffix)
    {
        for (var td = start; td != null; td = td.ParentType)
        {
            try
            {
                var methods = td.GetMethods();
                if (methods == null) continue;
                foreach (var m in methods)
                {
                    if (m.Name != name) continue;
                    var ps = m.GetParameters();
                    if (ps == null || ps.Count != paramCount) continue;
                    if (ps[0].Type?.FullName?.EndsWith(firstParamSuffix) == true) return m;
                }
            }
            catch { }
        }
        return null;
    }

    // ---------- components in the live scene ----------

    /// <summary>Every component of a type in the current scene, as the engine's own
    /// list, or null. One copy of an idiom that had grown four of them
    /// (<see cref="FieldHeadingService"/>, <see cref="FieldPadService"/>,
    /// <c>GuiTextReader</c>, <c>MissionGuideReader</c>).
    ///
    /// <para><b>Never cache what this returns.</b> The wrappers are over live scene
    /// objects that a city load, a section stream or an NPC despawn destroys, and a
    /// managed wrapper outliving its object is the finalizer-on-the-GC-thread crash
    /// documented in <c>docs/sf6-architecture.md</c>. Re-ask instead; the call is a
    /// scene walk, so ask at a human cadence, not per frame.</para></summary>
    public static ManagedObject SceneComponents(string typeName)
    {
        try
        {
            var runtimeType = TDB.Get().FindType(typeName)?.GetRuntimeType();
            var find = TDB.Get().FindType("via.Scene")?.GetMethod("findComponents(System.Type)");
            if (runtimeType == null || find == null) return null;

            var sceneMgr = API.GetNativeSingleton("via.SceneManager");
            var scene = (sceneMgr as IObject)?.Call("get_CurrentScene") as IObject;
            if (scene == null) return null;

            return find.InvokeBoxed(typeof(object), scene, new object[] { runtimeType }) as ManagedObject;
        }
        catch { return null; }
    }

    /// <summary>The first component of a type in the current scene, or null.</summary>
    public static ManagedObject FirstSceneComponent(string typeName)
    {
        var list = SceneComponents(typeName);
        return list != null && FlowHelper.GetListCount(list) > 0 ? FlowHelper.GetListItem(list, 0) : null;
    }

    // ---------- enums straight out of the TDB ----------

    /// <summary>Every member of a TDB enum as (name, value), ascending. Byte-backed
    /// enums (<c>app.gCollision.LayerId : byte</c>) MUST be read at their own
    /// width — an int read would drag in the neighbouring constant's bytes.</summary>
    public static List<(string name, int value)> ReadEnum(string typeName, bool byteWidth)
    {
        var list = new List<(string name, int value)>();
        try
        {
            var fields = TDB.Get().FindType(typeName)?.GetFields();
            if (fields == null) return list;
            foreach (var f in fields)
            {
                if (f.Name == "value__" || !f.IsStatic()) continue;
                try
                {
                    var raw = f.GetDataBoxed(byteWidth ? typeof(byte) : typeof(int), 0, false);
                    if (raw != null) list.Add((f.Name, Convert.ToInt32(raw)));
                }
                catch { }
            }
            list.Sort((a, b) => a.value.CompareTo(b.value));
        }
        catch { }
        return list;
    }

    /// <summary>The numeric value of one enum member, or -1 when absent.</summary>
    public static int EnumValue(string typeName, string memberName)
    {
        foreach (var (name, value) in ReadEnum(typeName, false))
            if (name == memberName) return value;
        return -1;
    }
}
