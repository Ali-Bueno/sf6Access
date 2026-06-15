using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the text-entry dialogs (GUI "TextInputDialogFG") used to search rooms
/// by player name or user code — "Buscar por nome de jogador" / "por código de
/// usuário". They are GUI overlays with no flow Param of their own (so F8
/// auto-dump never captured them), and were completely silent on entry. Reads
/// the title, prompt, any text typed so far, and the buttons when the dialog
/// appears or its content changes.
/// </summary>
public class TextInputDialogHooks
{
    private const string DIALOG_GUI = "TextInputDialog"; // matches TextInputDialogFG too

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 20;
    private const int POLL_READ_INTERVAL = 6;

    private static readonly List<(string owner, ManagedObject view)> _views = new();
    private static string _lastText;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] TextInputDialogHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_views.Count == 0 ? _pollCounter % 30 == 0 : _pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            _views.Clear();
            _views.AddRange(GuiTextReader.FindGuiViews(DIALOG_GUI));
            if (_views.Count == 0) _lastText = null;
        }

        if (_views.Count == 0 || _pollCounter % POLL_READ_INTERVAL != 0) return;
        PollDialog();
    }

    private static void PollDialog()
    {
        try
        {
            string title = null, message = null, input = null;
            var buttons = new List<string>();

            foreach (var (owner, view) in _views)
            {
                foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
                {
                    if (string.IsNullOrWhiteSpace(t.Text)) continue;
                    string s = t.Text.Replace('\n', ' ').Trim();
                    switch (t.Name)
                    {
                        case "e_text_title": title ??= s; break;
                        case "e_text_message": message ??= s; break;
                        // The characters typed so far (name/code being entered)
                        case "e_text_input":
                        case "e_text_name":
                        case "e_text_edit": input ??= s; break;
                        case "e_text": if (!buttons.Contains(s)) buttons.Add(s); break;
                    }
                }
            }

            var parts = new List<string>();
            if (title != null) parts.Add(title);
            if (message != null) parts.Add(message);
            if (!string.IsNullOrEmpty(input)) parts.Add(input);
            parts.AddRange(buttons);
            if (parts.Count == 0) return;

            string text = string.Join(". ", parts);
            if (text == _lastText) return;
            _lastText = text;

            API.LogInfo($"[SF6Access] Text input dialog: {text}");
            ScreenReaderService.Speak(text, interrupt: false);
        }
        catch { }
    }
}
