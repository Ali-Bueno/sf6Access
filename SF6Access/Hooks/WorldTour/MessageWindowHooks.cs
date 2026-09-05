using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// Reads the dialogue box itself — whatever opened it.
///
/// <para><b>Why this exists instead of a fourth per-system reader.</b> Chasing
/// dialogue by its owning flow kept losing cases: story scenes go through
/// <c>UIFlowSpTalkNovelMain</c>, staged talks through <c>SpTalkCtrl</c>, casual
/// chat through <c>WTContactSystem</c> — and a shopkeeper turned out to be none
/// of those, while still drawing its line in the same
/// <c>MessageWindow</c> widget (identified by the "Transcripción" prompt sitting
/// under it). Each new source meant another silent case and another test round.
/// So this reader keys on the WINDOW, which is the one thing every case has in
/// common, and does not care which system put the text there.</para>
///
/// <para>It stands down while <see cref="SF6Access.Hooks.SpTalkNovelHooks"/> is
/// bound: that reader owns the novel path and also handles branch choices, which
/// are not visible in this widget's text.</para>
/// </summary>
public sealed class MessageWindowHooks : ScreenAdapter
{
    private const string MESSAGE_WINDOW = "MessageWindow";
    private const string CONV_ELEMENT = "e_text_conversation";
    private const string NAME_ELEMENT = "e_text_name";

    // No flow param of its own — this reader is defined by a GUI widget, which is
    // the entire point of it.
    public override string[] OwnedTypes => System.Array.Empty<string>();

    public MessageWindowHooks()
    {
        // Locate() walks the GUI tree, so it is not free. A dialogue box stays up
        // for seconds; half a second to notice it is fine.
        SearchInterval = 30;
        ReadInterval = 5;
    }

    // A dialogue box left "open" forever is not a cosmetic bug: this is a
    // ScreenAdapter, so while it is Active the whole mod treats a menu as owning
    // the screen and every World Tour field reader goes quiet. That is exactly
    // what happened after a battle — the widget kept its last line, this adapter
    // never deactivated, and NPCs stopped being announced entirely.
    //
    // So the window counts as open only while its text is still MOVING. Dialogue
    // advances every few seconds; text frozen for this long is a leftover, not a
    // conversation.
    private const long STALE_MS = 10000;

    private string _lastLine;
    private string _seenText;
    private long _seenAt;

    protected override bool Locate()
    {
        string text = ReadLine().text;
        if (string.IsNullOrEmpty(text))
        {
            _seenText = null;
            return false;
        }

        long now = System.Environment.TickCount64;
        if (text != _seenText)
        {
            _seenText = text;
            _seenAt = now;
            return true;
        }
        return now - _seenAt < STALE_MS;
    }

    protected override void OnActivate()
    {
        _lastLine = null;
        API.LogInfo("[SF6Access] Dialogue window open");
        OnPoll();
    }

    protected override void OnDeactivate()
    {
        _lastLine = null;
        API.LogInfo("[SF6Access] Dialogue window closed");
    }

    protected override void OnPoll()
    {
        // The novel reader owns its own path, choices included; two readers on one
        // window would double-speak every line.
        if (SF6Access.Hooks.SpTalkNovelHooks.DialogueActive) return;

        var (text, speaker) = ReadLine();
        if (string.IsNullOrEmpty(text) || text == _lastLine) return;
        _lastLine = text;

        string announcement = string.IsNullOrEmpty(speaker) ? text : $"{speaker}: {text}";
        API.LogInfo($"[SF6Access] Dialogue: {announcement}");
        Speak(announcement);
    }

    /// <summary>The line currently in the window, and who is saying it.</summary>
    private static (string text, string speaker) ReadLine()
    {
        string text = null, speaker = null;
        try
        {
            foreach (var t in GuiTextReader.ReadTextsByOwner(MESSAGE_WINDOW))
            {
                if (t.Name == CONV_ELEMENT && !string.IsNullOrWhiteSpace(t.Text)) text = Flatten(t.Text);
                else if (t.Name == NAME_ELEMENT && !string.IsNullOrWhiteSpace(t.Text)) speaker = Flatten(t.Text);
            }
        }
        catch { }
        return (text, speaker);
    }

    /// <summary>Dialogue wraps across visual rows with embedded newlines, and the
    /// screen reader stops speaking at one — so a multi-row line would be read
    /// only as far as its first break.</summary>
    private static string Flatten(string raw)
    {
        string clean = FlowHelper.CleanTags(raw);
        return string.IsNullOrEmpty(clean) ? null : clean.Replace('\n', ' ').Replace('\r', ' ').Trim();
    }
}
