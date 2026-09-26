using System;
using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Turns the World Tour camera to face a world direction, so that "hold the stick
/// up" then walks the player that way.
///
/// <para><b>Why the camera and not the avatar.</b> World Tour movement is
/// CAMERA-relative (every clock direction in this mod is built on that, see
/// <see cref="FieldDirectionService"/>). Rotating the avatar would leave "forward"
/// pointing wherever the camera still looked, which is no help at all. The avatar's
/// rotation is rewritten every frame by <c>AvatarFieldParam_Common.ApplyDefaultRot</c>
/// (<c>AvatarFieldParam_Common.cs:565</c>) anyway, so a transform write would not
/// survive.</para>
///
/// <para><b>What is written:</b> <c>WTFollowCameraController.CurrentRotation</c>
/// (<c>WTFollowCameraController.cs:20</c>), a <c>via.vec3</c> of euler angles the
/// follow layer drives the camera from. Held for <see cref="AIM_MS"/> because the
/// follow layer interpolates and a single frame would be smoothed away.</para>
///
/// <para><b>How it is written, and why two attempts failed silently first.</b>
/// <c>REFrameworkNET.ValueType</c> is ALWAYS A COPY — it owns a managed
/// <c>byte[]</c> and has no constructor that wraps foreign memory (verified by
/// decompiling <c>REFramework.NET.dll</c> itself, 2026-09-09). So every route
/// through one hands back detached bytes: the untyped getter did, and so did
/// <c>Field.GetDataBoxed</c> on the container. Writing to that copy succeeds and
/// reading it straight back returns what was written, so BOTH attempts verified
/// perfectly while the engine saw nothing — the second attempt's "fresh view" was
/// merely a fresh COPY.
///
/// The only address inside the live object comes from
/// <c>Field.GetDataRaw(objectAddress, isValueType: false)</c>. The float is then
/// stored through the <c>via.vec3</c> field metadata with <c>isValueType: true</c>,
/// because the container AT THAT ADDRESS is the struct — the same contract
/// <see cref="FieldOutBuffer.SetComponent"/> has always used correctly. The
/// verification re-reads that address. <b>A read-back through the wrapper you wrote
/// through is not a verification</b>, and that lesson cost three test sessions.</para>
///
/// <para><b>Calibration is DIFFERENTIAL.</b> The stubs do not say which euler
/// component is the yaw (the base class carries both a <c>deg2_to_rad3_xy</c> and a
/// <c>_xz</c> converter, <c>WTCameraControllerBase.cs:452/458</c>) nor whether it is
/// radians or degrees. Comparing a component against the camera's ABSOLUTE bearing
/// was tried and matched nothing in game — it also requires the two angles to share
/// a zero and a winding direction, which nothing promised. This instead watches the
/// camera while the player turns it and asks a weaker question: which component
/// CHANGES in proportion to the heading changing, and by what factor. Offsets
/// cancel in a difference. In game it measured <c>CurrentRotation.y in radians</c>
/// to three decimals, twice. The aim is applied as a DELTA, recomputed every frame
/// of the turn so it converges instead of firing once and hoping.</para>
/// </summary>
public static class CameraAimService
{
    private const string CONTROLLER = "app.worldtour.WTPlayerCameraController";
    private const string ROTATION_FIELD = "CurrentRotation";

    /// <summary>How long the aim is held against the controller's own update. Four
    /// times <see cref="CameraHoldService.HOLD_MS"/>, the beat this mod already
    /// calls "long enough to react, short enough not to feel like a stutter".</summary>
    private const long AIM_MS = 4 * CameraHoldService.HOLD_MS;

    /// <summary>How much the camera must have turned between two samples for that
    /// pair to count as evidence. Half a clock hour: the finest turn this mod can
    /// describe, and small enough that ordinary looking around produces it.</summary>
    private const double MIN_TURN_DEG = FieldDirectionService.DEGREES_PER_HOUR / 2.0;

    /// <summary>A hypothesis is accepted when the component's measured change is
    /// within a fifth of what it predicts. Loose enough for a frame of interpolation
    /// lag, tight enough that a pitch change never passes as yaw.</summary>
    private const double MATCH_FRACTION = 0.2;

    /// <summary>Two independent turns must agree before the mapping is trusted.</summary>
    private const int EVIDENCE_NEEDED = 2;

    /// <summary>Sampling gap while calibrating: 10 Hz. Sampling stops entirely once
    /// calibrated, so walking the scene for the controller is not a standing cost.</summary>
    private const long SAMPLE_MS = 100;

    private static readonly string[] Components = { "x", "y", "z" };

