using System.Collections.Generic;
using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// The navigation radar's SIDEWAYS sensor — the one ray pair the game does not
/// publish at a range a player can use.
///
/// <para><b>Why this exists.</b> The radar used to cast the game's own
/// <c>SIDE_R</c> / <c>SIDE_L</c> types, and in play the sides never fired. Measuring
/// every sideways segment the state publishes (from its own
/// <c>GetCastRayPosition</c> output, player at (-2.510, 0.091, -72.181)) says why:
/// <c>SIDE_*</c> reach 0.50 m, <c>BESIDE_*</c> 0.40 m, <c>*_FOWARD</c> 0.60 m — you
/// would have to be all but touching a wall for one to report. That is correct for
/// what the GAME uses them for (wall-hugging and step-around), and useless as
/// echolocation. The engine simply publishes no long sideways ray.</para>
///
/// <para><b>What it does instead.</b> <c>AvatarState_FieldBase</c> also exposes a
/// free-form overload, <c>CastRayAll(ref vec3 start, ref vec3 end, CastRayResult,
/// eFilterInfo)</c>, so the radar can cast a segment of its own. Every number in
/// that segment is still the game's:</para>
/// <list type="bullet">
/// <item><b>Origin</b> — the start point <c>GetCastRayPosition</c> writes for
///   <c>SIDE_R</c>/<c>SIDE_L</c> itself, so the ray leaves from the game's own
///   chest height and never from a Y this code picked.</item>
/// <item><b>Direction</b> — that same published segment, normalized. Reading the
///   direction off the game's own ray is what keeps the sides from being MIRRORED:
///   the dump shows <c>SIDE_R</c> running along <b>−AxisX</b>, not +AxisX, which
///   agrees with the handedness <see cref="FieldDirectionService"/> confirmed in
///   game for the clock readout. Nothing here assumes an axis sign.</item>
/// <item><b>Length</b> — the length of <c>FRONT_LONG</c>, the longest forward probe
///   the state publishes (2.00 m at runtime). The radar's sideways reach is defined
///   AS the game's own longest forward reach, so the sensor is symmetric and no
///   distance is invented. If the game ever changes that ray, the sides follow.</item>
/// </list>
///
/// <para><b>Cast budget.</b> This does not add casts: the two long rays REPLACE the
/// two short published ones, since any hit inside 0.50 m is also a hit inside the
/// longer segment. It adds three <c>GetCastRayPosition</c> reads per sample (both
/// side segments and the reach), which write into caller-owned buffers and cast
/// nothing.</para>
///
/// <para><b>Safety.</b> The endpoints are <c>ref</c> parameters the engine READS, so
/// they must be populated before the call — the opposite direction of travel from
/// the <c>out</c> buffers elsewhere, but the same rule about the memory: unmanaged,
/// over-reserved and aligned <see cref="FieldOutBuffer"/>, never a managed
/// <c>CreateValueType</c>. Each write is read back through the field metadata before
/// the cast; a mismatch skips the call rather than handing the engine a half-filled
/// struct. The buffers are allocated once and kept for the process — unmanaged memory
/// never moves, so there is nothing to re-acquire, and re-allocating four blocks
/// several times a second would be churn for nothing.</para>
/// </summary>
public static class FieldNavSideRays
{
    /// <summary>Below this squared length a published segment carries no usable
    /// direction. A degeneracy guard against dividing by zero, not a game value: it
    /// only distinguishes "the engine wrote a real segment" from "the engine wrote
    /// nothing", exactly as <see cref="FieldDirectionService"/> does for headings.</summary>
    private const float MIN_SEGMENT_SQR_LEN = 1e-6f;

    /// <summary>Shapes that pick the two overloads apart:
    /// <c>GetCastRayPosition(CastRayTypes, out vec3, out vec3)</c> against the
    /// by-segment <c>CastRayAll(ref vec3, ref vec3, CastRayResult, eFilterInfo)</c> —
    /// the by-type <c>CastRayAll</c> the forward stack uses has three parameters and
    /// a <c>CastRayTypes</c> first, so the two can never be confused.</summary>
    private const string RAY_TYPE_SUFFIX = "CastRayTypes";
    private const string VECTOR_SUFFIX = "vec3";

