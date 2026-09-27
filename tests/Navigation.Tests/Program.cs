using SF6Access.Services.WorldTour;

// Dependency-free behavioral tests: run with dotnet run --project tests/Navigation.Tests.
int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    passed++;
}

var route = new RouteLookAhead(0, 0, .5f);
Check(route.Add(0, 0, 0) && !route.HasPoint, "ignore the route origin");
Check(route.Add(0, 0, 1), "retain a close corner candidate");
Check(!route.Add(2, 0, 1), "stop before cutting a right-angle corner");
Check(route.X == 0 && route.Z == 1, "aim remains at the one-metre corner");
Check(route.Turn == -1, "positive X from positive Z is left in the calibrated game basis");

route = new RouteLookAhead(0, 0, .5f);
route.Add(0, 0, 1);
route.Add(-2, 0, 1);
Check(route.Turn == 1, "negative X from positive Z is right");

route = new RouteLookAhead(0, 0, .5f);
Check(route.Add(0, 0, .3f), "corner just outside half a body radius survives");
Check(!route.Add(1, 0, .3f) && route.Z == .3f, "very close bend is not discarded as a wiggle");

route = new RouteLookAhead(0, 0, .5f);
route.Add(0, 0, 2);
Check(!route.Add(0, 0, 4) && route.Z == 4 && route.Turn == 0, "bounded straight look-ahead");

route = new RouteLookAhead(0, 0, .5f);
route.Add(.01f, 0, -1);
Check(route.Add(-.01f, 0, -2) && route.Z == -2, "bearing wraps across 180 degrees without a false turn");

route = new RouteLookAhead(20, 40, .5f);
route.Add(20, 5, 41);
Check(!route.Add(22, 5, 41) && route.X == 20 && route.Y == 5, "world translation and elevation preserve the corner");
Check(!route.Add(float.NaN, 0, 0) && route.X == 20, "invalid points cannot replace a valid aim");

var memory = new NavigationNoticeMemory();
Check(memory.Wants("door-1", "closed", 10, false, 0), "new access is announced");
memory.Spoke("door-1", "closed", 10, false, 0);
Check(!memory.Wants("door-1", "closed", 10, false, 5000), "unchanged access stays silent");
Check(memory.Wants("door-2", "closed", 10, false, 5000), "same description does not merge different doors");
Check(!memory.Wants("door-1", "closed", 4, false, 1000), "approach updates leave time for speech");
Check(memory.Wants("door-1", "closed", 4, false, 5000), "halving distance announces approach");
memory.Spoke("door-1", "closed", 4, false, 5000);
Check(memory.Wants("door-1", "closed", 4, true, 10000), "turning toward an access confirms alignment");
memory.Spoke("door-1", "closed", 4, true, 10000);
Check(!memory.Wants("door-1", "closed", 4, true, 15000), "holding alignment does not chatter");
Check(memory.Wants("door-1", "open", 4, true, 15001), "door availability changes are news");
Check(!memory.Wants("bad", "open", float.NaN, false, 15002), "invalid distance is not a discovery");
Check(!memory.Wants("bad", "open", -1, false, 15002), "negative distance is refused");
memory.Prune(40002);
Check(memory.Wants("door-1", "closed", 4, true, 40002), "streamed-out identities expire");
memory.Spoke("door-1", "closed", 4, true, 40002);
memory.Clear();
Check(memory.Wants("door-1", "closed", 4, true, 40003), "new area has no stale announcements");

