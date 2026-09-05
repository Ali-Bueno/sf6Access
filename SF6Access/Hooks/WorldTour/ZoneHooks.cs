using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// "Where am I?" for the World Tour field — the RE7 mod's room announcements
/// ported to an open city, where knowing which part of town you are in is half of
/// orientation.
///
/// <list type="bullet">
/// <item>The area is announced whenever it CHANGES, hands-free, like walking
///   through a door in RE7.</item>
/// <item><b>Z</b> speaks it again on demand, at any time.</item>
/// </list>
///
/// <para>The name itself is resolved by <see cref="ZoneNameService"/>, which
/// prefers the game's own district and falls back to the nearest named landmark.
/// This hook only decides WHEN to say it — and the difference matters, because a
/// landmark answer is spoken as "near X, N metres away" rather than as a district
/// the player is inside. Being told you are somewhere you are not is worse than
/// being told nothing.</para>
///
/// <para>Gated like every other hands-free World Tour reader: nothing in menus,
/// nothing during a fight, nothing over dialogue or the panel tutorial, and
/// nothing on top of an announcement still being spoken. Arriving somewhere is
/// exactly when the game is most likely to be talking, so a change noticed while
/// muted is REMEMBERED and spoken as soon as it is allowed — consuming it on the
/// spot would lose the announcement outright. The key deliberately bypasses all of
/// it: an explicit press is a request, and a request must always be answered.</para>
/// </summary>
public class ZoneHooks
{
    private const string ZONE_KEY = "wt_zone";

    // Roughly 4x/second, matching the field radar's target poll. The service
    // behind it refreshes on its own clock, so this is only how often the answer
    // is compared, not how often the game is queried.
    private const int POLL_INTERVAL = 15;
    private static int _pollCounter;

    // Hold after the reader speaks, so the zone announcement does not cut off an
    // announcement still in progress (same value and reason as the arrival
    // readout in FieldAwarenessHooks).
    private const long READER_HOLD_MS = 1200;

    // On-demand "say the area again" key. Keyboard only: every gamepad button the
    // testers tried in the field turned out to be a game action, and Start is
    // already the nearby radar's. A zero button mask binds no pad button at all.
    private const int VK_Z = 0x5A;
    private static readonly ReadoutShortcut ZoneKey = new(VK_Z, ReadoutShortcut.PAD_NONE);

    // The change noticed while the reader was not allowed to speak.
    private static ZoneReading _pending;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] ZoneHooks initialized (Z = speak current area)");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        // Sampled every frame: a short press falls between two polls otherwise.
        bool wantsZone = ZoneKey.Pressed();

        FieldPresenceService.Refresh();
        if (!FieldPresenceService.InField)
        {
            if (wantsZone)
                API.LogInfo("[SF6Access] Zone key pressed outside the World Tour field");
            // Re-entering the field re-announces where the player has landed.
            GameStateTracker.Remove(ZONE_KEY);
            ZoneNameService.Reset();
            _pending = default;
            _pollCounter = 0;
            return;
        }

        if (wantsZone)
        {
            _pending = default;
            Announce(ZoneNameService.Current(), manual: true);
        }

        if (++_pollCounter >= POLL_INTERVAL)
        {
            _pollCounter = 0;
            var zone = ZoneNameService.Current();
            // Keyed on the NAME, not on the section id or the landmark id: two
            // fast-travel points share the name 'Beat Square', and walking from one
            // to the other has not changed which area the player is in.
            if (zone.Ok && GameStateTracker.HasChanged(ZONE_KEY, zone.Name))
                _pending = zone;
        }

        if (_pending.Ok && MayAnnounce())
        {
            var zone = _pending;
            _pending = default;
            Announce(zone, manual: false);
        }
    }

    /// <summary>Whether the automatic announcement may speak right now.</summary>
    private static bool MayAnnounce()
    {
        if (!FieldPresenceService.CanSpeak) return false;
        if (SF6Access.Hooks.SpTalkNovelHooks.DialogueActive) return false;
        // The panel guide owns the mic while its tutorial is running.
        if (PadGuideHooks.Active) return false;
        return System.Environment.TickCount64 - ScreenReaderService.LastInterruptTick >= READER_HOLD_MS;
    }

    /// <summary>Say where the player is, in wording that matches how confidently
    /// it is known.</summary>
    private static void Announce(ZoneReading zone, bool manual)
    {
        if (!zone.Ok)
        {
            // "I don't know" is an ANSWER: only ever said to someone who asked.
            if (manual) ScreenReaderService.Speak(LocalizedText.ZoneUnknown(), interrupt: true);
            return;
        }

        string spoken = zone.Source == ZoneSource.NearestPoint
            // NOT containment — the nearest landmark, with its distance, so the
            // player can judge for themselves how much it tells them.
            ? LocalizedText.AtMeters(LocalizedText.ZoneNear(zone.Name),
                                     (int)System.Math.Round(zone.Distance))
            : LocalizedText.ZoneHere(zone.Name);

        // The automatic one queues behind whatever is being said; the requested one
        // takes the floor.
        ScreenReaderService.Speak(spoken, interrupt: manual);
        if (!manual) return;
        // A manual read counts as having announced this area, so walking on does
        // not immediately repeat it.
        GameStateTracker.HasChanged(ZONE_KEY, zone.Name);
    }
}
