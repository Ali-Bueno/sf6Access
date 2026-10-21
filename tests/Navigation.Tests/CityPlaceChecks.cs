using SF6Access.Services.WorldTour;

internal static class CityPlaceChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var focus = new CityPlaceFocus(15, 20, 3);
        void Poll(params (string Key, float Angle, float Meters)[] places)
        {
            focus.Begin();
            foreach (var p in places) focus.Offer(p.Key, p.Angle, p.Meters);
            focus.End();
        }
        Poll(("near shop", 9, 10), ("far park", 0, 950));
        check(focus.Key == "far park", "a directly faced city landmark wins over a nearer off-axis shop");
        check(!focus.CanSound, "an unheard city focus cannot play a cue associated with the previous spoken name");
        focus.Spoken("near shop");
        check(!focus.CanSound, "a delayed spoken callback for a different focus cannot unlock the new cue");
        focus.Spoken("far park");
        check(focus.CanSound, "city cue becomes eligible after the focused name is actually spoken");
        Poll(("near shop", 0, 10), ("far park", 2, 950));
        check(focus.Key == "far park" && focus.CanSound, "small angular wobble retains the heard landmark");
        Poll(("near shop", 0, 10), ("far park", 8, 950));
        check(focus.Key == "near shop" && !focus.CanSound, "deliberately facing a better aligned place releases the previous cue");
        focus.Spoken("near shop");
        Poll(("near shop", 19, 10));
        check(focus.Key == "near shop", "city focus survives the wider leaving cone");
        Poll(("near shop", 21, 10));
        check(focus.Key == null && !focus.CanSound, "looking away silences the focused landmark");
        Poll(("far park", 0, 950)); focus.Spoken("far park");
        Poll();
        check(focus.Key == null && !focus.CanSound, "an unavailable city landmark cannot persist as a ghost focus");
        Poll(("corrupt", float.NaN, 10), ("infinite", 0, float.PositiveInfinity), ("negative", -1, 10), ("valid", 8, 1000));
        check(focus.Key == "valid", "invalid bearings and distances do not take the city scan");
        focus.Clear();
        Poll(("further", 0, 1000), ("nearer", 0, 500));
        check(focus.Key == "nearer", "collinear landmarks resolve deterministically, with named directory retaining the other");
        focus.Spoken("nearer"); focus.Clear();
        check(focus.Key == null && !focus.CanSound, "leaving city listening clears both focused and heard identities");

        var far = new Destination("Arcade", "place", DestinationCategory.Places, 1500, 2, 0);
        var close = new Destination("Zoo", "place", DestinationCategory.Places, 5, 2, 0);
        var rest = new Destination("Hangout", "day/night", DestinationCategory.Rest, 700, 1, 500);
        var bus = new Destination("Bus stop", "travel", DestinationCategory.Travel, -1500, 0, 0);
        var crowd = new Destination("Nearby NPC", null, DestinationCategory.People, 2, 1, 0);
        var movingShop = new Destination("Shopkeeper", null, DestinationCategory.Shops, 3, 1, 0,
            Actor: new DestinationActor(DestinationActorSource.Avatar, 1, 2, 400, "Shopkeeper", 0));
        var ladder = far with { Name = "Ladder", Category = DestinationCategory.Access };
        var saveAction = new Destination("Save", null, DestinationCategory.Saved, 0, 0, 0, DestinationAction.SaveHere);
        var places = new[] { close, rest, far, bus, crowd, movingShop, ladder, saveAction };
        var initial = DestinationPicker.Build(places, 0, 0, 8);
        var names = CityPlaceDirectory.ByName(initial);
        check(names.Select(p => p.Name).SequenceEqual(new[] { "Arcade", "Bus stop", "Hangout", "Zoo" }),
            "named city index is alphabetic and includes both map ends without local crowd/actions");
        check(CityPlaceDirectory.ByName(DestinationPicker.Build(places, 1600, 0, 8)).SequenceEqual(names),
            "moving across the city does not reorder the named index");
        check(names[0] == far && names[2] == rest && names[1] == bus,
            "named index keeps original place category and coordinates for sounds and selection");
        check(CityPlaceDirectory.ByName(initial.Concat(initial)).SequenceEqual(names),
            "repeated input groups do not duplicate named city entries");
        check(!CityPlaceFocus.Includes(far with { X = float.NaN })
            && !CityPlaceFocus.Includes(far with { X = 0, Y = 0, Z = 0 }),
            "citywide mode cannot manufacture positions for unknown places");
        var sameNameAbove = far with { Y = 22 };
        var floors = DestinationPicker.Build(new[] { far, sameNameAbove }, 0, 0, 8);
        check(floors.Single().Items.Count == 2, "the picker keeps same-name positions outside the 3D merge radius");
        var picker = new DestinationPicker();
        var namedGroup = new DestinationPicker.Group(DestinationCategory.KnownPlaces, names);
        picker.Load(new() { namedGroup }, DestinationCategory.KnownPlaces);
        check(picker.Current == far && picker.Current.Category == DestinationCategory.Places,
            "a view category does not replace its selected destination's semantics");
        picker.MoveItem(2);
        check(picker.Current == rest, "a far hangout can be chosen by its stable alphabetical row");
    }
}
