using System.Collections.Generic;
using REFrameworkNET;

namespace SF6Access.Services;

/// <summary>
/// One TDB lookup per (concrete type, member) for the whole process. Every hot
/// read goes through here so that a member which does not exist on a type is
/// asked for ONCE and never again.
///
/// <para><b>Why this exists — the mechanism.</b> REFrameworkNET's
/// <c>IObject.GetField(name)</c> is not a field read; it is (UnifiedObject.hpp)
/// <c>FindField(name)</c>, and on a miss <c>HandleInvokeMember_Internal("get_" +
/// name)</c>. Both halves log when they miss — <c>"Member not found: X"</c> and
/// <c>"Method not found: get_X"</c> — and the getter half, when it HITS, actually
/// INVOKES the property and boxes the result. So a read written as
/// <c>GetField(name) ?? GetField("&lt;name&gt;k__BackingField") ?? Call("get_" +
/// name)</c> can log three lines and invoke the same getter twice while returning
/// a perfectly good value on the last attempt. Measured on a 89-second World Tour
/// session: 6,966 of the log's 9,398 lines were exactly this, and every discarded
/// box is a finalizable wrapper whose Release runs on the GC thread — the thread
/// the <c>ManagedObject.Internal_Finalize</c> access violation came from.</para>
///
/// <para><b>Keyed on the concrete type's FullName</b>, never on a single
/// process-wide latch: the camera manager, the avatar field state and the flow
/// params all change type between screens and areas, and a latch bound to one
/// type but applied to the next is the stale binding the house rules warn about.
/// TDB metadata itself is static — a member either exists on a type or it never
/// will — so the answer, positive or negative, is good for the process.</para>
///
/// <para><b>What is cached is metadata, never an engine object:</b>
/// <see cref="REFrameworkNET.Field"/> and <see cref="REFrameworkNET.Method"/>
/// handles describe the type, hold no instance and take no reference.</para>
/// </summary>
public static class MemberAccess
{
    /// <summary>One resolved way to reach a member: either a field of the
    /// containing type or its property getter.</summary>
    public readonly struct Accessor
    {
        private readonly Field _field;
        private readonly Method _getter;

        internal Accessor(Field field, Method getter) { _field = field; _getter = getter; }

        /// <summary>Read the member off <paramref name="owner"/>.
        /// <para><paramref name="want"/> is the CLR type REFramework converts the
        /// boxed value to; <c>null</c> leaves it exactly as <c>IObject.Call</c> and
        /// <c>IObject.GetField</c> return it (a <c>ManagedObject</c> for a reference
        /// member, a <c>REFrameworkNET.ValueType</c> for a struct one).
        /// <paramref name="valueContainer"/> describes the CONTAINER, not the
        /// member — a value-type container has no managed header before its
        /// fields.</para></summary>
        public object Read(object owner, ulong address, System.Type want, bool valueContainer)
        {
            if (_getter != null) return _getter.InvokeBoxed(want, owner, null);
            return _field?.GetDataBoxed(want, address, valueContainer);
        }
    }

    /// <summary>The answer for "this type has no such member".</summary>
    public static readonly Accessor[] None = new Accessor[0];

    // Separate namespaces per resolution ORDER: the two orders below answer
    // different questions about the same name and must not share an entry.
    private static readonly Dictionary<string, Method> _methods = new Dictionary<string, Method>();
    private static readonly Dictionary<string, Accessor[]> _declaredFirst = new Dictionary<string, Accessor[]>();
    private static readonly Dictionary<string, Accessor[]> _getterFirst = new Dictionary<string, Accessor[]>();
    private static readonly Dictionary<string, Accessor[]> _fieldsFirst = new();

