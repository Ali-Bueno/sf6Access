using System;
using System.Collections.Generic;
using System.Text;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Block C of the one-shot World Tour navigation probe: the avatar's OWN sensing
/// rays, cast through the game's API instead of a hand-rolled sweep.
///
/// <para><c>AvatarBase.GetFieldState()</c> returns an
/// <c>app.worldtour.avatar.AvatarState_FieldBase</c>, which is where World Tour
/// keeps the ray set it uses every frame to decide whether the avatar can step,
/// climb, hang or fall. Each ray is named by the nested <c>CastRayTypes</c> enum,
/// and the state itself owns the geometry (origin, direction, length) for every one
/// of them — so the probe never has to invent a height, a reach or an ordinal. It
/// asks for the segment (<c>GetCastRayPosition</c>), then asks the game to cast it.</para>
///
/// <para><b>Which cast, and why only one of the two.</b> The state exposes two:</para>
/// <list type="bullet">
/// <item><c>bool CastRay(CastRayTypes, out CollisionSystem.HitResult)</c> — its out
///   parameter is a REFERENCE type, so the engine writes a pointer or a whole record
///   THROUGH the address we pass, into memory no caller can shape. It is never
///   called; the dump says so and why. This is the call that killed the game.</item>
/// <item><c>void CastRayAll(CastRayTypes, CastRayResult, eFilterInfo)</c> — its
///   result is an ordinary BY-VALUE object argument the engine mutates in place (the
///   same signature marks its <c>vec3</c> parameters by-ref, so the absence of a
///   marker here is real information). It is the safe call, and it gives strictly
///   more than <c>HitResult</c>: every contact along the ray, each a
///   <c>via.physics.ContactPoint</c> with position, normal, distance and
///   time-of-impact — which is where per-ray hit distance now comes from.</item>
/// </list>
///
/// <para>Nothing here is gated on the collision capsule: this block must produce
/// data even when the capsule is unreadable. Every ray is cast under its own guard,
/// so a type the current state rejects costs one line, not the whole block.</para>
///
/// <para>Diagnostic only, one run per F10 press. Method handles are resolved fresh
/// each run against the LIVE state object's own type chain — the concrete field
/// state changes with what the avatar is doing, and a cached binding from a
/// previous state is exactly the stale handle the house rules warn about.</para>
/// </summary>
public static class FieldRayProbe
{
    private const string STATE_TYPE = "app.worldtour.avatar.AvatarState_FieldBase";
    private const string CAST_RAY_ENUM = STATE_TYPE + ".CastRayTypes";

    /// <summary>The enum's closing sentinel — a count, not a ray.</summary>
    private const string CAST_RAY_SENTINEL = "_CAST_RAY_MAX";

    /// <summary>The filter the sweep uses. <c>CastRayAll</c> takes one explicitly
    /// (unlike <c>CastRay</c>, which picks it internally), and this is the engine's
    /// own named filter for terrain RAY casts — the TerrainRay layer exists for
    /// exactly this query. It is looked up by NAME in <c>eFilterInfo</c>, never by
    /// id, and block B prints the layer and mask it resolves to.</summary>
    private const string SWEEP_FILTER = "TerrainRayFilter";

    /// <summary>Contacts printed per ray. <c>CastRayAll</c> returns every surface
    /// along the segment; a dump only needs enough to see the layering.</summary>
    private const int MAX_CONTACTS_LISTED = 3;

    /// <summary>The only rays actually CAST this run — a deliberately small blast
    /// radius while <c>CastRayAll</c> is still unproven in game.
    ///
    /// <para>The hit/no-hit question for all 32 ray types is already answered and
    /// recorded (open street: ground-only hits; against a wall: the full forward
    /// stack), so re-casting everything buys nothing. What is still open is whether
    /// <c>CastRayAll</c> is safe and whether <c>getContactPoint</c> yields real
    /// distances — and that needs a handful of rays, not all of them.</para>
    ///
    /// <para>The set is the one a navigation radar would live on: the forward stack
    /// at the heights that distinguish a kerb from a rail from a wall from an
    /// overhang (FOOT/plain/WAIST/BUST/HIWALL), the two sideways feelers that make a
    /// corridor readable, the long forward reach for "how far to the next
    /// obstruction", and the downward GROUND probe for floor height. Every name is
    /// resolved against the TDB enum at run time; a name the game does not publish
    /// is reported and skipped, never guessed at by ordinal.</para></summary>
    private static readonly string[] RADAR_RAYS =
    {
        "FOOT_FRONT", "FRONT", "WAIST_FRONT", "BUST_FRONT", "HIWALL_FRONT",
        "SIDE_R", "SIDE_L", "FRONT_LONG", "GROUND",
    };

