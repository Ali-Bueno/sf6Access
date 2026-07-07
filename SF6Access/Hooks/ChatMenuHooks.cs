using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Announces the Battle Hub text-chat window (app.UIFlowChat.Menu.Param, opened
/// with T): the window header on open, and the focused input-bar element as you
/// move along it. The bar's elements are icon buttons with no readable text, so
/// the generic GroupFocus reader stayed silent on them — the player could not
/// tell whether the cursor was on the text field, the phrase list or the sticker
/// list. GroupFocus still handles the content (typed text echo, the opened phrase
/// list, log messages); this only supplies the missing element LABELS, announced
/// once per focus move so it never fights the text echo. Migrated to ScreenAdapter.
/// </summary>
public sealed class ChatMenuHooks : SingleParamScreenAdapter
{
    private const string CHAT_GUI = "BattleHubChatMenu";

    protected override string ParamType => "app.UIFlowChat.Menu.Param";

    public ChatMenuHooks()
    {
        SearchInterval = 15;
        ReadInterval = 5;
    }

    private ManagedObject _rootGroup;
    private ManagedObject _inputGroup;
    private ManagedObject _buttonsGroup;
    private string _lastFocusKey;

    protected override void OnBind()
    {
        _rootGroup = FlowHelper.GetObjectField(Param, "RootGroup");
        _inputGroup = FlowHelper.GetObjectField(Param, "InputGroup");
        _buttonsGroup = FlowHelper.GetObjectField(Param, "ButtonsGroup");
        _lastFocusKey = null;
        AnnounceOpen();
    }

    protected override void OnExit()
    {
        _rootGroup = _inputGroup = _buttonsGroup = null;
        _lastFocusKey = null;
    }

    protected override void Poll() => PollInputBarFocus();

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
    private void PollInputBarFocus()
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

            // Chat input-bar element labels are icons (no in-game text)
            string label = LocalizedText.ChatSlot(slot);
            API.LogInfo($"[SF6Access] Chat input focus: {label}");
            ScreenReaderService.Speak(label);
        }
        catch { }
    }
}
