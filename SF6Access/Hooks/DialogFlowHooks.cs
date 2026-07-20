using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Reads app.UIFlowDialog.* flows: confirmation dialogs, command dialogs and
/// tips/help windows (TipsParam — e.g. avatar arcade help with pages).
/// All variants expose TitleMessage/Message string fields; page flips change
/// Message, so change detection re-reads each page. Message box button
/// navigation is polled from the param's SimpleList_2/SimpleList_3 because
/// UIAgent.FocusChanged does not fire for these dialogs in every context.
///
/// ScreenAdapter (multi-prefix): also watches app.UIFlowItemPreview for the
/// item-received popups. MainMenuHooks reads IsDialogActive for suppression —
/// it is true only while a UIFlowDialog param exists (not for preview-only).
/// Registered in ScreenRegistry.
/// </summary>
public sealed class DialogFlowHooks : ScreenAdapter
{
    private const string TYPE_PREFIX = "app.UIFlowDialog.";
    private const string ITEM_PREVIEW_PREFIX = "app.UIFlowItemPreview";
    private static readonly string[] WatchedPrefixes = { TYPE_PREFIX, ITEM_PREVIEW_PREFIX };

    public override string[] OwnedTypes => WatchedPrefixes;

    // The style-obtained dialog announces once; give the master-name lookup a
    // couple of seconds' worth of polls before latching a nameless read.
    private const int ENROLLING_MAX_RETRIES = 24;

    /// <summary>True while a UIFlowDialog param is active (buttons handled here).</summary>
    public static bool IsDialogActive { get; private set; }

    public DialogFlowHooks()
    {
        // The original hook did its find+read in one 5-frame tick.
        SearchInterval = 5;
        ReadInterval = 5;
    }

    private System.Collections.Generic.Dictionary<string, (string typeName, ManagedObject param)> _found = new();

    private string _lastAnnounced;
    private int _lastButtonIndex = -2;
    private int _enrollingRetries;
    private string _lastItemPreview;

    protected override bool Locate()
    {
        // Single pass over the flow handles for both watched prefixes
        _found = FlowHelper.FindFirstFlowParamsByPrefixes(WatchedPrefixes);
        return _found.Count > 0;
    }

    protected override void OnDeactivate()
    {
        _found.Clear();
        ResetDialogState();
        _lastItemPreview = null;
        IsDialogActive = false;
    }

    private void ResetDialogState()
    {
        _lastAnnounced = null;
        _lastButtonIndex = -2;
        _enrollingRetries = 0;
    }

    protected override void OnPoll()
    {
        PollItemPreview(_found.TryGetValue(ITEM_PREVIEW_PREFIX, out var preview) ? preview.param : null);

        if (!_found.TryGetValue(TYPE_PREFIX, out var dialog))
        {
            ResetDialogState();
            IsDialogActive = false;
            return;
        }

        IsDialogActive = true;
        AnnounceDialogText(dialog.param, dialog.typeName);
        PollButtonSelection(dialog.param);
    }