    /// <summary>The single ray the filter comparison runs on. GROUND is the one probe
    /// that finds something from any position an avatar can stand in — the earlier
    /// run had it hitting even in open street where every forward ray missed — so it
    /// is the ray that can actually tell the filters apart.</summary>
    private const string FILTER_COMPARISON_RAY = "GROUND";

    public static void DumpRays(StringBuilder sb, ManagedObject avatar)
    {
        var state = FieldProbeService.FieldState(avatar);
        sb.AppendLine($"GetFieldState() = {state?.GetTypeDefinition()?.GetFullName() ?? "null"}");
        if (state == null)
        {
            sb.AppendLine("[no field state -> the avatar's own ray API is unreachable]");
            return;
        }

        var td = state.GetTypeDefinition();
        var getPos = Find(td, "GetCastRayPosition", 3, "CastRayTypes");
        var castRay = Find(td, "CastRay", 2, "CastRayTypes");
        var castRayAll = Find(td, "CastRayAll", 3, "CastRayTypes");
        sb.AppendLine($"GetCastRayPosition={Sig(getPos)} CastRay={Sig(castRay)} CastRayAll={Sig(castRayAll)}");
        ReportCastRaySkip(sb, castRay);

        var types = FieldProbeService.ReadEnum(CAST_RAY_ENUM, byteWidth: false);
        sb.AppendLine($"{CAST_RAY_ENUM}: {types.Count} members");
        if (types.Count == 0) { sb.AppendLine("[enum not in the TDB -> nothing to cast]"); return; }

        int filterId = FieldProbeService.EnumValue(FieldProbeService.FILTER_ENUM, SWEEP_FILTER);
        // The result object belongs to the engine, is passed by value and is reused
        // (cleared) for every ray, so one probe run allocates exactly one.
        var result = FieldProbeService.NewInstance(castRayAll?.GetParameters()?[1].Type);
        sb.AppendLine($"CastRayAll filter {SWEEP_FILTER}={filterId}, result = " +
                      $"{result?.GetTypeDefinition()?.GetFullName() ?? "COULD NOT BE ALLOCATED"} " +
                      "(by-value class argument, engine-sized, not globalized)");
        sb.AppendLine($"Segments read for all {types.Count} ray types; CastRayAll restricted to " +
                      $"{RADAR_RAYS.Length} named radar rays: {string.Join(", ", RADAR_RAYS)}");
        sb.AppendLine();

        int casts = Sweep(sb, state, types, getPos, castRayAll, result, filterId);
        sb.AppendLine();
        try { casts += FilterComparison(sb, state, types, castRayAll, result); }
        catch (Exception ex) { sb.AppendLine($"filter comparison failed: {ex.GetType().Name}: {ex.Message}"); }
        sb.AppendLine();
        sb.AppendLine($"BLAST RADIUS: {casts} CastRayAll calls this run.");
    }

    /// <summary>Say, in the dump, that the direct hit test was not run and why —
    /// reading the refusal off the method's OWN parameter type rather than asserting
    /// it, so the line stays true if the game's signature ever changes.</summary>
    private static void ReportCastRaySkip(StringBuilder sb, Method castRay)
    {
        if (castRay == null) { sb.AppendLine("CastRay: not found (nothing skipped)"); return; }
        var t = castRay.GetParameters()?[1].Type;
        sb.AppendLine($"CastRay NOT CALLED: {FieldOutBuffer.Refusal(t)}");
        sb.AppendLine("  -> per-ray hits and distances come from CastRayAll contact points instead.");
    }

    /// <summary>Every named ray's SEGMENT — that read is cheap and proven safe, so it
    /// still covers the whole enum — but the cast itself only for the radar subset.
    /// Returns how many casts were actually performed.</summary>
    private static int Sweep(StringBuilder sb, ManagedObject state, List<(string name, int value)> types,
                             Method getPos, Method castRayAll, ManagedObject result, int filterId)
    {
        int casts = 0;
        foreach (var (name, value) in types)
        {
            if (name == CAST_RAY_SENTINEL) continue;
            try
            {
                bool cast = Array.IndexOf(RADAR_RAYS, name) >= 0;
                int n = -1;
                if (cast) { n = Cast(state, value, castRayAll, result, filterId); casts++; }
                sb.AppendLine($"  {name} ({value}): {Segment(state, value, getPos)} | " +
                              (cast ? Contacts(n) : "[not cast this run - segment only]"));
                if (cast) ListContacts(sb, result, n);
            }
            catch (Exception ex) { sb.AppendLine($"  {name} ({value}): [FAILED {ex.GetType().Name}: {ex.Message}]"); }
        }
        return casts;
    }

