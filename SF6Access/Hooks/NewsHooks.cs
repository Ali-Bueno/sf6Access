using System;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the news / mailbox screen (app.UIFlowMailBox).
/// Announces the selected news title on list navigation and reads the body text.
/// Titles and bodies are resolved via app.MailData.Util.GetTitleText/GetBodyText
/// which return the localized server-provided strings.
///
/// SingleParamScreenAdapter with ReadInterval = 1: the article-open flag (set by
/// the UIFlowMailText.OnEnter hook, kept in the static [PluginEntryPoint]) must
/// be honored the same frame; the regular reads run every READ_TICKS frames.
/// MainMenuHooks reads IsInNewsMenu. Registered in ScreenRegistry.
/// </summary>
public sealed class NewsHooks : SingleParamScreenAdapter
{
    private const string PARAM_TYPE = "app.UIFlowMailBox.UIFlowParam";
    protected override string ParamType => PARAM_TYPE;

    // Regular reads run every 10th frame (the original POLL_READ_INTERVAL).
    private const int READ_TICKS = 10;

    private static NewsHooks _self;
    public static bool IsInNewsMenu => _self != null && _self.Active;

    public NewsHooks()
    {
        SearchInterval = 60;
        ReadInterval = 1; // per-frame article-open flag; reads gated by READ_TICKS
        _self = this;
    }

    private static Method _getTitleTextMethod;
    private static Method _getBodyTextMethod;
    private static Method _getItemNameMethod;
    private static bool _tdbCached;

    private ManagedObject _mailList;
    private ManagedObject _scrollList;
    private ManagedObject _mailText;
    private ManagedObject _header;
    private ManagedObject _mainGroup;

    private int _tick;
    private string _lastMailId;
    private int _lastTab = -1;
    private int _lastGroupFocus = -2;
    private int _lastListIndex = -1;

    // Pressing confirm on a news enters the article-reading state
    // (UIFlowMailBox.UIFlowMailText). Polling the MainGroup focus index missed
    // this transition, so the article opened silently. OnEnter sets this flag
    // and the poll re-reads the open mail (title + body + View Items).
    private static volatile bool _announceArticle;

    [PluginEntryPoint]
    public static void Initialize()
    {
        HookArticleOpen();
        API.LogInfo("[SF6Access] NewsHooks initialized");
    }

    /// <summary>
    /// Hook the article-reading state so opening a news with confirm reliably
    /// announces it — the focus-index poll did not catch this transition.
    /// </summary>
    private static void HookArticleOpen()
    {
        var onEnter = TDB.Get().FindType("app.UIFlowMailBox.UIFlowMailText")?.GetMethod("OnEnter");
        if (onEnter == null)
        {
            API.LogWarning("[SF6Access] UIFlowMailText.OnEnter not found");
            return;
        }
        onEnter.AddHook(false).AddPre(args =>
        {
            _announceArticle = true;
            return PreHookResult.Continue;
        });
        API.LogInfo("[SF6Access] Article-open hook installed");
    }

    protected override void OnBind()
    {
        CacheTDB();
        _mailList = FlowHelper.GetObjectField(Param, "MailList");
        _scrollList = FlowHelper.GetObjectField(_mailList, "ScrollList")
            ?? FlowHelper.Call(_mailList, "get_ScrollList") as ManagedObject;
        _mailText = FlowHelper.GetObjectField(Param, "MailText");
        _header = FlowHelper.GetObjectField(Param, "Header");
        _mainGroup = FlowHelper.GetObjectField(Param, "MainGroup");
        _lastMailId = null;
        _lastTab = -1;
        _lastGroupFocus = -2;
        _lastListIndex = -1;

        API.LogInfo($"[SF6Access] News menu active (mailList={_mailList != null}, header={_header != null})");

        // Announce the initially selected mail right away
        PollSelectedMail();
    }

    protected override void OnExit()
    {
        API.LogInfo("[SF6Access] News menu ended");
        _mailList = null;
        _scrollList = null;
        _mailText = null;
        _header = null;
        _mainGroup = null;
        _lastMailId = null;
        _lastTab = -1;
        _lastGroupFocus = -2;
        _lastListIndex = -1;
    }

