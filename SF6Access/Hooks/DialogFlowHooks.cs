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
    private const string ITEM_PREVIEW_PREFIX = "app.UIFlowItemPreview";
    private static readonly string[] WatchedPrefixes = { TYPE_PREFIX, ITEM_PREVIEW_PREFIX };

    private static int _pollCounter;
    private const int POLL_INTERVAL = 5;

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

        // Single pass over the flow handles for both watched prefixes
        var found = FlowHelper.FindFirstFlowParamsByPrefixes(WatchedPrefixes);

        PollItemPreview(found.TryGetValue(ITEM_PREVIEW_PREFIX, out var preview) ? preview.param : null);

        if (!found.TryGetValue(TYPE_PREFIX, out var dialog))
        {
            _lastAnnounced = null;
            _lastButtonIndex = -2;
            IsDialogActive = false;
            return;
        }

        IsDialogActive = true;
        AnnounceDialogText(dialog.param, dialog.typeName);
        PollButtonSelection(dialog.param);
    }

    private static string _lastItemPreview;

    /// <summary>
    /// Item-received popups (claiming rewards etc.): app.UIFlowItemPreview
    /// params carry a UIPartsItemPreview with title/description texts.
    /// </summary>
    private static void PollItemPreview(ManagedObject param)
    {
        try
        {
            if (param == null)
            {
                _lastItemPreview = null;
                return;
            }

            var preview = FlowHelper.GetObjectField(param, "Preview");
            string title = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(preview, "TitleText"));
            string desc = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(preview, "DescriptionText"));

            string text = !string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(desc)
                ? $"{title}. {desc}"
                : title ?? desc;
            if (string.IsNullOrEmpty(text) || text == _lastItemPreview) return;
            _lastItemPreview = text;

            API.LogInfo($"[SF6Access] Item preview: {text}");
            ScreenReaderService.Speak(text);
        }
        catch { }
    }

    private static void AnnounceDialogText(ManagedObject param, string foundType)
    {
        // Resolve platform tags first: the Steam-store dialog's whole message
        // is a <PLATMSG> tag that plain tag stripping erased ("Confirmation")
        string title = FlowHelper.CleanTags(FlowHelper.ResolvePlatformTags(
            FlowHelper.ReadStringField(param, "TitleMessage")));
        string message = FlowHelper.CleanTags(FlowHelper.ResolvePlatformTags(
            FlowHelper.ReadStringField(param, "Message")));

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

        // Style-obtained dialog: the body reads "...obtained 's Battle Style..."
        // with the master's name drawn as a texture. Resolve it from the param's
        // MasterId and splice it in (and prefer the full body GUI text, which the
        // Message/Text field truncates to the first sentence).
        if (foundType != null && foundType.Contains("Enrolling"))
        {
            string body = ReadEnrollingBody() ?? message;
            int masterId = FlowHelper.ReadIntField(param, "MasterId", 0);
            string master = masterId > 0 ? FlowHelper.ResolveMasterFighterName((uint)masterId) : null;
            if (!string.IsNullOrWhiteSpace(master) && !string.IsNullOrEmpty(body))
                body = body.Replace("'s Battle Style", $"{master}'s Battle Style");
            if (!string.IsNullOrEmpty(body)) message = body;
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

    /// <summary>Full body text of the style-obtained dialog ("Enrolling" GUI,
    /// element "e_text_body") — the param's Message field truncates it.</summary>
    private static string ReadEnrollingBody()
    {
        try
        {
            foreach (var (owner, view) in GuiTextReader.FindGuiViews("Enrolling"))
                foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
                    if (t.Name == "e_text_body" && !string.IsNullOrWhiteSpace(t.Text))
                        return t.Text.Replace('\n', ' ').Trim();
        }
        catch { }
        return null;
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

            // The actually-selected button's on-screen text (localized)
            string label = FlowHelper.ReadSelectedItemText(list)
                ?? FlowHelper.ReadListRowText(list, idx);

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
