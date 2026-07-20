using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Reads World Tour "novel"-style in-gameplay dialogue
/// (app.worldtour.UIFlowSpTalkNovelMain) — the visual-novel text box shown while
/// exploring / talking to NPCs, distinct from Battle Hub Special Talk subtitles
/// (<see cref="SpTalkHooks"/>) and arcade/cutscene subtitles (ArcadeHooks). None
/// covered this flow, so its lines were silent.
///
/// The line is NOT set through the Param's setMessage/setChoice (those never fire
/// for this path); instead the text lives in the on-screen "MessageWindow" GUI as
/// e_text_conversation (dialogue) + e_text_name (speaker). via.gui.Text holds the
/// FULL string even during the typewriter reveal, so polling it and deduping on the
/// text announces each line exactly once. This is the primary dialogue UI (always
/// shown), so it is NOT gated by the cutscene Subtitles option.
///
/// Branch choices live on the active novel item (UIPartsNovelItem.canSelect() /
/// getChoiceIndex() / ChoiceItems); the list is read on appearance and the focused
/// option as the cursor moves.
/// </summary>
public sealed class SpTalkNovelHooks : SingleParamScreenAdapter
{
    protected override string ParamType => "app.worldtour.UIFlowSpTalkNovelMain.Param";

    // The talk message-window GUI holds the current line.
    private const string MESSAGE_WINDOW = "MessageWindow";
    private const string CONV_ELEMENT = "e_text_conversation";
    private const string NAME_ELEMENT = "e_text_name";

    public SpTalkNovelHooks()
    {
        SearchInterval = 30;
        ReadInterval = 5;
    }

    /// <summary>True while a World Tour dialogue box is on screen — read by the
    /// field tracker so periodic guidance never talks over dialogue lines.</summary>
    public static bool DialogueActive { get; private set; }

    private string _lastLine;
    private string _lastChoiceSig;
    private int _lastChoiceIndex = -1;

    protected override void OnBind()
    {
        ResetState();
        DialogueActive = true;
        API.LogInfo("[SF6Access] WT novel dialogue active");
        Poll();
    }

    protected override void OnExit()
    {
        DialogueActive = false;
        API.LogInfo("[SF6Access] WT novel dialogue ended");
        ResetState();
    }

    private void ResetState()
    {
        _lastLine = null;
        _lastChoiceSig = null;
        _lastChoiceIndex = -1;
    }

    protected override void Poll()
    {
        PollLine();
        PollChoices();
    }

    /// <summary>Announce the current dialogue line (speaker + text) when it changes.</summary>
    private void PollLine()
    {
        string conversation = null, name = null;
        foreach (var t in GuiTextReader.ReadTextsByOwner(MESSAGE_WINDOW))
        {
            // Novel lines wrap across visual rows with embedded newlines; the
            // screen reader stops speaking at a '\n', so a multi-row line was
            // read only up to its first break. Flatten newlines to spaces (as
            // GuideTextHooks/TutorialHooks already do) to speak the whole line.
            if (t.Name == CONV_ELEMENT && !string.IsNullOrWhiteSpace(t.Text)) conversation = Flatten(t.Text);
            else if (t.Name == NAME_ELEMENT && !string.IsNullOrWhiteSpace(t.Text)) name = Flatten(t.Text);
        }
        if (string.IsNullOrEmpty(conversation) || conversation == _lastLine) return;
        _lastLine = conversation;

        string announcement = string.IsNullOrEmpty(name) ? conversation : $"{name}: {conversation}";
        API.LogInfo($"[SF6Access] Novel: {announcement}");
        Speak(announcement);
    }

    /// <summary>Announce branch choices: the whole list on appearance, the focused option as it moves.</summary>
    private void PollChoices()
    {
        var item = FindActiveChoiceItem();
        if (item == null) { _lastChoiceSig = null; _lastChoiceIndex = -1; return; }

        var labels = ReadChoiceLabels(item);
        if (labels == null || labels.Count == 0) return;

        int idx = FlowHelper.CallInt(item, "getChoiceIndex");
        string sig = string.Join("|", labels);

        // New set of choices → read the full list and baseline the cursor.
        if (sig != _lastChoiceSig)
        {
            _lastChoiceSig = sig;
            _lastChoiceIndex = idx;
            API.LogInfo($"[SF6Access] Novel choices: {string.Join(". ", labels)}");
            Speak(string.Join(". ", labels), interrupt: false);
            return;
        }

        // Cursor moved → read the focused option.
        if (idx >= 0 && idx < labels.Count && idx != _lastChoiceIndex)
        {
            _lastChoiceIndex = idx;
            API.LogInfo($"[SF6Access] Novel choice [{idx}]: {labels[idx]}");
            Speak(labels[idx]);
        }
    }

    /// <summary>The novel text item currently holding selectable choices, or null.</summary>
    private ManagedObject FindActiveChoiceItem()
    {
        var list = FlowHelper.GetObjectField(Param, "TextItems");
        int n = FlowHelper.GetListCount(list);
        for (int i = 0; i < n; i++)
        {
            var item = FlowHelper.Call(list, "get_Item", i) as ManagedObject;
            if (item == null) continue;
            if (FlowHelper.Call(item, "canSelect") is bool b && b) return item;
        }
        return null;
    }

    /// <summary>The choice labels on a novel item (UIPartsNovelChoiceItem.Text), in order.</summary>
    private static List<string> ReadChoiceLabels(ManagedObject item)
    {
        var choiceList = FlowHelper.GetObjectField(item, "ChoiceItems");
        int n = FlowHelper.GetListCount(choiceList);
        if (n <= 0) return null;
        var labels = new List<string>(n);
        for (int i = 0; i < n; i++)
        {
            var ci = FlowHelper.Call(choiceList, "get_Item", i) as ManagedObject;
            string text = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(ci, "Text"));
            if (!string.IsNullOrWhiteSpace(text)) labels.Add(Flatten(FlowHelper.CleanTags(text)));
        }
        return labels.Count > 0 ? labels : null;
    }

    /// <summary>Collapse the embedded newlines of a wrapped line into single
    /// spaces so the screen reader speaks the whole line, not just the first row.</summary>
    private static string Flatten(string text)
        => string.IsNullOrEmpty(text) ? text
           : System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
}
