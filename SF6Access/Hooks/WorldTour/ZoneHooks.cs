using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// The single speaker for "which part of town is this": announces a zone hands-free when
/// the player walks into it (the game's own section banner when it fires, else the section
/// or landmark region from <see cref="ZoneNameService"/>), and Z answers "where am I" on
/// demand (<see cref="WhereAmIReadout"/>). Design: <c>docs/wt-orientation.md</c>.
/// </summary>
public class ZoneHooks
{
    // About four times a second; the service refreshes on its own slower clock.
    private const int POLL_INTERVAL = 15;
    private static int _pollCounter;

    // Hold after an interrupting line so a zone name does not cut it off.
    private const long READER_HOLD_MS = 1200;

    // A section crossing is also seen by the game's banner, whose text arrives up to a
    // second later (SectionNoticeHooks.TEXT_WAIT_FRAMES); wait that long plus one poll
    // for the richer banner wording before speaking the table name.
    private const long BANNER_GRACE_MS = 1500;

    // At the Seaside Street / Buckler edge the game re-raised its banner every 0.4-6 s
    // (2026-09-27 log). Walking back into a section heard within half a minute is a bounce:
    // it is only announced once the player has stayed there for 3 s.
    private const long REPEAT_WINDOW_MS = 30000;
    private const long BOUNCE_DWELL_MS = 3000;
    private static readonly ZoneAnnounceGate _gate = new(REPEAT_WINDOW_MS, BOUNCE_DWELL_MS);

    private const int VK_Z = 0x5A;
    private static readonly ReadoutShortcut ZoneKey = new(VK_Z, ReadoutShortcut.PAD_NONE);

    /// <summary>A zone waiting to be spoken: its key name, the spoken line and when it is due.</summary>
    private readonly record struct Pending(string Name, string Text, uint SectionId, long DueAt);

    private static Pending _pending;
    private static uint _announcedSection;
    private static string _announcedName;
    private static bool _bannersSeen;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] ZoneHooks initialized (Z = where am I)");
    }

    /// <summary>The game raised its section banner (from <see cref="SectionNoticeHooks"/>).</summary>
    internal static void OnSectionNotice(uint sectionId, string text)
    {
        _bannersSeen = true;
        // A banner whose id could not be read belongs to the section the player is in now.
        if (sectionId == 0) sectionId = WorldTourStateService.CurrentSectionId;
        if (sectionId != 0 && sectionId == _announcedSection)
        {
            // Back in the section already announced: whatever was waiting is stale.
            _pending = default;
            return;
        }
        // Due at once (0) unless it is a bounce back, which waits for the player to stay.
        long now = System.Environment.TickCount64;
        _pending = new Pending(text, text, sectionId, _gate.IsBounce(sectionId, now) ? _gate.DueAt(sectionId, now) : 0);
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        // Sampled every frame: a short press falls between two polls otherwise.
        bool wantsZone = ZoneKey.Pressed();

        FieldPresenceService.Refresh();
        if (!FieldPresenceService.InField)
        {
            // Re-entering the field re-announces where the player has landed.
            ZoneNameService.Reset();
            PlaceCache.Reset();
            _pending = default;
            _announcedSection = 0;
            _announcedName = null;
            _bannersSeen = false;
            _gate.Clear();
            _pollCounter = 0;
            return;
        }

        if (wantsZone)
        {
            _pending = default;
            var zone = ZoneNameService.Current();
            MarkAnnounced(zone.Name, zone.SectionId);
            ScreenReaderService.Speak(WhereAmIReadout.Compose(zone), interrupt: true);
        }

        if (++_pollCounter >= POLL_INTERVAL)
        {
            _pollCounter = 0;
            Queue(ZoneNameService.Current());
        }

        long now = System.Environment.TickCount64;
        if (_pending.Text != null && now >= _pending.DueAt && MayAnnounce())
        {
            var p = _pending;
            _pending = default;
            if (!StillThere(p)) return;
            MarkAnnounced(p.Name, p.SectionId);
            ScreenReaderService.Speak(p.Text, interrupt: false);
        }
    }

    /// <summary>Queues a zone the player has just walked into, if it is news. A newer zone
    /// replaces one still waiting, except the game's banner for the same section. A section
    /// heard recently waits for the player to stay (<see cref="ZoneAnnounceGate"/>).</summary>
    private static void Queue(ZoneReading zone)
    {
        if (!zone.Ok) return;
        if (zone.Source == ZoneSource.Section)
        {
            // Id 0 is a held-over name (a gap between sections): never news.
            if (zone.SectionId == 0 || zone.SectionId == _pending.SectionId) return;
            if (zone.SectionId == _announcedSection)
            {
                    // Stepped back before a waiting line came due (grace or bounce): drop it.
                // A banner due at once is the game's own word and is kept.
                if (_pending.DueAt != 0) _pending = default;
                return;
            }
            long now = System.Environment.TickCount64;
            _pending = new Pending(zone.Name, zone.Name, zone.SectionId,
                                   System.Math.Max(now + BANNER_GRACE_MS, _gate.DueAt(zone.SectionId, now)));
            return;
        }

        if (zone.Name == _announcedName) return;
        // Keyed now, not when spoken, so a region is queued once however long speech waits.
        _announcedName = zone.Name;
        if (zone.Source == ZoneSource.Region && _bannersSeen) return;
        string text = zone.Source == ZoneSource.Region ? LocalizedText.RegionOf(zone.Name) : zone.Name;
        _pending = new Pending(zone.Name, text, 0, 0);
    }

    /// <summary>A section line that had to wait is only spoken if the player is still in it
    /// (id 0 = between sections, not a contradiction).</summary>
    private static bool StillThere(Pending p)
    {
        if (p.SectionId == 0 || p.DueAt == 0) return true;
        uint now = WorldTourStateService.CurrentSectionId;
        return now == 0 || now == p.SectionId;
    }

    private static void MarkAnnounced(string name, uint sectionId)
    {
        _gate.Spoke(sectionId, System.Environment.TickCount64);
        if (sectionId != 0) _announcedSection = sectionId;
        if (!string.IsNullOrEmpty(name)) _announcedName = name;
    }

    private static bool MayAnnounce()
    {
        if (!FieldPresenceService.CanSpeak) return false;
        if (SF6Access.Hooks.SpTalkNovelHooks.DialogueActive) return false;
        if (PadGuideHooks.Active || DestinationPickerHooks.IsOpen) return false;
        return System.Environment.TickCount64 - ScreenReaderService.LastInterruptTick >= READER_HOLD_MS;
    }
}
