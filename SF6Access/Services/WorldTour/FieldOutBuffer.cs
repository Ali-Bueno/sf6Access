using System;
using System.Runtime.InteropServices;
using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// The ONLY buffer the World Tour probe hands to native code for an <c>out</c> /
/// <c>ref</c> parameter — and the reason the probe stopped killing the game.
///
/// <para><b>Why not <c>TypeDefinition.CreateValueType()</c>.</b> That allocates a
/// managed <c>byte[ValueTypeSize]</c> on the GC heap, and <c>ValueType.Ptr()</c>
/// takes its address inside a <c>fixed</c> block that is released BEFORE the pointer
/// is returned. The address REFramework passes to the engine is therefore UNPINNED:
/// a garbage collection during the native call relocates the array and the engine
/// keeps writing into memory that is no longer ours. It is also exactly
/// <c>ValueTypeSize</c> bytes with no slack, on an 8-byte-aligned managed array — so
/// a whole-register store of a <c>via.vec3</c> can run off the end, and an ALIGNED
/// SIMD store can fault outright. Every symptom of the crash this class replaced
/// (correct data returned, game dead seconds later, nothing in the log) is what that
/// looks like.</para>
///
/// <para><b>What this does instead.</b> Unmanaged memory, which never moves; rounded
/// up to and aligned on the engine's OWN vector-register width, read from the TDB;
/// zeroed before every call. The engine's write lands inside bytes we own no matter
/// which store width it picks.</para>
///
/// <para><b>Value types only.</b> A by-ref parameter whose type is a REFERENCE type
/// is refused and must never be worked around — see <see cref="Refusal"/>.</para>
/// </summary>
public sealed class FieldOutBuffer : IDisposable
{
    /// <summary>RE Engine's four-float math value type. Its TDB size is the widest
    /// single store the engine can make into one of these buffers (a <c>via.vec3</c>
    /// out-write is commonly a whole-register store of this width), so it is both the
    /// rounding unit and the alignment used here. The value is READ from the TDB — if
    /// the game does not publish the type, no buffer is created and the caller skips
    /// the call rather than guessing a width.</summary>
    private const string VECTOR_REGISTER_TYPE = "via.vec4";

    private static int _registerWidth = -1;

    private IntPtr _block;

    /// <summary>The parameter type this buffer was shaped by.</summary>
    public TypeDefinition Type { get; private set; }

    /// <summary>Bytes actually reserved — always at least the type's own size.</summary>
    public int Bytes { get; private set; }

    /// <summary>Where the engine writes. Stable for the lifetime of this object.</summary>
    public ulong Address { get; private set; }

    /// <summary>The argument handed to <c>InvokeBoxed</c>. A <c>NativeObject</c>
    /// returns its stored address verbatim from <c>Ptr()</c>, which is exactly what
    /// REFramework copies into the native argument slot.</summary>
    public NativeObject View { get; private set; }

    private FieldOutBuffer() { }

    /// <summary>A buffer for an out/ref parameter, or null when one cannot be shown
    /// to be safe. Callers MUST treat null as "skip the call" and print
    /// <see cref="Refusal"/>.</summary>
    public static FieldOutBuffer Acquire(TypeDefinition td)
    {
        int width = RegisterWidth();
        if (td == null || !td.IsValueType() || td.ValueTypeSize < 1 || width < 1) return null;

        int size = (int)td.ValueTypeSize;
        // Round the reservation up to a whole vector register, then over-allocate by
        // one more so the payload can be moved to an aligned address inside the block.
        int reserved = ((size + width - 1) / width) * width;
        IntPtr block = IntPtr.Zero;
        try
        {
            block = Marshal.AllocHGlobal(reserved + width);
            long aligned = ((long)block + width - 1) & ~((long)width - 1);
            var buf = new FieldOutBuffer
            {
                _block = block,
                Type = td,
                Bytes = reserved,
                Address = (ulong)aligned,
            };
            buf.View = NativeObject.FromAddress(buf.Address, td);
            buf.Clear();
            return buf;
        }
        catch
        {
            if (block != IntPtr.Zero) Marshal.FreeHGlobal(block);
            return null;
        }
    }

    /// <summary>Why <see cref="Acquire"/> said no, in words the dump prints. A skipped
    /// call has to explain itself: an honest gap in the data is usable, a crash is not.</summary>
    public static string Refusal(TypeDefinition td)
    {
        if (td == null) return "parameter type unknown -> call SKIPPED";
        if (!td.IsValueType())
            return $"{td.FullName} is a REFERENCE type, so this out/ref parameter is an address the " +
                   "engine writes a pointer or a whole record THROUGH; no caller buffer can be shown " +
                   "correct for it -> call SKIPPED";
        if (td.ValueTypeSize < 1)
            return $"{td.FullName} reports ValueTypeSize {td.ValueTypeSize} -> size unknown, call SKIPPED";
        if (RegisterWidth() < 1)
            return $"{VECTOR_REGISTER_TYPE} is not in the TDB -> the engine's store width is unknown, call SKIPPED";
        return $"{td.FullName} buffer could not be reserved -> call SKIPPED";
    }

    /// <summary>Zero the whole reservation. Done before every call so a value left by
    /// a previous ray can never be mistaken for one the engine just wrote.</summary>
    public void Clear()
    {
        if (Address == 0) return;
        for (int i = 0; i < Bytes; i++) Marshal.WriteByte((IntPtr)(long)(Address + (ulong)i), 0);
    }

    /// <summary>One float field of the struct the engine wrote, located by the
    /// game's OWN field metadata (the buffer has no managed header, hence the
    /// value-type read).</summary>
    public float Component(string name)
    {
        if (Address == 0) return 0f;
        try
        {
            var f = Type?.GetField(name);
            return f == null ? 0f : Convert.ToSingle(f.GetDataBoxed(typeof(float), Address, true));
        }
        catch { return 0f; }
    }

    /// <summary>Write one float field of the struct — the mirror of
    /// <see cref="Component"/>, and the reason this class is not only an OUT buffer.
    /// A <c>ref</c> parameter the engine READS (the endpoints of
    /// <c>CastRayAll(ref vec3 start, ref vec3 end, ...)</c>) needs the value in place
    /// BEFORE the call, and it has to land where the engine looks for it: at the
    /// offset the game's own field metadata names, through the same
    /// container-is-a-value-type contract the read side already proves correct.
    ///
    /// <para>False means the field is not published or the write threw — callers
    /// must then skip the call rather than hand the engine a half-filled struct.</para></summary>
    public bool SetComponent(string name, float value)
    {
        if (Address == 0) return false;
        try
        {
            var f = Type?.GetField(name);
            if (f == null) return false;
            f.SetDataBoxed(Address, value, true);
            return true;
        }
        catch { return false; }
    }

    /// <summary>What the engine was handed, for the dump: kind and exact byte count.</summary>
    public override string ToString() =>
        $"unmanaged out buffer, {Bytes} bytes for {Type?.FullName ?? "?"} ({Type?.ValueTypeSize} declared)";

    public void Dispose()
    {
        if (_block == IntPtr.Zero) return;
        Marshal.FreeHGlobal(_block);
        _block = IntPtr.Zero;
        Address = 0;
        View = null;
    }

    private static int RegisterWidth()
    {
        if (_registerWidth < 0)
        {
            try
            {
                var td = TDB.Get()?.FindType(VECTOR_REGISTER_TYPE);
                _registerWidth = td != null && td.IsValueType() ? (int)td.ValueTypeSize : 0;
            }
            catch { _registerWidth = 0; }
        }
        return _registerWidth;
    }
}
