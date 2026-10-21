using SF6Access.Services.WorldTour;

internal static class LookRetargetChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var crate = new Destination("Breakable object", null, DestinationCategory.Places, 20, 0, 5);
        var actor = new DestinationActor(DestinationActorSource.Avatar, 11, 111, 400, "Chelsea", 0);
        var person = new Destination("Chelsea", null, DestinationCategory.People, 10, 0, 5, Actor: actor);
        var spoken = new SpokenDestinationMemory();
        int candidateReads = 0;
        bool focused = true;

        void Offer(Destination destination) => spoken.Offer(destination, () => { candidateReads++; return focused; });
        LookTapAction Tap(bool hasSelection, bool followingMission, out Destination chosen)
        {
            chosen = default;
            bool candidate = !hasSelection && !followingMission && spoken.TryTake(out chosen);
            return LookRetargetPolicy.Decide(hasSelection, followingMission, candidate);
        }

        check(Tap(false, false, out _) == LookTapAction.Aim,
            "without guidance or a spoken target, R3 reaches the no-selection response");
        Offer(crate);
        check(Tap(false, false, out var chosen) == LookTapAction.Choose && chosen == crate,
            "free exploration still selects the actually spoken breakable");

        Offer(person);
        int beforeCancel = candidateReads;
        check(Tap(true, false, out _) == LookTapAction.Cancel && candidateReads == beforeCancel,
            "the recorded breakable scenario cancels on the next R3 without reading or choosing another candidate");
        spoken.Clear(); // The production cancel branch rearms the scanner and clears its old candidate.
        check(Tap(false, false, out _) == LookTapAction.Aim,
            "another quick tap after cancel cannot restart the old beacon or select an unheard target");

        Offer(person);
        check(Tap(false, false, out chosen) == LookTapAction.Choose && chosen == person,
            "after cancellation, a freshly spoken focus can be chosen without the picker");
        var tracking = new ActorTrackingState(person);
        tracking.Observe(default);
        Offer(crate);
        beforeCancel = candidateReads;
        check(tracking.Pending && Tap(true, false, out _) == LookTapAction.Cancel && candidateReads == beforeCancel,
            "temporary actor-read failure never blocks an explicit cancel");
        tracking.Observe(new(true, default));
        check(tracking.Lost && Tap(true, false, out _) == LookTapAction.Cancel && candidateReads == beforeCancel,
            "confirmed target loss still cancels first instead of replacing the destination on that press");

        focused = false;
        check(Tap(true, false, out _) == LookTapAction.Cancel && candidateReads == beforeCancel,
            "cancellation does not depend on a readable or currently faced replacement");
        check(Tap(false, true, out _) == LookTapAction.Cancel && candidateReads == beforeCancel,
            "explicit mission following cancels even without a stored map destination or a readable replacement");
        check(LookRetargetPolicy.Decide(true, true, true) == LookTapAction.Cancel,
            "cancellation wins when selection, mission and candidate signals coincide");
        check(LookRetargetPolicy.Decide(false, true, false) == LookTapAction.Cancel,
            "a followed mission can be cancelled while its target is temporarily absent");

        spoken.Clear(); focused = true;
        var dwell = new ScanDwell();
        dwell.Observe("Chelsea", 0, true); dwell.Spoken("Chelsea"); dwell.Clear();
        check(!dwell.Observe("Chelsea", 1000, true) && dwell.Observe("Chelsea", 1300, true),
            "cancellation rearms the same focus without requiring a look-away");
        Offer(person);
        check(Tap(false, false, out chosen) == LookTapAction.Choose && chosen == person,
            "free exploration after mission cancellation accepts a newly spoken target without reviving the mission");
    }
}
