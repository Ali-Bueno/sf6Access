using SF6Access.Services.WorldTour;

internal static class HeadingAnnouncementChecks
{
    public static void Run(Action<bool, string> check)
    {
        var heading = new HeadingAnnouncement(8);
        check(heading.Observe(0, false, 0) == -1 && heading.Observe(0, false, 500) == -1,
            "initial facing stays quiet until the player turns");
        check(heading.Observe(12, true, 600) == 0, "a deliberate turn can identify the current compass point");
        heading.Spoke(0, 600);
        check(heading.Observe(23, true, 800) == -1 && heading.Observe(21, true, 1000) == -1,
            "wobbling at a compass boundary does not alternate compass words");
        check(heading.Observe(30, true, 1200) == -1 && heading.Observe(35, false, 1400) == 1,
            "a new compass sector speaks after settling even when the stick has stopped");
        heading.Spoke(1, 1400);
        check(!heading.IsCurrent(1) && heading.Observe(45, true, 1800) == -1,
            "the same heading is not repeated while standing or looking within its sector");
        check(heading.Observe(80, true, 1850) == -1 && !heading.IsCurrent(1),
            "turning away invalidates an older queued direction immediately");
        check(heading.Observe(90, false, 2050) == 2,
            "another settled point becomes available after the short speech gap");
        heading.Spoke(2, 2050);
        heading.Observe(130, true, 2150);
        check(heading.Observe(135, false, 2350) == -1 && heading.Observe(135, false, 2700) == 3,
            "rapid successive directions leave room for a short compass word");

        heading.Reset();
        heading.Observe(315, false, 0);
        heading.Observe(359, true, 100);
        check(heading.Observe(1, false, 300) == 0, "north wraps through zero without a false opposite direction");
        heading.Reset();
        heading.Observe(0, false, 0);
        heading.Observe(45, true, 100);
        heading.Observe(90, true, 200);
        check(heading.Observe(135, true, 300) == -1 && heading.Observe(140, false, 500) == 3,
            "a fast sweep drops crossed sectors and reads the direction that settles");
        check(heading.Observe(float.NaN, false, 600) == -1 && !heading.IsCurrent(3),
            "an unreadable camera invalidates a pending heading");
        heading.Observe(180, false, 700);
        check(heading.Observe(180, false, 1200) == -1,
            "recovering a camera reading does not itself announce a direction");
    }
}
