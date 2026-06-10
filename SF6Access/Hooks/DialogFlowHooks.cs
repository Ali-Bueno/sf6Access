using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Reads app.UIFlowDialog.* flows: confirmation dialogs, command dialogs and
/// tips/help windows (TipsParam — e.g. avatar arcade help with pages).
/// All variants expose TitleMessage/Message string fields; page flips change
/// Message, so change detection re-reads each page.
/// </summary>
public class DialogFlowHooks
{
    private const string TYPE_PREFIX = "app.UIFlowDialog.";

    private static int _pollCounter;
    private const int POLL_INTERVAL = 30;

    private static string _lastAnnounced;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] DialogFlowHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        if (++_pollCounter % POLL_INTERVAL != 0) return;

        var param = FlowHelper.FindFlowParamByPrefix(TYPE_PREFIX, out string foundType);
        if (param == null)
        {
            _lastAnnounced = null;
            return;
        }

        string title = FlowHelper.CleanTags(FlowHelper.ReadStringField(param, "TitleMessage"));
        string message = FlowHelper.CleanTags(FlowHelper.ReadStringField(param, "Message"));

        // Tips/help windows keep their text in via.gui.Text components instead
        if (string.IsNullOrEmpty(title))
            title = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(param, "TitleText"));
        if (string.IsNullOrEmpty(message))
            message = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(param, "Text"));

        // Help windows render in a dedicated "Tips_Media" GUI (verified via F9 dump)
        if (string.IsNullOrEmpty(message))
        {
            var tipTexts = GuiTextReader.ReadTextsByOwner("Tips");
            if (tipTexts.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var t in tipTexts)
                {
                    if (string.IsNullOrWhiteSpace(t.Text)) continue;
                    if (sb.Length > 0) sb.Append(". ");
                    sb.Append(t.Text.Trim());
                }
                message = sb.ToString();
            }
        }

        // Tips windows show page indicators (e.g. "1 / 3")
        string page = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(param, "Page"));
        string pageTotal = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(param, "PageTotal"));

        var parts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(title)) parts.Add(title);
        if (!string.IsNullOrEmpty(message)) parts.Add(message);
        if (!string.IsNullOrEmpty(page) && !string.IsNullOrEmpty(pageTotal))
            parts.Add($"{page} / {pageTotal}");

        if (parts.Count == 0) return;

        string announcement = string.Join(". ", parts);
        if (announcement == _lastAnnounced) return;
        _lastAnnounced = announcement;

        API.LogInfo($"[SF6Access] Dialog [{foundType}]: {announcement}");
        ScreenReaderService.Speak(announcement, interrupt: false);
    }
}
