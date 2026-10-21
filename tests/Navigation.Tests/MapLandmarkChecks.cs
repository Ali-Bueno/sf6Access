using SF6Access.Services.WorldTour;

internal static class MapLandmarkChecks
{
    public static void Run(Action<bool, string> check)
    {
        var landmarks = new[]
        {
            new RegionTracker.Landmark("Square", 10, 10),
            new RegionTracker.Landmark("Station", 40, 40),
        };
        var relation = MapLandmarkRelation.Nearest(landmarks, 13, 14);
        check(relation is { Name: "Square", Meters: 5, X: 3, Z: 4 },
            "map reference describes the place from the landmark, not the player's camera");
        check(MapLandmarkRelation.Nearest(landmarks, 40, 40) is { Name: "Station", Meters: 0 },
            "a place at a landmark has zero separation without an invented compass direction");
        check(MapLandmarkRelation.Nearest(landmarks, float.NaN, 14) == null,
            "unknown destination position cannot create a reference");
        check(MapLandmarkRelation.Nearest(Array.Empty<RegionTracker.Landmark>(), 13, 14) == null,
            "no known landmarks means no guessed map reference");
        check(MapLandmarkRelation.Nearest(new[]
        {
            new RegionTracker.Landmark("", 13, 14),
            new RegionTracker.Landmark("Bad", float.PositiveInfinity, 14),
            landmarks[0],
        }, 13, 14) == relation, "unreadable landmarks cannot displace a valid map reference");
        check(MapLandmarkRelation.Nearest(landmarks, 7, 6) is { Name: "Square", X: -3, Z: -4 },
            "opposite side of a landmark reverses its relation");
        check(MapLandmarkRelation.Nearest(new[] { landmarks[0], landmarks[0] with { Name = "Duplicate" } }, 13, 14)
            is { Name: "Square" }, "equal-distance map references keep stable source order");
    }
}