    /// <summary>Candidate mappings from a DEGREE of heading change to a change in
    /// the stored component: radians or degrees, either winding direction.</summary>
    private static readonly (double factor, string name)[] Hypotheses =
    {
        (Math.PI / 180.0, "radians"),
        (-Math.PI / 180.0, "radians (negated)"),
        (1.0, "degrees"),
        (-1.0, "degrees (negated)"),
    };

    private static readonly Dictionary<string, Field> ComponentFields = new();

    private static Field _rotationField;
    private static bool _fieldMissingLogged;

    private static string _yawComponent;
    private static double _degToStored;
    private static bool _calibrated, _writeFailed;

    private static long _nextSampleAt;
    private static bool _haveSample;
    private static double _lastHeadingDeg;
    private static readonly double[] LastComponents = new double[3];
    private static string _candidateComponent;
    private static double _candidateFactor;
    private static int _evidence;

    private static long _aimUntil;
    private static double _desiredDeg;

    /// <summary>Has the yaw mapping been measured, and does writing it reach the
    /// live controller?</summary>
    public static bool Available => _calibrated && !_writeFailed;

    /// <summary>What the mapping is, or why there is none — for the log.</summary>
    public static string Status { get; private set; } = "not calibrated yet (turn the camera)";

    /// <summary>Turn the camera to face a world direction from the player's
    /// position. False when it could not be done, in which case nothing was written
    /// and the caller should speak the bearing instead.</summary>
    public static bool AimAt(float px, float pz, float targetX, float targetZ)
    {
        float dx = targetX - px, dz = targetZ - pz;
        if (dx == 0f && dz == 0f) return false;
        if (!Available) return false;

        _desiredDeg = Math.Atan2(dx, dz) * 180.0 / Math.PI;
        _aimUntil = Environment.TickCount64 + AIM_MS;

        // Freeze look input for the same beat, so the stick the player may still be
        // holding does not immediately undo the turn.
        CameraHoldService.Hold();
        return Apply();
    }

    /// <summary>Called every frame: holds an aim in progress, and otherwise keeps
    /// calibrating until the mapping is known.</summary>
    public static void Tick()
    {
        if (Environment.TickCount64 < _aimUntil) { Apply(); return; }
        if (!_calibrated) Calibrate();
    }

    /// <summary>Forget everything measured. The controller is rebuilt per city.</summary>
    public static void Reset()
    {
        _calibrated = _writeFailed = _haveSample = _fieldMissingLogged = false;
        _rotationField = null;
        ComponentFields.Clear();
        _yawComponent = _candidateComponent = null;
        _evidence = 0;
        _aimUntil = _nextSampleAt = 0;
        Status = "not calibrated yet (turn the camera)";
    }

    // ---------- applying an aim ----------

    private static bool Apply()
    {
        ulong vec = RotationAddress();
        if (vec == 0) return false;

        var forward = FieldDirectionService.GetCameraForward();
        if (!forward.Ok) return false;
        double headingDeg = Math.Atan2(forward.X, forward.Z) * 180.0 / Math.PI;

        // A DELTA, not an absolute angle: no shared zero is needed between the
        // engine's euler and our bearing, and re-deriving it every frame lets the
        // turn converge rather than overshoot once.
        double turnDeg = Delta(_desiredDeg, headingDeg);
        double current = ReadComponent(vec, _yawComponent);
        if (!double.IsFinite(current)) return false;

        return WriteComponent(vec, _yawComponent, (float)(current + turnDeg * _degToStored));
    }

    // ---------- measuring the mapping ----------

    private static void Calibrate()
    {
        long now = Environment.TickCount64;
        if (now < _nextSampleAt) return;
        _nextSampleAt = now + SAMPLE_MS;

        ulong vec = RotationAddress();
        if (vec == 0) return;

        var forward = FieldDirectionService.GetCameraForward();
        if (!forward.Ok) return;
        double heading = Math.Atan2(forward.X, forward.Z) * 180.0 / Math.PI;

        var comps = new double[Components.Length];
        for (int i = 0; i < Components.Length; i++)
        {
            comps[i] = ReadComponent(vec, Components[i]);
            // Fitting a mapping to a failed read would calibrate on garbage.
            if (!double.IsFinite(comps[i])) return;
        }

        if (_haveSample)
        {
            double turned = Delta(heading, _lastHeadingDeg);
            if (Math.Abs(turned) >= MIN_TURN_DEG) Judge(turned, comps);
        }

        _lastHeadingDeg = heading;
        Array.Copy(comps, LastComponents, comps.Length);
        _haveSample = true;
    }

