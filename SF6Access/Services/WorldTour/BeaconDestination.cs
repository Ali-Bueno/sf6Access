using System;
using static SF6Access.Services.WorldTour.LevelRoutePlanner;

namespace SF6Access.Services.WorldTour;

/// <summary>Selected destination and the aim toward it: a route bend, a ladder on the way, or an unconfirmed nearby approach.</summary>
public readonly struct BeaconDestination
{
    public bool Ok { get; init; }
    /// <summary>The aim is the next bend of a walkable route.</summary>
    public bool ViaRoute { get; init; }
    public bool IsApproach { get; init; }
    /// <summary>At the foot of the destination: the stand-in is reached and the aim is the destination itself.</summary>
    public bool AtFoot { get; init; }
    public bool IsTracked { get; init; }
    public bool NoRoute { get; init; }
    /// <summary>The path query could not produce a readable answer; no steering direction is confirmed.</summary>
    public bool Unconfirmed { get; init; }
    public bool TargetLost { get; init; }
    public bool InContact { get; init; }
    /// <summary>A ladder search is running; there is no aim yet.</summary>
    public bool Searching { get; init; }
    /// <summary>No news this refresh: rate limited, or the avatar is off the navmesh (jumping, on a ladder).</summary>
    public bool Holding { get; init; }
    /// <summary>A cached answer may still guide, but the next native query belongs to this reader.</summary>
    public bool WaitingForQuery { get; init; }
    public long SampledAt { get; init; }
    /// <summary>This leg ends at a ladder rather than at the destination.</summary>
    public bool Ladder { get; init; }
    public bool LadderUp { get; init; }
    /// <summary>The ladders lead only as near as they can; the destination is beyond.</summary>
    public bool Partial { get; init; }
    /// <summary>No walk reaches this ladder: something the navmesh does not model stands in the way.</summary>
    public bool Blocked { get; init; }
    /// <summary>A partial route has been climbed; guiding on toward the destination from there.</summary>
    public bool Closest { get; init; }
    /// <summary>The ladder leg's access target (0 when unknown) and lowest rung above its floor.</summary>
    public ulong LadderAccess { get; init; }
    public float BottomRungM { get; init; }
    public int Leg { get; init; }
    public int LaddersLeft { get; init; }
    /// <summary>Aim point: a route bend, or the leg's end.</summary>
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    /// <summary>End of the current leg: the destination, a ladder end or an approach point.</summary>
    public float TargetX { get; init; }
    public float TargetY { get; init; }
    public float TargetZ { get; init; }
    public float AboveM { get; init; }
    public float RouteM { get; init; }
    public string Word { get; init; }
    public string Key { get; init; }
    public int Turn { get; init; }

    /// <summary>A mission target farther than this from where it was a refresh ago is a different target (the
    /// next of several), not the same one walking: twice the arrival band, like the route's own reach.</summary>
    private static float MissionJumpM => 2f * Hooks.WorldTour.MissionBeaconHooks.ARRIVED_M;

    private static string _selection, _missionSlot;
    private static Spot _missionAt;
    private static int _missionGeneration;
    private static readonly RouteQueryCache<FieldNavPath.PathAnswer> Cache = new(FieldNavPath.MinQueryIntervalMs * 2);

    public static BeaconDestination Resolve()
    {
        if (MapSelectionMemory.Exploring)
        {
            Select(null);
            LevelRoute.Reset();
            return default;
        }
        var picked = MapSelectionMemory.Refresh();
        if (picked.Ok)
        {
            Select(picked.Key);
            if (picked.Lost || picked.Pending)
                return new BeaconDestination
                {
                    Ok = true, IsTracked = true, Word = picked.Label, Key = picked.Key,
                    X = picked.X, Y = picked.Y, Z = picked.Z,
                    TargetX = picked.X, TargetY = picked.Y, TargetZ = picked.Z,
                    Holding = picked.Pending, Unconfirmed = picked.Lost, TargetLost = picked.Lost,
                    SampledAt = Environment.TickCount64,
                };
            return Route(picked.X, picked.Y, picked.Z, picked.Label, true) with { InContact = picked.InContact };
        }
        var mission = MissionTargetService.Find();
        if (!mission.Ok) { Select(null); LevelRoute.Reset(); return default; }
        Select(MissionKey(mission));
        return Route(mission.X, mission.Y, mission.Z, LocalizedText.MissionWord(), false);
    }

    /// <summary>One key per objective, kept while the game swaps the object that holds it.</summary>
    private static string MissionKey(in MissionTargetService.Target mission)
    {
        var at = new Spot(mission.X, mission.Y, mission.Z);
        if (mission.Slot != _missionSlot || Distance(at, _missionAt) > MissionJumpM)
        {
            _missionSlot = mission.Slot;
            _missionGeneration++;
        }
        _missionAt = at;
        return $"mission:{mission.Slot}:{_missionGeneration}";
    }

    private static void Select(string key)
    {
        if (_selection == key) return;
        _selection = key;
        Cache.Clear();
        ApproachFinder.Reset();
    }

    public static void Reset() { Select(null); Cache.Clear(); }

    private static BeaconDestination Route(float x, float y, float z, string word, bool tracked)
    {
        var plain = new BeaconDestination
        {
            Ok = true, X = x, Y = y, Z = z, TargetX = x, TargetY = y, TargetZ = z,
            Word = word, IsTracked = tracked, Key = _selection, SampledAt = Environment.TickCount64,
        };
        if (!FieldNavPath.Enabled) return plain;
        var me = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());
        if (!me.ok) return plain with { Holding = true };
        var player = new Spot(me.x, me.y, me.z);
        var target = new Spot(x, y, z);
        var level = LevelRoute.Guide(_selection, target, player);
        switch (level.Phase)
        {
            case LevelRoute.Phase.Searching:
                return plain with { Searching = true, NoRoute = true };
            case LevelRoute.Phase.Failed:
                return Approach(plain, player);
            case LevelRoute.Phase.Following:
                var leg = RouteTo(plain, level.To, level.Final ? word : RouteNotice.LadderWord(), out var legAnswer);
                bool hold = LevelRoute.LegResult(legAnswer, player);
                var grip = LadderCatalog.GripOf(level.Link);
                return leg with
                {
                    Holding = leg.Holding || hold, NoRoute = leg.NoRoute && !hold && !level.Blocked, Blocked = level.Blocked,
                    Ladder = !level.Final, LadderUp = level.Up, Leg = level.Leg, LaddersLeft = level.LaddersLeft,
                    Partial = level.Partial, LadderAccess = grip.Access, BottomRungM = level.Up ? grip.BottomRungM : 0f,
                };
        }
        var direct = RouteTo(plain, target, word, out var answer);
        if (!direct.NoRoute) return direct;
        LevelRoute.Begin(_selection, target, player, askDirect: answer.EndOffMesh);
        return plain with { Searching = true, NoRoute = true };
    }

    /// <summary>After a failed or partial ladder search: the nearest reachable point below or above the destination.</summary>
    private static BeaconDestination Approach(BeaconDestination plain, Spot player)
    {
        plain = plain with { Closest = LevelRoute.AtClosest };
        // Up a partial route, a stand-in reached only by leaving the block (a 133 m street walk from the roof,
        // 2026-09-26) is not "the foot of" anything: the beacon points at the destination instead.
        float longest = plain.Closest ? Distance(player, new Spot(plain.TargetX, plain.TargetY, plain.TargetZ)) + NeighbourhoodM
                                      : float.PositiveInfinity;
        if (!ApproachFinder.TryGet(plain.TargetX, plain.TargetY, plain.TargetZ, player.Y, out float sx, out float sy, out float sz))
        {
            ApproachFinder.Probe(plain.TargetX, plain.TargetY, plain.TargetZ, player.X, player.Y, player.Z, longest);
            return plain with
            {
                NoRoute = true, AboveM = plain.TargetY - player.Y,
                WaitingForQuery = ApproachFinder.WaitingForQuery,
            };
        }
        var stand = new Spot(sx, sy, sz);
        if (Flat(player, stand) <= Hooks.WorldTour.MissionBeaconHooks.ARRIVED_M && MathF.Abs(player.Y - sy) < LevelM)
            ApproachFinder.MarkArrived();
        // At the foot the stand-in has done its job: circling it helps nobody (2026-09-26, 16 m from the boss on
        // his roof), so the sound points at the destination while the player looks for the way up.
        if (ApproachFinder.Arrived)
            return plain with { IsApproach = true, AtFoot = true, AboveM = plain.TargetY - player.Y };
        var dest = RouteTo(plain, stand, plain.Word, out var _);
        return dest with { IsApproach = true, AboveM = plain.TargetY - sy };
    }

    private static BeaconDestination RouteTo(BeaconDestination plain, Spot q, string word, out FieldNavPath.PathAnswer answer)
    {
        long now = Environment.TickCount64;
        answer = FieldNavPath.TryReach(q.X, q.Y, q.Z);
        bool waiting = answer.Throttled;
        long sampledAt = waiting ? 0 : now;
        // Reuse keeps its original age and still reserves a refresh; an actual failure replaces it.
        if (waiting && Cache.TryRead(_selection, (q.X, q.Y, q.Z), now, out var cached, out sampledAt))
            answer = cached;
        else if (!waiting) Cache.Store(_selection, (q.X, q.Y, q.Z), answer, sampledAt);
        bool readableCorner = answer.HasCorner && float.IsFinite(answer.CornerX)
            && float.IsFinite(answer.CornerY) && float.IsFinite(answer.CornerZ);
        var status = RouteAnswerPolicy.Classify(answer.Ok, answer.Exists, answer.Throttled,
            answer.StartOffMesh, readableCorner);
        bool route = status == RouteAnswerStatus.Confirmed;
        return plain with
        {
            X = route ? answer.CornerX : q.X, Y = route ? answer.CornerY : q.Y, Z = route ? answer.CornerZ : q.Z,
            TargetX = q.X, TargetY = q.Y, TargetZ = q.Z, Word = word,
            ViaRoute = route, RouteM = route && float.IsFinite(answer.PathM) && answer.PathM > 0 ? answer.PathM : 0,
            Turn = route ? answer.Turn : 0,
            NoRoute = status == RouteAnswerStatus.Unreachable,
            Unconfirmed = status == RouteAnswerStatus.Unknown, Holding = status == RouteAnswerStatus.Pending,
            WaitingForQuery = waiting, SampledAt = sampledAt,
        };
    }
}
