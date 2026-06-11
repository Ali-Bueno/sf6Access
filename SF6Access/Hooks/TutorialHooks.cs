using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the in-battle tutorial overlays (Fighting Ground
/// tutorials): app.esports.UI11430 (instructor dialog with _Message00-02),
/// UI11434 (section banner, e.g. "Movement 1: Walking") and
/// FGTutorialBattleAnnounceUI (transient announcements). All carry their text
/// in string "_Message*" fields and/or via.gui.Text fields — read both and
/// announce changes.
/// </summary>
public class TutorialHooks
{
    private static readonly string[] Prefixes =
    {
        "app.esports.UI1143",                  // UI11430 dialog, UI11434 banner, siblings
        "app.esports.FGTutorialBattleAnnounce",
    };

    private static int _pollCounter;
    private const int POLL_INTERVAL = 10;

    // Per-overlay state. Demos type messages out (the text grows each poll)
    // and loop the same message — announce a text only once it's STABLE
    // across two polls. A demo loop re-shows the LAST announced text with
    // nothing announced in between; stepping back/forward through guide pages
    // always announces something else first — so remembering only the last
    // announced text silences loops while keeping navigation readable.
    private sealed class OverlayState
    {
        public string LastSeen;
        public string LastAnnounced;
        public int EmptySince = -1;
    }

    // Loop/step gaps are ~0.5s (verified in logs); a full guide reset passes
    // through banner+intro for several seconds — 3s separates the two
    private static readonly Dictionary<string, OverlayState> _overlays = new();
    private const int EMPTY_FORGET_FRAMES = 180;

    // Cached text-bearing field names per param type
    private sealed class TypeFields
    {
        public List<string> StringFields = new();
        public List<string> TextFields = new();
    }
    private static readonly Dictionary<string, TypeFields> _fieldCache = new();

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] TutorialHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        if (++_pollCounter % POLL_INTERVAL != 0) return;

        var seen = new HashSet<string>();
        foreach (var prefix in Prefixes)
        {
            foreach (var (typeName, param) in FlowHelper.FindFlowParamsByPrefix(prefix))
            {
                // The combo trial recipe panel has its own hook
                if (typeName.Contains("UI11439")) continue;
                if (!seen.Add(typeName)) continue;
                try
                {
                    string text = ReadTutorialText(typeName, param);
                    _overlays.TryGetValue(typeName, out var state);

                    if (string.IsNullOrEmpty(text))
                    {
                        // Forget only after the text has been gone a while:
                        // demo loops blank it for a second between cycles
                        if (state != null)
                        {
                            if (state.EmptySince < 0) state.EmptySince = _pollCounter;
                            else if (_pollCounter - state.EmptySince > EMPTY_FORGET_FRAMES)
                                _overlays.Remove(typeName);
                        }
                        continue;
                    }

                    if (state == null)
                        _overlays[typeName] = state = new OverlayState();
                    state.EmptySince = -1;

                    // Wait for the typewriter to finish: announce only when
                    // the text is identical across two consecutive polls
                    if (text != state.LastSeen)
                    {
                        state.LastSeen = text;
                        continue;
                    }

                    // Suppress only the demo-loop case: the same text (or a
                    // partially re-typed copy of it) coming straight back
                    if (state.LastAnnounced != null &&
                        (state.LastAnnounced == text || state.LastAnnounced.Contains(text)))
                        continue;
                    state.LastAnnounced = text;

                    API.LogInfo($"[SF6Access] Tutorial [{typeName}]: {text}");
                    ScreenReaderService.Speak(text, interrupt: false);
                }
                catch { }
            }
        }

        // A type whose param disappeared entirely counts as empty too —
        // otherwise a full guide reset replays the same first texts and each
        // type's last-announced memory suppresses them. Demo loops only tear
        // overlays down for ~0.5s, well under the forget threshold.
        List<string> gone = null;
        foreach (var kvp in _overlays)
        {
            if (seen.Contains(kvp.Key)) continue;
            var state = kvp.Value;
            if (state.EmptySince < 0) state.EmptySince = _pollCounter;
            else if (_pollCounter - state.EmptySince > EMPTY_FORGET_FRAMES)
                (gone ??= new List<string>()).Add(kvp.Key);
        }
        if (gone != null)
        {
            foreach (var key in gone) _overlays.Remove(key);
        }
    }

    private static string ReadTutorialText(string typeName, ManagedObject param)
    {
        var fields = GetTypeFields(typeName, param);
        var parts = new List<string>();

        foreach (var name in fields.StringFields)
        {
            string raw = FlowHelper.ReadStringField(param, name);
            LogRawTags(raw);
            AddPart(parts, FlowHelper.CleanTags(FlowHelper.SpeakableIcons(raw)));
        }

        // Command displays render separately from the dialog message
        foreach (var name in fields.TextFields)
        {
            if (!name.Contains("Command")) continue;
            var textObj = FlowHelper.GetObjectField(param, name);
            string raw = FlowHelper.Call(textObj, "get_Message") as string;
            LogRawTags(raw);
            AddPart(parts, FlowHelper.CleanTags(FlowHelper.SpeakableIcons(raw)));
        }

        // Fallback to on-screen text components when no message strings are set
        if (parts.Count == 0)
        {
            foreach (var name in fields.TextFields)
                AddPart(parts, FlowHelper.ReadGuiText(FlowHelper.GetObjectField(param, name)));
        }

        return parts.Count > 0 ? string.Join(". ", parts) : null;
    }

    /// <summary>Add a text segment unless an existing one already contains it —
    /// guide overlays expose the full message AND a partially-typed copy of it.</summary>
    private static void AddPart(List<string> parts, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Replace('\n', ' ').Trim();

        foreach (var existing in parts)
        {
            if (existing.Contains(text)) return;
        }
        // A longer copy can also arrive AFTER its partial: replace the partial
        for (int i = 0; i < parts.Count; i++)
        {
            if (text.Contains(parts[i])) { parts[i] = text; return; }
        }
        parts.Add(text);
    }

    // Log each distinct tagged message once: reveals the icon tag format so
    // the SpeakableIcons mapping can be refined from session logs
    private static readonly HashSet<string> _loggedRaw = new();

    private static void LogRawTags(string raw)
    {
        if (string.IsNullOrEmpty(raw) || !raw.Contains('<')) return;
        if (_loggedRaw.Count > 100 || !_loggedRaw.Add(raw)) return;
        API.LogInfo($"[SF6Access] Tutorial raw: {raw}");
    }

    private static TypeFields GetTypeFields(string typeName, ManagedObject param)
    {
        if (_fieldCache.TryGetValue(typeName, out var cached)) return cached;

        var result = new TypeFields();
        try
        {
            var td = param.GetTypeDefinition();
            int depth = 0;
            while (td != null && depth++ < 4)
            {
                var fields = td.GetFields();
                if (fields != null)
                {
                    foreach (var field in fields)
                    {
                        try
                        {
                            string cleanName = field.Name?.Replace("<", "").Replace(">k__BackingField", "");
                            if (string.IsNullOrEmpty(cleanName)) continue;

                            string fieldType = field.Type?.FullName;
                            if (fieldType == "System.String" && cleanName.StartsWith("_Message"))
                                result.StringFields.Add(field.Name);
                            else if (fieldType == "via.gui.Text")
                                result.TextFields.Add(cleanName);
                        }
                        catch { }
                    }
                }
                td = td.ParentType;
            }
            result.StringFields.Sort();
        }
        catch { }

        _fieldCache[typeName] = result;
        return result;
    }
}