    protected override void Poll()
    {
        if (_announceArticle)
        {
            _announceArticle = false;
            // Force a fresh read of the open mail (defeat the id dedup) so the
            // article speaks the moment it opens, not only on a focus change.
            _lastMailId = null;
            PollSelectedMail();
        }

        if (++_tick % READ_TICKS != 0) return;

        PollTabChange();
        PollListCursor();
        PollSelectedMail();
        PollGroupFocus();
    }

    /// <summary>
    /// Read the highlighted headline as the cursor moves through the news list.
    /// The list and the open message live in separate MainGroup columns, so
    /// scrolling the list does not change the MainGroup focus index, and the
    /// opened mail (GetSelectedMail) only changes on confirm — neither path
    /// announced the headline you were scrolling past. The cursor position is
    /// the ScrollList's SelectedIndex; read the title of MailList[index].
    /// Title only — the body/items/claim are read once the message pane is
    /// focused (PollGroupFocus). Setting _lastMailId here keeps PollSelectedMail
    /// from re-reading the same mail's body on the same tick.
    /// </summary>
    private void PollListCursor()
    {
        if (_scrollList == null) return;

        int idx = FlowHelper.CallInt(_scrollList, "get_SelectedIndex");
        if (idx < 0 || idx == _lastListIndex) return;
        _lastListIndex = idx;

        var mails = FlowHelper.GetObjectField(_mailList, "MailList")
            ?? FlowHelper.Call(_mailList, "get_MailList") as ManagedObject;
        var mail = FlowHelper.GetListItem(mails, idx);
        if (mail == null) return;

        _lastMailId = GetMailId(mail);

        string title = ResolveMailText(mail, "Title");
        if (string.IsNullOrEmpty(title)) return;

        API.LogInfo($"[SF6Access] News list cursor {idx}: {title}");
        Speak(title);
    }

    /// <summary>Re-read the open mail when focus moves into the text pane.</summary>
    private void PollGroupFocus()
    {
        if (_mainGroup == null) return;

        int idx = FlowHelper.ReadIntField(_mainGroup, "_FocusIndex");
        if (idx < 0 || idx == _lastGroupFocus) return;

        bool first = _lastGroupFocus == -2;
        _lastGroupFocus = idx;
        if (first) return;

        API.LogInfo($"[SF6Access] News group focus: {idx}");

        // Moving into the message pane: re-announce the current mail content
        _lastMailId = null;
        PollSelectedMail();
    }

    private static void CacheTDB()
    {
        if (_tdbCached) return;
        _tdbCached = true;
        var utilType = TDB.Get().FindType("app.MailData.Util");
        _getTitleTextMethod = utilType?.GetMethod("GetTitleText(app.MailData.MailText)");
        _getBodyTextMethod = utilType?.GetMethod("GetBodyText(app.MailData.MailText)");
        if (_getTitleTextMethod == null || _getBodyTextMethod == null)
            API.LogWarning("[SF6Access] MailData.Util text methods not found");

        // Item name resolver for mail attachments (localized)
        var invType = TDB.Get().FindType("app.InventoryManager");
        _getItemNameMethod = invType?.GetMethod("GetName(app.network.api.Enum.ItemCategory, System.UInt32)")
            ?? invType?.GetMethod("GetName(app.ItemCategory, System.UInt32)")
            ?? invType?.GetMethod("GetName");
        if (_getItemNameMethod == null)
            API.LogWarning("[SF6Access] InventoryManager.GetName not found");
    }

    private void PollTabChange()
    {
        if (_header == null) return;

        var tabObj = FlowHelper.Call(_header, "GetSelectedTab");
        if (tabObj == null) return;

        int tab;
        try { tab = Convert.ToInt32(tabObj); } catch { return; }

        if (tab == _lastTab) return;
        bool first = _lastTab < 0;
        _lastTab = tab;

        if (first) return; // Don't announce the initial tab, only changes

        // TabItem enum: 0 = Mail (news), 1 = Ticker (notification history)
        string name = tab == 0 ? "News" : "History";
        API.LogInfo($"[SF6Access] News tab: {name}");
        Speak(name);
    }

