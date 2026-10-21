using System;
using System.Collections.Generic;
using SF6Access.Hooks.WorldTour;
using static SF6Access.Services.WorldTour.LevelRoutePlanner;

namespace SF6Access.Services.WorldTour;

/// <summary>Read-only previews of local walking routes; selected guidance retains exclusive query ownership.</summary>
internal static class ExplorationRoutes
{
    // One candidate per compass sector at most; a complete pass takes two seconds at the native query floor.
    private const int MaximumTargets = FieldHeadingService.SECTORS;
    private const long LifetimeMs = (MaximumTargets + 2) * FieldNavPath.MinQueryIntervalMs;
    private const long GatherMs = 4 * FieldNavPath.MinQueryIntervalMs;
    private static float RangeM => 4 * FieldBeaconHooks.HOME_RANGE_M;
    private static readonly PlaceRouteMemory Memory = new(LifetimeMs, MissionBeaconHooks.ARRIVED_M / 2f);
    private static long _gatherAt, _attemptAt;
    private static uint _city, _situation;
    private static long _epoch;

    internal readonly record struct Choice(PlaceRouteMemory.Route Route, int Hour, float OffDegrees, long Epoch);

    internal static void Update()
    {
        FieldPresenceService.Refresh();
        if (!FieldFeedback.CanOutput || !MapSelectionMemory.Exploring || CityListeningMode.Active || !FieldNavPath.Enabled)
        { Reset(); return; }
        long now = Environment.TickCount64;
        uint city = WorldTourStateService.CityId, situation = WorldTourStateService.SituationId;
        if (city == 0) { Reset(); return; }
        if (_city != city || _situation != situation) { Reset(); _city = city; _situation = situation; }
        var me = FieldPresenceService.PlayerPos;
        var origin = new Spot(me.X, me.Y, me.Z);
        if (now >= _gatherAt)
        {
            _gatherAt = now + GatherMs;
            Memory.Load(Gather(origin), city, situation);
        }
        if (now < _attemptAt || now < FieldNavPath.NextQueryTick || !Memory.TryNext(out var target)) return;
        var live = AvatarFieldReader.ReadPlayerPos(null);
        if (!live.ok) return;
        origin = new Spot(live.x, live.y, live.z);
        var d = target.Destination;
        var answer = FieldNavPath.TryReachFrom(origin.X, origin.Y, origin.Z, d.X, d.Y, d.Z);
        if (answer.Throttled) return;
        _attemptAt = now + FieldNavPath.MinQueryIntervalMs;
        bool confirmed = answer.Ok && answer.Exists && !answer.StartOffMesh && !answer.EndOffMesh
            && answer.HasCorner && answer.GeometryVerified;
        Memory.Record(target, origin, new Spot(answer.CornerX, answer.CornerY, answer.CornerZ),
            answer.PathM, confirmed, now);
    }

    private static IEnumerable<PlaceRouteMemory.Target> Gather(Spot player)
    {
        var nearby = new List<PlaceCache.Place>();
        foreach (var place in PlaceCache.Places())
        {
            var d = place.Destination;
            if (d.Action != DestinationAction.Go || !d.HasPosition || d.Actor.IsValid
                || d.Category is not (DestinationCategory.Rest or DestinationCategory.Places or DestinationCategory.Travel
                    or DestinationCategory.Shops or DestinationCategory.Saved or DestinationCategory.Pins)) continue;
            float meters = d.FlatDistanceTo(player.X, player.Z);
            if (meters > MissionBeaconHooks.ARRIVED_M && meters <= RangeM) nearby.Add(place);
        }
        nearby.Sort((a, b) => a.Destination.FlatDistanceTo(player.X, player.Z)
            .CompareTo(b.Destination.FlatDistanceTo(player.X, player.Z)));
        var selected = new List<PlaceRouteMemory.Target>();
        var sectors = new HashSet<int>();
        var keys = new HashSet<string>();
        foreach (var place in nearby)
        {
            var d = place.Destination;
            double angle = Math.Atan2(d.X - player.X, d.Z - player.Z);
            int sector = ((int)Math.Round(angle * MaximumTargets / (2 * Math.PI)) + MaximumTargets) % MaximumTargets;
            if (!sectors.Add(sector)) continue;
            keys.Add(place.Key);
            selected.Add(new(place.Key, d));
        }
        foreach (var place in nearby)
        {
            if (selected.Count >= MaximumTargets) break;
            if (keys.Add(place.Key)) selected.Add(new(place.Key, place.Destination));
        }
        return selected;
    }

    internal static List<Choice> Current()
    {
        var result = new List<Choice>();
        if (!FieldFeedback.CanOutput || !MapSelectionMemory.Exploring || CityListeningMode.Active || !FieldNavPath.Enabled
            || _city != WorldTourStateService.CityId || _situation != WorldTourStateService.SituationId) return result;
        var me = AvatarFieldReader.ReadPlayerPos(null);
        if (!me.ok) return result;
        var forward = FieldDirectionService.GetCameraForward();
        foreach (var route in Memory.Current(new Spot(me.x, me.y, me.z), Environment.TickCount64,
                     FieldRayCaster.CapsuleRadius() / 2f))
        {
            float dx = route.Next.X - me.x, dz = route.Next.Z - me.z;
            var bearing = FieldDirectionService.GetBearing(forward, dx, dz);
            if (!bearing.Ok) continue;
            result.Add(new(route, FieldDirectionService.ClockHour(forward, dx, dz),
                AxisLatch.OffDeg(bearing.Ahead, bearing.Right), _epoch));
        }
        result.Sort((a, b) => a.Route.Meters.CompareTo(b.Route.Meters));
        return result;
    }

    internal static string Describe(Choice choice) => string.Format(
        ExplorationText.Get("path_choice", "At {0} o'clock: walking route toward {1}, {2} meters"),
        choice.Hour, choice.Route.Target.Destination.Label, Math.Max(1, (int)MathF.Round(choice.Route.Meters)));

    internal static void Reset()
    {
        Memory.Clear(); _city = _situation = 0; _gatherAt = _attemptAt = 0;
        _epoch++;
    }
}