    // Bound per CONCRETE field-state type: the state changes with what the avatar is
    // doing, and a handle cached from a previous state is the stale binding the house
    // rules warn about.
    private static readonly Dictionary<string, Method> GetPosByState = new();
    private static readonly Dictionary<string, Method> CastSegByState = new();

    private static FieldOutBuffer _start, _end, _reachStart, _reachEnd;
    private static bool _buffersTried;

    /// <summary>Cast both long sideways rays into <paramref name="result"/>.
    /// <para>False means the extended probe could not be run at all (an unbound
    /// overload, an unusable buffer, an unreadable segment). The caller MUST then
    /// fall back to the game's own short feelers: reporting the sides open because
    /// the sensor failed is the one answer a navigation radar may never give.</para></summary>
    public static bool TryCast(ManagedObject state, ManagedObject result, int filterId,
                               int rightRayId, int leftRayId, int reachRayId,
                               out bool rightBlocked, out bool leftBlocked)
    {
        rightBlocked = false;
        leftBlocked = false;
        if (state == null || result == null || filterId < 0) return false;
        if (rightRayId < 0 || leftRayId < 0 || reachRayId < 0) return false;

        var td = state.GetTypeDefinition();
        string typeName = td?.GetFullName();
        if (typeName == null) return false;

        var getPos = Resolve(GetPosByState, td, typeName, "GetCastRayPosition", 3, RAY_TYPE_SUFFIX);
        var castSeg = Resolve(CastSegByState, td, typeName, "CastRayAll", 4, VECTOR_SUFFIX);
        if (getPos == null || castSeg == null) return false;
        if (!EnsureBuffers(getPos, castSeg)) return false;

        float reach = ReachMeters(state, getPos, reachRayId);
        if (reach <= 0f) return false;

        // Both or neither: a single side that answered is not a reading, because the
        // caller has no way to say "right known, left unknown".
        return CastOne(state, getPos, castSeg, result, filterId, rightRayId, reach, out rightBlocked)
               && CastOne(state, getPos, castSeg, result, filterId, leftRayId, reach, out leftBlocked);
    }

    /// <summary>One extended ray: ask the game where its own short feeler runs, keep
    /// that origin and direction, push the far end out to the reach, cast.</summary>
    private static bool CastOne(ManagedObject state, Method getPos, Method castSeg, ManagedObject result,
                                int filterId, int rayId, float reach, out bool blocked)
    {
        blocked = false;
        try
        {
            if (!Segment(state, getPos, rayId, _start, _end, out float dx, out float dy, out float dz,
                         out float length))
                return false;

            float scale = reach / length;
            float sx = _start.Component("x"), sy = _start.Component("y"), sz = _start.Component("z");
            // The far end REPLACES the short one the engine just wrote; the origin
            // buffer is left exactly as the game filled it, so the ray starts where
            // the game's own side ray starts.
            if (!WriteVector(_end, sx + dx * scale, sy + dy * scale, sz + dz * scale)) return false;

            result.Call("clear");
            castSeg.InvokeBoxed(null, state, new object[] { _start.View, _end.View, result, filterId });
            blocked = FieldProbeService.ContactCount(result) > 0;
            return true;
        }
        catch { return false; }
    }

    /// <summary>The reach the sideways rays inherit: the length of the state's own
    /// longest forward probe, measured from the segment it publishes rather than
    /// stated as a number here.</summary>
    private static float ReachMeters(ManagedObject state, Method getPos, int reachRayId)
    {
        try
        {
            return Segment(state, getPos, reachRayId, _reachStart, _reachEnd,
                           out float _x, out float _y, out float _z, out float length)
                ? length : 0f;
        }
        catch { return 0f; }
    }

