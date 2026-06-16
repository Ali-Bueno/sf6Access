using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the "reissue Fighting Pass" warning dialog (app.UIFlowRevivalPassWarningDialog)
/// shown after choosing to obtain the premium pass. The warning paragraph is
/// rendered as an image (no readable via.gui.Text anywhere in an F9 dump), so it
/// is announced from a fixed string. The reward list IS readable: each item
/// (UIPartsRevivalPassWarningDialogItem) exposes its name, count and a "Sold Out"
/// panel (items already obtained), read as the cursor moves through UIScrollList.
/// </summary>
public class RevivalPassWarningHooks
{
    private const string PARAM_TYPE = "app.UIFlowRevivalPassWarningDialog.FlowParam";

    // The warning text is image-based and unreadable — hardcoded fallback so it
    // is at least announced. (Last resort: no localized source is reachable.)
    private const string WARNING_MESSAGE =
        "The currently available Fighting Pass is a reissue. Fighter Coins are not " +
        "included in the rewards. Rewards may include items you have already obtained. " +
        "Check your acquisition status in the reward list, then proceed to the confirm " +
        "transaction window. Items marked Sold Out are those you already obtained. " +
        "For accessories, you can obtain up to 3 of the same item.";

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 15;
    private const int POLL_READ_INTERVAL = 6;

    private static bool _active;
    private static ManagedObject _param;
    private static string _lastItem;
    private static volatile bool _navDirty;

    public static bool IsActive => _active;

    [PluginEntryPoint]
    public static void Initialize()
    {
        HookNavigation();
        API.LogInfo("[SF6Access] RevivalPassWarningHooks initialized");
    }

    private static void HookNavigation()
    {
        try
        {
            var td = TDB.Get().FindType("app.UIPartsRevivalPassWarningDialog");
            var m = td?.GetMethod("ListChanged") ?? td?.GetMethod("ListChanged()");
            m?.AddHook(false).AddPost((ref ulong retval) => _navDirty = true);
        }
        catch (System.Exception ex)
        {
            API.LogWarning($"[SF6Access] RevivalPassWarning nav hook failed: {ex.Message}");
        }
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var param = FlowHelper.FindFlowParam(PARAM_TYPE);
            if (param != null && !_active)
            {
                _active = true;
                _param = param;
                _lastItem = null;
                API.LogInfo("[SF6Access] Reissue pass warning opened");
                ScreenReaderService.Speak(WARNING_MESSAGE, interrupt: false);
            }
            else if (param == null && _active)
            {
                _active = false;
                _param = null;
                _lastItem = null;
                API.LogInfo("[SF6Access] Reissue pass warning closed");
            }
            else if (param != null) _param = param;
        }

        if (!_active) return;

        if (_navDirty) { _navDirty = false; _lastItem = null; }
        if (_pollCounter % POLL_READ_INTERVAL == 0) PollSelectedItem();
    }

    private static void PollSelectedItem()
    {
        var dialog = FlowHelper.GetObjectField(_param, "UIDialog");
        if (dialog == null) return;

        // FocusMode: 0 = ItemList (browse the pass rewards), 1 = Button (proceed/buy)
        int focusMode = FlowHelper.CallInt(dialog, "GetFocusMode", -1);

        string text;
        if (focusMode == 1)
        {
            // The proceed / purchase button
            var btn = FlowHelper.GetObjectField(dialog, "UIButtonGroupItem");
            var control = FlowHelper.GetObjectField(btn, "Control")
                ?? FlowHelper.Call(btn, "get_Control") as ManagedObject;
            text = GuiTextReader.ReadControlTextJoined(control);
            text = string.IsNullOrEmpty(text) ? "Confirm" : text.Trim();
            text = $"button|{text}"; // key the dedup so item<->button moves re-announce
        }
        else
        {
            // Focused reward via the dialog's own accessor (the list's
            // get_SelectedItem proved unreliable here)
            var reward = FlowHelper.Call(dialog, "GetSelectedReward") as ManagedObject;
            if (reward == null) return;

            int category = FlowHelper.ReadIntField(reward, "ItemCategory");
            int itemId = FlowHelper.ReadIntField(reward, "ItemId");
            int num = FlowHelper.ReadIntField(reward, "Num", 1);
            bool received = FlowHelper.ReadBoolField(reward, "Received");

            string name = itemId >= 0 ? FlowHelper.ResolveItemName(category, (uint)itemId) : null;
            if (string.IsNullOrEmpty(name)) return;

            text = name.Trim();
            if (num > 1) text += $" x{num}";
            if (received) text += ". Sold out, already obtained";
        }

        if (text == _lastItem) return;
        _lastItem = text;

        // Strip the internal "button|" key prefix before speaking
        string spoken = text.StartsWith("button|") ? text.Substring(7) : text;
        API.LogInfo($"[SF6Access] Reissue selection (focusMode={focusMode}): {spoken}");
        ScreenReaderService.Speak(spoken);
    }
}
