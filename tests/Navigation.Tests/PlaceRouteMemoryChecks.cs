using SF6Access.Services.WorldTour;
using static SF6Access.Services.WorldTour.LevelRoutePlanner;
using Target = SF6Access.Services.WorldTour.PlaceRouteMemory.Target;

internal static class PlaceRouteMemoryChecks
{
    private const float BodyRadiusM = .25f; // Recorded borrowed-agent radius; production passes the live capsule radius.

    private static List<PlaceRouteMemory.Route> Read(PlaceRouteMemory memory, Spot player, long now) =>
        memory.Current(player, now, BodyRadiusM / 2);

    internal static void Run(Action<bool, string> check)
    {
        Fairness(check);
        IdentityAndLifetime(check);
        FirstLegValidity(check);
        FailedEvidence(check);
        WorldTransitions(check);
    }

    private static Target Place(string key, float x = 20, float z = 20) =>
        new(key, new Destination(key, null, DestinationCategory.Places, x, 0, z));

    private static void Record(PlaceRouteMemory memory, Target target, long now, bool confirmed = true) =>
        memory.Record(target, new(0, 0, 0), new(0, 0, 4), 30, confirmed, now);

    private static void Fairness(Action<bool, string> check)
    {
        var memory = new PlaceRouteMemory(2500, 2);
        var a = Place("a"); var b = Place("b"); var c = Place("c"); var d = Place("d");
        memory.Load([a, b, c], 1, 1);
        check(memory.TryNext(out var target) && target == a, "place routes start with an unqueried destination");
        check(memory.TryNext(out var retry) && retry == a, "a throttled query without a record does not consume its destination");
        Record(memory, a, 100, false);
        memory.Load([c, a, b], 1, 1);
        check(memory.TryNext(out target) && target == c, "resorting selects an unqueried destination before a failed destination");
        Record(memory, c, 350);
        memory.Load([a, c, b], 1, 1);
        check(memory.TryNext(out target) && target == b, "repeated nearest-place resorting cannot starve the last unqueried place");
        Record(memory, b, 600);
        memory.Load([b, c, a], 1, 1);
        check(memory.TryNext(out target) && target == a, "once all places were attempted the oldest failure is retried");
        Record(memory, a, 850);
        memory.Load([b, a], 1, 1);
        check(memory.TryNext(out target) && target == b, "removing the oldest place hands the next slot to a retained place");
        check(Read(memory, new(0, 0, 0), 900).All(r => r.Target != c), "removed destinations cannot remain in route readouts");
        memory.Load([b, d, a, d], 1, 1);
        check(memory.TryNext(out target) && target == d, "a newly discovered place receives a slot despite the current sort order");
        Record(memory, d, 1100);
        check(Read(memory, new(0, 0, 0), 1100).Count(r => r.Target == d) == 1, "duplicate place keys do not duplicate route choices");
        check(memory.TryNext(out target) && target == b, "sampling a new place returns fairly to the oldest retained destination");
    }

    private static void IdentityAndLifetime(Action<bool, string> check)
    {
        var memory = new PlaceRouteMemory(1000, 2);
        var a = Place("a");
        memory.Load([a], 1, 1);
        Record(memory, a, 100);
        check(Read(memory, new(0, 0, 0), 99).Count == 0, "a future timestamp cannot validate a preview");
        check(Read(memory, new(0, 0, 0), 100).Single().SampledAt == 100, "a route preserves its actual native sample timestamp");
        Read(memory, new(0, 0, 0), 900);
        memory.Load([a], 1, 1);
        check(Read(memory, new(0, 0, 0), 1100).Count == 1, "the original timestamp stays valid through the configured lifetime");
        check(Read(memory, new(0, 0, 0), 1101).Count == 0, "reads and unchanged gathers do not renew expired route evidence");
        Record(memory, a, 1200);
        check(Read(memory, new(0, 0, 0), 1200).Single().SampledAt == 1200, "only a newly recorded answer restores expired evidence");
        var moved = a with { Destination = a.Destination with { X = 25 } };
        memory.Load([moved], 1, 1);
        check(Read(memory, new(0, 0, 0), 1200).Count == 0, "same-key coordinate changes invalidate the old route");
        check(memory.TryNext(out var next) && next == moved, "a moved endpoint is queried at its new coordinates");
        Record(memory, a, 1250);
        check(Read(memory, new(0, 0, 0), 1250).Count == 0, "an answer for the old endpoint cannot restore the changed place");
        Record(memory, moved, 1500);
        check(Read(memory, new(0, 0, 0), 1500).Single().Target == moved, "new endpoint evidence names the current destination");
        memory.Load([], 1, 1);
        Record(memory, moved, 1750);
        check(!memory.TryNext(out _) && Read(memory, new(0, 0, 0), 1750).Count == 0,
            "a late result cannot resurrect a removed destination");
    }