    private void PollSelectedMail()
    {
        if (_mailList == null) return;

        var mail = FlowHelper.Call(_mailList, "GetSelectedMail") as ManagedObject;
        if (mail == null) return;

        string id = GetMailId(mail);
        if (string.IsNullOrEmpty(id) || id == _lastMailId) return;
        _lastMailId = id;

        string title = ResolveMailText(mail, "Title");
        string body = ResolveMailText(mail, "Body");

        // Reward/gift mails may have no text data — read the displayed pane instead
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(body))
        {
            var paneControl = FlowHelper.GetObjectField(_mailText, "Control")
                ?? FlowHelper.Call(_mailText, "get_Control") as ManagedObject;
            body = GuiTextReader.ReadControlTextJoined(paneControl);
            API.LogInfo($"[SF6Access] News pane fallback: '{body}'");
        }

        // Attached rewards: localized item names + claim button label
        string attachmentText = ReadAttachments(mail);
        string claimButton = null;
        if (!string.IsNullOrEmpty(attachmentText))
            claimButton = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(_mailText, "ButtonText"));

        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(body) && string.IsNullOrEmpty(attachmentText))
            return;

        API.LogInfo($"[SF6Access] News: {title} (body={body?.Length ?? 0} chars, items={attachmentText}, claim={claimButton})");

        if (!string.IsNullOrEmpty(title))
            Speak(title);
        if (!string.IsNullOrEmpty(body))
            Speak(body, interrupt: false);
        if (!string.IsNullOrEmpty(attachmentText))
            Speak(attachmentText, interrupt: false);
        if (!string.IsNullOrEmpty(claimButton))
            Speak(claimButton, interrupt: false);
    }

    /// <summary>Stable id for a mail (Mail.Id field; address as last resort).</summary>
    private static string GetMailId(ManagedObject mail)
    {
        // Mail.Id is a plain field at runtime (get_Id does not exist)
        string id = FlowHelper.ReadStringField(mail, "Id");
        if (!string.IsNullOrEmpty(id)) return id;
        try { return mail.GetAddress().ToString(); } catch { return null; }
    }

    /// <summary>Resolve attached item names via InventoryManager.GetName (localized).</summary>
    private static string ReadAttachments(ManagedObject mail)
    {
        var attachments = FlowHelper.GetObjectField(mail, "AttachmentList");
        int count = FlowHelper.GetListCount(attachments);
        if (count == 0) return null;

        var invMgr = API.GetManagedSingleton("app.InventoryManager");
        var items = new System.Collections.Generic.List<string>();

        for (int i = 0; i < count && i < 20; i++)
        {
            var att = FlowHelper.GetListItem(attachments, i);
            if (att == null) continue;

            int category = FlowHelper.ReadIntField(att, "ItemCategory");
            int itemId = FlowHelper.ReadIntField(att, "ItemId");
            int num = FlowHelper.ReadIntField(att, "Num", 1);

            string name = null;
            if (_getItemNameMethod != null && invMgr != null && itemId >= 0)
            {
                try
                {
                    name = _getItemNameMethod.InvokeBoxed(
                        typeof(string), invMgr, new object[] { category, (uint)itemId }) as string;
                    name = FlowHelper.CleanTags(name);
                }
                catch { }
            }
            API.LogInfo($"[SF6Access] Attachment [{i}]: cat={category}, id={itemId}, num={num}, name='{name}'");

            if (string.IsNullOrEmpty(name)) continue;
            items.Add(num > 1 ? $"{name} x{num}" : name);
        }

        return items.Count > 0 ? string.Join(", ", items) : null;
    }

    /// <summary>Resolve a MailText field (Title/Body) to its localized string.</summary>
    private static string ResolveMailText(ManagedObject mail, string fieldName)
    {
        var mailText = FlowHelper.GetObjectField(mail, fieldName);
        if (mailText == null) return null;

        var method = fieldName == "Body" ? _getBodyTextMethod : _getTitleTextMethod;
        if (method != null)
        {
            try
            {
                var text = method.InvokeBoxed(typeof(string), null, new object[] { mailText }) as string;
                if (!string.IsNullOrEmpty(text)) return FlowHelper.CleanTags(text);
            }
            catch { }
        }

        // Fallback: raw Text property on MailText
        var raw = FlowHelper.Call(mailText, "get_Text") as string;
        return FlowHelper.CleanTags(raw);
    }
}
