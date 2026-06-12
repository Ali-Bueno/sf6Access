using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the fighter profile (app.UICFNFightersProfile* flows).
/// Profile tabs are pure info panels: their params expose via.gui.Text fields
/// (character name, play time, league points...) but no navigable lists, so
/// each tab's texts are read out when the tab flow appears or finishes loading.
/// </summary>
public class ProfileHooks
{
    private const string TYPE_PREFIX = "app.UICFNFightersProfileTab";

    private static int _pollCounter;
    private const int POLL_INTERVAL = 30;
    private const int MAX_TEXTS = 30;

    private static string _activeType;
    private static string _lastAnnounced;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] ProfileHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        if (++_pollCounter % POLL_INTERVAL != 0) return;

        // Child tab flows come and go as the user switches profile tabs;
        // the most specific (newest) one describes the visible panel
        var matches = FlowHelper.FindFlowParamsByPrefix(TYPE_PREFIX);
        if (matches.Count == 0)
        {
            if (_activeType != null)
            {
                API.LogInfo("[SF6Access] Profile ended");
                _activeType = null;
                _lastAnnounced = null;
            }
            return;
        }

        var (typeName, param) = matches[0];

        // Wait until the panel finished loading its API data when it says so
        var setuped = FlowHelper.Call(param, "get_IsSetuped");
        if (setuped is bool b && !b) return;

        string text = ReadPanelTexts(param, typeName);
        if (string.IsNullOrEmpty(text)) return;

        // Announce on tab change or when the panel content changes
        if (typeName == _activeType && text == _lastAnnounced) return;
        _activeType = typeName;
        _lastAnnounced = text;

        API.LogInfo($"[SF6Access] Profile panel [{typeName}]: {text}");
        ScreenReaderService.Speak(text, interrupt: false);
    }

    /// <summary>
    /// Read all via.gui.Text fields declared on the param type. The labels on
    /// screen are rendered as images, so each value is prefixed with a label
    /// derived from its field name (_LeaguePointText → "League Point") —
    /// bare numbers like "10060" were meaningless otherwise.
    /// </summary>
    private static string ReadPanelTexts(ManagedObject param, string typeName)
    {
        var parts = new List<string>();
        try
        {
            var td = param.GetTypeDefinition();
            int depth = 0;
            while (td != null && depth++ < 4 && parts.Count < MAX_TEXTS)
            {
                var fields = td.GetFields();
                if (fields != null)
                {
                    foreach (var field in fields)
                    {
                        try
                        {
                            if (field.Type?.FullName != "via.gui.Text") continue;
                            var textObj = param.GetField(field.Name) as ManagedObject;
                            string text = FlowHelper.ReadGuiText(textObj);
                            if (string.IsNullOrWhiteSpace(text)) continue;

                            string label = HumanizeFieldName(field.Name);
                            parts.Add(label != null ? $"{label}: {text.Trim()}" : text.Trim());
                            if (parts.Count >= MAX_TEXTS) break;
                        }
                        catch { }
                    }
                }
                td = td.ParentType;
            }

            // The play tab also shows the favorite modes with their hours —
            // they only exist as GUI texts, paired num + mode name
            if (typeName.Contains("TabPlay"))
                AppendFavoriteModes(parts);
        }
        catch { }
        return parts.Count > 0 ? string.Join(". ", parts) : null;
    }

    /// <summary>"_LeaguePointText" → "League Point"; null when nothing remains.</summary>
    private static string HumanizeFieldName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        name = name.TrimStart('_');
        if (name.EndsWith("Text")) name = name.Substring(0, name.Length - 4);
        if (name.Length == 0) return null;
        return System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");
    }

    private static void AppendFavoriteModes(List<string> parts)
    {
        try
        {
            foreach (var (owner, view) in GuiTextReader.FindGuiViews("CFNFighterProfileChildPlayTab"))
            {
                string pendingTime = null;
                foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
                {
                    if (t.Name == "e_text_favorite_mode_num")
                        pendingTime = t.Text?.Trim();
                    else if (t.Name == "e_text_favorite_mode" && pendingTime != null)
                    {
                        parts.Add($"{t.Text?.Trim()} {pendingTime}");
                        pendingTime = null;
                    }
                }
                break;
            }
        }
        catch { }
    }
}
