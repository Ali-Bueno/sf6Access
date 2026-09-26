using System;
using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Turns a filled <c>via.physics.CastRayResult</c> into a <see cref="RayHit"/>: the
/// reading-and-classifying half of <see cref="FieldRayCaster"/>, kept apart from the
/// casting half the way <see cref="FieldNavVerdictService"/> is kept apart from
/// <see cref="FieldNavRadarService"/>.
///
/// <para><b>The rule, ported from the RE7 mod (<c>src/RayCaster.cs</c>).</b> A surface
/// the avatar can walk up is never a wall: the cut is the contact normal's Y against
/// <c>cos(CharacterController.SlopeLimit)</c> — the controller's OWN limit — and the
/// same cut with the sign flipped is a ceiling. Below the height at which the game
/// itself probes for low obstacles, a contact is a step the avatar surmounts by
/// itself. Both thresholds come from <see cref="FieldRayMetrics"/> and are passed in.</para>
///
/// <para><b>Value types.</b> <c>getContactPoint</c> returns a <c>ContactPoint</c> BY
/// VALUE, which REFramework boxes from the method's own TDB return type; naming the
/// generated <c>via.physics.ContactPoint</c> or <c>via.vec3</c> interface as the
/// target type wraps that in a dispatch proxy which reads back as all zeros. The type
/// publishes no property getters either — asking for one logged "Method not found"
/// 73,591 times in a single session — so the three fields are bound once, by name.</para>
/// </summary>
internal static class FieldRayContacts
{
    /// <summary>Layers — <c>app.gCollision.LayerId</c> names, resolved through
    /// <c>via.physics.System.getLayerName(uint)</c> — that carry the level itself. A
    /// contact on any other layer is a prop: it blocks, so it counts towards
    /// <see cref="RayHit.Distance"/>, but it does not shape the street.</summary>
    private static readonly string[] ARCHITECTURAL_LAYERS = { "Terrain", "Static" };

    private const string PHYSICS_SYSTEM = "via.physics.System";

    private static Method _getContactPoint, _getContactCollidable, _getLayerName;
    private static Field _fDistance, _fNormal, _fPosition;
    private static readonly Dictionary<uint, bool> ArchByLayer = new();
    private static bool _fieldsProbed;

    /// <summary>Whether the layer route bound — when it did not, every contact counts
    /// as architectural and the two distances converge. Said in the route line rather
    /// than simulated.</summary>
    public static bool LayerNamesBound => _getLayerName != null;

    /// <summary>Bind the readers against the result type the cast overload declares.
    /// False means a cast whose contacts could not be measured, which is not made.</summary>
    public static bool Bind(TypeDefinition resultType)
    {
        _getContactPoint = resultType?.GetMethod("getContactPoint(System.UInt32)");
        _getContactCollidable = resultType?.GetMethod("getContactCollidable(System.UInt32)");
        try { _getLayerName = TDB.Get().FindType(PHYSICS_SYSTEM)?.GetMethod("getLayerName(System.UInt32)"); }
        catch { _getLayerName = null; }
        return _getContactPoint != null;
    }

