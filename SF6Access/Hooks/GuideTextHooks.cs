using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Announces contextual description texts shown for the focused menu item:
/// - "InputGuide" GUI, element "e_text": the focused item's tooltip
///   ("A mode where you fight masters to obtain Special Moves...")
/// - "GameGuideWidget" GUI: rotating hint banner
/// GUI views are cached and only their subtrees are re-read each poll;
/// the cache is refreshed periodically because menus recreate these widgets.
/// </summary>
public class GuideTextHooks
{
    private sealed class WatchedGui
    {
        public string Owner;
        public ManagedObject View;
        public string NameFilter;  // only announce text elements with this name (null = all)
        public string LastText;
        public string PendingText; // detected change waiting for its announce slot
        public long PendingSince;
    }

    // Tooltips must come AFTER the element name. If the element was already
    // announced (an interrupting speak at/after detection), the tooltip can
    // queue immediately; otherwise wait briefly for the element to arrive.
    private const long PENDING_DELAY_MS = 150;

    // ownerContains -> element name filter
    private static readonly (string owner, string nameFilter)[] Watched =
    {
        ("InputGuide", "e_text"),
        ("GameGuideWidget", null),
        ("Gallery", "c_text_detail"),  // focused cutscene/comic/illustration title
    };

    private static int _pollCounter;
    private const int POLL_INTERVAL = 10;
    private const int REFRESH_INTERVAL = 600;

    private static readonly List<WatchedGui> _guis = new();

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] GuideTextHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        // Menus recreate these widgets; re-find them periodically
        // (faster while we have none at all)
        if (_pollCounter % REFRESH_INTERVAL == 0 ||
            (_guis.Count == 0 && _pollCounter % 120 == 0))
            RefreshGuis();

        if (_guis.Count == 0 || _pollCounter % POLL_INTERVAL != 0) return;

        long now = System.Environment.TickCount64;

        foreach (var gui in _guis)
        {
            try
            {
                string text = ReadGuiText(gui);
                if (!string.IsNullOrEmpty(text) && text != gui.LastText)
                {
                    // Newly discovered instance: baseline silently — announcing
                    // here replayed stale screen tooltips lingering under battles
                    bool first = gui.LastText == null;
                    gui.LastText = text;
                    if (first) continue;

                    gui.PendingText = text;
                    gui.PendingSince = now;
                }

                if (gui.PendingText == null) continue;

                // Element name already spoken since this tooltip appeared?
                // Then queue right away — it lands after the name. Otherwise
                // wait briefly in case the element announcement is on its way.
                bool elementSpoken = ScreenReaderService.LastInterruptTick >= gui.PendingSince - 300;
                if (!elementSpoken && now - gui.PendingSince < PENDING_DELAY_MS) continue;

                // Training menu tooltips repeat the row's guide with bare
                // glyph gaps ("Ative um espaço com .") — fill in the buttons
                string announcement = TrainingMenuHooks.FillGuideIcons(gui.PendingText);

                // Fighting Ground items: FGMenuHooks reads the focused item's
                // description itself and already spoke "name. description", so
                // don't repeat the same InputGuide description here.
                if (gui.Owner != null && gui.Owner.Contains("InputGuide") &&
                    FGMenuHooks.SuppressGuideDesc(announcement))
                {
                    gui.PendingText = null;
                    continue;
                }

                // Status menu appends the InputGuide tooltip to its slot/item
                // announcements itself — don't double-announce it here.
                if (gui.Owner != null && gui.Owner.Contains("InputGuide") &&
                    StatusMenuHooks.IsInStatusMenu)
                {
                    gui.PendingText = null;
                    continue;
                }

                API.LogInfo($"[SF6Access] Guide [{gui.Owner}]: {announcement}");
                ScreenReaderService.Speak(announcement, interrupt: false);
                gui.PendingText = null;
            }
            catch { }
        }
    }

    private static void RefreshGuis()
    {
        // Preserve per-INSTANCE state across refreshes, keyed by view address:
        // two GUIs can share one name (a stale "choose a tutorial" InputGuide
        // under the battle one) and an owner-keyed carry made them re-announce
        // on every refresh
        var lastByAddress = new Dictionary<ulong, string>();
        foreach (var g in _guis)
        {
            try
            {
                ulong addr = g.View?.GetAddress() ?? 0;
                if (addr != 0) lastByAddress[addr] = g.LastText;
            }
            catch { }
        }

        _guis.Clear();
        foreach (var (owner, nameFilter) in Watched)
        {
            foreach (var (foundOwner, view) in GuiTextReader.FindGuiViews(owner))
            {
                string last = null;
                try
                {
                    ulong addr = view?.GetAddress() ?? 0;
                    if (addr != 0) lastByAddress.TryGetValue(addr, out last);
                }
                catch { }

                _guis.Add(new WatchedGui
                {
                    Owner = foundOwner,
                    View = view,
                    NameFilter = nameFilter,
                    LastText = last
                });
            }
        }
    }

    private static string ReadGuiText(WatchedGui gui)
    {
        var texts = GuiTextReader.ReadViewTexts(gui.View, gui.Owner);
        var parts = new List<string>();
        foreach (var t in texts)
        {
            if (gui.NameFilter != null && t.Name != gui.NameFilter) continue;
            if (string.IsNullOrWhiteSpace(t.Text)) continue;
            parts.Add(t.Text.Replace('\n', ' ').Trim());
            if (parts.Count >= 4) break;
        }
        return parts.Count > 0 ? string.Join(". ", parts) : null;
    }
}