var first = new NavReading(FrontBlock.Blocked, FrontProfile.Wall, true, 1, false, false, false, true);
var sides = new NavReading(FrontBlock.Blocked, FrontProfile.Wall, true, .5f, false, true, true, true);
Check(first.SameStateAs(sides), "side flicker cannot suppress a confirmed front warning");
var drop = new NavReading(FrontBlock.Blocked, FrontProfile.Wall, true, 1, false, false, false, false);
Check(!first.SameStateAs(drop), "ground loss remains a state change");
Check(!first.SameStateAs(default), "missing sensor data is not a valid baseline");
// Level routes: ground (y 0), roof A (y 12, x < 50), roof C (y 12, x >= 50), roof B (y 20), tower D (y 30).
string Region(LevelRoutePlanner.Spot s) => s.Y < 6 ? "ground" : s.Y > 25 ? "D" : s.Y > 16 ? "B" : s.X < 50 ? "A" : "C";
LevelRoutePlanner Plan(LevelRoutePlanner.Spot from, LevelRoutePlanner.Spot to, LevelRoutePlanner.Link[] links,
                       int budget = 48, Func<LevelRoutePlanner.Spot, LevelRoutePlanner.Spot>? snap = null)
{
    var planner = new LevelRoutePlanner(from, to, links, budget);
    while (planner.Next() is { } q)
    {
        var a = planner.SpotOf(q.From);
        var b = planner.SpotOf(q.To);
        if (snap == null) { planner.Answer(Region(a) == Region(b)); continue; }
        var sa = snap(a); var sb = snap(b);
        planner.Answer(Region(sa) == Region(sb), sa, sb);
    }
    return planner;
}
var ground = new LevelRoutePlanner.Spot(5, 0, 5);
var roofA = new LevelRoutePlanner.Spot(5, 12, 5);
var roofB = new LevelRoutePlanner.Spot(25, 20, 0);
var toA = new LevelRoutePlanner.Link(new(0, 0, -1), new(0, 12, 0));
var aToB = new LevelRoutePlanner.Link(new(10, 12, 0), new(20, 20, 0));
var toC = new LevelRoutePlanner.Link(new(100, 0, 0), new(100, 12, 50));

var level = Plan(ground, roofA, new[] { toC, toA });
Check(level.Plan is { Count: 2 } && level.Plan[0].Link == 1 && level.Plan[0].Up, "one ladder up to a roof");
Check(level.Plan![0].To == toA.Low && level.Plan[0].Exit == toA.High, "walk to the ladder foot, leave at its top");
Check(level.Plan[1].Link == -1 && level.Plan[1].To == roofA, "last leg is the destination");
Check(level.Asked == 2, "nearest ladder answers in two questions");

level = Plan(roofA, new(5, 0, 20), new[] { toA });
Check(level.Plan is { Count: 2 } && !level.Plan[0].Up && level.Plan[0].To == toA.High, "one ladder down from a roof");

level = Plan(ground, roofB, new[] { toC, aToB, toA });
Check(level.Plan is { Count: 3 } && level.Plan[0].Link == 2 && level.Plan[1].Link == 1, "two ladders chain ground, roof A, roof B");

var tower = new LevelRoutePlanner.Spot(30, 30, 10);
level = Plan(ground, tower, new[] { toC, toA, aToB });
Check(level.Finished && level.Partial && level.Plan is { Count: 3 } && level.Plan[0].Link == 1 && level.Plan[1].Link == 2,
      "an unreachable tower climbs as high as the ladders go");
Check(level.Plan![2].Link == -1 && level.Plan[2].To == aToB.High, "a partial plan stops at the highest reached ladder top");
level = Plan(roofB, tower, new[] { toC, toA, aToB });
Check(level.Finished && level.Plan == null, "no ladder nearer the target's height, no plan");
level = Plan(ground, new(80, 30, 80), new[] { toC, toA, aToB });
Check(level.Finished && level.Plan == null, "a ladder top in another block is not a destination");
var hidden = new LevelRoutePlanner.Link(new(60, 30, 0), new(60, 40, 0));
level = Plan(ground, tower, new[] { hidden, toA });
Check(level.Partial && level.Plan is { Count: 2 } && level.Plan[0].Link == 1, "a ladder whose foot cannot be walked to is left out");
level = Plan(ground, new(9, 1, 9), new[] { toA }, budget: 48);
Check(level.Plan == null, "a destination on the player's own level gets no partial climb");
level = Plan(ground, roofA, new[] { toC, toA }, budget: 1);
Check(level.Finished && level.Plan == null && level.Asked == 1, "question budget bounds the search");
level = Plan(ground, roofA, new[] { toC, toA }, budget: 2);
Check(level.Plan is { Count: 2 }, "the last answer inside the budget can still complete a plan");
level = Plan(ground, roofA, Array.Empty<LevelRoutePlanner.Link>());
Check(level.Finished && level.Plan == null && level.Asked == 0, "no ladders, no questions");

var moved = new LevelRoutePlanner.Spot(1, 12, 0);
level = Plan(ground, roofA, new[] { toA }, snap: s => s == toA.High ? moved : s);
Check(level.Plan![0].Exit == moved, "an endpoint moved onto the navmesh replaces the ladder end");