    /// <summary>Fold every contact of the cast just made into one answer.
    /// <c>castRayAll</c> promises no ordering, so minima are taken, not contact 0.</summary>
    public static RayHit Gather(ManagedObject result, float feetY, ulong self, float slopeCos, float step)
    {
        int n = FieldProbeService.ContactCount(result);
        float blocking = 0f, arch = 0f, nx = 0f, ny = 0f, nz = 0f;
        float rawNearest = 0f, discardedNearest = 0f;
        var cls = HitClass.None;
        ulong obj = 0;

        for (uint i = 0; i < n; i++)
        {
            object cp = null;
            try { cp = _getContactPoint.InvokeBoxed(typeof(object), result, new object[] { i }); }
            catch { }
            if (!(cp is UnifiedObject uo)) continue;
            BindFields(uo);

            // A distance of 0 is a failed read, not a contact at the avatar's feet.
            float d = FieldProbeService.ToFloat(Read(_fDistance, uo, typeof(float)));
            if (!float.IsFinite(d) || d <= 0f) continue;

            // The raw nearest, over EVERY contact and before any classification, so the
            // diagnostic line below can prove what the cast actually saw.
            if (rawNearest <= 0f || d < rawNearest) rawNearest = d;

            float cny = Component(_fNormal, uo, "y");
            var c = Classify(cny, Component(_fPosition, uo, "y") - feetY, slopeCos, step);
            if (c == HitClass.Ground || c == HitClass.Ceiling)
            {
                // Walkable or overhead, so never a WALL — but it is still opaque. Remember
                // how near it was: nothing behind it may be reported as though the beam had
                // seen through it (see the clamp below).
                if (discardedNearest <= 0f || d < discardedNearest) discardedNearest = d;
                continue;
            }

            // Provenance is the expensive half of a contact — two InvokeBoxed calls, two
            // member reads and a layer lookup — and a contact that can better neither
            // minimum changes nothing, so it is never asked for. This is a COST guard,
            // not an early exit: every contact is still measured, which it has to be
            // because castRayAll promises no ordering. It is what makes the reactive
            // radar's reach affordable at 30 Hz, now that a body sweep casts three rays
            // per beam where one used to do.
            bool bettersBlocking = blocking <= 0f || d < blocking;
            bool bettersArch = arch <= 0f || d < arch;
            if (!bettersBlocking && !bettersArch) continue;

            var (architectural, owner) = Provenance(result, i);
            if (owner != 0 && owner == self) continue;   // the avatar's own collider

            if (blocking <= 0f || d < blocking)
            {
                blocking = d; cls = c; obj = owner;
                nx = Component(_fNormal, uo, "x"); ny = cny; nz = Component(_fNormal, uo, "z");
            }
            // Architectural = geometry that really blocks, standing on a level layer.
            // With no walkable cut nothing can be excluded, so Unknown counts here too
            // and the two distances converge — which is what the route line then says.
            if (architectural && c != HitClass.Step && (arch <= 0f || d < arch)) arch = d;
        }

        // A GROUND or CEILING contact does NOT bound the ray. This reverses an earlier
        // rule here, and the log is why: with the rays mistakenly leaving the body at
        // ankle height they skimmed the street and struck it 60-90 m away, and clamping
        // to that made the reported distance the distance to a patch of pavement. It
        // then changed with every undulation of the ground as the avatar walked, so the
        // beam's line broke over and over and cued each time — "it sounds more the more
        // I move". The floor under a horizontal ray is not an obstacle, it is the floor.
        //
        // The rule the clamp was written for still holds, and still applies: something
        // OPAQUE in front of a wall must not let the wall behind it be reported. But a
        // sill, a kerb or a bollard is classified Step or Wall, and an unreadable
        // surface is Unknown — all three already count towards the blocking minimum
        // above, so they bound the ray by being measured, not by being clamped. Nothing
        // reaching this point is both discarded and opaque.
        LogContacts(n, rawNearest, discardedNearest, blocking);
        return new RayHit(blocking, arch, cls, nx, ny, nz, obj);
    }

    /// <summary>How often the contact diagnostic may speak. The tester cannot see the
    /// screen, so this line is how "it reported something behind a wall" gets proved or
    /// disproved from the log alone; three seconds keeps it readable while a 30 Hz radar
    /// runs.</summary>
    private const long CONTACT_LOG_GAP_MS = 3000;

    private static long _nextContactLogAt;
    private static bool _contactLoggedOnce;

