using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// The World Tour navigation radar's SENSOR: nine ray casts per sample, turned
/// into a <see cref="NavReading"/>. It speaks to nobody —
/// <see cref="SF6Access.Hooks.WorldTour.FieldNavRadarHooks"/> owns the keys, the
/// pacing and the announcements.
///
/// <para>This is echolocation of GEOMETRY (walls, openings, drops), complementary
/// to <see cref="SF6Access.Hooks.WorldTour.FieldAwarenessHooks"/>, which names
/// PEOPLE. Neither knows about the other.</para>
///
/// <para><b>Why the game's own rays.</b> <c>AvatarState_FieldBase</c> is where
/// World Tour keeps the ray set it uses every frame to decide whether the avatar
/// can step, climb, hang or fall. Every ray is named (<c>CastRayTypes</c>) and the
/// game owns its height and reach, so the radar never invents an offset: the
/// obstacle class comes from WHICH named rays hit, not from a measured height.
/// Names are resolved against the TDB enum, never by ordinal — a name the game
/// does not publish is simply not cast.</para>
///
/// <para><b>Rays measure, the game judges (2026-09-05).</b> The rays no longer decide
/// whether the avatar is BLOCKED — they only say what is ahead and how far. Deriving
/// "you cannot go forward" from the ladder was wrong in both directions in play: the
/// rungs have different reaches so a real obstruction could fall between them, and
/// <c>TerrainRayFilter</c> does not see fences and props the capsule collides with
/// anyway, while kerbs the avatar climbs by itself read as walls. The verdict now
/// comes from the game's own collision state through
/// <see cref="FieldNavVerdictService"/>; this class is the DISTANCE and DESCRIPTION
/// sensor it is paired with.</para>
///
/// <para><b>The one exception:</b> the SIDEWAYS rays are cast as segments of our own
/// (<see cref="FieldNavSideRays"/>), because every sideways ray the game publishes
/// reaches half a metre or less. Their origin, direction and reach are still read
/// from the game, and they REPLACE the published pair, so the sweep is nine casts
/// either way.</para>
///
/// <para><b>Safety rules carried over from the F10 probe</b> (each learned by
/// crashing the game): only <c>CastRayAll</c> is called, never
/// <c>CastRay(type, out HitResult)</c> (a reference type behind an <c>out</c>);
/// the <c>CastRayResult</c> is an engine-allocated by-value argument and is
/// allocated fresh per sample rather than held across frames or globalized; and
/// <c>ContactPoint</c> is read as the plain boxed value REFramework returns, never
/// through the generated <c>via.physics.ContactPoint</c> interface (which yields a
/// dispatch proxy that reads as zeros).</para>
///
/// <para>Everything static is cached at first use: the two TDB enums once for the
/// process, the <c>CastRayAll</c> handle per concrete field-state type (the
/// concrete state changes with what the avatar is doing, so a single cached handle
/// would be the stale binding the house rules warn about).</para>
/// </summary>
public static class FieldNavRadarService
{
    private const string PLAYER_MANAGER = "app.worldtour.WTPlayerManager";
    private const string STATE_TYPE = "app.worldtour.avatar.AvatarState_FieldBase";
    private const string CAST_RAY_ENUM = STATE_TYPE + ".CastRayTypes";

    /// <summary>The collision filter the sweep uses. CONFIRMED in game by the F10
    /// probe's filter comparison: <c>TerrainRayFilter</c>, <c>EffectRay</c>,
    /// <c>Camera</c> and <c>BattleLine</c> all hit terrain while <c>Terrain</c>
    /// (mask 0x0) hits nothing at all. Resolved by NAME from
    /// <c>app.CollisionSystem.eFilterInfo</c>, never by id.</summary>
    private const string SWEEP_FILTER = "TerrainRayFilter";

