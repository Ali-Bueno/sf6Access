using SF6Access.Services.WorldTour;

internal static class ApproachProbeChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const long queryInterval = 250;
        var search = new ApproachProbeProgress(queryInterval);
        long now = 0;
        bool eventuallyReady = true, sameCandidate = true, respectsQueryInterval = true, boundedBackoff = true;
        for (int i = 0; i < 40; i++)
        {
            eventuallyReady &= search.Ready(now);
            search.Record(ApproachProbeProgress.Verdict.Unavailable, now);
            sameCandidate &= search.Candidate == 0 && !search.Found;
            respectsQueryInterval &= !search.Ready(now + queryInterval - 1);
            long delay = queryInterval * (1L << Math.Min(i, 4));
            boundedBackoff &= !search.Ready(now + delay - 1) && search.Ready(now + delay);
            now += delay;
        }
        check(eventuallyReady, "an unavailable approach candidate eventually retries");
        check(sameCandidate, "many unavailable answers cannot exhaust geometric candidates");
        check(respectsQueryInterval, "unavailable data cannot retry faster than the path query interval");
        check(boundedBackoff, "unavailable retries back off but remain bounded");
        search.Record(ApproachProbeProgress.Verdict.Reached, now);
        check(search.Found && search.Candidate == 0 && !search.Ready(now + 10000),
            "the original candidate can succeed after a long temporary outage");

        search.Reset();
        check(search.Ready(0) && !search.Found && search.Candidate == 0,
            "a different target resets the candidate and retry delay");
        search.Record(ApproachProbeProgress.Verdict.Deferred, 0);
        check(search.Candidate == 0 && !search.Ready(249) && search.Ready(250),
            "throttled or off-mesh probes keep the current candidate");
        search.Record(ApproachProbeProgress.Verdict.Rejected, 250);
        check(search.Candidate == 1 && search.Ready(250),
            "confirmed unreachability advances to the next candidate");
        search.Record(ApproachProbeProgress.Verdict.Unavailable, 250);
        check(!search.Ready(499) && search.Ready(500),
            "a confirmed answer resets the unavailable backoff");
        search.Record(ApproachProbeProgress.Verdict.Rejected, 500);
        search.Record(ApproachProbeProgress.Verdict.Reached, 750);
        check(search.Found && search.Candidate == 2,
            "confirmed failures progress until another candidate is reachable");
    }
}
