using REFrameworkNET;

namespace SF6Access.Services.Ui;

/// <summary>
/// Watches a tab list's selected index and announces the tab's on-screen label
/// when it changes — the Tab bar archetype (read only the tab name on switch).
/// Seeds silently on the first read; falls back to "Tab N" when the label can't
/// be read from the list's Control subtree.
/// </summary>
public sealed class TabWatcher
{
    private readonly string _logTag;
    private ManagedObject _tabList;
    private int _lastIndex = -1;

    public TabWatcher(string logTag)
    {
        _logTag = logTag;
    }

    public void Bind(ManagedObject tabList)
    {
        _tabList = tabList;
        _lastIndex = -1;
    }

    public void Reset()
    {
        _tabList = null;
        _lastIndex = -1;
    }

    /// <summary>Returns the tab index when it changed (and was announced), else -1.</summary>
    public int Poll()
    {
        if (_tabList == null) return -1;

        int idx = FlowHelper.CallInt(_tabList, "get_SelectedIndex");
        if (idx < 0 || idx == _lastIndex) return -1;

        bool first = _lastIndex == -1;
        _lastIndex = idx;
        if (first) return -1;

        string label = ReadTabLabel(idx) ?? $"Tab {idx + 1}";
        API.LogInfo($"[SF6Access] {_logTag} tab [{idx}]: {label}");
        ScreenReaderService.Speak(label);
        return idx;
    }

    private string ReadTabLabel(int idx)
    {
        try
        {
            var control = FlowHelper.GetObjectField(_tabList, "Control")
                ?? FlowHelper.Call(_tabList, "get_Control") as ManagedObject;
            var texts = GuiTextReader.ReadControlTexts(control);
            if (idx >= 0 && idx < texts.Count) return texts[idx].Text;
        }
        catch { }
        return null;
    }
}
