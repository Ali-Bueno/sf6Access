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
    /// <para>Deliberately NOT globalized: it lives for one synchronous call inside a
    /// single frame. Rooting it would leak one object per probe run and keep it alive
    /// for the GC to walk long after the call that filled it — and globalizing an
    /// object the engine has just written through is how a silent failure becomes a
    /// delayed crash.</para></summary>
    public static ManagedObject NewInstance(TypeDefinition td)
    {
        if (td == null || td.IsValueType()) return null;
        try { return td.CreateInstance(0); }
        catch { return null; }
    }

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
        try { var v = (owner as IObject)?.Call("get_" + name); if (v != null) return v; }
        catch { }

        if (!(owner is UnifiedObject uo)) return null;
        bool valueContainer = owner is REFrameworkNET.ValueType;
        System.Type want = expected ?? typeof(object);
        for (var td = uo.GetTypeDefinition(); td != null; td = td.ParentType)
        {
            try
            {
                var m = td.GetMethod("get_" + name);
                if (m != null) { var v = m.InvokeBoxed(want, uo, null); if (v != null) return v; }
            }
            catch { }
            try
            {
                var f = td.GetField(name) ?? td.GetField($"<{name}>k__BackingField");
                if (f != null) { var v = f.GetDataBoxed(want, uo.GetAddress(), valueContainer); if (v != null) return v; }
            }
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
