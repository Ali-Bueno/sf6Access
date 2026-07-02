namespace SF6Access.Services.Ui;

/// <summary>
/// The recurring "should I speak this focused row, and what exactly" decision,
/// extracted from the ~two dozen hooks that hand-rolled it. Tracks the last
/// (index, text) it saw and returns the string to announce — or null to stay
/// silent — applying the menu-navigation contract:
///  - first read is suppressed (screens announce their title separately),
///  - an unchanged row stays silent,
///  - moving to another row speaks the whole row,
///  - editing the same row's value speaks only the changed segment (DiffSegments).
/// </summary>
public sealed class ChangeGate
{
    private int _lastIndex = -2;
    private string _lastText;

    /// <summary>When true, the initially-focused row is announced instead of
    /// suppressed. Most screens read their title first and leave this false.</summary>
    public bool AnnounceFirst { get; set; }

    public void Reset()
    {
        _lastIndex = -2;
        _lastText = null;
    }

    /// <summary>
    /// Evaluate a newly focused row; returns the text to speak, or null to stay
    /// silent. <paramref name="index"/> separates moving to another row (speak
    /// the whole row) from editing the same row's value (speak only the diff).
    /// When a screen has no meaningful index, pass a constant so every change
    /// reads as a same-row value edit.
    /// </summary>
    public string Evaluate(int index, string text)
    {
        bool first = _lastIndex == -2;
        bool indexChanged = index != _lastIndex;
        bool textChanged = !string.IsNullOrEmpty(text) && text != _lastText;
        string previous = _lastText;

        _lastIndex = index;
        if (!string.IsNullOrEmpty(text)) _lastText = text;

        if (string.IsNullOrEmpty(text)) return null;
        if (first) return AnnounceFirst ? text : null;
        if (!indexChanged && !textChanged) return null;

        return indexChanged ? text : FlowHelper.DiffSegments(previous, text);
    }

    /// <summary>Evaluate and speak in one call. Returns true when it spoke.</summary>
    public bool Announce(int index, string text, bool interrupt = true)
    {
        string toSpeak = Evaluate(index, text);
        if (toSpeak == null) return false;
        ScreenReaderService.Speak(toSpeak, interrupt);
        return true;
    }
}
