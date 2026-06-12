using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

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
/// </summary>
public class ArcadeHooks
{
    private const string SUBTITLE_PARAM = "app.worldtour.DemoSubtitles.UIFlowDemoSubtibles.Param";
    private const string COMIC_SUBTITLE_PARAM = "app.UIFlowComicDemoSubtitle.Param";
    private const string WIN_MESSAGE_PARAM = "app.esports.UIFlowWinMessage.Param";
    private static readonly string[] WatchedTypes = { SUBTITLE_PARAM, COMIC_SUBTITLE_PARAM, WIN_MESSAGE_PARAM };

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 30;
    private const int POLL_READ_INTERVAL = 5;

    private static ManagedObject _subtitleParam;
    private static ManagedObject _comicSubtitleParam;
    private static ManagedObject _winMessageParam;
    private static bool _isActive;

    private static string _lastDialog;
    private static string _lastWinMessage;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] ArcadeHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var found = FlowHelper.FindFlowParams(WatchedTypes);
            found.TryGetValue(SUBTITLE_PARAM, out _subtitleParam);
            found.TryGetValue(COMIC_SUBTITLE_PARAM, out _comicSubtitleParam);
            found.TryGetValue(WIN_MESSAGE_PARAM, out _winMessageParam);

            bool active = _subtitleParam != null || _comicSubtitleParam != null || _winMessageParam != null;
            if (active && !_isActive)
            {
                _isActive = true;
                _lastDialog = null;
                _lastWinMessage = null;
                API.LogInfo($"[SF6Access] Arcade text active (subtitles={_subtitleParam != null}, " +
                    $"comic={_comicSubtitleParam != null}, winMessage={_winMessageParam != null})");
            }
            else if (!active && _isActive)
            {
                _isActive = false;
                API.LogInfo("[SF6Access] Arcade text ended");
            }
        }

        if (!_isActive || _pollCounter % POLL_READ_INTERVAL != 0) return;

        if (_subtitleParam != null) PollSubtitles();
        if (_comicSubtitleParam != null) PollComicSubtitles();
        if (_winMessageParam != null) PollWinMessage();
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