    /// <summary>One rate-limited line: how many contacts the cast returned, the nearest
    /// of them before any classification, the nearest that classification threw away,
    /// and what was finally reported. If the reported distance is ever larger than the
    /// raw nearest without a clamp, the choice of minimum is wrong and this line says
    /// so. Always speaks once, then only when the clamp actually fires.</summary>
    private static void LogContacts(int n, float raw, float discarded, float reported)
    {
        long now = Environment.TickCount64;
        bool first = !_contactLoggedOnce;
        if (!first && now < _nextContactLogAt) return;
        _contactLoggedOnce = true;
        _nextContactLogAt = now + CONTACT_LOG_GAP_MS;
        API.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "[SF6Access] Radar contacts: n={0} rawNearest={1:F2} discardedNearest={2:F2} " +
            "reported={3:F2}", n, raw, discarded, reported));
    }

    /// <summary>Ground, ceiling, step or wall. A surface leaning off vertical by less
    /// than the controller's own <c>SlopeLimit</c> is floor or a ramp going up, or a
    /// ceiling coming down; neither is ever a wall. An unreadable normal stays a wall:
    /// silently dropping a real wall is the one mistake forbidden here.</summary>
    private static HitClass Classify(float normalY, float heightAboveFeet, float slopeCos, float step)
    {
        if (!float.IsFinite(slopeCos)) return HitClass.Unknown;
        if (normalY >= slopeCos) return HitClass.Ground;
        if (normalY <= -slopeCos) return HitClass.Ceiling;
        if (float.IsFinite(step) && float.IsFinite(heightAboveFeet)
            && heightAboveFeet > 0f && heightAboveFeet < step) return HitClass.Step;
        return HitClass.Wall;
    }

    /// <summary>Whether contact <paramref name="i"/> stands on a level layer, and which
    /// GameObject owns it. SF6 publishes no <c>isFixedObject</c> (what the RE7 mod used
    /// to tell props from architecture), so the collision LAYER separates them here —
    /// the game's own classification of the collidable, resolved by NAME and cached per
    /// layer id. Static level geometry carries no <c>Collidable</c> and no GameObject at
    /// all, and that IS the level, so anything unreadable counts as architectural rather
    /// than being demoted to clutter.</summary>
    private static (bool architectural, ulong owner) Provenance(ManagedObject result, uint i)
    {
        object coll = null, go = null;
        try { coll = _getContactCollidable?.InvokeBoxed(typeof(object), result, new object[] { i }); }
        catch { }
        try { go = FieldProbeService.Member(coll, "GameObject"); } catch { }
        ulong owner = (go as UnifiedObject)?.GetAddress() ?? 0UL;

        var fi = coll == null ? null : FieldProbeService.Member(coll, "FilterInfo");
        if (fi == null || _getLayerName == null) return (true, owner);
        uint layer;
        try { layer = Convert.ToUInt32(FieldProbeService.Member(fi, "Layer", typeof(uint)) ?? 0u); }
        catch { return (true, owner); }

        if (!ArchByLayer.TryGetValue(layer, out bool arch))
        {
            string name = null;
            try { name = _getLayerName.InvokeBoxed(typeof(string), null, new object[] { layer }) as string; }
            catch { }
            arch = name == null || Array.IndexOf(ARCHITECTURAL_LAYERS, name) >= 0;
            ArchByLayer[layer] = arch;
        }
        return (arch, owner);
    }

    private static void BindFields(UnifiedObject cp)
    {
        if (_fieldsProbed) return;
        _fieldsProbed = true;
        var td = cp.GetTypeDefinition();
        _fDistance = td?.GetField("Distance");
        _fNormal = td?.GetField("Normal");
        _fPosition = td?.GetField("Position");
        if (_fDistance == null || _fNormal == null || _fPosition == null)
            API.LogWarning("[SF6Access] FieldRayCaster: ContactPoint is missing Distance/Normal/Position; " +
                           "contacts that cannot be measured are dropped, never guessed at.");
    }

    /// <summary>A contact is a struct returned by value: read it pinned (see <see cref="PinnedValue"/>).</summary>
    private static object Read(Field f, UnifiedObject owner, System.Type want)
    {
        try { return PinnedValue.Field(f, owner, want); }
        catch { return null; }
    }

    /// <summary>One component of a vec3 FIELD of a contact, read with NO target type
    /// (see the dispatch-proxy note in this class's summary).</summary>
    private static float Component(Field f, UnifiedObject owner, string axis)
    {
        var v = Read(f, owner, typeof(object));
        return v == null ? float.NaN : FlowHelper.ReadVecComponent(v, axis);
    }
}
