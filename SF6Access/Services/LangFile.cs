using System.Collections.Generic;
using REFrameworkNET;

namespace SF6Access.Services;

/// <summary>
/// Loads the mod's translation files: one "key=text" UTF-8 file per game
/// language in &lt;game&gt;\reframework\plugins\managed\SF6Access.lang\
/// (es.txt, ja.txt, … — named after FlowHelper.UiLang, lowercased). Lookup
/// order: current language's file → en.txt → the in-code English default, so
/// a missing file or key can never silence an announcement, and translators
/// can fix any wording without recompiling the mod. Files only need the keys
/// that DIFFER from English. The current language is re-checked every few
/// seconds (same cadence as the command vocabulary).
/// </summary>
public static class LangFile
{
    private const string LANG_DIR = "SF6Access.lang";
    private const long RECHECK_MS = 10000;

    private static Dictionary<string, string> _current;
    private static Dictionary<string, string> _english;
    private static FlowHelper.UiLang _loadedLang = (FlowHelper.UiLang)(-1);
    private static long _checkedTick;
    private static string _dir;
    private static bool _dirResolved;

    /// <summary>Translated text for a key, with the given English default.</summary>
    public static string Get(string key, string fallback)
    {
        try
        {
            Ensure();
            if (_current != null && _current.TryGetValue(key, out var v)) return v;
            if (_english != null && _english.TryGetValue(key, out var e)) return e;
        }
        catch { }
        return fallback;
    }

    private static void Ensure()
    {
        long now = System.Environment.TickCount64;
        if (_loadedLang != (FlowHelper.UiLang)(-1) && now - _checkedTick < RECHECK_MS) return;
        _checkedTick = now;

        var lang = FlowHelper.GetDisplayLang();
        if (lang == _loadedLang) return;
        _loadedLang = lang;

        _english ??= Load(FlowHelper.UiLang.En);
        _current = lang == FlowHelper.UiLang.En ? _english : Load(lang);
        API.LogInfo($"[SF6Access] Language file loaded: {lang} ({_current?.Count ?? 0} entries)");
    }

    private static Dictionary<string, string> Load(FlowHelper.UiLang lang)
    {
        try
        {
            if (!_dirResolved)
            {
                _dirResolved = true;
                string gameDir = System.IO.Path.GetDirectoryName(System.Environment.ProcessPath);
                if (gameDir != null)
                    _dir = System.IO.Path.Combine(gameDir, "reframework", "plugins", "managed", LANG_DIR);
            }
            if (_dir == null) return null;

            string path = System.IO.Path.Combine(_dir, lang.ToString().ToLowerInvariant() + ".txt");
            if (!System.IO.File.Exists(path)) return null;

            var entries = new Dictionary<string, string>();
            foreach (var line in System.IO.File.ReadAllLines(path, System.Text.Encoding.UTF8))
            {
                string t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                string key = t.Substring(0, eq).Trim();
                string value = t.Substring(eq + 1).Trim();
                if (key.Length > 0 && value.Length > 0) entries[key] = value;
            }
            return entries;
        }
        catch { return null; }
    }
}