    /// <summary>Test every component against every hypothesis for one turn, and
    /// commit only when two separate turns agree.</summary>
    private static void Judge(double turnedDeg, double[] comps)
    {
        for (int i = 0; i < Components.Length; i++)
            foreach (var (factor, name) in Hypotheses)
            {
                double expected = turnedDeg * factor;
                // The component wraps; compare on the circle the hypothesis lives
                // on, so a turn across the seam is not counted as a miss.
                double period = Math.Abs(factor) > 0.5 ? 360.0 : 2.0 * Math.PI;
                double measured = Wrap(comps[i] - LastComponents[i], period);
                if (Math.Abs(measured - expected) > Math.Abs(expected) * MATCH_FRACTION) continue;

                if (_candidateComponent == Components[i] && _candidateFactor == factor)
                {
                    if (++_evidence < EVIDENCE_NEEDED) return;
                    _yawComponent = Components[i];
                    _degToStored = factor;
                    _calibrated = true;
                    Status = $"{ROTATION_FIELD}.{Components[i]} in {name}";
                    API.LogInfo($"[SF6Access] Camera aim calibrated: yaw is {Status} " +
                                $"(a {turnedDeg:0.0} deg turn moved it by {measured:0.000})");
                    return;
                }

                _candidateComponent = Components[i];
                _candidateFactor = factor;
                _evidence = 1;
                return;
            }
    }

    // ---------- engine access ----------

    /// <summary>The LIVE address of the rotation field inside the controller, or 0.
    /// Deliberately NOT a <c>ValueType</c>: that class is always a detached copy,
    /// which is the whole reason this feature failed twice.</summary>
    private static ulong RotationAddress()
    {
        try
        {
            var controller = FieldProbeService.FirstSceneComponent(CONTROLLER);
            if (controller == null) return 0;
            if (!ResolveField(controller)) return 0;
            return _rotationField.GetDataRaw(controller.GetAddress(), false);
        }
        catch { return 0; }
    }

    /// <summary>One float of the vec3 at a live address. The container there is the
    /// STRUCT, hence <c>isValueType: true</c>.</summary>
    private static double ReadComponent(ulong vecAddress, string name)
    {
        try
        {
            var f = ComponentField(name);
            if (f == null || vecAddress == 0) return double.NaN;
            return Convert.ToDouble(f.GetDataBoxed(typeof(float), vecAddress, true));
        }
        catch { return double.NaN; }
    }

    /// <summary>Store one float in place, and prove it landed by re-reading THAT
    /// ADDRESS.</summary>
    private static bool WriteComponent(ulong vecAddress, string name, float value)
    {
        try
        {
            var f = ComponentField(name);
            if (f != null && vecAddress != 0)
            {
                f.SetDataBoxed(vecAddress, value, true);
                if (Convert.ToSingle(f.GetDataBoxed(typeof(float), vecAddress, true)) == value) return true;
            }
        }
        catch { }

        _writeFailed = true;
        Status = $"writing {ROTATION_FIELD}.{name} does not reach the live controller";
        API.LogWarning("[SF6Access] Camera aim: " + Status + "; directions will be spoken instead");
        return false;
    }

    /// <summary>The <c>via.vec3</c> component field, cached. Its type comes from the
    /// rotation field itself, so <c>via.vec3</c> is never named by hand.</summary>
    private static Field ComponentField(string name)
    {
        if (ComponentFields.TryGetValue(name, out var cached)) return cached;
        Field f = null;
        try { f = _rotationField?.Type?.GetField(name); } catch { }
        ComponentFields[name] = f;
        return f;
    }

    /// <summary>Find <c>CurrentRotation</c> on the controller or any of its bases —
    /// it is declared on <c>WTFollowCameraController</c> (<c>:20</c>), above the
    /// concrete controller, and a derived TypeDefinition does not expose what its
    /// parent declares. The stub calls it a property, so the compiler-generated
    /// backing-field name is tried as well.</summary>
    private static bool ResolveField(ManagedObject controller)
    {
        if (_rotationField != null) return true;

        for (var td = controller.GetTypeDefinition(); td != null; td = td.ParentType)
            foreach (var name in new[] { ROTATION_FIELD, "<" + ROTATION_FIELD + ">k__BackingField" })
            {
                try
                {
                    var f = td.GetField(name);
                    if (f == null) continue;
                    _rotationField = f;
                    API.LogInfo($"[SF6Access] Camera aim: {name} found on {td.GetFullName()} " +
                                $"(type {f.Type?.FullName})");
                    return true;
                }
                catch { }
            }

        if (!_fieldMissingLogged)
        {
            _fieldMissingLogged = true;
            Status = $"{CONTROLLER} publishes no {ROTATION_FIELD} field";
            API.LogWarning("[SF6Access] Camera aim: " + Status);
        }
        return false;
    }

    private static double Delta(double a, double b) => Wrap(a - b, 360.0);

    private static double Wrap(double v, double period)
    {
        double half = period / 2.0;
        while (v > half) v -= period;
        while (v <= -half) v += period;
        return v;
    }
}