    private static void FirstLegValidity(Action<bool, string> check)
    {
        var memory = new PlaceRouteMemory(1000, 2);
        var target = Place("around-corner", 40, 40);
        memory.Load([target], 1, 1);
        // Feed the actual look-ahead result for a one-metre right-angle bend, not the distant destination bearing.
        var look = new RouteLookAhead(30, 20, .5f);
        look.Add(30, 2, 21);
        look.Add(32, 2, 21);
        var origin = new Spot(30, 2, 20);
        var corner = new Spot(look.X, look.Y, look.Z);
        memory.Record(target, origin, corner, 30, look.HasPoint, 100);
        check(Read(memory, origin, 100).Single().Next == new Spot(30, 2, 21),
            "a preview retains the verified first bend instead of the destination's straight bearing");
        check(Read(memory, new(30, 2, 20.9f), 200).Count == 1, "approaching a close corner keeps its first leg available");
        check(Read(memory, corner, 200).Count == 0, "reaching a corner suppresses its sampled incoming leg");
        check(Read(memory, new(30, 2, 21.1f), 200).Count == 0, "passing a corner invalidates its direction inside the movement allowance");
        memory.Record(target, origin, new(32, 2, 22), 30, true, 300);
        check(Read(memory, new(31, 2, 21), 300).Count == 1, "advancing along an unpassed diagonal leg preserves its sampled direction");
        check(Read(memory, new(32.01f, 2, 20), 300).Count == 0, "moving beyond the origin confidence radius suppresses the old leg");
        check(Read(memory, new(30, 4.01f, 20), 300).Count == 0, "vertical displacement also invalidates cached walking evidence");
        memory.Record(target, origin, new(30, 2, 24), 30, true, 400);
        check(Read(memory, new(30, 2, 22), 400).Count == 1, "movement exactly at the configured boundary remains eligible");
        check(Read(memory, new(30, 2, 21), 400).Count == 1, "walking one metre forward along the verified segment preserves the preview");
        check(Read(memory, new(31, 2, 20), 400).Count == 0, "one metre of lateral movement requires new walking evidence");
        check(Read(memory, new(30, 3, 20), 400).Count == 0, "one metre of vertical movement cannot reuse a ground-level segment");
        check(Read(memory, new(30.02f, 2.02f, 20.02f), 400).Count == 1, "small three-dimensional idle jitter stays inside the body-radius tolerance");
        check(Read(memory, new(30, 2, 19.8f), 400).Count == 0, "walking backward beyond the origin tolerance cannot reuse an infinite line");
        check(Read(memory, new(30, 2, 19.95f), 400).Count == 1, "minor backward jitter remains inside the origin cap");
        check(memory.Current(origin, 400, float.NaN).Count == 0
            && memory.Current(origin, 400, float.PositiveInfinity).Count == 0
            && memory.Current(origin, 400, -1).Count == 0, "invalid corridor tolerances cannot validate a preview");
        check(memory.Current(origin, 400, 0).Count == 1
            && memory.Current(new(30.01f, 2, 20), 400, 0).Count == 0, "zero tolerance permits only the exact sampled segment");
        memory.Record(target, origin, new(30, 4, 22), 30, true, 450);
        check(Read(memory, new(30, 3, 21), 450).Count == 1, "advancing on a sampled sloped segment remains valid in three dimensions");
        check(Read(memory, new(30, 2, 21), 450).Count == 0, "being below a sloped segment is not treated as following its horizontal shadow");
        memory.Record(target, origin, origin, 30, true, 500);
        check(Read(memory, origin, 500).Count == 0, "a zero-length first leg cannot claim a walking direction");
    }