level = new LevelRoutePlanner(ground, roofA, new[] { toA }, 48);
Check(level.Known(LevelRoutePlanner.Player, LevelRoutePlanner.Target) == 0, "direct failure is known from the start");
var twin = new LevelRoutePlanner.Link(new(0, 0, -2), new(0, 12, 1));
level = new LevelRoutePlanner(ground, new(80, 30, 80), new[] { toA, twin }, 48);
int targetQuestions = 0;
while (level.Next() is { } tq)
{
    if (tq.To == LevelRoutePlanner.Target) targetQuestions++;
    level.Answer(Region(level.SpotOf(tq.From)) == Region(level.SpotOf(tq.To)));
}
Check(targetQuestions == 4, "each ladder end is asked about the target once");
level = new LevelRoutePlanner(ground, new(9, 0, 9), new[] { toA }, 48, askDirect: true);
Check(level.Next() is { From: LevelRoutePlanner.Player, To: LevelRoutePlanner.Target }, "an off-mesh target asks the direct walk first");
level.Answer(true, to: new LevelRoutePlanner.Spot(9, 0, 8));
Check(level.Next() == null && level.Plan is { Count: 1 } && level.Plan[0].To == new LevelRoutePlanner.Spot(9, 0, 8),
      "a target moved onto the navmesh can be walked to directly");

// Chapter 2-2: street, a 5 m roof split by a sign the navmesh does not model, a 10 m roof, the boss apart.
var boss = new LevelRoutePlanner.Spot(15, 12.2f, 17);
string Block(LevelRoutePlanner.Spot s) => s == boss ? "boss" : s.Y < 2.5f ? "street" : s.Y >= 7.5f ? "roof10" : s.Z < 8 ? "roof5" : "behindSign";
var ladderA = new LevelRoutePlanner.Link(new(0, 0, 0), new(0, 5, 0));
var ladderB = new LevelRoutePlanner.Link(new(0, 4.8f, 10.5f), new(0, 10.1f, 10.5f));
level = new LevelRoutePlanner(new(20, 0, -20), boss, new[] { ladderB, ladderA }, 48);
while (level.Next() is { } bq) level.Answer(Block(level.SpotOf(bq.From)) == Block(level.SpotOf(bq.To)));
Check(level.Partial && level.Plan is { Count: 3 } && level.Plan[0].Link == 1 && !level.Plan[0].Blocked,
      "a sign-split roof: the first ladder is walked to");
Check(level.Plan![1].Link == 0 && level.Plan[1].Blocked && level.Plan[2].To == ladderB.High,
      "the ladder behind the sign is a blocked leg, and the plan stops at its top");

var legs = new[]
{
    new LevelRoutePlanner.Leg(toA.Low, toA.High, 0, true),
    new LevelRoutePlanner.Leg(roofA, roofA, -1, false),
};
Check(LevelRoutePlanner.Progress(legs, 0, new(20, 0, 20), 4, 2) == 0, "walking to the ladder stays on the first leg");
Check(LevelRoutePlanner.Progress(legs, 0, new(0.5f, 12, 0.5f), 4, 2) == 1, "standing at the ladder top advances");
Check(LevelRoutePlanner.Progress(legs, 0, new(0, 7, 0), 4, 2) == 0, "halfway up the ladder is not the top yet");
Check(LevelRoutePlanner.Progress(legs, 1, new(6, 12, 6), 4, 2) == 1, "walking on the roof keeps the last leg");
Check(LevelRoutePlanner.Progress(legs, 1, new(1, 0, -1), 4, 2) == 0, "back down at the foot returns to the ladder leg");
Check(LevelRoutePlanner.Progress(legs, 1, new(40, 0, 40), 4, 2) == 1, "a fall far away is left to the route check");
var shortLadder = new[]
{
    new LevelRoutePlanner.Leg(new(0, 0, 0), new(0, 4, 0), 0, true),
    new LevelRoutePlanner.Leg(new(3, 4, 3), new(3, 4, 3), -1, false),
};
Check(LevelRoutePlanner.Progress(shortLadder, 0, new(6, 2.5f, 0), 4, 2) == 0, "a ramp beside a short ladder is not its top");

// --- Orientation: destination picker ---
var places = new[]
{
    new Destination("Save", null!, DestinationCategory.Saved, 0, 0, 0, DestinationAction.SaveHere),
    new Destination("Far shop", "shop", DestinationCategory.Shops, 100, 0, 0),
    new Destination("Near shop", "shop", DestinationCategory.Shops, 10, 0, 0),
    new Destination("Near shop", "shop", DestinationCategory.Shops, 12, 0, 1),
    new Destination("Beat Square", null!, DestinationCategory.Places, 0, 0, 50),
    new Destination("Beat Square", null!, DestinationCategory.Places, 0, 0, -80),
    new Destination("My spot", null!, DestinationCategory.Saved, 5, 0, 5),
    new Destination("  ", null!, DestinationCategory.People, 1, 0, 1),
};
var groups = DestinationPicker.Build(places, 0, 0, 8);
Check(groups.Count == 3, "empty names are dropped and only non-empty categories remain");
Check(groups[0].Category == DestinationCategory.Places && groups[2].Category == DestinationCategory.Saved,
      "categories follow the enum order");