    /// <summary>Read concrete storage before interface getters. Used by the exploration
    /// readers, where a false/empty interface result must not hide a live backing field.</summary>
    public static object ReadFieldsFirst(object owner, string name, System.Type expected = null)
    {
        if (owner is not UnifiedObject obj) return null;
        try
        {
            var td = obj.GetTypeDefinition();
            string type = FullName(td);
            if (type == null) return null;
            string key = type + "::" + name;
            if (!_fieldsFirst.TryGetValue(key, out var found))
            {
                var entries = new List<Accessor>();
                AddField(entries, td, name);
                AddField(entries, td, BackingField(name));
                if (entries.Count == 0) AddGetter(entries, td, name);
                found = Store(_fieldsFirst, key, type, name, entries);
            }
            foreach (var accessor in found)
            {
                try
                {
                    return obj is REFrameworkNET.ValueType vt
                        ? PinnedValue.Use(vt, address => accessor.Read(obj, address, expected, true))
                        : accessor.Read(obj, obj.GetAddress(), expected, false);
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    /// <summary>The method as the engine will dispatch it. <c>TypeDefinition.GetMethod</c>
    /// IS <c>FindMethod</c> (TypeDefinition.hpp: <c>GetMethod(name) { return
    /// FindMethod(name); }</c>), which is the identical lookup <c>IObject.Call</c>
    /// performs on the object's own TypeDefinition — so this resolves exactly what a
    /// direct call would, no more and no less.
    /// <para>That lookup is NOT limited to the concrete type: the native
    /// <c>RETypeDefinition::get_method</c> walks <c>for (super = this; super; super =
    /// super-&gt;get_parent_type())</c>, so inherited methods resolve from the derived
    /// type, and a second pass matches full prototype strings
    /// (<c>"getChildren(System.Type)"</c>). One lookup per name is therefore the whole
    /// search.</para>
    /// Null when the type does not have it — reported once, then never asked again.</summary>
    /// <param name="logMiss">False where a miss is an expected step of a longer
    /// search and the caller reports the final verdict itself.</param>
    public static Method FindMethod(TypeDefinition td, string name, bool logMiss = true)
    {
        string type = FullName(td);
        if (type == null) return null;

        string key = type + "::" + name;
        if (_methods.TryGetValue(key, out var cached)) return cached;

        Method found = null;
        try { found = td.GetMethod(name); } catch { }
        _methods[key] = found;
        if (found == null && logMiss)
            API.LogInfo($"[SF6Access] {type}.{name} not found — not asked again");
        return found;
    }

    /// <summary>Accessors in the order <c>IObject.GetField</c> itself would try
    /// them: the field of that name, then the property getter, then the
    /// auto-property backing field. One lookup each is the whole search — the
    /// native <c>get_field</c> and <c>get_method</c> both walk the parent chain
    /// themselves — so this is the same scope <c>IObject.GetField</c> had.</summary>
    public static Accessor[] DeclaredMember(TypeDefinition td, string name)
    {
        string type = FullName(td);
        if (type == null) return None;

        string key = type + "::" + name;
        if (_declaredFirst.TryGetValue(key, out var cached)) return cached;

        var found = new List<Accessor>();
        AddField(found, td, name);
        AddGetter(found, td, name);
        AddField(found, td, BackingField(name));
        return Store(_declaredFirst, key, type, name, found);
    }

    /// <summary>Accessors in the order the engine-object probe needs them — the
    /// getter first, then the field — at EVERY level of the hierarchy. The getter
    /// half matters because interface-declared getters do not dispatch on IL2CPP
    /// concrete types.
    /// <para>The explicit <c>ParentType</c> walk is kept because it is what the
    /// un-cached probe did, and this resolver must reproduce its result exactly. It
    /// is in fact REDUNDANT: the native <c>get_field</c>/<c>get_method</c> already
    /// walk <c>get_parent_type()</c> themselves, so the level-0 lookup would find an
    /// inherited member anyway. The walk only ever adds duplicate entries, never a
    /// different answer — and it costs nothing now that it runs once per
    /// type.</para></summary>
    public static Accessor[] InheritedMember(TypeDefinition td, string name)
    {
        string type = FullName(td);
        if (type == null) return None;

        string key = type + "::" + name;
        if (_getterFirst.TryGetValue(key, out var cached)) return cached;

        var found = new List<Accessor>();
        try
        {
            for (var t = td; t != null; t = t.ParentType)
            {
                AddGetter(found, t, name);
                // The plain field OR the backing field, never both: that is the
                // choice the un-cached probe made at each level.
                int before = found.Count;
                AddField(found, t, name);
                if (found.Count == before) AddField(found, t, BackingField(name));
            }
        }
        catch { }
        return Store(_getterFirst, key, type, name, found);
    }

    private static Accessor[] Store(Dictionary<string, Accessor[]> cache, string key,
                                    string type, string name, List<Accessor> found)
    {
        var result = found.Count == 0 ? None : found.ToArray();
        cache[key] = result;
        if (result.Length == 0)
            API.LogInfo($"[SF6Access] {type}.{name} not found — not asked again");
        return result;
    }

    private static void AddField(List<Accessor> into, TypeDefinition td, string name)
    {
        try { var f = td.GetField(name); if (f != null) into.Add(new Accessor(f, null)); } catch { }
    }

    private static void AddGetter(List<Accessor> into, TypeDefinition td, string name)
    {
        try { var m = td.GetMethod("get_" + name); if (m != null) into.Add(new Accessor(null, m)); } catch { }
    }

    /// <summary>The compiler-generated storage name of an auto-property.</summary>
    private static string BackingField(string name) => $"<{name}>k__BackingField";

    private static string FullName(TypeDefinition td)
    {
        try { return td?.GetFullName(); } catch { return null; }
    }
}