    private static void FailedEvidence(Action<bool, string> check)
    {
        var memory = new PlaceRouteMemory(1000, 2);
        var a = Place("a");
        memory.Load([a], 1, 1);
        Record(memory, a, 100);
        Record(memory, a, 200, false);
        check(Read(memory, new(0, 0, 0), 200).Count == 0,
            "an unconfirmed or unverified-geometry answer clears a previous confirmed route");
        var invalid = new[]
        {
            (new Spot(float.NaN, 0, 0), new Spot(0, 0, 4), 30f),
            (new Spot(0, float.PositiveInfinity, 0), new Spot(0, 0, 4), 30f),
            (new Spot(0, 0, 0), new Spot(0, 0, float.NegativeInfinity), 30f),
            (new Spot(0, 0, 0), new Spot(0, float.NaN, 4), 30f),
            (new Spot(0, 0, 0), new Spot(0, 0, 4), float.NaN),
            (new Spot(0, 0, 0), new Spot(0, 0, 4), float.PositiveInfinity),
            (new Spot(0, 0, 0), new Spot(0, 0, 4), 0f),
            (new Spot(0, 0, 0), new Spot(0, 0, 4), -1f)
        };
        for (int i = 0; i < invalid.Length; i++)
        {
            Record(memory, a, 300);
            var (start, next, meters) = invalid[i];
            memory.Record(a, start, next, meters, true, 400);
            check(Read(memory, new(0, 0, 0), 400).Count == 0, $"invalid route evidence replaces rather than preserves an old claim ({i})");
        }
        Record(memory, a, 500);
        check(Read(memory, new(float.NaN, 0, 0), 500).Count == 0, "unreadable player coordinates cannot yield a preview");
        memory.Load([Place("invalid", float.NaN), Place("missing", 0, 0), Place("", 10, 10)], 1, 1);
        check(!memory.TryNext(out _) && Read(memory, new(0, 0, 0), 500).Count == 0,
            "invalid endpoints, absent positions and missing identities never enter the native-query queue");
    }

    private static void WorldTransitions(Action<bool, string> check)
    {
        var memory = new PlaceRouteMemory(1000, 2);
        var a = Place("shared-key");
        memory.Load([a], 1, 10);
        Record(memory, a, 100);
        memory.Load([a], 1, 10);
        check(Read(memory, new(0, 0, 0), 100).Count == 1, "an unchanged world gather preserves useful walking evidence");
        memory.Load([a], 1, 11);
        check(Read(memory, new(0, 0, 0), 100).Count == 0, "a day/night situation transition invalidates same-city route evidence");
        Record(memory, a, 200);
        memory.Load([a], 2, 11);
        check(Read(memory, new(0, 0, 0), 200).Count == 0, "the same key and coordinates in a new city require a new query");
        Record(memory, a, 300);
        memory.Load([a], 0, 0);
        check(!memory.TryNext(out _) && Read(memory, new(0, 0, 0), 300).Count == 0, "loading through city zero clears both directions and pending targets");
        memory.Load([a], 2, 11);
        check(memory.TryNext(out _) && Read(memory, new(0, 0, 0), 300).Count == 0, "returning to a known city cannot revive pre-loading evidence");
        Record(memory, a, 400);
        memory.Clear();
        check(!memory.TryNext(out _) && Read(memory, new(0, 0, 0), 400).Count == 0, "leaving exploration clears its query queue and route snapshots");
    }
}
