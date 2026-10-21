using SF6Access.Services.WorldTour;

internal static class SpokenDestinationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var memory = new SpokenDestinationMemory();
        var shop = new Destination("Shop", null, DestinationCategory.Shops, 10, 0, 0);
        var travel = new Destination("Travel", null, DestinationCategory.Travel, 20, 0, 0);
        check(!memory.TryTake(out _), "R3 has no target before any name is spoken");
        bool stillOnPath = true;
        memory.Offer(shop, () => stillOnPath);
        memory.DiscardInvalid();
        check(memory.TryTake(out var chosen) && chosen == shop,
            "an unrelated scan update preserves a still-current spoken walking route");
        check(!memory.TryTake(out _), "a spoken destination is consumed once");
        memory.Offer(shop, () => stillOnPath);
        memory.Offer(travel, () => true);
        check(memory.TryTake(out chosen) && chosen == travel,
            "the last spoken target wins across direct focus and path previews");
        memory.Offer(shop, () => stillOnPath);
        stillOnPath = false;
        check(!memory.TryTake(out _), "leaving the sampled path invalidates R3 before the next scan tick");
        stillOnPath = true;
        check(!memory.TryTake(out _), "returning to a path does not resurrect a discarded announcement");
        memory.Offer(travel, () => true);
        memory.Clear();
        check(!memory.TryTake(out _), "a menu or field reset clears the last spoken selection");
    }
}
