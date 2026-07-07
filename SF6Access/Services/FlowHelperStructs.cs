using System;
using System.Runtime.InteropServices;
using REFrameworkNET;

namespace SF6Access.Services;

/// <summary>
/// Struct-field reads (via.Color, via.vec2). GetDataBoxed returns a
/// REFrameworkNET.ValueType for engine structs; the raw bytes are read from
/// its address (same technique as FlowHelper.ResolveGuid).
/// </summary>
public static partial class FlowHelper
{
    /// <summary>
    /// Read a via.Color field as its packed rgba uint (r = lowest byte, per
    /// via.Color's byte accessors r/g/b/a). Null when unreadable.
    /// </summary>
    public static uint? ReadColorField(ManagedObject obj, string fieldName)
    {
        var bytes = ReadStructField(obj, fieldName, 4);
        if (bytes == null) return null;
        return BitConverter.ToUInt32(bytes, 0);
    }

    /// <summary>Read a via.vec2/Float2 field as two floats. Null when unreadable.</summary>
    public static (float x, float y)? ReadVec2Field(ManagedObject obj, string fieldName)
    {
        var bytes = ReadStructField(obj, fieldName, 8);
        if (bytes == null) return null;
        return (BitConverter.ToSingle(bytes, 0), BitConverter.ToSingle(bytes, 4));
    }

    private static byte[] ReadStructField(ManagedObject obj, string fieldName, int size)
    {
        if (obj == null) return null;
        try
        {
            var field = obj.GetTypeDefinition()?.GetField(fieldName);
            if (field == null) return null;

            ulong addr = obj.GetAddress();
            if (addr == 0) return null;

            var boxed = field.GetDataBoxed(typeof(object), addr, false);
            if (boxed is not REFrameworkNET.ValueType vt) return null;

            ulong vtAddr = vt.GetAddress();
            if (vtAddr == 0) return null;

            var bytes = new byte[size];
            Marshal.Copy((IntPtr)(long)vtAddr, bytes, 0, size);
            return bytes;
        }
        catch { return null; }
    }
}
