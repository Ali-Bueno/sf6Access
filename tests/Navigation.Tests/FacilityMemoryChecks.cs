using SF6Access.Services.WorldTour;

internal static class FacilityMemoryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const uint city = 400;
        const ulong session = 20;
        const float mergeM = 8;
        long interval = FacilityMemory.RefreshIntervalMs;
        var first = new Destination("First shop", "clothes", DestinationCategory.Shops, 100, 2, 200);
        var later = new Destination("Later shop", "food", DestinationCategory.Shops, 900, 2, 800);
        var memory = new FacilityMemory();

        check(!memory.TryBeginRefresh(0) && !memory.Remember(first, mergeM),
            "an unknown city session cannot query or learn a facility");
        check(memory.Bind(city, session) && memory.TryBeginRefresh(0),
            "the first readable city session immediately permits discovery");
        check(memory.Remember(first, mergeM) && memory.Places.SequenceEqual([first]),
            "the first registered static facility becomes a known destination");
        check(memory.Bind(city, session) && !memory.TryBeginRefresh(interval - 1),
            "repeated catalog calls in the same session preserve the five-second native-read limit");
        check(memory.TryBeginRefresh(interval),
            "a nonempty catalog refreshes when due instead of freezing after the first discovery");
        check(memory.Remember(later, mergeM) && memory.Places.SequenceEqual([first, later]),
            "a later-loaded shop is learned without losing the earlier streamed-out shop");

        check(memory.TryBeginRefresh(2 * interval) && memory.Places.Count == 2,
            "an empty or failed refresh leaves previously known static destinations available");
        check(!memory.TryBeginRefresh(2 * interval + 1),
            "an empty or failed refresh still observes the native-read backoff");
        check(memory.TryBeginRefresh(3 * interval) && !memory.Remember(first, mergeM)
            && memory.Places.Count == 2,
            "recovery and repeated registered entries do not duplicate known facilities");
        var groups = DestinationPicker.Build(memory.Places, -900, -800, mergeM);
        check(groups.Count == 1 && groups[0].Items.Count == 2,
            "both discovered facilities remain selectable from the other end of the city");

        check(!memory.Remember(first with { X = first.X + 2, Z = first.Z + 2 }, mergeM)
            && memory.Places[0] == first,
            "matching nearby native name and kind preserve the original static coordinate");
        check(memory.Remember(first with { Y = first.Y + mergeM + 1 }, mergeM),
            "a same-name facility vertically beyond the merge distance remains a separate destination");
        check(memory.Remember(first with { X = first.X + 6, Z = first.Z + 6 }, mergeM),
            "three-dimensional distance prevents merging a diagonal beyond the common place radius");
        check(memory.Remember(first with { Kind = "food" }, mergeM)
            && memory.Remember(first with { Name = "Other shop" }, mergeM),
            "different native kinds or names do not merge merely because coordinates overlap");

        int validCount = memory.Places.Count;
        var actor = new DestinationActor(DestinationActorSource.Registry, 30, session, city, "Clerk", 1);
        foreach (var invalid in new[]
        {
            first with { Actor = actor },
            first with { Actor = new DestinationActor(DestinationActorSource.Registry, 30, 0, 0, "", 0) },
            first with { Category = DestinationCategory.People },
            first with { Action = DestinationAction.Explore },
            first with { Name = " " },
            first with { X = float.NaN },
            first with { Y = float.PositiveInfinity },
            first with { Z = float.NegativeInfinity },
            first with { X = 0, Y = 0, Z = 0 },
        })
            check(!memory.Remember(invalid, mergeM), "facility memory rejects live actors, actions and invalid metadata");
        foreach (float invalidRadius in new[] { -1f, float.NaN, float.PositiveInfinity })
            check(!memory.Remember(later with { Name = "Invalid radius" }, invalidRadius),
                "an invalid merge radius cannot admit a facility");
        check(memory.Places.Count == validCount && memory.Places.All(p => p.Actor == default),
            "facility memory contains only validated copied static locations");

        check(memory.Bind(100, session) && memory.Places.Count == 0 && memory.TryBeginRefresh(3 * interval + 1),
            "switching cities clears old locations and permits the new city's first discovery immediately");
        memory.Remember(later, mergeM);
        check(memory.Bind(city, session) && memory.Places.Count == 0,
            "returning to a city cannot reuse another city's remembered locations");
        memory.Remember(first, mergeM);
        check(memory.Bind(city, session + 1) && memory.Places.Count == 0 && memory.TryBeginRefresh(3 * interval + 2),
            "a recreated city manager starts a new native scope without retained old-session destinations");

        memory.Remember(first, mergeM);
        check(!memory.Bind(0, session + 1) && memory.Places.Count == 0
            && !memory.TryBeginRefresh(4 * interval) && !memory.Remember(later, mergeM),
            "an unreadable city clears prior destinations and prevents learning under an unknown city");
        memory.Bind(city, session + 1);
        memory.Remember(first, mergeM);
        check(!memory.Bind(city, 0) && memory.Places.Count == 0,
            "an unreadable native owner cannot expose old-session destinations");
        check(memory.Bind(city, session + 2) && memory.TryBeginRefresh(4 * interval),
            "a readable session recovers immediately after unknown context");
    }
}
