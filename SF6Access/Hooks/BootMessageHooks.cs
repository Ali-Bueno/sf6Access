using System.Collections.Generic;
using System.Text;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for full-screen informational messages:
/// - app.UIFlowFirstBootConsentDialog: EULA/GDPR consent (TitleMessage/ContentMessage strings)
/// - app.UIFlowWarningMessage: server warnings such as "game updated, please restart" (BodyMessage)
/// - app.UIFlowAttention: startup caution screen (text only in GUI tree, read via GuiTextReader)
/// Announces each message once when its text appears or changes.
/// </summary>
public class BootMessageHooks
{
    private const string CONSENT_TYPE = "app.UIFlowFirstBootConsentDialog.Param";
    private const string WARNING_TYPE = "app.UIFlowWarningMessage.Param";
    private const string ATTENTION_TYPE = "app.UIFlowAttention.Param";

    private static readonly string[] WatchedTypes = { CONSENT_TYPE, WARNING_TYPE, ATTENTION_TYPE };

    private static int _pollCounter;
    private const int POLL_INTERVAL = 60;

    // Last announced text per flow type (cleared when the flow ends)
    private static readonly Dictionary<string, string> _lastAnnounced = new();

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] BootMessageHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        if (++_pollCounter % POLL_INTERVAL != 0) return;

        var found = FlowHelper.FindFlowParams(WatchedTypes);

        CheckFlow(found, CONSENT_TYPE, ReadConsentText);
        CheckFlow(found, WARNING_TYPE, ReadWarningText);
        CheckFlow(found, ATTENTION_TYPE, ReadAttentionText);
    }

    private static void CheckFlow(Dictionary<string, ManagedObject> found, string type,
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
        ScreenReaderService.Speak(text);
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

    private static string ReadAttentionText(ManagedObject param)
    {
        // The caution screen has no text fields on its Param — scrape the GUI tree
        var texts = GuiTextReader.ReadSceneTexts(visibleOnly: true);
        if (texts.Count == 0) return null;

        var sb = new StringBuilder();
        foreach (var t in texts)
            Append(sb, t.Text);
        return sb.ToString().Trim();
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
