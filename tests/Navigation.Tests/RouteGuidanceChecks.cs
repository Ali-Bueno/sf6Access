using SF6Access.Services.WorldTour;

internal static class RouteGuidanceChecks
{
    internal static void Run(Action<bool, string> check)
    {
        QueryFairness(check);
        ApproachQueryFairness(check);
        AnswersAndFreshness(check);
    }

    private static void ApproachQueryFairness(Action<bool, string> check)
    {
        const long interval = 250;
        var schedule = new RouteQuerySchedule(interval);
        var approach = new ApproachProbeProgress(interval);
        long nextQuery = 510; // A background query at 260 owns the native gate until 510.
        var probes = new List<long>();
        long[] frames = [500, 510, 770, 1010, 1020, 1280, 1520, 1530];
        foreach (long now in frames)
        {
            if (schedule.Due(now))
            {
                bool waiting = false;
                if (approach.Ready(now))
                {
                    waiting = now < nextQuery;
                    if (!waiting)
                    {
                        probes.Add(now);
                        nextQuery = now + interval;
                        approach.Record(ApproachProbeProgress.Verdict.Rejected, now);
                    }
                }
                schedule.Resolved(now, waiting, nextQuery, backgroundSearch: true);
            }
            if (schedule.MayPump && now >= nextQuery) nextQuery = now + interval;
        }
        check(probes.SequenceEqual(new long[] { 510, 1020, 1530 }) && approach.Candidate == 3,
            "background retries cannot consume every eligible approach candidate query");
        approach.Record(ApproachProbeProgress.Verdict.Unavailable, 1530);
        check(!approach.Ready(1600), "unavailable approach data retains its own retry backoff");
        schedule.Resolved(1600, false, 1780, backgroundSearch: true);
        check(schedule.MayPump, "an approach waiting on its data backoff does not reserve native query ownership");
    }

    private static void QueryFairness(Action<bool, string> check)
    {
        const long interval = 250;
        var schedule = new RouteQuerySchedule(interval);
        long nextQuery = 0;
        var live = new List<long>();
        var background = new List<long>();
        long[] frames = [0, 260, 500, 510, 770, 1000, 1010, 1020, 1280, 1500, 1520, 1530, 1790, 2030, 2040];
        foreach (long now in frames)
        {
            if (schedule.Due(now))
            {
                bool throttled = now < nextQuery;
                if (!throttled) { live.Add(now); nextQuery = now + interval; }
                schedule.Resolved(now, throttled, nextQuery, backgroundSearch: true);
            }
            if (schedule.MayPump && now >= nextQuery)
            { background.Add(now); nextQuery = now + interval; }
        }
        check(live.SequenceEqual(new long[] { 0, 510, 1020, 1530, 2040 }),
            "irregular background query timing cannot starve live steering");
        check(background.SequenceEqual(new long[] { 260, 770, 1280, 1790 }),
            "reserving a pending live query still lets the background search progress");
        long[] grants = live.Concat(background).Order().ToArray();
        check(grants.Zip(grants.Skip(1), (a, b) => b - a >= interval).All(x => x),
            "query fairness retains the global native rate limit");

        schedule.Resolved(2500, true, 2510, backgroundSearch: true);
        check(!schedule.MayPump && !schedule.Due(2509) && schedule.Due(2510),
            "cached guidance still reserves the next real query slot when its refresh was throttled");
        schedule.Resolved(2510, false, 2760, backgroundSearch: false);
        check(schedule.MayPump && !schedule.Due(2759) && schedule.Due(2760),
            "finishing a background search restores the normal live cadence");
        schedule.Reset();
        check(schedule.MayPump && schedule.Due(0), "a new field session clears pending query ownership");
    }

    private static void AnswersAndFreshness(Action<bool, string> check)
    {
        check(RouteAnswerPolicy.Classify(false, false, false, false, false) == RouteAnswerStatus.Unknown,
            "an unreadable query cannot claim a walkable direction");
        check(RouteAnswerPolicy.Classify(true, true, false, false, false) == RouteAnswerStatus.Unknown,
            "a route without a readable corner cannot steer");
        check(RouteAnswerPolicy.Classify(false, false, true, false, false) == RouteAnswerStatus.Pending,
            "throttled queries remain pending rather than unreachable");
        check(RouteAnswerPolicy.Classify(true, false, false, true, false) == RouteAnswerStatus.Pending,
            "an off-mesh avatar does not disprove the walking route");
        check(RouteAnswerPolicy.Classify(true, false, false, false, false) == RouteAnswerStatus.Unreachable,
            "a readable negative query remains a confirmed connectivity failure");
        check(RouteAnswerPolicy.Classify(true, true, false, false, true) == RouteAnswerStatus.Confirmed,
            "a readable route and corner authorize route guidance");

        var cache = new RouteQueryCache<RouteAnswerStatus>(500);
        var point = (1f, 2f, 3f);
        cache.Store("a", point, RouteAnswerStatus.Confirmed, 100);
        check(cache.TryRead("a", point, 300, out var answer, out long sampledAt)
            && answer == RouteAnswerStatus.Confirmed && sampledAt == 100,
            "same-target cache reuse carries the original route sample time");
        check(cache.TryRead("a", point, 600, out _, out sampledAt) && sampledAt == 100
            && !cache.TryRead("a", point, 601, out _, out _),
            "repeated reads cannot extend a cached route's lifetime");
        check(!cache.TryRead("b", point, 300, out _, out _)
            && !cache.TryRead("a", (1f, 2f, 4f), 300, out _, out _),
            "another target or endpoint cannot borrow a cached steering direction");
        cache.Store("a", point, RouteAnswerStatus.Unreachable, 400);
        check(cache.TryRead("a", point, 500, out answer, out sampledAt)
            && answer == RouteAnswerStatus.Unreachable && sampledAt == 400,
            "a real failed query replaces the previously confirmed route");
        cache.Store("a", point, RouteAnswerStatus.Unknown, 600);
        check(cache.TryRead("a", point, 700, out answer, out _) && answer == RouteAnswerStatus.Unknown,
            "unreadable data cannot resurrect the earlier successful query");
        cache.Clear();
        check(!cache.TryRead("a", point, 700, out _, out _), "city or destination reset clears shared route answers");

        check(RouteAnswerPolicy.WalkingState(false, true, true, false) == "approach-near",
            "the foot of an elevated objective remains an approach even without a walking corner");
        check(RouteAnswerPolicy.WalkingState(true, true, false, true) == "approach-near",
            "being near an approach point is distinct from reaching the objective");
        check(RouteAnswerPolicy.WalkingState(false, false, false, false) == "unavailable",
            "ordinary absent routes still remain unavailable");
    }

}
