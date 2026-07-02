using System;
using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the item/reward dialog opened from the notifications &amp; mailbox when
/// claiming a reward (app.UIFlowItemListDialog.FlowParam, GUI "ItemListDialog").
///
/// On open it announces the whole picture once (the received item(s) and the
/// action button). After that it tracks focus through the dialog's own
/// UIPartsItemDialog: its FocusMode tells whether the cursor is on the item list
/// or on the button, so navigating the list announces the selected item and
/// moving to the button announces its label ("Receive" / "Close"). Without this
/// the dialog read as one undifferentiated block and the user could not tell
/// which element the cursor was on. Migrated to ScreenAdapter.
/// </summary>
public sealed class ItemListDialogHooks : SingleParamScreenAdapter
{
    private const string DIALOG_GUI = "ItemListDialog";

    protected override string ParamType => "app.UIFlowItemListDialog.FlowParam";

    public ItemListDialogHooks()
    {
        SearchInterval = 20;
        ReadInterval = 6;
    }

    private ManagedObject _dialog;
    private string _lastSummary;
    private string _lastFocus;

    protected override void OnBind()
    {
        _dialog = FlowHelper.GetObjectField(Param, "Dialog");
        _lastSummary = null;
        _lastFocus = null;
        API.LogInfo("[SF6Access] Item/reward dialog opened");
    }

    protected override void OnExit()
    {
        _dialog = null;
        _lastSummary = null;
        _lastFocus = null;
        API.LogInfo("[SF6Access] Item/reward dialog closed");
    }

    protected override void Poll()
    {
        // Whole picture once on open (item names + button), then focus tracking.
        AnnounceSummary();
        PollFocus();
    }

    /// <summary>The full dialog text, announced once when it appears.</summary>
    private void AnnounceSummary()
    {
        try
        {
            var items = new List<string>();
            foreach (var (owner, view) in GuiTextReader.FindGuiViews(DIALOG_GUI))
            {
                foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
                {
                    if (string.IsNullOrWhiteSpace(t.Text)) continue;
                    // e_text holds both the item names and the button labels;
                    // e_text_count is the bare quantity number, skip it
                    if (t.Name != "e_text") continue;
                    string s = t.Text.Replace('\n', ' ').Trim();
                    if (!string.IsNullOrEmpty(s) && !items.Contains(s)) items.Add(s);
                }
            }
            if (items.Count == 0) return;

            string text = string.Join(". ", items);
            if (text == _lastSummary) return;
            _lastSummary = text;

            API.LogInfo($"[SF6Access] Item/reward dialog: {text}");
            // Interrupt: the dialog pops over the article, whose long body may
            // still be reading — queuing behind it left the dialog effectively
            // silent until the article finished.
            ScreenReaderService.Speak(text, interrupt: true);
        }
        catch { }
    }

    /// <summary>Announce the focused element (selected item or the button) as the
    /// cursor moves through the dialog.</summary>
    private void PollFocus()
    {
        if (_dialog == null) return;
        try
        {
            // FocusMode: 0 = ItemList, 1 = Button
            var modeObj = FlowHelper.Call(_dialog, "GetFocusMode");
            if (modeObj == null) return;
            int mode = Convert.ToInt32(modeObj);

            string focusKey, announce;
            if (mode == 1)
            {
                string label = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(_dialog, "ButtonText"));
                if (string.IsNullOrWhiteSpace(label)) return;
                announce = label.Trim();
                focusKey = "btn|" + announce;
            }
            else
            {
                string name = ReadSelectedItem();
                if (string.IsNullOrWhiteSpace(name)) return;
                announce = name;
                focusKey = "item|" + name;
            }

            // The open summary already covered the initial focus — baseline it so
            // only an actual move re-announces.
            if (_lastFocus == null) { _lastFocus = focusKey; return; }
            if (focusKey == _lastFocus) return;
            _lastFocus = focusKey;

            API.LogInfo($"[SF6Access] Item dialog focus: {announce}");
            ScreenReaderService.Speak(announce, interrupt: true);
        }
        catch { }
    }

    /// <summary>Localized name (with quantity) of the dialog's selected item.</summary>
    private string ReadSelectedItem()
    {
        var item = FlowHelper.Call(_dialog, "GetSelectedItem") as ManagedObject;
        if (item == null) return null;

        int category = FlowHelper.ReadIntField(item, "ItemCategory");
        int id = FlowHelper.ReadIntField(item, "ItemId");
        int num = FlowHelper.ReadIntField(item, "Num", 1);
        if (id < 0) return null;

        string name = FlowHelper.ResolveItemName(category, (uint)id);
        if (string.IsNullOrEmpty(name)) return null;
        return num > 1 ? $"{name} x{num}" : name;
    }
}