    /// <summary>
    /// Item-received popups (claiming rewards etc.): app.UIFlowItemPreview
    /// params carry a UIPartsItemPreview with title/description texts.
    /// </summary>
    private void PollItemPreview(ManagedObject param)
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
            Speak(text);
        }
        catch { }
    }

    private void AnnounceDialogText(ManagedObject param, string foundType)
    {
        // Resolve platform tags first: the Steam-store dialog's whole message
        // is a <PLATMSG> tag that plain tag stripping erased ("Confirmation")
        string title = FlowHelper.CleanTags(FlowHelper.ResolvePlatformTags(
            FlowHelper.ReadStringField(param, "TitleMessage")));
        string message = FlowHelper.CleanTags(FlowHelper.ResolvePlatformTags(
            FlowHelper.ReadStringField(param, "Message")));

        // Tips/help windows keep their text in via.gui.Text components instead
        if (string.IsNullOrEmpty(title))
            title = ResolveMessageText(FlowHelper.ReadGuiText(FlowHelper.GetObjectField(param, "TitleText")));
        if (string.IsNullOrEmpty(message))
            message = ResolveMessageText(FlowHelper.ReadGuiText(FlowHelper.GetObjectField(param, "Text")));

        // The authoritative tip body: each page's PageData carries the body +
        // title as message Guids (ArrPage[current].Message / .TitleMessage). This
        // avoids scraping the GUI, whose body renders as unresolved
        // <PAD ref>/<KBM ref> platform-variant tags (the tip sounded half-read:
        // only the heading survived tag stripping).
        if (string.IsNullOrEmpty(message))
            message = ReadTipPageBody(param, ref title);

        // Last resort: the dedicated "Tips_Media" GUI. Read the RAW text (the
        // cleaned form drops the <PAD ref>/<KBM ref> body) and resolve it.
        if (string.IsNullOrEmpty(message))
        {
            var tipTexts = GuiTextReader.ReadTextsByOwner("Tips");
            if (tipTexts.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var t in tipTexts)
                {
                    string resolved = ResolveMessageText(
                        !string.IsNullOrWhiteSpace(t.Raw) ? t.Raw : t.Text);
                    if (string.IsNullOrWhiteSpace(resolved)) continue;
                    resolved = resolved.Replace('\n', ' ').Trim();
                    // Skip the heading element here — it's already the title, so
                    // including it would speak the heading twice.
                    if (!string.IsNullOrEmpty(title) &&
                        string.Equals(resolved, title.Trim(), System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (sb.Length > 0) sb.Append(". ");
                    sb.Append(resolved);
                }
                message = sb.ToString();
            }
        }

        // Style-obtained dialog: the body reads "...obtained 's Battle Style..."
        // with the master's name rendered from a separate WLTAG element. Resolve
        // the name (param MasterId, then the GUI's e_text_style WLTAG) and splice
        // it in. The dialog announces once — hold off while the name is still
        // unresolvable instead of latching a nameless read.
        if (foundType != null && foundType.Contains("Enrolling"))
        {
            string body = ReadGuiElementText("Enrolling", "e_text_body")
                ?? message?.Replace('\n', ' ');
            int masterId = FlowHelper.ReadIntField(param, "MasterId", 0);
            string master = masterId > 0 ? FlowHelper.ResolveMasterFighterName((uint)masterId) : null;
            if (string.IsNullOrWhiteSpace(master))
                master = FlowHelper.ResolveWLTags(ReadGuiElementRaw("Enrolling", "e_text_style"));
            if (string.IsNullOrWhiteSpace(master) && _enrollingRetries++ < ENROLLING_MAX_RETRIES)
                return;
            if (!string.IsNullOrWhiteSpace(master) && !string.IsNullOrEmpty(body))
            {
                // Splice into the English body; localized bodies phrase the
                // possessive differently, so fall back to prepending the name
                string spliced = body.Replace("'s Battle Style", $"{master}'s Battle Style");
                body = spliced != body ? spliced : $"{master}. {body}";
            }
            if (!string.IsNullOrEmpty(body)) message = body;
        }

        // New-special-move dialog: the command line, its supplement notes and
        // the style tag ("MAI") are separate fields/GUI texts the generic
        // title+message read skipped — the dialog sounded half-read.
        string command = null, supplement = null, style = null;
        if (foundType != null && foundType.Contains("SPMoveGet"))
        {
            command = FlowHelper.CleanTags(FlowHelper.SpeakableIcons(
                FlowHelper.ReadStringField(param, "CommandMessage")));
            supplement = FlowHelper.CleanTags(FlowHelper.SpeakableIcons(
                FlowHelper.ReadStringField(param, "SupplementCommandMessage")));
            style = ReadGuiElementText("SPMoveGet", "e_text_style");
        }

        // Tips windows show page indicators (e.g. "1 / 3")
        string page = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(param, "Page"));
        string pageTotal = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(param, "PageTotal"));

        var parts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(title)) parts.Add(title);
        if (!string.IsNullOrEmpty(style)) parts.Add(style);
        if (!string.IsNullOrEmpty(command)) parts.Add(command);
        if (!string.IsNullOrEmpty(supplement)) parts.Add(supplement);
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
        Speak(announcement, interrupt: isMessageBox);
    }

    /// <summary>Resolve a raw message string to speakable text: platform-variant
    /// tags (&lt;PLATMSG&gt;/&lt;PAD&gt;/&lt;KBM&gt;) via the game's exchange
    /// functions, then input icons to words, then strip any residual tags.</summary>
    private static string ResolveMessageText(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        return FlowHelper.CleanTags(FlowHelper.SpeakableIcons(
            FlowHelper.ResolvePlatformTags(raw)));
    }

    /// <summary>The current tip page's body from PageData message Guids
    /// (ArrPage[current].Message), plus its title into <paramref name="title"/>
    /// when the param carried none. Null when unavailable.</summary>
    private string ReadTipPageBody(ManagedObject param, ref string title)
    {
        try
        {
            var arrPage = FlowHelper.GetObjectField(param, "ArrPage");
            int total = FlowHelper.GetListCount(arrPage);
            if (total == 0) return null;

            // Current page from the on-screen pager ("1" of "3" → index 0);
            // default to the first page when it can't be read.
            int idx = 0;
            if (int.TryParse(FlowHelper.ReadGuiText(FlowHelper.GetObjectField(param, "Page")), out int oneBased))
                idx = System.Math.Clamp(oneBased - 1, 0, total - 1);

            var page = FlowHelper.GetListItem(arrPage, idx);
            if (page == null) return null;

            if (string.IsNullOrEmpty(title))
                title = ResolveMessageText(FlowHelper.ResolveGuidField(page, "TitleMessage"));

            string rawBody = FlowHelper.ResolveGuidField(page, "Message");
            string body = ResolveMessageText(rawBody);
            API.LogInfo($"[SF6Access] Tip page {idx + 1}/{total} raw=[{rawBody}] resolved=[{body}]");
            return body;
        }
        catch { return null; }
    }

    /// <summary>Cleaned text of one named element in a dialog's own GUI view
    /// (dialogs render parts of their content outside the param's fields).</summary>
    private static string ReadGuiElementText(string owner, string element)
    {
        try
        {
            foreach (var t in GuiTextReader.ReadTextsByOwner(owner))
                if (t.Name == element && !string.IsNullOrWhiteSpace(t.Text))
                    return t.Text.Replace('\n', ' ').Trim();
        }
        catch { }
        return null;
    }

    /// <summary>Raw (tags intact) text of one named GUI element — for WLTAG-composed
    /// texts whose cleaned form is empty.</summary>
    private static string ReadGuiElementRaw(string owner, string element)
    {
        try
        {
            foreach (var t in GuiTextReader.ReadTextsByOwner(owner))
                if (t.Name == element && !string.IsNullOrWhiteSpace(t.Raw))
                    return t.Raw;
        }
        catch { }
        return null;
    }

    /// <summary>Announce the focused button when navigating a message box.</summary>
    private void PollButtonSelection(ManagedObject param)
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
            Speak(label);
        }
        catch { }
    }
}
