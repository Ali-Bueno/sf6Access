using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the fighter profile (app.UICFNFightersProfile* flows).
/// Profile tabs are pure info panels: their params expose via.gui.Text fields
/// (character name, play time, league points...) but no navigable lists, so
/// each tab's texts are read out when the tab flow appears or finishes loading.
///
/// ScreenAdapter: locates by type-FullName PREFIX (the tab child flows come and
/// go as the user switches tabs; the newest match describes the visible panel).
/// Registered in ScreenRegistry.
/// </summary>
public sealed class ProfileHooks : ScreenAdapter
{
    private const string TYPE_PREFIX = "app.UICFNFightersProfileTab";
    // A prefix, not a concrete Param type — every tab flow shares it.
    private static readonly string[] Types = { TYPE_PREFIX };

    public override string[] OwnedTypes => Types;

    private const int MAX_TEXTS = 30;

    public ProfileHooks()
    {
        // The original hook did its find+read in one 30-frame tick; keep both
        // intervals equal so search and read land on the same frames.
        SearchInterval = 30;
        ReadInterval = 30;
    }

    private ManagedObject _param;
    private string _typeName;
    private string _activeType;
    private string _lastAnnounced;

    protected override bool Locate()
    {
        var matches = FlowHelper.FindFlowParamsByPrefix(TYPE_PREFIX);
        if (matches.Count == 0)
        {
            _param = null;
            _typeName = null;
            return false;
        }
        (_typeName, _param) = matches[0];
        return true;
    }

    protected override void OnDeactivate()
    {
        _param = null;
        _typeName = null;
        _activeType = null;
        _lastAnnounced = null;
        API.LogInfo("[SF6Access] Profile ended");
    }

    protected override void OnPoll()
    {
        if (_param == null) return;

        // Wait until the panel finished loading its API data when it says so
        var setuped = FlowHelper.Call(_param, "get_IsSetuped");
        if (setuped is bool b && !b) return;

        string text = ReadPanelTexts(_param, _typeName);
        if (string.IsNullOrEmpty(text)) return;

        // Announce on tab change or when the panel content changes
        if (_typeName == _activeType && text == _lastAnnounced) return;
        _activeType = _typeName;
        _lastAnnounced = text;

        API.LogInfo($"[SF6Access] Profile panel [{_typeName}]: {text}");
        Speak(text, interrupt: false);
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

                            string label = LangFile.GetByText("profile", HumanizeFieldName(field.Name));
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