Check(groups[1].Items.Count == 2 && groups[1].Items[0].Name == "Near shop", "same name within the merge radius is one place, nearest first");
Check(groups[0].Items.Count == 2 && groups[0].Items[0].Z == 50, "same name far apart stays two places");
Check(groups[2].Items[0].Action == DestinationAction.SaveHere, "position-less actions sort on top");
var picker = new DestinationPicker();
picker.Load(groups, DestinationCategory.Shops);
Check(picker.Category == DestinationCategory.Shops && picker.Index == 0, "opens on the preferred category, nearest item");
picker.MoveItem(-1);
Check(picker.Index == 1, "moving up from the first item wraps to the last");
picker.MoveCategory(1);
Check(picker.Category == DestinationCategory.Saved && picker.Index == 0, "next category resets to its first item");
picker.MoveCategory(1);
Check(picker.Category == DestinationCategory.Places, "categories wrap around");
picker.Load(groups, DestinationCategory.Masters);
Check(picker.Category == DestinationCategory.Places, "a missing preferred category falls back to the first");
picker.Load(new List<DestinationPicker.Group>(), DestinationCategory.Places);
picker.MoveItem(1); picker.MoveCategory(1);
Check(picker.IsEmpty, "an empty picker ignores movement");
Check(new Destination("x", "shop", DestinationCategory.Shops, 1, 0, 0).Label == "x, shop", "label joins the kind word");
Check(!new Destination("x", null!, DestinationCategory.Shops, 0, 0, 0).HasPosition, "an exact origin is no position");
Check(DestinationPicker.Build(new[] { new Destination("Lost", null!, DestinationCategory.Shops, 0, 0, 0) }, 0, 0, 8).Count == 0,
      "a place whose position failed to read is not offered");

// --- Orientation: tap versus hold ---
var gesture = new HoldTapDetector(500);
Check(gesture.Update(true, 0) == HoldTapDetector.Gesture.None, "press starts silently");
Check(gesture.Update(false, 200) == HoldTapDetector.Gesture.Tap, "short press is a tap on release");
gesture.Update(true, 1000);
Check(gesture.Update(true, 1499) == HoldTapDetector.Gesture.None, "not held long enough yet");
Check(gesture.Update(true, 1500) == HoldTapDetector.Gesture.Hold, "hold fires once at the threshold");
Check(gesture.Update(true, 3000) == HoldTapDetector.Gesture.None, "hold does not repeat");
Check(gesture.Update(false, 3100) == HoldTapDetector.Gesture.None, "release after a hold is not a tap");
gesture.Update(true, 4000);
gesture.Swallow();
Check(gesture.Update(false, 4100) == HoldTapDetector.Gesture.None, "a swallowed press does not tap");
gesture.Swallow();
Check(gesture.Update(false, 4200) == HoldTapDetector.Gesture.None && gesture.Update(true, 4300) == HoldTapDetector.Gesture.None
      && gesture.Update(false, 4400) == HoldTapDetector.Gesture.Tap, "swallowing ends at the next release");

// --- Orientation: landmark regions with hysteresis ---
var landmarks = new[]
{
    new RegionTracker.Landmark("A", 0, 0),
    new RegionTracker.Landmark("B", 100, 0),
    new RegionTracker.Landmark("A", -200, 0),
};
var region = new RegionTracker(4, 0.2f, 1000);
Check(region.Update(landmarks, 10, 0, 0) && region.Current == "A", "first reading is immediate");
Check(!region.Update(landmarks, 52, 0, 100), "just past the midpoint is not a change");
Check(!region.Update(landmarks, 60, 0, 200) && region.Current == "A", "a clear lead must also hold for a while");
Check(!region.Update(landmarks, 60, 0, 900), "still within the dwell");
Check(region.Update(landmarks, 60, 0, 1200) && region.Current == "B", "a held clear lead changes the region");
Check(!region.Update(landmarks, 45, 0, 1300) && region.Current == "B", "walking back a little does not flip back");
Check(!region.Update(landmarks, 20, 0, 1400), "a lead that appears restarts the dwell");
Check(!region.Update(landmarks, 60, 0, 2000) && region.Current == "B", "a lead that vanished resets the candidate");
region.Update(landmarks, -150, 0, 3000);
Check(region.Update(landmarks, -150, 0, 4100) && region.Current == "A", "a shared name counts its nearest copy");
region.Reset();
Check(region.Current == null && !region.Update(Array.Empty<RegionTracker.Landmark>(), 0, 0, 0), "no landmarks, no region");

