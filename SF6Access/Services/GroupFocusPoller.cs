using REFrameworkNET;

namespace SF6Access.Services;

/// <summary>
/// Polls focus across a fixed set of UIPartsGroup/list sources nested in a
/// flow Param and announces the focused row's GUI text. PartsField == null
/// means the group lives directly on the Param. Left/right value edits on the
/// same row announce only the changed segment. Announcements with more text
/// segments than a real row can carry (a focused child wrapping a whole tab
/// panel) are skipped — nested groups read the actual row.
/// </summary>
public sealed class GroupFocusPoller
{
    public readonly struct Source
    {
        public Source(string partsField, string fieldName, bool isList = false)
        {
            PartsField = partsField;
            FieldName = fieldName;
            IsList = isList;
        }

        public string PartsField { get; }
        public string FieldName { get; }
        public bool IsList { get; }    // SelectedIndex-based instead of _FocusIndex
    }

    // A real settings/menu row never carries more than this many text
    // segments — anything larger is a panel wrap, not a row
    private const int MAX_ROW_SEGMENTS = 6;

    private readonly string _logTag;
    private readonly bool _announceFirst;
    private readonly Source[] _sources;
    private readonly int[] _lastIdx;
    private readonly string[] _lastText;

    /// <summary>Opt-in: skip rows whose every segment is a bare integer.
    /// In the avatar creator a group can wrap the preset grid panel, whose
    /// "row text" is the visible cell numbers ("36. 35. 34. 33. 32. 31") —
    /// the dedicated grid tracker already reads those cells properly.</summary>
    public bool SkipPureNumericRows { get; init; }

    public GroupFocusPoller(string logTag, bool announceFirst, params Source[] sources)
    {
        _logTag = logTag;
        _announceFirst = announceFirst;
        _sources = sources;
        _lastIdx = new int[sources.Length];
        _lastText = new string[sources.Length];
        Reset();
    }

    public void Reset()
    {
        for (int g = 0; g < _sources.Length; g++)
        {
            _lastIdx[g] = -2;
            _lastText[g] = null;
        }
    }

    public void Poll(ManagedObject param)
    {
        if (param == null) return;

        for (int g = 0; g < _sources.Length; g++)
        {
            try
            {
                var src = _sources[g];

                // Re-resolve each tick: tab parts initialize after the Param appears
                var owner = src.PartsField == null
                    ? param
                    : FlowHelper.GetObjectField(param, src.PartsField);
                var obj = FlowHelper.GetObjectField(owner, src.FieldName);
                if (obj == null) continue;

                int idx = src.IsList
                    ? FlowHelper.CallInt(obj, "get_SelectedIndex")
                    : FlowHelper.ReadIntField(obj, "_FocusIndex");
                if (idx < 0) continue;

                string text = src.IsList
                    ? FlowHelper.ReadSelectedItemText(obj)
                    : ReadFocusedRowText(obj, idx);

                bool first = _lastIdx[g] == -2;
                bool indexChanged = idx != _lastIdx[g];
                bool textChanged = !string.IsNullOrEmpty(text) && text != _lastText[g];
                string previousText = _lastText[g];

                _lastIdx[g] = idx;
                if (!string.IsNullOrEmpty(text)) _lastText[g] = text;

                if (string.IsNullOrEmpty(text)) continue;
                if (first)
                {
                    // Announce the initially-focused row (queued, after the
                    // screen title) — but only the field that holds focus
                    if (!_announceFirst || !IsPartsFocused(obj)) continue;
                    Announce(g, idx, text, interrupt: false);
                    continue;
                }
                if (!indexChanged && !textChanged) continue;

                // Same row, value edited with left/right: only the new value
                string announcement = !indexChanged
                    ? FlowHelper.DiffSegments(previousText, text)
                    : text;

                bool announced = Announce(g, idx, announcement, interrupt: true);

                // Switching tab must re-read the content row even when its
                // text is unchanged — make the other sources announce again
                if (announced && indexChanged && src.FieldName.Contains("Tab"))
                    ResetOtherSources(g);
            }
            catch { }
        }
    }

    private bool Announce(int g, int idx, string announcement, bool interrupt)
    {
        var parts = announcement.Split(
            new[] { ". " }, System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > MAX_ROW_SEGMENTS)
        {
            API.LogInfo($"[SF6Access] {_logTag} focus [g{g},{idx}] skipped (panel, {parts.Length} segments)");
            return false;
        }

        if (SkipPureNumericRows)
        {
            bool allNumeric = true;
            foreach (var p in parts)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(p.Trim().TrimEnd('.'), @"^\d+$"))
                { allNumeric = false; break; }
            }
            if (allNumeric)
            {
                API.LogInfo($"[SF6Access] {_logTag} focus [g{g},{idx}] skipped (numeric panel)");
                return false;
            }
        }

        API.LogInfo($"[SF6Access] {_logTag} focus [g{g},{idx}]: {announcement}");
        ScreenReaderService.Speak(announcement, interrupt);
        return true;
    }

    private void ResetOtherSources(int current)
    {
        for (int g = 0; g < _sources.Length; g++)
        {
            if (g == current || _sources[g].FieldName.Contains("Tab")) continue;
            _lastIdx[g] = -2;
            _lastText[g] = null;
        }
    }

    private static string ReadFocusedRowText(ManagedObject group, int idx)
    {
        // GetFocusChild is authoritative — _Children order can be reversed
        // relative to the focus index
        var child = FlowHelper.Call(group, "GetFocusChild") as ManagedObject;
        if (child == null)
        {
            var children = FlowHelper.GetObjectField(group, "_Children");
            child = FlowHelper.GetListItem(children, idx);
        }
        var control = FlowHelper.GetObjectField(child, "Control")
            ?? FlowHelper.Call(child, "get_Control") as ManagedObject;
        return GuiTextReader.ReadControlTextJoined(control);
    }

    /// <summary>True when the UIParts group/list reports holding input focus.</summary>
    private static bool IsPartsFocused(ManagedObject partsObj)
    {
        var result = FlowHelper.Call(partsObj, "get_IsFocus");
        if (result is bool b) return b;
        return FlowHelper.ReadBoolField(partsObj, "_IsFocus");
    }
}
