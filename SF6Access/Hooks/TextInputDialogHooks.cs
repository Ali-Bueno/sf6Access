using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the text-entry dialogs (UITextInputDialog, GUI "TextInputDialogFG")
/// used to search rooms by player name or user code. They are GUI overlays with
/// no flow Param, so:
/// - the static OpenTextInputDialog is hooked to capture the live dialog, whose
///   MainGroup._FocusIndex tells when the NAME FIELD (vs the buttons) is focused
///   and whose TextInputElement holds the typed text;
/// - on appearance the title + prompt + buttons are announced ONCE;
/// - moving onto the field announces it (prompt + current text); typing
///   announces only the new text (not the whole dialog again);
/// - the Cancelar / Buscar buttons are announced by MainMenuHooks (which stops
///   suppressing focus while IsActive is true).
/// </summary>
public class TextInputDialogHooks
{
    private const string DIALOG_GUI = "TextInputDialog"; // matches TextInputDialogFG too

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 20;
    private const int POLL_READ_INTERVAL = 4;

    private static readonly List<(string owner, ManagedObject view)> _views = new();
    private static bool _active;
    private static bool _announced;

    private static ManagedObject _dialog;          // captured UITextInputDialog
    private static int _fieldFocusIndex = int.MinValue; // MainGroup focus of the field (default focus)
    private static int _lastFocusIndex = int.MinValue;
    private static string _lastTyped = "";

    public static bool IsActive => _active;

    [PluginEntryPoint]
    public static void Initialize()
    {
        try
        {
            var td = TDB.Get().FindType("app.UITextInputDialog");
            var method = td?.GetMethod("OpenTextInputDialog");
            if (method != null)
            {
                var hook = method.AddHook(false);
                hook.AddPost((ref ulong retval) =>
                {
                    try { _dialog = ManagedObject.ToManagedObject(retval); ResetDialogState(); }
                    catch { }
                });
                API.LogInfo("[SF6Access] TextInputDialogHooks: OpenTextInputDialog hook installed");
            }
            else
            {
                API.LogInfo("[SF6Access] OpenTextInputDialog not found; field focus/typed text unavailable");
            }
        }
        catch (System.Exception ex)
        {
            API.LogError($"[SF6Access] TextInputDialogHooks init failed: {ex.Message}");
        }
    }

    private static void ResetDialogState()
    {
        _fieldFocusIndex = int.MinValue;
        _lastFocusIndex = int.MinValue;
        _lastTyped = "";
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_views.Count == 0 ? _pollCounter % 30 == 0 : _pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            _views.Clear();
            _views.AddRange(GuiTextReader.FindGuiViews(DIALOG_GUI));
            if (_views.Count == 0)
            {
                _active = false;
                _announced = false;
                _dialog = null;
                ResetDialogState();
            }
        }

        if (_views.Count == 0 || _pollCounter % POLL_READ_INTERVAL != 0) return;

        AnnounceAppearanceOnce();
        PollFieldAndTyping();
    }

    /// <summary>Title + prompt + buttons, once, when the dialog shows up.</summary>
    private static void AnnounceAppearanceOnce()
    {
        if (_announced) { _active = true; return; }
        try
        {
            string title = null, message = null;
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
                        case "e_text": if (!buttons.Contains(s)) buttons.Add(s); break;
                    }
                }
            }

            var parts = new List<string>();
            if (title != null) parts.Add(title);
            if (message != null) parts.Add(message);
            parts.AddRange(buttons);
            if (parts.Count == 0) return;

            _active = true;
            _announced = true;
            string text = string.Join(". ", parts);
            API.LogInfo($"[SF6Access] Text input dialog: {text}");
            ScreenReaderService.Speak(text, interrupt: false);
        }
        catch { }
    }

    /// <summary>
    /// Announce the name field when focus lands on it, and the typed text as the
    /// user types — using the captured UITextInputDialog (MainGroup focus index
    /// + TextInputElement). The buttons are left to MainMenuHooks.
    /// </summary>
    private static void PollFieldAndTyping()
    {
        if (_dialog == null) return;
        try
        {
            var mainGroup = FlowHelper.GetObjectField(_dialog, "MainGroup");
            int focus = FlowHelper.ReadIntField(mainGroup, "_FocusIndex", int.MinValue);
            string typed = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(_dialog, "TextInputElement")) ?? "";

            // The dialog opens with the field focused, so its index is the
            // baseline that identifies "on the field" later.
            if (_fieldFocusIndex == int.MinValue && focus != int.MinValue)
            {
                _fieldFocusIndex = focus;
                _lastFocusIndex = focus;
                _lastTyped = typed;
                return; // appearance announce already covered the field
            }

            bool onField = focus == _fieldFocusIndex;

            // Moved onto the field from a button: announce it (typed text, or the
            // prompt so the user knows what to enter).
            if (focus != _lastFocusIndex)
            {
                _lastFocusIndex = focus;
                if (onField)
                {
                    string label = !string.IsNullOrEmpty(typed) ? typed : FieldPrompt();
                    if (!string.IsNullOrEmpty(label))
                    {
                        API.LogInfo($"[SF6Access] Name field: {label}");
                        ScreenReaderService.Speak(label);
                    }
                }
                _lastTyped = typed;
                return;
            }

            // Typing: announce only what changed, not the whole dialog.
            if (onField && typed != _lastTyped)
            {
                _lastTyped = typed;
                string spoken = string.IsNullOrEmpty(typed) ? FieldEmpty() : typed;
                API.LogInfo($"[SF6Access] Name field typed: {spoken}");
                ScreenReaderService.Speak(spoken);
            }
        }
        catch { }
    }

    /// <summary>The dialog's prompt text ("Informe um nome de jogador...").</summary>
    private static string FieldPrompt()
    {
        foreach (var (owner, view) in _views)
            foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
                if (t.Name == "e_text_message" && !string.IsNullOrWhiteSpace(t.Text))
                    return t.Text.Replace('\n', ' ').Trim();
        return null;
    }

    private static string FieldEmpty() => FieldPrompt() ?? "";
}
