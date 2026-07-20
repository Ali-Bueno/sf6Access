using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Clock-direction math for the World Tour field radar (WT-1): turns a
/// player→target offset into an announced clock hour ("Luke at 2 o'clock").
///
/// <para><b>Reference frame:</b> the announced hour is CAMERA-relative — in the
/// WT field the stick moves the avatar relative to the camera, so "12" must mean
/// "push the stick up", not "where the avatar model happens to face". The camera
/// forward comes from <c>app.CameraManager</c> (<c>LookAtPosition −
/// CameraPosition</c>, two positions, so no quaternion decomposition and no sign
/// ambiguity; <c>CameraVec</c> is the fallback). The avatar's own facing
/// (<c>via.Transform.AxisZ</c>, RE Engine's forward-axis convention) is exposed
/// only for the calibration diagnostics that compare both frames in one test.</para>
///
/// <para><b>Calibration status:</b> the left/right sense of the hour (whether a
/// target to the player's right reads 3 or 9) depends on RE Engine's XZ
/// handedness and is NOT yet runtime-confirmed — if the in-game test reads
/// mirrored, flip the sign of <c>rightward</c> in <see cref="ClockHour"/>.</para>
/// </summary>
public static class FieldDirectionService
{
    // Clock-face geometry: 12 hours over 360°.
    private const float DEGREES_PER_HOUR = 360f / 12f;

    // Below this squared XZ length a forward vector has no usable heading (e.g.
    // a camera looking straight down projects to ~zero); treat it as unreadable.
    private const float MIN_FLAT_SQR_LEN = 1e-6f;

    /// <summary>A direction projected onto the ground (XZ) plane.</summary>
    public readonly struct FlatDir
    {
        public readonly float X;
        public readonly float Z;
        public readonly bool Ok;
        public FlatDir(float x, float z, bool ok) { X = x; Z = z; Ok = ok; }
    }

    /// <summary>The active camera's ground-plane forward, from
    /// <c>app.CameraManager</c>: primary source is <c>LookAtPosition −
    /// CameraPosition</c>; falls back to the manager's own <c>CameraVec</c>.</summary>
    public static FlatDir GetCameraForward()
    {
        var cam = WorldTourStateService.GetCameraManager();
        if (cam == null) return default;

        var pos = ReadVec(cam, "CameraPosition");
        var look = ReadVec(cam, "LookAtPosition");
        if (pos.ok && look.ok)
        {
            var dir = Flatten(look.x - pos.x, look.z - pos.z);
            if (dir.Ok) return dir;
        }

        var vec = ReadVec(cam, "CameraVec");
        return vec.ok ? Flatten(vec.x, vec.z) : default;
    }

    /// <summary>The avatar's own ground-plane facing —
    /// GameObject → Transform → <c>AxisZ</c> (RE Engine's forward axis).
    /// Diagnostic-only until the frame question is settled in game.</summary>
    public static FlatDir GetAvatarForward(ManagedObject avatar)
    {
        try
        {
            var go = FlowHelper.Call(avatar, "get_GameObject") as ManagedObject;
            var tr = FlowHelper.Call(go, "get_Transform") as ManagedObject;
            var axisZ = FlowHelper.Call(tr, "get_AxisZ");
            if (axisZ == null) return default;
            float x = FlowHelper.ReadVecComponent(axisZ, "x");
            float z = FlowHelper.ReadVecComponent(axisZ, "z");
            if (!float.IsFinite(x) || !float.IsFinite(z)) return default;
            return Flatten(x, z);
        }
        catch { return default; }
    }

    /// <summary>The clock hour (1–12) of the offset <c>(dx, dz)</c> relative to
    /// <c>forward</c>: 12 = straight ahead, 3 = right, 6 = behind, 9 = left
    /// (pending the handedness calibration noted in the class doc). Returns 0
    /// when the forward frame is unusable.</summary>
    public static int ClockHour(FlatDir forward, float dx, float dz)
    {
        if (!forward.Ok) return 0;
        float ahead = dx * forward.X + dz * forward.Z;
        // Rightward basis = up × forward in a Y-up world: (fz, -fx) on the XZ
        // plane. If the in-game test reads mirrored (3↔9), negate this dot.
        float rightward = dx * forward.Z - dz * forward.X;
        if (ahead == 0f && rightward == 0f) return 0;

        double deg = System.Math.Atan2(rightward, ahead) * 180.0 / System.Math.PI;
        int hour = (int)System.Math.Round(deg / DEGREES_PER_HOUR);
        hour = ((hour % 12) + 12) % 12;
        return hour == 0 ? 12 : hour;
    }

    /// <summary>Normalize an XZ direction; <c>Ok=false</c> when it is too short
    /// to carry a heading (vertical vector, failed read).</summary>
    private static FlatDir Flatten(float x, float z)
    {
        float sqr = x * x + z * z;
        if (!float.IsFinite(sqr) || sqr < MIN_FLAT_SQR_LEN) return default;
        float len = (float)System.Math.Sqrt(sqr);
        return new FlatDir(x / len, z / len, true);
    }

    private static (float x, float z, bool ok) ReadVec(ManagedObject owner, string prop)
    {
        try
        {
            var boxed = (object)FlowHelper.GetObjectField(owner, prop)
                        ?? FlowHelper.Call(owner, "get_" + prop);
            if (boxed == null) return (0f, 0f, false);
            float x = FlowHelper.ReadVecComponent(boxed, "x");
            float z = FlowHelper.ReadVecComponent(boxed, "z");
            return (x, z, float.IsFinite(x) && float.IsFinite(z));
        }
        catch { return (0f, 0f, false); }
    }
}
