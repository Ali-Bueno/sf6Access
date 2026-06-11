using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Reads app.UIFlowDialog.* flows: confirmation dialogs, command dialogs and
/// tips/help windows (TipsParam — e.g. avatar arcade help with pages).
/// All variants expose TitleMessage/Message string fields; page flips change
/// Message, so change detection re-reads each page. Message box button
/// navigation is polled from the param's SimpleList_2/SimpleList_3 because
/// UIAgent.FocusChanged does not fire for these dialogs in every context.
/// </summary>
public class DialogFlowHooks
{
    private const string TYPE_PREFIX = "app.UIFlowDialog.";

    private static int _pollCounter;
    private const int POLL_INTERVAL = 10;

    private static string _lastAnnounced;
    private static int _lastButtonIndex = -2;

    /// <summary>True while a UIFlowDialog param is active (buttons handled here).</summary>
    public static bool IsDialogActive { get; private set; }

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
            _lastButtonIndex = -2;
            IsDialogActive = false;
            return;
        }

        IsDialogActive = true;
        AnnounceDialogText(param, foundType);
        PollButtonSelection(param);
    }

    private static void AnnounceDialogText(ManagedObject param, string foundType)
    {
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

        // Confirmation boxes are a modal context change — interrupt so the user
        // hears them immediately; tips are informational and queue instead
        bool isMessageBox = foundType?.Contains("MessageBox") == true;

        API.LogInfo($"[SF6Access] Dialog [{foundType}]: {announcement}");
        ScreenReaderService.Speak(announcement, interrupt: isMessageBox);
    }

    /// <summary>Announce the focused button when navigating a message box.</summary>
    private static void PollButtonSelection(ManagedObject param)
    {
        try
        {
            ManagedObject list = null;
            foreach (var name in new[] { "SimpleList_2", "SimpleList_3" })
            {
                var candidate = FlowHelper.GetObjectField(param, name);
                if (candidate == null) continue;
                if (FlowHelper.CallInt(candidate, "get_SelectedIndex") >= 0)
                {
                    list = candidate;
                    break;
                }
            }
            if (list == null) return;

            int idx = FlowHelper.CallInt(list, "get_SelectedIndex");
            if (idx < 0 || idx == _lastButtonIndex) return;

            bool first = _lastButtonIndex == -2;
            _lastButtonIndex = idx;
            if (first) return; // dialog text announcement covers the initial state

            string label = FlowHelper.ReadListRowText(list, idx);

            // Fallback: the param's button label array
            if (string.IsNullOrEmpty(label))
            {
                var arr = FlowHelper.GetObjectField(param, "ArrItemMessage");
                if (arr != null && idx < FlowHelper.GetListCount(arr))
                {
                    var raw = FlowHelper.Call(arr, "Get", idx) as string;
                    label = FlowHelper.CleanTags(raw);
                }
            }

            if (string.IsNullOrEmpty(label)) label = idx == 0 ? "Yes" : "No";

            API.LogInfo($"[SF6Access] Dialog button [{idx}]: {label}");
            ScreenReaderService.Speak(label);
        }
        catch { }
    }
}
