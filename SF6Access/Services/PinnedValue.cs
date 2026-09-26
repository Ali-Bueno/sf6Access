using System;
using System.Reflection;
using REFrameworkNET;

namespace SF6Access.Services;

/// <summary>
/// Reads a REFramework <c>ValueType</c> (a struct returned or read BY VALUE) with its bytes pinned.
/// REFramework keeps those bytes in a managed <c>byte[]</c>, and <c>GetAddress()</c> takes their
/// address inside a <c>fixed</c> block that has already ended when it returns. A .NET GC between that
/// call and the copy moves the array, and the copy then reads freed memory: an uncatchable
/// <c>AccessViolationException</c>. Reading a <c>via.vec3</c> field allocates the result first, which
/// is exactly when a GC runs — the radar died this way in <c>FieldRayContacts</c> on 2026-09-26.
/// While <see cref="Use{T}"/> runs, the array cannot move, so every address REFramework derives from
/// the value (field reads, a method's <c>this</c>) stays valid.
/// </summary>
internal static class PinnedValue
{
    /// <summary>REFramework.NET's private byte store (decompiled from REFramework.NET.dll, 2026-09-26).</summary>
    private static readonly FieldInfo Bytes =
        typeof(REFrameworkNET.ValueType).GetField("m_data", BindingFlags.Instance | BindingFlags.NonPublic);

    private static bool _warned;

    /// <summary>Runs <paramref name="read"/> with the value's address pinned until it returns.</summary>
    public static unsafe T Use<T>(REFrameworkNET.ValueType value, Func<ulong, T> read)
    {
        if (Bytes?.GetValue(value) is byte[] { Length: > 0 } data)
            fixed (byte* p = data) return read((ulong)p);
        // A REFramework update renamed the store: keep the old unpinned read rather than lose every position.
        if (!_warned)
        {
            _warned = true;
            API.LogWarning("[SF6Access] PinnedValue: REFramework.NET ValueType has no m_data array; value reads are unpinned");
        }
        return read(value.GetAddress());
    }

    /// <summary>One field of a struct, pinned when the owner is a <c>ValueType</c>.</summary>
    public static object Field(Field field, UnifiedObject owner, System.Type want)
    {
        if (field == null || owner == null) return null;
        return owner is REFrameworkNET.ValueType vt
            ? Use(vt, address => field.GetDataBoxed(want, address, true))
            : field.GetDataBoxed(want, owner.GetAddress(), false);
    }
}
