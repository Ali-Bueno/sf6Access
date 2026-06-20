using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Announces the Battle Hub text-chat window (app.UIFlowChat.Menu.Param, opened
/// with T): the window header on open, and the focused input-bar element as you
/// move along it. The bar's elements are icon buttons with no readable text, so
/// the generic GroupFocus reader stayed silent on them — the player could not
/// tell whether the cursor was on the text field, the phrase list or the sticker
/// list. GroupFocus still handles the content (typed text echo, the opened phrase
/// list, log messages); this only supplies the missing element LABELS, announced
/// once per focus move so it never fights the text echo.
/// </summary>
public class ChatMenuHooks
{
    private const string PARAM_TYPE = "app.UIFlowChat.Menu.Param";
    private const string CHAT_GUI = "BattleHubChatMenu";

    // Localized labels for the chat input-bar elements (icons, no in-game text).
    // Indexed [lang][slot]; slots: 0 Message, 1 Send, 2 Phrases, 3 Stickers.
    private static readonly string[][] Labels =
    {
        new[] { "Message", "Send", "Phrases", "Stickers" },      // En
        new[] { "Mensaje", "Enviar", "Frases", "Stickers" },     // Es
        new[] { "Mensagem", "Enviar", "Frases", "Stickers" },    // Pt
    };

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 15;
    private const int POLL_READ_INTERVAL = 5;

    private static bool _active;
    private static ManagedObject _param;
    private static ManagedObject _rootGroup;
    private static ManagedObject _inputGroup;
    private static ManagedObject _buttonsGroup;
    private static string _lastFocusKey;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] ChatMenuHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var param = FlowHelper.FindFlowParam(PARAM_TYPE);
            if (param != null && !_active)
            {
                _active = true;
                _param = param;
                _rootGroup = FlowHelper.GetObjectField(param, "RootGroup");
                _inputGroup = FlowHelper.GetObjectField(param, "InputGroup");
                _buttonsGroup = FlowHelper.GetObjectField(param, "ButtonsGroup");
                _lastFocusKey = null;
                AnnounceOpen();
            }
            else if (param == null && _active)
            {
                _active = false;
                _param = _rootGroup = _inputGroup = _buttonsGroup = null;
                _lastFocusKey = null;
            }
        }

        if (_active && _pollCounter % POLL_READ_INTERVAL == 0)
            PollInputBarFocus();
    }

    /// <summary>Speak the chat header + "To:" destination from the window's GUI.</summary>
    private static void AnnounceOpen()
    {
        try
        {
            string tab = null, dest = null;
            foreach (var (owner, view) in GuiTextReader.FindGuiViews(CHAT_GUI))
            {
                foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
                {
                    if (string.IsNullOrWhiteSpace(t.Text)) continue;
                    string s = t.Text.Replace('\n', ' ').Trim();
                    if (t.Name == "e_text_tab" && tab == null) tab = s;
                    else if (s.StartsWith("To:") && dest == null) dest = s;
                }
            }

            var parts = new List<string> { "Chat" };
            if (!string.IsNullOrEmpty(tab) && (dest == null || dest.IndexOf(tab, System.StringComparison.OrdinalIgnoreCase) < 0))
                parts.Add(tab);
            if (!string.IsNullOrEmpty(dest)) parts.Add(dest);

            string text = string.Join(". ", parts);
            API.LogInfo($"[SF6Access] Chat opened: {text}");
            ScreenReaderService.Speak(text);
        }
        catch { }
    }

    /// <summary>
    /// Announce the label of the focused input-bar element on a focus move. The
    /// RootGroup focus picks the section (Log / InputGroup / ButtonsGroup); the
    /// section's own focus picks the leaf (TextInput/SendButton, Phrases/Stickers).
    /// Log focus is left to GroupFocus (it reads the messages).
    /// </summary>
    private static void PollInputBarFocus()
    {
        if (_rootGroup == null) return;
        try
        {
            int root = FlowHelper.ReadIntField(_rootGroup, "_FocusIndex");
            int slot = -1;
            switch (root)
            {
                case 1: // InputGroup: TextInput / SendButton
                    slot = FlowHelper.ReadIntField(_inputGroup, "_FocusIndex") == 1 ? 1 : 0;
                    break;
                case 2: // ButtonsGroup: FixedPhraseList / StampList
                    slot = FlowHelper.ReadIntField(_buttonsGroup, "_FocusIndex") == 1 ? 3 : 2;
                    break;
                default:
                    return; // Log (or none): GroupFocus reads the messages
            }

            string key = $"{root}:{slot}";
            if (key == _lastFocusKey) return;
            _lastFocusKey = key;

            string label = Labels[(int)FlowHelper.GetDisplayLang()][slot];
            API.LogInfo($"[SF6Access] Chat input focus: {label}");
            ScreenReaderService.Speak(label);
        }
        catch { }
    }
}