    /// <summary>The forward height stack, LOW TO HIGH, with the obstacle class each
    /// rung means when it is the HIGHEST rung that hits. Reading the ladder top-down
    /// (rather than testing for exact combinations) keeps the class correct when a
    /// lower ray misses under an overhang.
    ///
    /// <para>The result is a DESCRIPTION of what lies ahead within these rays' own
    /// reach — "there is something about this tall in front of you" — and never the
    /// answer to "am I stopped", which only the game's own collision can give.</para>
    ///
    /// <para><c>FRONT</c> shares the low tier with <c>FOOT_FRONT</c>: both sit below
    /// the waist ray, and the state model has no separate class between "steppable"
    /// and "waist-high".</para></summary>
    private static readonly (string Ray, FrontProfile Profile)[] FrontStack =
    {
        ("FOOT_FRONT",   FrontProfile.Step),
        ("FRONT",        FrontProfile.Step),
        ("WAIST_FRONT",  FrontProfile.WaistHigh),
        ("BUST_FRONT",   FrontProfile.Wall),
        ("HIWALL_FRONT", FrontProfile.TallWall),
    };

    /// <summary>The long forward reach. It feeds DISTANCE only and never the
    /// obstacle class: it is cast at the plain forward height but reaches far past
    /// the stack, so classifying by it would report a wall two metres away as
    /// something the player is standing against.</summary>
    private const string RAY_FRONT_LONG = "FRONT_LONG";

    /// <summary>The sideways feelers — what makes a corridor or a doorway readable.
    ///
    /// <para>Normally NOT cast as published: what is used is their SEGMENT (origin
    /// and direction), extended by <see cref="FieldNavSideRays"/> — see there for why
    /// every sideways ray the game publishes is too short to echolocate with. They
    /// are cast as published only when that extended probe cannot run.</para></summary>
    private const string RAY_SIDE_R = "SIDE_R";
    private const string RAY_SIDE_L = "SIDE_L";

    /// <summary>The downward probe. No hit means no floor: a ledge or a hole.</summary>
    private const string RAY_GROUND = "GROUND";

    private static readonly string[] ExtraRays = { RAY_FRONT_LONG, RAY_SIDE_R, RAY_SIDE_L, RAY_GROUND };

    // --- cached lookups (resolved at first use, never per frame) ---
    private static bool _enumsRead;
    private static readonly Dictionary<string, int> RayIds = new();
    private static int _filterId = -1;
    private static readonly Dictionary<string, Method> CastRayAllByState = new();
    private static TypeDefinition _resultType;
    private static Method _getContactPoint;
    private static Field _contactDistanceField;
    private static bool _contactDistanceProbed;
    private static bool _unavailableLogged;
    private static bool _shortSidesLogged;

    /// <summary>Cast the nine rays once and classify what came back. Returns a
    /// reading with <c>Ok == false</c> when the avatar, its field state or the ray
    /// API could not be reached — the caller must treat that as "no information",
    /// never as "everything is open".</summary>
    public static NavReading Sample()
    {
        try
        {
            ReadEnumsOnce();
            if (_filterId < 0 || RayIds.Count == 0) return default;

            var pm = API.GetManagedSingleton(PLAYER_MANAGER) as ManagedObject;
            var avatar = FlowHelper.Call(pm, "GetAvatarPlayer") as ManagedObject;
            var state = FieldProbeService.FieldState(avatar);
            if (state == null) return default;

            var castRayAll = ResolveCastRayAll(state);
            if (castRayAll == null) return default;

            // ONE globalized container, shared by every cast this process makes and
            // cleared before each. The comment here used to say the opposite — that
            // rooting an engine-written object was the route to a delayed crash — and
            // it had the danger exactly backwards: NewInstance yields a per-thread
            // LOCAL object (negative reference count, an index into that thread's
            // table) which the engine's frame GC reclaims, so the delayed crash was
            // REFramework's finalizer dereferencing it from the GC thread. See
            // FieldProbeService.SharedInstance.
            var result = FieldProbeService.SharedInstance(_resultType);
            if (result == null) return default;

            var front = FrontProfile.Open;
            float nearest = 0f;
            foreach (var rung in FrontStack)
                if (Cast(state, castRayAll, result, rung.Ray, out float d))
                {
                    front = rung.Profile;   // the ladder is low-to-high: the last hit wins
                    Fold(ref nearest, d);
                }

            bool longHit = Cast(state, castRayAll, result, RAY_FRONT_LONG, out float far);
            if (longHit) Fold(ref nearest, far);

            ReadSides(state, castRayAll, result, out bool left, out bool right);

            // The ground answers a yes/no question, so its contact distance is read
            // and dropped. (`out _` is unusable here: REFramework's generated
            // bindings declare a NAMESPACE called `_`.)
            bool ground = Cast(state, castRayAll, result, RAY_GROUND, out float unusedG);

            var block = Verdict(avatar, state, ref front);
            return new NavReading(block, front, nearest > 0f, nearest, !longHit, left, right, ground);
        }
        catch (System.Exception ex)
        {
            if (!_unavailableLogged)
            {
                _unavailableLogged = true;
                API.LogWarning($"[SF6Access] Nav radar sample failed: {ex.GetType().Name}: {ex.Message}");
            }
            return default;
        }
    }

