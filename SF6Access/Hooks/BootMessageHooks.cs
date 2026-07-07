using System;
using System.Collections.Generic;
using System.Text;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for full-screen informational messages:
/// - app.UIFlowFirstBootConsentDialog: EULA/GDPR consent (TitleMessage/ContentMessage strings)
/// - app.UIFlowWarningMessage: server warnings such as "game updated, please restart" (BodyMessage)
/// - Boot phase (everything before the title screen): generic scan of all
///   visible scene texts — covers the autosave caution, Microsoft Azure
///   PlayFab notice and other splash screens that have no flow params.
/// Announces each message once when its text appears or changes.
///
/// ScreenAdapter: the boot-phase scan has NO flow param, so Locate() also
/// reports active while the boot phase is still running; after the title screen
/// the adapter only wakes for the watched params. Registered in ScreenRegistry.
/// </summary>
public sealed class BootMessageHooks : ScreenAdapter
{
    private const string CONSENT_TYPE = "app.UIFlowFirstBootConsentDialog.Param";
    private const string WARNING_TYPE = "app.UIFlowWarningMessage.Param";
    private const string ACCOUNT_TYPE = "app.UIFlowFighterAccountCreate.Param";

    private static readonly string[] WatchedTypes = { CONSENT_TYPE, WARNING_TYPE, ACCOUNT_TYPE };

    public override string[] OwnedTypes => WatchedTypes;

    // GUI owners that are chrome or already read by other hooks — excluded
    // from generic scene scans (InputGuide alone changes between screens and
    // would re-trigger a full re-announcement)
    private static readonly string[] IgnoredOwners =
        { "InputGuide", "Resident_Cmn", "Ticker", "OnlineBannerUI", "GameGuideWidget", "MessageBox" };

    public BootMessageHooks()
    {
        // The original hook did its find+read in one 60-frame tick.
        SearchInterval = 60;
        ReadInterval = 60;
    }

    private Dictionary<string, ManagedObject> _found = new();

    // Last announced text per flow type (cleared when the flow ends)
    private readonly Dictionary<string, string> _lastAnnounced = new();

    private bool _bootPhaseOver;
    private string _lastBootText;
    private bool _languageListAnnounced;
    private string _lastAccountText;

    protected override bool Locate()
    {
        _found = FlowHelper.FindFlowParams(WatchedTypes);
        // The boot splash/legal screens have no flow params — stay active until
        // the title screen ends the boot phase.
        return _found.Count > 0 || !_bootPhaseOver;
    }

    protected override void OnDeactivate()
    {
        _found.Clear();
        _lastAnnounced.Clear();
        _lastAccountText = null;
    }

    protected override void OnPoll()
    {
        CheckFlow(_found, CONSENT_TYPE, ReadConsentText);
        CheckFlow(_found, WARNING_TYPE, ReadWarningText);

        PollBootScreens();
        PollAccountCreate(_found);
    }

    /// <summary>
    /// Until the title screen appears, read every visible on-screen text:
    /// splash/legal screens (PlayFab, autosave caution...) have no flow params.
    /// </summary>
    private void PollBootScreens()
    {
        if (_bootPhaseOver) return;

        if (FlowTrackerHooks.IsFlowActive("UIFlowTitle") ||
            FlowTrackerHooks.IsFlowActive("UIStartMenu") ||
            FlowTrackerHooks.IsFlowActive("UIFlowModeSelect"))
        {
            _bootPhaseOver = true;
            return;
        }

        // Dialogs and consent screens already have dedicated readers — the
        // generic scan would only duplicate them
        if (FlowTrackerHooks.IsFlowActive("UIFlowDialog") ||
            FlowTrackerHooks.IsFlowActive("UIFlowFirstBootConsentDialog"))
            return;

        // The first-boot language list re-renders its visible window on every
        // cursor move; read the screen once on entry, then let the focus
        // announcements cover navigation
        bool languageList = FlowTrackerHooks.IsFlowActive("UIFlowFirstBootOptionSetting");
        if (!languageList) _languageListAnnounced = false;
        else if (_languageListAnnounced) return;

        try
        {
            string text = ReadFilteredSceneText();
            if (string.IsNullOrEmpty(text) || text == _lastBootText) return;

            // The language list registers its flow one poll later than its
            // first scan — the gate above misses that window. Same leading
            // segment (screen title) = same screen, just scrolled: skip.
            if (!string.IsNullOrEmpty(_lastBootText) &&
                FirstSegment(text) == FirstSegment(_lastBootText))
            {
                _lastBootText = text;
                return;
            }

            _lastBootText = text;
            if (languageList) _languageListAnnounced = true;

            API.LogInfo($"[SF6Access] Boot screen: {Truncate(text, 200)}");
            Speak(text, interrupt: false);
        }
        catch { }
    }

