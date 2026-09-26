using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// Speaks the sub-area you have just walked into — "Hong Hu Lu Chinatown - Calle
/// principal" — by reading the game's OWN on-screen section notice.
///
/// <para><b>Why this and not our own zone logic.</b> The mod already names the
/// district (<see cref="ZoneNameService"/>), but only on demand and only at district
/// grain, and it derives the name itself from a keyed table lookup. The game
/// meanwhile has <c>app.UICityHud_SectionNotice</c>: the little banner a sighted
/// player sees appear when they cross into a named part of the city. It decides
/// WHEN the crossing happened and it has already resolved WHAT to call it, in the
/// player's own language, in <c>mTextSection</c> (<c>UICityHud_SectionNotice.cs:147</c>).
/// Reading that is reading exactly what is on screen — no guessing about section
/// ids, no table lookup that might reject a good record, and no risk of announcing
/// a boundary the game does not consider one.</para>
///
/// <para><b>Found the way every other HUD reader in this mod finds its component.</b>
/// <c>UICityHud_*</c> objects ARE in the scene — <see cref="MissionGuideReader"/>
/// has relied on that since 2026-09-07. (The MINIMAP is the exception that proved
/// unreachable, twice, on 2026-09-09; nothing here goes near it.)</para>
///
/// <para><b>Announced once per change, never repeated.</b> The banner lingers on
/// screen and its text stays put, so speaking whatever it currently says would
/// repeat the same sentence every poll. Only a change is news.</para>
/// </summary>
public class SectionNoticeHooks
{
    private const string NOTICE_COMPONENT = "app.UICityHud_SectionNotice";

    /// <summary>The banner's own text object, already localized and already
    /// formatted the way the game words it.</summary>
    private const string F_TEXT = "mTextSection";

    /// <summary>Poll cadence in LateUpdate ticks — 15 at 60 fps, about four times a
    /// second. Crossing a boundary is not a fast event and the read is a scene walk;
    /// this is the same order the zone reader already uses.</summary>
    private const int POLL_TICKS = 15;

    /// <summary>Hold after the reader speaks with an interrupt: an area name is
    /// useful but never worth cutting off a line the player is still hearing.</summary>
    private const long READER_HOLD_MS = 1200;

    private static int _tick;
    private static string _lastSpoken;
    private static bool _wasInField;
    private static bool _logged;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] SectionNoticeHooks initialized (speaks the game's own sub-area banner)");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();
        if (!FieldPresenceService.CanSpeak)
        {
            if (_wasInField) { _wasInField = false; _lastSpoken = null; }
            return;
        }
        _wasInField = true;

        if (--_tick > 0) return;
        _tick = POLL_TICKS;

        if (SF6Access.Hooks.SpTalkNovelHooks.DialogueActive) return;
        if (System.Environment.TickCount64 - ScreenReaderService.LastInterruptTick < READER_HOLD_MS) return;

        string area = Read();
        if (string.IsNullOrWhiteSpace(area) || area == _lastSpoken) return;

        _lastSpoken = area;
        ScreenReaderService.Speak(area, interrupt: false);
        API.LogInfo($"[SF6Access] Section notice: '{area}'");
    }

    /// <summary>The banner's current text, or null when the notice component is not
    /// in the scene (which is the normal state outside a city).</summary>
    private static string Read()
    {
        try
        {
            var notice = FieldProbeService.FirstSceneComponent(NOTICE_COMPONENT);
            if (notice == null) return null;

            if (!_logged)
            {
                _logged = true;
                API.LogInfo($"[SF6Access] Section notice: {NOTICE_COMPONENT} found in the scene");
            }

            return FlowHelper.CleanTags(
                FlowHelper.ReadGuiText(FlowHelper.GetObjectField(notice, F_TEXT)))?.Trim();
        }
        catch { return null; }
    }
}
