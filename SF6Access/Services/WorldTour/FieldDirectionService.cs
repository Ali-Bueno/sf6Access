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
/// ambiguity; <c>CameraVec</c> is the fallback). If an avatar-facing frame is
/// ever needed instead, it is GameObject → Transform → <c>get_AxisZ</c> (RE
/// Engine's forward-axis convention) — see the repo docs.</para>
///
/// <para><b>Calibration status (2026-07-20, in game):</b> forward axis confirmed
/// (target dead ahead reads 12) and left/right handedness confirmed (rotating
/// the camera right drops the hour toward 11) — see the <c>rightward</c> comment
/// in <see cref="ClockHour"/> for the confirmed sign convention.</para>
/// </summary>
public static class FieldDirectionService
{
    // Clock-face geometry: 12 hours over 360°.
    private const float DEGREES_PER_HOUR = 360f / 12f;

    // Below this squared XZ length a direction has no usable heading (a camera
    // looking straight down projects to ~zero, a target standing on top of the
    // player likewise); treat it as unreadable.
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

    /// <summary>An offset expressed in the frame of a forward direction: how much
    /// of it lies AHEAD and how much to the RIGHT, both normalized to [-1, 1]. The
    /// clock hour is one reading of it; a stereo pan (<see cref="Right"/>) and a
    /// front/back test (<see cref="Behind"/>) are two others, and they all have to
    /// agree, so they all come from here.</summary>
    public readonly struct Bearing
    {
        public readonly float Ahead;
        public readonly float Right;
        public readonly bool Ok;
        public Bearing(float ahead, float right, bool ok) { Ahead = ahead; Right = right; Ok = ok; }

        /// <summary>True when the target sits in the back half of the frame.</summary>
        public bool Behind => Ahead < 0f;
    }

    /// <summary>Resolve the offset <c>(dx, dz)</c> into <paramref name="forward"/>'s
    /// frame. <c>Ok=false</c> when either direction is unusable.</summary>
    public static Bearing GetBearing(FlatDir forward, float dx, float dz)
    {
        if (!forward.Ok) return default;
        var to = Flatten(dx, dz);
        if (!to.Ok) return default;

        float ahead = to.X * forward.X + to.Z * forward.Z;
        // Rightward basis = forward × up = (-fz, fx) on the XZ plane: RE
        // Engine's world is right-handed Y-up, CONFIRMED in game 2026-07-20 —
        // with the target at 12, rotating the camera right must drop the hour
        // toward 11 (the opposite sign read 1, i.e. mirrored).
        float rightward = to.Z * forward.X - to.X * forward.Z;
        return new Bearing(ahead, rightward, true);
    }

    /// <summary>The clock hour (1–12) of the offset <c>(dx, dz)</c> relative to
    /// <c>forward</c>: 12 = straight ahead, 3 = right, 6 = behind, 9 = left.
    /// Returns 0 when the forward frame is unusable.</summary>
    public static int ClockHour(FlatDir forward, float dx, float dz)
    {
        var b = GetBearing(forward, dx, dz);
        if (!b.Ok) return 0;

        double deg = System.Math.Atan2(b.Right, b.Ahead) * 180.0 / System.Math.PI;
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
