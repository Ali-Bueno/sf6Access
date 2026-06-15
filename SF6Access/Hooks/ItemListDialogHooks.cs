using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the item/reward dialog opened from the notifications &amp; mailbox when
/// claiming a reward (app.UIFlowItemListDialog.FlowParam, GUI "ItemListDialog").
/// It lists the received item(s) and the action button(s) ("Fechar" / confirm /
/// back). Announced when the dialog appears and whenever its on-screen text
/// changes (button focus moving between confirm/back).
/// </summary>
public class ItemListDialogHooks
{
    private const string PARAM_TYPE = "app.UIFlowItemListDialog.FlowParam";
    private const string DIALOG_GUI = "ItemListDialog";

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 20;
    private const int POLL_READ_INTERVAL = 6;

    private static bool _active;
    private static string _lastText;

    /// <summary>True while the reward/item dialog is up, so other generic
    /// readers can stand down if needed.</summary>
    public static bool IsActive => _active;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] ItemListDialogHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            bool present = FlowHelper.FindFlowParam(PARAM_TYPE) != null;
            if (present && !_active)
            {
                _active = true;
                _lastText = null;
                API.LogInfo("[SF6Access] Item/reward dialog opened");
            }
            else if (!present && _active)
            {
                _active = false;
                _lastText = null;
                API.LogInfo("[SF6Access] Item/reward dialog closed");
            }
        }

        if (!_active || _pollCounter % POLL_READ_INTERVAL != 0) return;
        PollDialog();
    }

    private static void PollDialog()
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
            if (text == _lastText) return;
            _lastText = text;

            API.LogInfo($"[SF6Access] Item/reward dialog: {text}");
            ScreenReaderService.Speak(text, interrupt: false);
        }
        catch { }
    }
}
