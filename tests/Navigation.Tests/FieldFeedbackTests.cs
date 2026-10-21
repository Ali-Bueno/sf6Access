using System;
using SF6Access.Services.WorldTour;

internal static class FieldFeedbackTests
{
    public static void Run(Action<bool, string> check)
    {
        var speech = new FieldSpeechPolicy();
        check(speech.Offer("route", "old bearing", FieldSpeechPriority.Route, 0, 2500), "field speech accepts a live route line");
        speech.Offer("route", "new bearing", FieldSpeechPriority.Route, 20, 2500);
        check(!speech.TryTake(119, 0, out _), "field readers coalesce for one scan poll");
        check(speech.TryTake(120, 0, out var notice) && notice.Text == "new bearing", "latest bearing replaces stale bearing from the same reader");
        check(!speech.TryTake(3000, 0, out _), "a source never accumulates a speech queue");

        speech.Clear();
        speech.Offer("passing", "shop", FieldSpeechPriority.Ambient, 0, 2500);
        speech.Offer("zone", "street", FieldSpeechPriority.Zone, 0, 2500);
        speech.Offer("look", "person", FieldSpeechPriority.Scan, 0, 2500);
        speech.Offer("route", "route found", FieldSpeechPriority.Route, 0, 2500);
        speech.Offer("contact", "talk", FieldSpeechPriority.Interaction, 0, 2500);
        check(speech.TryTake(100, 0, out notice) && notice.Source == "contact", "interaction beats every competing field reader");
        check(!speech.TryTake(700, 0, out _), "background speech cannot interrupt an interaction");
        check(speech.TryTake(1100, 0, out notice) && notice.Source == "route", "route availability precedes discovery when interaction finishes");
        speech.Clear();

        // Regression scenario from the busy street log: many independent readers in one short burst.
        for (int i = 0; i < 14; i++)
            speech.Offer("street-" + i, "Nearby person " + i, FieldSpeechPriority.Ambient, 0, 2500);
        speech.Offer("contact", "Interact: person", FieldSpeechPriority.Interaction, 0, 2500);
        int burstLines = 0;
        string? firstSource = null;
        for (long time = 0; time <= 3000; time += 100)
        {
            if (!speech.TryTake(time, 0, out notice)) continue;
            firstSource ??= notice.Source;
            burstLines++;
        }
        check(firstSource == "contact" && burstLines <= 3,
            "busy-street burst gives contact first and discards excess stale discoveries");
        check(!speech.TryTake(10000, 0, out _), "old street discoveries never drain after walking onward");
        speech.Clear();

        speech.Offer("route", new string('r', 100), FieldSpeechPriority.Route, 0, 10000);
        check(speech.TryTake(100, 0, out _), "long route explanation starts");
        speech.Offer("contact", "Interact: shop", FieldSpeechPriority.Interaction, 200, 1200);
        check(!speech.TryTake(300, 0, out _), "higher priority does not create a same-frame speech burst");
        check(speech.TryTake(600, 0, out notice) && notice.Source == "contact", "current contact preempts a long route explanation");
        speech.Clear();

        speech.Offer("look", new string('a', 100), FieldSpeechPriority.Scan, 0, 1500);
        check(speech.TryTake(100, 0, out _), "long automatic scan starts");
        speech.Offer("look", new string('b', 100), FieldSpeechPriority.Scan, 200, 1500);
        check(!speech.TryTake(300, 0, out _), "replacement scan still respects the burst floor");
        check(speech.TryTake(600, 0, out notice) && notice.Text[0] == 'b', "new deliberate look replaces the obsolete active scan");
        speech.Offer("look", new string('b', 100), FieldSpeechPriority.Scan, 700, 1500);
        check(!speech.TryTake(1200, 0, out _), "identical active scan cannot keep interrupting itself");
        speech.Clear();

        int callbacks = 0;
        speech.Offer("look", "person to select", FieldSpeechPriority.Scan, 0, 1500, () => callbacks++);
        check(!speech.TryTake(500, 2000, out _), "manual speech protects against automatic scan");
        check(callbacks == 0, "offering a scan does not activate its R3 candidate");
        check(!speech.TryTake(2000, 2000, out _), "a scan expires instead of playing after a long manual readout");
        check(callbacks == 0, "expired scan cannot mark a target heard");
        speech.Offer("look", "current person", FieldSpeechPriority.Scan, 2100, 1500, () => callbacks++);
        check(speech.TryTake(2200, 0, out notice), "fresh scan speaks after explicit readout");
        notice.OnSpoken?.Invoke();
        check(callbacks == 1, "only emitted scan commits a candidate");
        speech.Clear();

        bool current = true;
        speech.Offer("prompt", "climb", FieldSpeechPriority.Interaction, 0, 2500, isCurrent: () => current);
        current = false;
        check(!speech.TryTake(100, 0, out _), "vanished action prompt is rejected before speaking");
        current = true;
        check(!speech.TryTake(200, 0, out _), "invalidated prompt is removed, not deferred");
        speech.Offer("route", "route unavailable", FieldSpeechPriority.Route, 300, 1000);
        speech.Forget("route");
        check(!speech.TryTake(500, 0, out _), "destination change forgets queued route status");

        speech.Offer("look", "same old object", FieldSpeechPriority.Scan, 0, 1000);
        speech.Offer("look", "same old object", FieldSpeechPriority.Scan, 900, 1000);
        check(!speech.TryTake(1000, 0, out _), "repeat offers do not keep one stale utterance alive forever");
        check(!speech.Offer("", "text", FieldSpeechPriority.Zone, 0, 1000), "empty speech source is rejected");
        check(!speech.Offer("zone", " ", FieldSpeechPriority.Zone, 0, 1000), "blank speech is rejected");

        long protectedUntil = FieldSpeechPolicy.ProtectUntil(100, 0, new string('x', 100), true);
        long queuedUntil = FieldSpeechPolicy.ProtectUntil(500, protectedUntil, "OK", false);
        check(queuedUntil > protectedUntil, "short queued external line cannot shorten a long manual readout");
        check(FieldSpeechPolicy.ProtectUntil(500, protectedUntil, "OK", true) < protectedUntil,
            "new explicit interrupt replaces the previous reading reservation");

        var scan = new ScanDwell();
        check(!scan.Observe("npc", 0, false), "stationary camera without a deliberate look stays silent");
        check(!scan.Observe("npc", 500, false), "time alone does not turn passive focus into a scan");
        check(scan.Observe("npc", 600, true), "deliberate turn can identify an already settled focus");
        scan.Spoken("npc");
        check(!scan.Observe("npc", 900, true), "holding one focus does not repeat its name");
        scan.Observe(null, 1000, true);
        check(!scan.Observe("npc", 1100, true), "looking back starts a fresh dwell");
        check(scan.Observe("npc", 1400, false), "a settled look may finish after the camera stops");
        scan.Observe("shop", 1500, true);
        check(!scan.Observe("door", 1600, true), "fast sweep does not recite each flashed object");
        check(!scan.Observe("door", 1899, false), "scan dwell waits for its full interval");
        check(scan.Observe("door", 1900, false), "settled replacement identifies only the current object");
        scan.Clear();
        check(!scan.Observe("door", 2000, false), "menu or movement reset requires another deliberate look");

        // A still-focused NPC must remain eligible after speech outlasts the old 1.5 s look window.
        scan.Clear();
        speech.Clear();
        callbacks = 0;
        for (long time = 0; time <= 8500; time += 100)
        {
            if (scan.Observe("npc", time, true))
                speech.Offer("look", "NPC ahead", FieldSpeechPriority.Scan, time, 2500,
                    () => { scan.Spoken("npc"); callbacks++; }, () => scan.PendingKey == "npc");
            if (speech.TryTake(time, 8000, out notice)) notice.OnSpoken?.Invoke();
        }
        check(callbacks == 1, "a focus held during a long readout recovers and speaks exactly once afterward");
        check(!scan.Observe("npc", 20000, true), "holding the named focus does not create periodic name spam");

        scan.Clear();
        speech.Clear();
        scan.Observe("old NPC", 0, true);
        scan.Observe("old NPC", 300, true);
        speech.Offer("look", "old NPC", FieldSpeechPriority.Scan, 300, 2500,
            isCurrent: () => scan.PendingKey == "old NPC");
        scan.Observe("door", 400, true);
        check(!speech.TryTake(500, 0, out _), "a new focus invalidates an unheard NPC before its speech can play");
        scan.Clear();
        check(!scan.Observe("door", 1000, true) && scan.Observe("door", 1300, true),
            "returning from a menu rearms the focus after a short dwell without another camera turn");

        speech.Clear();
        speech.Offer("heading", "north", FieldSpeechPriority.Heading, 0, 2500);
        speech.Offer("contact", "Interact: shop", FieldSpeechPriority.Interaction, 0, 2500);
        check(speech.TryTake(100, 0, out notice) && notice.Source == "contact",
            "restored compass announcements leave confirmed interactions first");

        var cues = new FieldCuePolicy();
        check(cues.Available(0, FieldCueLane.Route, "route"), "first field cue can play immediately");
        cues.Reserve(0, FieldCueLane.Route, "route", TimeSpan.FromMilliseconds(350));
        check(!cues.Available(100, FieldCueLane.Route, "route"), "a lane cannot pile its own sounds");
        check(cues.Available(100, FieldCueLane.Geometry, "radar"), "wall geometry can sound during route guidance");
        cues.Reserve(100, FieldCueLane.Geometry, "radar", TimeSpan.FromMilliseconds(280));
        check(cues.Available(100, FieldCueLane.Focused, "ladder"), "focused object can sound during guidance and geometry");
        cues.Reserve(100, FieldCueLane.Focused, "ladder", TimeSpan.FromMilliseconds(360));
        check(!cues.Available(100, FieldCueLane.Nearby, "ladder"), "the same ladder cannot echo across object lanes");
        check(cues.Available(100, FieldCueLane.Nearby, "shop"), "different nearby kind can coexist with focused ladder");
        cues.Reserve(100, FieldCueLane.Route, "route", TimeSpan.FromMilliseconds(20));
        check(!cues.Available(349, FieldCueLane.Route, "route"), "short reservation cannot shorten an existing sound");
        check(cues.Available(350, FieldCueLane.Route, "route"), "next cue starts when its actual sample ends");
        check(!cues.Available(350, FieldCueLane.Geometry, "radar"), "finishing the route sample does not release geometry");
        check(cues.Available(460, FieldCueLane.Nearby, "ladder"), "object becomes available when its focused tail ends");
        cues.Reserve(1050, FieldCueLane.Route, "route", TimeSpan.Zero);
        check(cues.Available(1050, FieldCueLane.Route, "route"), "missing sound does not reserve a lane");
        cues.Reserve(1050, FieldCueLane.Route, "route", TimeSpan.FromMilliseconds(.5));
        check(!cues.Available(1050, FieldCueLane.Route, "route") && cues.Available(1051, FieldCueLane.Route, "route"),
            "fractional milliseconds round up to prevent overlap");

        // A street can contain dozens of crates closer than its only ladder or shop.
        int[] crowdedStreet = { 4, 4, 4, 0, 4, 2, 1, 3, 0 };
        int[] nearestOfEachKind = { 0, 3, 5, 6, 7 };
        for (int turn = 0; turn < nearestOfEachKind.Length * 2; turn++)
            check(SonarRota.PickKind(crowdedStreet, turn) == nearestOfEachKind[turn % nearestOfEachKind.Length],
                "nearby sonar rotates every available kind despite a crowd of identical props");
        check(SonarRota.PickKind(Array.Empty<int>(), 10) == -1, "empty nearby world has no phantom cue");
        check(SonarRota.PickKind(new[] { 1, 1, 1 }, 9) == 0, "same-kind crowd sounds only its nearest member");
        check(SonarRota.PickKind(new[] { 4, 0 }, 5) == 1, "leaving kinds behind retains a valid rotation");
    }
}