    private static string FirstSegment(string text)
    {
        int dot = text.IndexOf(". ", StringComparison.Ordinal);
        return dot > 0 ? text.Substring(0, dot) : text;
    }

    /// <summary>
    /// While the Capcom ID account screen is open, read its explanatory text
    /// when it appears. The text loads several seconds after the flow starts
    /// (after server communication), so a one-shot read at flow start misses
    /// it. This screen's texts render via MessageId with an empty Message
    /// string, so the scene-wide scan sees nothing — walk the screen's own
    /// GUI tree (reached from its button list) with MessageId resolution.
    /// </summary>
    private void PollAccountCreate(Dictionary<string, ManagedObject> found)
    {
        if (!found.TryGetValue(ACCOUNT_TYPE, out var param))
        {
            _lastAccountText = null;
            return;
        }

        try
        {
            var list = FlowHelper.GetObjectField(param, "mListTopButton");
            var root = GuiRootOf(FlowHelper.GetObjectField(list, "_List"));
            if (root == null) return;

            var texts = GuiTextReader.ReadControlTexts(root, visibleOnly: true);
            var sb = new StringBuilder();
            foreach (var t in texts)
                Append(sb, t.Text);

            string text = sb.ToString().Trim();
            if (string.IsNullOrEmpty(text) || text == _lastAccountText) return;

            // Only announce what was not already read (buttons stay on screen
            // while secondary texts appear)
            string announcement = FlowHelper.DiffSegments(_lastAccountText, text);
            _lastAccountText = text;
            if (string.IsNullOrWhiteSpace(announcement)) return;

            API.LogInfo($"[SF6Access] Account create screen: {Truncate(announcement, 200)}");
            Speak(announcement, interrupt: false);
        }
        catch { }
    }

    /// <summary>Climb the via.gui parent chain to the root control of the screen.</summary>
    private static ManagedObject GuiRootOf(ManagedObject playObject)
    {
        var current = playObject;
        for (int i = 0; i < 12 && current != null; i++)
        {
            var parent = FlowHelper.Call(current, "get_Parent") as ManagedObject;
            if (parent == null) break;
            current = parent;
        }
        return current;
    }

    private static string ReadFilteredSceneText()
    {
        var texts = GuiTextReader.ReadSceneTexts(visibleOnly: true);
        var sb = new StringBuilder();
        foreach (var t in texts)
        {
            if (IsIgnoredOwner(t.Owner)) continue;
            Append(sb, t.Text);
        }
        return sb.ToString().Trim();
    }

    private static bool IsIgnoredOwner(string owner)
    {
        if (string.IsNullOrEmpty(owner)) return false;
        foreach (var ignored in IgnoredOwners)
        {
            if (owner.Contains(ignored, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void CheckFlow(Dictionary<string, ManagedObject> found, string type,
        System.Func<ManagedObject, string> readText)
    {
        if (!found.TryGetValue(type, out var param))
        {
            if (_lastAnnounced.Remove(type))
                API.LogInfo($"[SF6Access] BootMessage flow ended: {type}");
            return;
        }

        string text;
        try { text = readText(param); }
        catch { return; }

        if (string.IsNullOrWhiteSpace(text)) return;

        _lastAnnounced.TryGetValue(type, out var last);
        if (text == last) return;
        _lastAnnounced[type] = text;

        API.LogInfo($"[SF6Access] BootMessage [{type}]: {Truncate(text, 200)}");
        Speak(text);
    }

    private static string ReadConsentText(ManagedObject param)
    {
        string title = FlowHelper.ReadStringField(param, "TitleMessage");
        string content = FlowHelper.ReadStringField(param, "ContentMessage");
        string offline = FlowHelper.ReadStringField(param, "OfflineMessage");

        var sb = new StringBuilder();
        Append(sb, FlowHelper.CleanTags(title));
        Append(sb, FlowHelper.CleanTags(content));
        Append(sb, FlowHelper.CleanTags(offline));
        return sb.ToString().Trim();
    }

    private static string ReadWarningText(ManagedObject param)
    {
        string body = FlowHelper.ReadStringField(param, "BodyMessage");
        if (string.IsNullOrWhiteSpace(body))
        {
            // Fallback: displayed multi-line text component
            var multiLine = FlowHelper.GetObjectField(param, "MultiLineText");
            var text = FlowHelper.GetObjectField(multiLine, "Text");
            body = FlowHelper.ReadGuiText(text) ?? FlowHelper.Call(multiLine, "get_Message") as string;
        }
        return FlowHelper.CleanTags(body);
    }

    private static void Append(StringBuilder sb, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (sb.Length > 0) sb.Append(". ");
        sb.Append(text.Trim());
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
        return text.Substring(0, max) + "...";
    }
}
