using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for arcade mode story screens:
/// - Cutscene dialogue: app.worldtour.DemoSubtitles.UIFlowDemoSubtibles.Param
///   ("Subtibles" is the game's own typo) shows one line at a time in
///   _TextName/_TextDialog; the player advances lines with a button press.
/// - Comic demo dialogue (arcade story scenes): app.UIFlowComicDemoSubtitle.Param
///   shows the current line in NameText/DialogueText.
/// - Victory quote: app.esports.UIFlowWinMessage.Param shows the winning
///   character's phrase in mText after a battle.
/// All are announced whenever their displayed text changes.
///
/// ScreenAdapter for the poll. The SetMessage-driven announce keeps its own
/// per-frame [Callback]: a subtitle line must speak the frame after the game
/// sets it, not up to a search-interval later (the adapter only polls while
/// active). Registered in ScreenRegistry.
/// </summary>
public sealed class ArcadeHooks : ScreenAdapter
{
    private const string SUBTITLE_PARAM = "app.worldtour.DemoSubtitles.UIFlowDemoSubtibles.Param";
    private const string COMIC_SUBTITLE_PARAM = "app.UIFlowComicDemoSubtitle.Param";
    private const string WIN_MESSAGE_PARAM = "app.esports.UIFlowWinMessage.Param";
    private static readonly string[] WatchedTypes = { SUBTITLE_PARAM, COMIC_SUBTITLE_PARAM, WIN_MESSAGE_PARAM };

    public override string[] OwnedTypes => WatchedTypes;

    public ArcadeHooks()
    {
        SearchInterval = 30;
        ReadInterval = 5;
    }

    // Shared with the static SetMessage callback below.
    private static ManagedObject _subtitleParam;
    private static ManagedObject _comicSubtitleParam;
    private static ManagedObject _winMessageParam;

    private static string _lastDialog;
    private static string _lastWinMessage;

    // Set by the SetMessage hook; the next frame reads the freshly-stored
    // OldName/OldDialog Guids from the Param (game thread — don't resolve
    // localized messages inside the hook itself)
    private static volatile bool _setMessagePending;

    [PluginEntryPoint]
    public static void Initialize()
    {
        // The GUI text poll missed lines whose via.gui.Text did not refresh
        // (the final line of the Battle Hub intro demo). SetMessage is called
        // by the game for every subtitle line and also stores both Guids in
        // OldName/OldDialog, so hooking it catches them all.
        var paramTd = TDB.Get().FindType(SUBTITLE_PARAM);
        var setMessage = paramTd?.GetMethod("SetMessage");
        if (setMessage != null)
        {
            var hook = setMessage.AddHook(false);
            hook.AddPost((ref ulong retval) => _setMessagePending = true);
            API.LogInfo("[SF6Access] UIFlowDemoSubtibles.Param.SetMessage hook installed");
        }
        else
        {
            API.LogError("[SF6Access] UIFlowDemoSubtibles.Param.SetMessage not found");
        }

        API.LogInfo("[SF6Access] ArcadeHooks initialized");
    }

    // Per-frame: a freshly-set subtitle line must not wait for the adapter's
    // search tick (the first line of a cutscene arrives before activation).
    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnSetMessagePending()
    {
        if (!_setMessagePending) return;
        _setMessagePending = false;
        if (_subtitleParam == null)
            FlowHelper.FindFlowParams(WatchedTypes).TryGetValue(SUBTITLE_PARAM, out _subtitleParam);
        AnnounceFromStoredGuids();
    }

    protected override bool Locate()
    {
        var found = FlowHelper.FindFlowParams(WatchedTypes);
        found.TryGetValue(SUBTITLE_PARAM, out _subtitleParam);
        found.TryGetValue(COMIC_SUBTITLE_PARAM, out _comicSubtitleParam);
        found.TryGetValue(WIN_MESSAGE_PARAM, out _winMessageParam);
        return _subtitleParam != null || _comicSubtitleParam != null || _winMessageParam != null;
    }

    protected override void OnActivate()
    {
        _lastDialog = null;
        _lastWinMessage = null;
        API.LogInfo($"[SF6Access] Arcade text active (subtitles={_subtitleParam != null}, " +
            $"comic={_comicSubtitleParam != null}, winMessage={_winMessageParam != null})");
    }

    protected override void OnDeactivate()
    {
        _subtitleParam = null;
        _comicSubtitleParam = null;
        _winMessageParam = null;
        API.LogInfo("[SF6Access] Arcade text ended");
    }

    protected override void OnPoll()
    {
        if (_subtitleParam != null) PollSubtitles();
        if (_comicSubtitleParam != null) PollComicSubtitles();
        if (_winMessageParam != null) PollWinMessage();
    }

    private static void AnnounceFromStoredGuids()
    {
        if (_subtitleParam == null) return;
        try
        {
            AnnounceDialog(FlowHelper.ResolveGuidField(_subtitleParam, "OldDialog"),
                FlowHelper.ResolveGuidField(_subtitleParam, "OldName"));
        }
        catch { }
    }

    private static void PollSubtitles()
    {
        string dialog = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(_subtitleParam, "_TextDialog"))
            ?? FlowHelper.ResolveGuidField(_subtitleParam, "OldDialog");
        AnnounceDialog(dialog,
            FlowHelper.ReadGuiText(FlowHelper.GetObjectField(_subtitleParam, "_TextName"))
            ?? FlowHelper.ResolveGuidField(_subtitleParam, "OldName"));
    }

    private static void PollComicSubtitles()
    {
        string dialog = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(_comicSubtitleParam, "DialogueText"));
        AnnounceDialog(dialog,
            FlowHelper.ReadGuiText(FlowHelper.GetObjectField(_comicSubtitleParam, "NameText")));
    }

    private static void AnnounceDialog(string dialog, string name)
    {
        if (string.IsNullOrEmpty(dialog) || dialog == _lastDialog) return;
        _lastDialog = dialog;

        // Cutscene subtitles follow the in-game Subtitles option: when the player
        // has them off, don't read them (the dialogue is voiced). Tracked even when
        // suppressed (lastDialog set above) so toggling back on doesn't re-read old lines.
        if (!FlowHelper.AreSubtitlesEnabled()) return;

        // Read every line including the first — cutscene dialogue must not
        // wait for a "change". Each new line interrupts the previous one,
        // matching the button-advance pacing.
        string announcement = string.IsNullOrEmpty(name) ? dialog : $"{name}: {dialog}";
        API.LogInfo($"[SF6Access] Subtitle: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    private static void PollWinMessage()
    {
        string text = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(_winMessageParam, "mText"));
        if (string.IsNullOrEmpty(text) || text == _lastWinMessage) return;
        _lastWinMessage = text;

        API.LogInfo($"[SF6Access] Win message: {text}");
        ScreenReaderService.Speak(text);
    }
}
