using System.Collections.Generic;
using REFrameworkNET;

namespace SF6Access.Services.Ui;

/// <summary>
/// Watches a fixed set of on-screen via.gui.Text fields on a flow Param and
/// announces each one when its value changes — the Slider / Checkbox / Dropdown
/// archetype, where the control pushes its value into a label the game shows.
/// Reads only the changed value (horizontal-navigation rule), seeds the baseline
/// silently on bind, and ignores unloaded placeholder values ("---").
/// </summary>
public sealed class ValueTextWatcher
{
    private readonly string _logTag;
    private readonly string[] _fields;
    private readonly Dictionary<string, ManagedObject> _texts = new();
    private readonly Dictionary<string, string> _last = new();

    public ValueTextWatcher(string logTag, params string[] fields)
    {
        _logTag = logTag;
        _fields = fields;
    }

    /// <summary>True for unloaded placeholder values like "--- PL".</summary>
    public static bool IsPlaceholder(string text) =>
        string.IsNullOrEmpty(text) || text.Contains("--");

    /// <summary>Re-resolve the text objects from a (possibly recreated) Param and
    /// seed each field's baseline value without announcing.</summary>
    public void Bind(ManagedObject param)
    {
        _texts.Clear();
        _last.Clear();
        if (param == null) return;

        foreach (var field in _fields)
        {
            var text = FlowHelper.GetObjectField(param, field);
            if (text == null) continue;
            _texts[field] = text;
            _last[field] = FlowHelper.ReadGuiText(text);
        }
    }

    public void Reset()
    {
        _texts.Clear();
        _last.Clear();
    }

    public void Poll()
    {
        foreach (var pair in _texts)
        {
            string value = FlowHelper.ReadGuiText(pair.Value);
            if (string.IsNullOrEmpty(value)) continue;

            _last.TryGetValue(pair.Key, out var last);
            if (value == last) continue;
            _last[pair.Key] = value;

            if (last == null) continue;        // first read — baseline only
            if (IsPlaceholder(value)) continue;

            API.LogInfo($"[SF6Access] {_logTag} {pair.Key}: {value}");
            ScreenReaderService.Speak(value);
        }
    }

    /// <summary>Join the current values of the given watched fields (skipping
    /// placeholders) into one string — e.g. league info announced on entry.
    /// Returns null when nothing readable is present.</summary>
    public string Compose(params string[] fields)
    {
        var parts = new List<string>();
        foreach (var field in fields)
        {
            _texts.TryGetValue(field, out var text);
            string value = FlowHelper.ReadGuiText(text);
            if (!IsPlaceholder(value)) parts.Add(value);
        }
        return parts.Count > 0 ? string.Join(". ", parts) : null;
    }
}