// --- Orientation: place file ---
var entry = new PlaceFile.Entry(400, 2, 7, "Style Lab", -36.25f, 0.1f, -110f);
Check(PlaceFile.TryParse(PlaceFile.Format(entry), out var parsed) && parsed == entry, "a place survives a round trip");
Check(PlaceFile.TryParse(PlaceFile.Format(entry with { Name = "a\tb\nc" }), out parsed) && parsed.Name == "a b c",
      "tabs and newlines in names cannot break the file");
Check(!PlaceFile.TryParse("400\t2\t7\tName\tx\t0\t0", out _), "a bad number is refused");
Check(!PlaceFile.TryParse("400\t2\t7\t \t1\t0\t0", out _), "an empty name is refused");
Check(!PlaceFile.TryParse("400\t2\tName\t1\t0\t0", out _), "a short line is refused");

// --- Orientation: zone bounce gate (Seaside Street / Buckler edge, 2026-09-27) ---
var zoneGate = new ZoneAnnounceGate(30000, 3000);
Check(!zoneGate.IsBounce(408, 5000) && zoneGate.DueAt(408, 5000) == 5000, "a section entered for the first time is announced at once");
zoneGate.Spoke(408, 5000);
zoneGate.Spoke(409, 11000);
Check(zoneGate.IsBounce(408, 14700) && zoneGate.DueAt(408, 14700) == 17700, "walking back within the window waits for a stay");
Check(!zoneGate.IsBounce(408, 35001), "after the window a section is fresh again");
Check(!zoneGate.IsBounce(0, 100), "no section is never a bounce");
zoneGate.Clear();
Check(zoneGate.DueAt(409, 7000) == 7000, "clearing forgets what was heard");

// --- Free navigation: look sweep cone ---
var cone = new ConeSweep(15, 30);
cone.BeginPoll();
Check(cone.Offer("shop", 10) && !cone.Offer("park", 20), "a place enters within half an hour, not beyond");
cone.EndPoll();
cone.BeginPoll();
Check(!cone.Offer("shop", 25) && cone.Contains("shop"), "wobbling inside the leave angle is not a new entry");
cone.EndPoll();
cone.BeginPoll();
Check(!cone.Offer("shop", 40) && !cone.Contains("shop"), "turning a full hour away leaves the cone");
cone.EndPoll();
cone.BeginPoll();
cone.Offer("park", 0);
cone.EndPoll();
cone.BeginPoll();
cone.EndPoll();
Check(!cone.Contains("park"), "a place no longer offered is forgotten");
cone.BeginPoll();
Check(cone.Offer("park", 0) && !cone.Offer("park", 0), "a forgotten place enters again, but once");
cone.EndPoll();

// --- Free navigation: passing by ---
var passing = new ProximityMemory(12, 24);
Check(!passing.IsNews("salon", 30), "far away is not news");
Check(passing.IsNews("salon", 11), "coming within range is news");
passing.Announced("salon");
Check(!passing.IsNews("salon", 5) && !passing.IsNews("salon", 20), "announced once, silent at the edge");
Check(!passing.IsNews("salon", 25) && passing.IsNews("salon", 10), "walking well away re-arms it");

// --- Free navigation: look-around versus drift ---
var turnDetector = new CameraTurnDetector(15);
Check(!turnDetector.Sample(0, 1, 0), "the first sample is never a turn");
Check(!turnDetector.Sample(0.0349f, 0.9994f, 1000), "two degrees in a second is drift");
Check(turnDetector.Sample(0.5f, 0.866f, 1100), "about twenty-eight degrees in a tenth of a second is a look");
turnDetector.Reset();
Check(!turnDetector.Sample(1, 0, 1200), "after a reset the next sample is not a turn");
Check(!turnDetector.Sample(0, 1, 1300, 1250), "a turn that started inside the mod's own aim is not a look");
Check(turnDetector.Sample(1, 0, 1400, 1250), "the next turn after the aim window is a look");
Console.WriteLine($"Passed {passed} navigation checks.");