    /// <summary>One published segment into a pair of buffers, returned as its delta
    /// and length. False when the engine wrote nothing usable.</summary>
    private static bool Segment(ManagedObject state, Method getPos, int rayId,
                                FieldOutBuffer start, FieldOutBuffer end,
                                out float dx, out float dy, out float dz, out float length)
    {
        dx = dy = dz = length = 0f;
        start.Clear();
        end.Clear();
        getPos.InvokeBoxed(null, state, new object[] { rayId, start.View, end.View });
        dx = end.Component("x") - start.Component("x");
        dy = end.Component("y") - start.Component("y");
        dz = end.Component("z") - start.Component("z");
        float sqr = dx * dx + dy * dy + dz * dz;
        if (!float.IsFinite(sqr) || sqr < MIN_SEGMENT_SQR_LEN) return false;
        length = (float)System.Math.Sqrt(sqr);
        return true;
    }

    /// <summary>Populate a <c>ref vec3</c> input, then read it back through the SAME
    /// field metadata the segment read uses. The read side is already proven correct
    /// in game (the probe dumps real coordinates), so a matching read-back is the
    /// evidence that the engine will find the value where we put it.</summary>
    private static bool WriteVector(FieldOutBuffer buf, float x, float y, float z)
    {
        if (!buf.SetComponent("x", x) || !buf.SetComponent("y", y) || !buf.SetComponent("z", z))
            return false;
        return buf.Component("x") == x && buf.Component("y") == y && buf.Component("z") == z;
    }

    /// <summary>Four buffers, once for the process. Shaped by the OUT parameter type
    /// of <c>GetCastRayPosition</c>, which must be the same struct the by-segment
    /// <c>CastRayAll</c> reads back — the same buffer plays both roles, so a mismatch
    /// is refused instead of reinterpreted.</summary>
    private static bool EnsureBuffers(Method getPos, Method castSeg)
    {
        if (_start != null) return true;
        if (_buffersTried) return false;
        _buffersTried = true;

        var outType = getPos.GetParameters()?[1].Type;
        var inType = castSeg.GetParameters()?[0].Type;
        if (outType == null || inType == null || outType.FullName != inType.FullName)
        {
            API.LogWarning($"[SF6Access] Nav radar sideways probe disabled: GetCastRayPosition writes " +
                           $"{outType?.FullName ?? "?"} but CastRayAll reads {inType?.FullName ?? "?"}");
            return false;
        }

        _start = FieldOutBuffer.Acquire(outType);
        _end = FieldOutBuffer.Acquire(outType);
        _reachStart = FieldOutBuffer.Acquire(outType);
        _reachEnd = FieldOutBuffer.Acquire(outType);
        if (_start != null && _end != null && _reachStart != null && _reachEnd != null) return true;

        API.LogWarning("[SF6Access] Nav radar sideways probe disabled: " + FieldOutBuffer.Refusal(outType));
        Release();
        return false;
    }

    private static void Release()
    {
        _start?.Dispose(); _end?.Dispose(); _reachStart?.Dispose(); _reachEnd?.Dispose();
        _start = _end = _reachStart = _reachEnd = null;
    }

    private static Method Resolve(Dictionary<string, Method> cache, TypeDefinition td, string typeName,
                                  string method, int paramCount, string firstParamSuffix)
    {
        if (cache.TryGetValue(typeName, out var cached)) return cached;
        var found = FieldProbeService.FindByShape(td, method, paramCount, firstParamSuffix);
        cache[typeName] = found;
        // Once per state type, the same way the forward stack reports its own
        // binding: a NOT FOUND here is exactly what sends the radar to the short
        // published feelers, so it has to be visible in the log.
        API.LogInfo($"[SF6Access] Nav radar sideways probe: {method} on {typeName} -> " +
                    $"{(found == null ? "NOT FOUND" : found.DeclaringType?.FullName ?? "ok")}");
        return found;
    }
}