    /// <summary>How far the sweep looks in each compass direction. The game's
    /// longest published ray (FRONT_LONG, 2 m) is a feeler, not a look around:
    /// this is a picture of the street, and a street in Metro City is of the order
    /// of ten metres across. UX choice, not a game value; documented here.</summary>
    public const float SWEEP_REACH_M = 12f;

    /// <summary>The compass sweep: one horizontal segment per compass point
    /// (<see cref="FieldHeadingService.SECTORS"/> of them, index 0 = north,
    /// clockwise), all from the origin of the game's own long forward ray, each
    /// <see cref="SWEEP_REACH_M"/> long. Entry k is the hit distance, 0 when open.
    /// Null when the sensor is unavailable or the compass has no north.</summary>
    public static float[] Sweep()
    {
        try
        {
            var north = FieldHeadingService.GetNorth();
            if (!north.Ok) return null;
            if (!FreeCastContext(out var state, out var result, out int originRay)) return null;

            // East is north turned clockwise a quarter: the same right-hand basis
            // the clock hours use (FieldDirectionService.GetBearing).
            float ex = -north.Z, ez = north.X;
            var hits = new float[FieldHeadingService.SECTORS];
            for (int k = 0; k < hits.Length; k++)
            {
                double rad = k * FieldHeadingService.DEGREES_PER_SECTOR * System.Math.PI / 180.0;
                float c = (float)System.Math.Cos(rad), sn = (float)System.Math.Sin(rad);
                float dx = north.X * c + ex * sn;
                float dz = north.Z * c + ez * sn;
                if (!FieldNavSideRays.TryCastDirection(state, result, _filterId, originRay, dx, dz,
                                                       SWEEP_REACH_M, out float d))
                    return null;
                hits[k] = d;
            }
            return hits;
        }
        catch (System.Exception ex)
        {
            API.LogWarning($"[SF6Access] Nav sweep failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Everything a free-form segment cast needs: the avatar's field
    /// state, a cast-result buffer and the id of the ray whose origin the segment
    /// starts from. Shared by the compass sweep and the beams.</summary>
    private static bool FreeCastContext(out ManagedObject state, out ManagedObject result, out int originRay)
    {
        state = null; result = null; originRay = -1;
        ReadEnumsOnce();
        if (_filterId < 0 || !RayIds.TryGetValue(RAY_FRONT_LONG, out originRay)) return false;

        var pm = API.GetManagedSingleton(PLAYER_MANAGER) as ManagedObject;
        var avatar = FlowHelper.Call(pm, "GetAvatarPlayer") as ManagedObject;
        state = FieldProbeService.FieldState(avatar);
        if (state == null) return false;
        if (ResolveCastRayAll(state) == null) return false;   // also binds _resultType

        result = FieldProbeService.SharedInstance(_resultType);
        return result != null;
    }

    /// <summary>One free-form forward cast, straight ahead along
    /// <paramref name="forward"/> for <paramref name="reach"/> metres from the
    /// game's own long-forward-ray origin. Shared cast setup for anything that
    /// needs the raw contacts rather than a hit/no-hit answer — currently
    /// <see cref="ContactCatalog"/>, so it does not duplicate the
    /// <see cref="FreeCastContext"/> plumbing <see cref="Sweep"/> and
    /// <see cref="Beams"/> already own.</summary>
    public static bool CastFront(FieldDirectionService.FlatDir forward, float reach, out ManagedObject result)
    {
        result = null;
        if (!forward.Ok || reach <= 0f) return false;
        if (!FreeCastContext(out var state, out result, out int originRay)) return false;
        return FieldNavSideRays.TryCastDirection(state, result, _filterId, originRay, forward.X, forward.Z,
                                                 reach, out float _);
    }

    /// <summary>Am I stopped, and will the game climb what is ahead by itself? Both
    /// answers come from <see cref="FieldNavVerdictService"/> — the game's own
    /// collision verdict — never from the ray ladder, which cannot tell "there is
    /// geometry along this segment" from "you cannot go forward".
    ///
    /// <para>Priority: a wall contact the game reports wins; failing that, an obstacle
    /// the game's own auto-step check has claimed is re-described as a
    /// <see cref="FrontProfile.Step"/>, because the player walks over it without
    /// doing anything; otherwise the ladder's class stands as a DESCRIPTION of what
    /// lies ahead and nothing is blocking.</para>
    ///
    /// <para>When no game-owned route binds at all, the old behaviour is kept
    /// verbatim — any hit in the height stack counts as blocked — so a radar that
    /// cannot reach the verdict is the previous radar, not a silent one. The verdict
    /// service logs that case as a warning.</para></summary>
    private static FrontBlock Verdict(ManagedObject avatar, ManagedObject state, ref FrontProfile front)
    {
        var v = FieldNavVerdictService.Read(avatar, state);

        // "The game will climb this by itself" is an answer in its own right, and it
        // holds whether or not a wall route bound — so it is applied first and ends
        // the question: nothing that the avatar surmounts on its own is a block.
        if (v.Steppable) { front = FrontProfile.Step; return FrontBlock.None; }
        if (v.Bound) return v.Block;
        return front == FrontProfile.Open ? FrontBlock.None : FrontBlock.Blocked;
    }

    /// <summary>Both sides, at the longest reach available. The extended probe is
    /// tried first (the game's own side segment stretched to its own longest forward
    /// reach); when it cannot run, the published short feelers are cast instead — two
    /// casts either way, so the sweep's blast radius is the same nine either way.</summary>
    private static void ReadSides(ManagedObject state, Method castRayAll, ManagedObject result,
                                  out bool left, out bool right)
    {
        if (FieldNavSideRays.TryCast(state, result, _filterId,
                                     RayId(RAY_SIDE_R), RayId(RAY_SIDE_L), RayId(RAY_FRONT_LONG),
                                     out right, out left))
            return;

        if (!_shortSidesLogged)
        {
            _shortSidesLogged = true;
            API.LogWarning("[SF6Access] Nav radar: extended sideways probe unavailable, falling back to the " +
                           "game's published short side feelers — the sides will only report at arm's length.");
        }
        right = Cast(state, castRayAll, result, RAY_SIDE_R, out float unusedR);
        left = Cast(state, castRayAll, result, RAY_SIDE_L, out float unusedL);
    }

    /// <summary>A ray's TDB value, or -1 when the game does not publish that name —
    /// which every caller treats as "not cast", never as an ordinal to guess.</summary>
    private static int RayId(string name) => RayIds.TryGetValue(name, out int id) ? id : -1;

    /// <summary>Keep the nearest usable contact distance. A hit whose distance
    /// reads back as zero is a failed read, not a contact at the avatar's feet, so
    /// it never becomes the reported distance.</summary>
    private static void Fold(ref float nearest, float d)
    {
        if (d > 0f && (nearest <= 0f || d < nearest)) nearest = d;
    }

    /// <summary>One named ray through <c>CastRayAll</c>. True when the engine
    /// reported at least one contact; <paramref name="distance"/> is the nearest
    /// contact's own distance, or 0 when it could not be read.</summary>
    private static bool Cast(ManagedObject state, Method castRayAll, ManagedObject result,
                             string rayName, out float distance)
    {
        distance = 0f;
        if (!RayIds.TryGetValue(rayName, out int rayId)) return false;
        try
        {
            result.Call("clear");
            castRayAll.InvokeBoxed(null, state, new object[] { rayId, result, _filterId });
            int count = FieldProbeService.ContactCount(result);
            if (count <= 0) return false;
            distance = NearestContact(result, (uint)count);
            return true;
        }
        catch { return false; }
    }

    /// <summary>The closest contact along the ray just cast. <c>CastRayAll</c>
    /// returns every surface the segment crosses and makes no ordering promise, so
    /// the minimum is taken rather than contact 0.</summary>
    internal static float NearestContact(ManagedObject result, uint count)
    {
        _getContactPoint ??= result.GetTypeDefinition()?.GetMethod("getContactPoint(System.UInt32)");
        if (_getContactPoint == null) return 0f;

        float best = 0f;
        for (uint i = 0; i < count; i++)
        {
            object cp = null;
            // VALUE-TYPE RETURN: REFramework boxes it from the method's own TDB
            // return type. Naming the generated ContactPoint interface as the
            // target type would wrap that in a proxy which reads as all zeros.
            try { cp = _getContactPoint.InvokeBoxed(typeof(object), result, new object[] { i }); }
            catch { }
            Fold(ref best, ContactDistance(cp));
        }
        return best;
    }

    /// <summary>The engine's own hit distance off a contact point, through the
    /// FIELD, cached on first use. <c>via.physics.ContactPoint</c> has no
    /// <c>get_Distance</c>: asking for one logged "Method not found: get_Distance"
    /// 73,591 times in one seven-minute session (2026-09-07) because the generic
    /// member walk tries the getter before the field on every contact of every
    /// cast. The walk stays as the one-time fallback for a build where the field
    /// cannot be bound either.</summary>
    internal static float ContactDistance(object cp)
    {
        if (cp is UnifiedObject uo)
        {
            if (!_contactDistanceProbed)
            {
                _contactDistanceProbed = true;
                var td = uo.GetTypeDefinition();
                _contactDistanceField = td?.GetField("Distance");
                if (_contactDistanceField == null)
                    API.LogWarning("[SF6Access] Nav radar: ContactPoint.Distance field not found, using the slow member walk");
            }
            if (_contactDistanceField != null)
                return FieldProbeService.ToFloat(PinnedValue.Field(_contactDistanceField, uo, typeof(float)));
        }
        return FieldProbeService.ToFloat(FieldProbeService.Member(cp, "Distance", typeof(float)));
    }

    /// <summary>Both enums, once for the process: they are static TDB metadata and
    /// cannot change while the game runs.</summary>
    private static void ReadEnumsOnce()
    {
        if (_enumsRead) return;
        _enumsRead = true;

        foreach (var (name, value) in FieldProbeService.ReadEnum(CAST_RAY_ENUM, byteWidth: false))
            if (IsRadarRay(name)) RayIds[name] = value;
        _filterId = FieldProbeService.EnumValue(FieldProbeService.FILTER_ENUM, SWEEP_FILTER);

        int wanted = FrontStack.Length + ExtraRays.Length;
        string msg = $"[SF6Access] Nav radar: {RayIds.Count}/{wanted} rays resolved from {CAST_RAY_ENUM}, " +
                     $"filter {SWEEP_FILTER}={_filterId}";
        if (RayIds.Count == wanted && _filterId >= 0) API.LogInfo(msg);
        else API.LogWarning(msg + " — a missing name is simply not cast; the radar degrades, it does not guess.");
    }

    private static bool IsRadarRay(string name)
    {
        foreach (var rung in FrontStack) if (rung.Ray == name) return true;
        foreach (string r in ExtraRays) if (r == name) return true;
        return false;
    }

    /// <summary>The <c>CastRayAll(CastRayTypes, CastRayResult, eFilterInfo)</c>
    /// overload for the LIVE state's concrete type, cached per type name. The
    /// concrete field state changes with what the avatar is doing, so the handle is
    /// re-resolved when the type changes instead of being cached once globally.</summary>
    private static Method ResolveCastRayAll(ManagedObject state)
    {
        var td = state.GetTypeDefinition();
        string typeName = td?.GetFullName();
        if (typeName == null) return null;
        if (CastRayAllByState.TryGetValue(typeName, out var cached)) return cached;

        // Picked by SHAPE, not by a hand-written signature string: CastRayAll has a
        // by-type overload and a by-segment one, and the first parameter tells them
        // apart. Shared with the F10 probe so both agree on which overload is safe.
        var found = FieldProbeService.FindByShape(td, "CastRayAll", 3, "CastRayTypes");
        CastRayAllByState[typeName] = found;
        _resultType ??= found?.GetParameters()?[1].Type;
        API.LogInfo($"[SF6Access] Nav radar bound CastRayAll on {typeName}: " +
                    $"{(found == null ? "NOT FOUND" : found.DeclaringType?.FullName ?? "ok")}");
        return found;
    }
}
