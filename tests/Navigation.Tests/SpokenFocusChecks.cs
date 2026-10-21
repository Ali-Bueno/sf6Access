using SF6Access.Services.WorldTour;

internal static class SpokenFocusChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var identity = new DestinationActor(DestinationActorSource.Avatar, 21, 210, 400, "Chelsea", 0);
        var chelsea = new Destination("Chelsea", null, DestinationCategory.People, 21, 0, 1, Actor: identity);
        var crate = new Destination("Crate", null, DestinationCategory.Places, 20, 0, 1);
        var gate = new SpokenFocusGate(45, 300);
        var memory = new SpokenDestinationMemory();
        long now = 0;
        float off = 8;
        bool readable = true, present = true, forced = false;
        Destination updated = chelsea;
        SpokenDestinationMemory.Check Verify(bool taking)
        {
            forced |= taking;
            if (!readable) return new(SpokenCandidateState.Pending, updated, "actor-unreadable");
            if (!present) return new(SpokenCandidateState.Invalid, updated, "actor-lost");
            return new(gate.Observe(off, now), updated, "bearing:" + off);
        }

        memory.OfferTracked(chelsea, Verify);
        now = 188; off = 24;
        memory.DiscardInvalid(); // A crate can now win the 20-degree raw scan cone, without being spoken.
        now = 1470; off = 31; updated = chelsea with { X = 23 };
        check(memory.TryTake(out var selected) && selected == updated && selected != crate && forced,
            "R3 after the Chelsea/crate sequence selects the last spoken live person with current coordinates");

        gate = new SpokenFocusGate(45, 300);
        memory.OfferTracked(chelsea, Verify);
        now = 2000; off = 49;
        check(!memory.TryTake(out _, out var reason) && reason.StartsWith("bearing:"),
            "a short outside-cone wobble preserves memory but never permits selection outside the cone");
        now = 2200; off = 30;
        check(memory.TryTake(out selected) && selected == updated,
            "returning during the brief wobble can select the heard person without another announcement");

        gate = new SpokenFocusGate(45, 300);
        memory.OfferTracked(chelsea, Verify);
        now = 3000; off = 50; memory.DiscardInvalid();
        now = 3300; memory.DiscardInvalid();
        now = 3400; off = 5;
        check(!memory.TryTake(out _), "a sustained look away cannot resurrect an older spoken person on return");
        gate = new SpokenFocusGate(45, 300);
        check(gate.Observe(95, 0) == SpokenCandidateState.Invalid,
            "turning past the target into the rear hemisphere invalidates it immediately");

        gate = new SpokenFocusGate(45, 300);
        memory.OfferTracked(chelsea, Verify);
        off = float.NaN;
        check(!memory.TryTake(out _), "an unreadable camera never selects the retained target");
        off = 10;
        check(memory.TryTake(out selected) && selected.Actor == identity,
            "a recovered camera reading retains the actual spoken identity");
        memory.OfferTracked(chelsea, Verify);
        readable = false;
        check(!memory.TryTake(out _, out reason) && reason == "actor-unreadable",
            "temporary actor proof failure returns a diagnostic and leaves selection retryable");
        readable = true;
        check(memory.TryTake(out selected) && selected.Actor == identity,
            "fresh exact actor proof restores the candidate without substituting another name");
        memory.OfferTracked(chelsea, Verify);
        present = false; memory.DiscardInvalid(); present = true;
        check(!memory.TryTake(out _), "a genuinely disappeared actor cannot silently reacquire its old spoken slot");
        memory.OfferTracked(chelsea, Verify);
        memory.Offer(crate, () => true);
        check(memory.TryTake(out selected) && selected == crate,
            "a different actually spoken object replaces the previous person");
        memory.OfferTracked(chelsea, Verify);
        memory.Clear();
        check(!memory.TryTake(out _), "a menu or explicit cancellation clears retained spoken ownership");

        var dwell = new ScanDwell();
        dwell.Observe("Chelsea", 0, true);
        check(dwell.Observe("Chelsea", 300, true), "a settled target is ready for its first verified announcement");
        // Context failed at dispatch: production does not commit Spoken and suppresses
        // further offers until a fresh readable snapshot exists.
        check(dwell.Observe("Chelsea", 1800, true),
            "a failed world snapshot leaves the same focused name eligible after context recovers");
        dwell.Spoken("Chelsea");
        check(!dwell.Observe("Chelsea", 2200, true),
            "the successful fresh announcement commits once without a repeated-name loop");
    }
}