    /// <summary>The segment the game would cast, both endpoints written by the engine
    /// into caller-owned buffers shaped by the method's OWN parameter types. A buffer
    /// that cannot be shown safe means no call at all.</summary>
    private static string Segment(ManagedObject state, int type, Method getPos)
    {
        if (getPos == null) return "[GetCastRayPosition unavailable]";
        var ps = getPos.GetParameters();
        using var start = FieldOutBuffer.Acquire(ps?[1].Type);
        using var end = FieldOutBuffer.Acquire(ps?[2].Type);
        if (start == null || end == null) return $"[segment: {FieldOutBuffer.Refusal(ps?[1].Type)}]";
        getPos.InvokeBoxed(null, state, new object[] { type, start.View, end.View });
        return $"{FieldProbeService.Vec(start)} -> {FieldProbeService.Vec(end)}";
    }

    /// <summary>One ray through <c>CastRayAll</c>. Returns the contact count, or -1
    /// when the call could not be made at all.</summary>
    private static int Cast(ManagedObject state, int type, Method castRayAll, ManagedObject result, int filterId)
    {
        if (castRayAll == null || result == null || filterId < 0) return -1;
        result.Call("clear");
        castRayAll.InvokeBoxed(null, state, new object[] { type, result, filterId });
        var n = FieldProbeService.Member(result, "NumContactPoints", typeof(uint));
        return n == null ? 0 : (int)Convert.ToUInt32(n);
    }

    private static string Contacts(int n) =>
        n < 0 ? "[CastRayAll unavailable -> no hit test]" : n == 0 ? "no hit" : $"HIT, {n} contacts";

    /// <summary>The nearest few contacts of the cast still sitting in the result.
    /// <c>getContactPoint</c> returns a VALUE type BY VALUE, which REFramework boxes
    /// from the method's own TDB return type — no caller buffer is involved, so this
    /// is safe. Naming the generated <c>via.physics.ContactPoint</c> INTERFACE as the
    /// target type would only wrap that in a dispatch proxy, which reads as all
    /// zeros, so the target type stays plain.</summary>
    private static void ListContacts(StringBuilder sb, ManagedObject result, int count)
    {
        if (count <= 0) return;
        var td = result.GetTypeDefinition();
        var getPoint = td?.GetMethod("getContactPoint(System.UInt32)");
        var getCollidable = td?.GetMethod("getContactCollidable(System.UInt32)");
        int limit = Math.Min(count, MAX_CONTACTS_LISTED);
        for (uint i = 0; i < limit; i++)
        {
            object cp = null, coll = null;
            try { cp = getPoint?.InvokeBoxed(typeof(object), result, new object[] { i }); } catch { }
            try { coll = getCollidable?.InvokeBoxed(typeof(object), result, new object[] { i }); } catch { }
            sb.AppendLine($"      [{i}] {FieldProbeService.Contact(cp)} " +
                          $"obj='{FieldProbeService.GameObjectName(FieldProbeService.Member(coll, "GameObject"))}'");
        }
        if (count > limit) sb.AppendLine($"      ... {count - limit} more");
    }

    /// <summary>Re-cast exactly ONE ray against every collision filter the game
    /// defines — the experiment that tells a future radar which filter to use.
    /// Returns how many casts it performed.</summary>
    private static int FilterComparison(StringBuilder sb, ManagedObject state,
                                        List<(string name, int value)> types, Method castRayAll,
                                        ManagedObject result)
    {
        int rayType = -1;
        foreach (var (name, value) in types) if (name == FILTER_COMPARISON_RAY) rayType = value;
        if (rayType < 0)
        {
            sb.AppendLine($"--- filter comparison: {FILTER_COMPARISON_RAY} is not in the TDB enum, skipped ---");
            return 0;
        }
        sb.AppendLine($"--- filter comparison on {FILTER_COMPARISON_RAY} ({rayType}) only ---");
        int casts = 0;
        foreach (var (name, value) in FieldProbeService.ReadEnum(FieldProbeService.FILTER_ENUM, false))
        {
            try
            {
                sb.AppendLine($"  {name} ({value}) -> {Contacts(Cast(state, rayType, castRayAll, result, value))}");
                casts++;
            }
            catch (Exception ex) { sb.AppendLine($"  {name} ({value}) -> [FAILED {ex.GetType().Name}]"); }
        }
        return casts;
    }

    /// <summary>Pick an overload by shape (see
    /// <see cref="FieldProbeService.FindByShape"/>), starting from the LIVE state's
    /// concrete type and falling back to the declared base when there is no live
    /// state to walk up from.</summary>
    private static Method Find(TypeDefinition start, string name, int paramCount, string firstParamSuffix) =>
        FieldProbeService.FindByShape(start ?? TDB.Get().FindType(STATE_TYPE), name, paramCount, firstParamSuffix);

    private static string Sig(Method m) => m == null ? "NOT FOUND" : $"ok({m.DeclaringType?.FullName ?? "?"})";
}
