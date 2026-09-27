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
        _pending = new Pending(text, text, sectionId, 0);
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
            _pending = default;
            _announcedSection = 0;
            _announcedName = null;
            _bannersSeen = false;
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
            MarkAnnounced(p.Name, p.SectionId);
            ScreenReaderService.Speak(p.Text, interrupt: false);
        }
    }

    /// <summary>Queues a zone the player has just walked into, if it is news. A newer zone
    /// replaces one still waiting, except the game's banner for the same section.</summary>
    private static void Queue(ZoneReading zone)
    {
        if (!zone.Ok) return;
        if (zone.Source == ZoneSource.Section)
        {
            // Id 0 is a held-over name (a gap between sections): never news.
            if (zone.SectionId == 0 || zone.SectionId == _pending.SectionId) return;
            if (zone.SectionId == _announcedSection)
            {
                // Stepped back before a table-name announcement came due: drop it. A banner
                // (due at once) is the game's own word and is kept.
                if (_pending.DueAt != 0) _pending = default;
                return;
            }
            _pending = new Pending(zone.Name, zone.Name, zone.SectionId,
                                   System.Environment.TickCount64 + BANNER_GRACE_MS);
            return;
        }

        if (zone.Name == _announcedName) return;
        // Keyed now, not when spoken, so a region is queued once however long speech waits.
        _announcedName = zone.Name;
        if (zone.Source == ZoneSource.Region && _bannersSeen) return;
        string text = zone.Source == ZoneSource.Region ? LocalizedText.RegionOf(zone.Name) : zone.Name;
        _pending = new Pending(zone.Name, text, 0, 0);
    }

    private static void MarkAnnounced(string name, uint sectionId)
    {
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
