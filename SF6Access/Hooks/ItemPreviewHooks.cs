using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the item preview screen (app.UIFlowItemPreview) that opens from the
/// Rewards / Battle Pass screen — both when confirming a reward
/// (BattlePassFlowParam) and when pressing the preview button R
/// (DefaultFlowParam). The preview's UIPartsItemPreview holds the item name
/// (TitleText), its description (DescriptionText) and the action button label.
/// </summary>
public class ItemPreviewHooks
{
    // Matches DefaultFlowParam and BattlePassFlowParam (both inherit FlowParamBase,
    // which owns the Preview part)
    private const string PARAM_PREFIX = "app.UIFlowItemPreview";

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 15;
    private const int POLL_READ_INTERVAL = 6;

    private static bool _active;
    private static ManagedObject _param;
    private static string _lastBody;
    private static string _lastAction;

    public static bool IsActive => _active;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] ItemPreviewHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var param = FlowHelper.FindFlowParamByPrefix(PARAM_PREFIX, out string foundType);
            if (param != null && !_active)
            {
                _active = true;
                _param = param;
                _lastBody = null;
                _lastAction = null;
                API.LogInfo("[SF6Access] Item preview opened");
            }
            else if (param == null && _active)
            {
                _active = false;
                _param = null;
                _lastBody = null;
                _lastAction = null;
                API.LogInfo("[SF6Access] Item preview closed");
            }
            else if (param != null)
            {
                _param = param; // keep the live instance
            }
        }

        if (!_active || _pollCounter % POLL_READ_INTERVAL != 0) return;
        PollPreview();
    }

    private static void PollPreview()
    {
        // The Preview part (and its texts) populate a few frames after the
        // param appears — re-read each poll instead of caching once
        var preview = FlowHelper.GetObjectField(_param, "Preview");
        if (preview == null) return;

        // Item name + description: announce once per item (interrupt false)
        string title = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(preview, "TitleText"));
        string desc = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(preview, "DescriptionText"));
        var bodyParts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(title)) bodyParts.Add(title.Trim());
        if (!string.IsNullOrEmpty(desc)) bodyParts.Add(desc.Trim());
        string body = bodyParts.Count > 0 ? string.Join(". ", bodyParts) : null;

        if (!string.IsNullOrEmpty(body) && body != _lastBody)
        {
            _lastBody = body;
            API.LogInfo($"[SF6Access] Item preview: {body}");
            ScreenReaderService.Speak(body, interrupt: false);
        }

        // Action button(s). The premium-purchase buttons are image-based (no
        // readable via.gui.Text), so derive the label from DisplayMode:
        // Preview / Receive / RecommendPremiumPass / RecommendPassAndTierBoost10.
        int mode = FlowHelper.CallInt(_param, "GetDisplayMode", -1);
        if (mode < 0) mode = FlowHelper.ReadIntField(_param, "DisplayMode");
        if (mode < 0) mode = FlowHelper.ReadIntField(preview, "Mode");

        // Prefer a focused button's own text (some buttons carry readable text
        // the scene scan misses); fall back to the single ButtonText01, then to
        // the mode-derived label.
        int focusedBtn;
        string action = ReadFocusedButton(preview, out focusedBtn);
        if (string.IsNullOrEmpty(action))
            action = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(preview, "ButtonText01"))?.Trim();
        if (string.IsNullOrEmpty(action))
            action = ModeLabel(mode);

        // Key on the focused button index too, so moving between the two
        // purchase buttons re-announces even when the label is identical
        string actionKey = $"{focusedBtn}|{action}";
        if (!string.IsNullOrEmpty(action) && actionKey != _lastAction)
        {
            _lastAction = actionKey;
            API.LogInfo($"[SF6Access] Item preview action (mode={mode}, focusBtn={focusedBtn}): {action}");
            ScreenReaderService.Speak(action, interrupt: false);
        }
    }

    /// <summary>Text of the focused Button01/02/03, with the focused index out. -1 when none.</summary>
    private static string ReadFocusedButton(ManagedObject preview, out int focusedBtn)
    {
        focusedBtn = -1;
        string[] names = { "Button01", "Button02", "Button03" };
        for (int i = 0; i < names.Length; i++)
        {
            var btn = FlowHelper.GetObjectField(preview, names[i]);
            if (btn == null) continue;

            var isFocus = FlowHelper.Call(btn, "get_IsFocus");
            bool focused = isFocus is bool b ? b : FlowHelper.ReadBoolField(btn, "_IsFocus");
            if (!focused) continue;

            focusedBtn = i;
            var control = FlowHelper.GetObjectField(btn, "Control")
                ?? FlowHelper.Call(btn, "get_Control") as ManagedObject;
            string text = GuiTextReader.ReadControlTextJoined(control);
            API.LogInfo($"[SF6Access] Preview button {names[i]} focused, text='{text}'");
            return string.IsNullOrEmpty(text) ? null : text.Trim();
        }
        return null;
    }

    private static string ModeLabel(int mode) => mode switch
    {
        1 => "Accept",
        2 => "Obtain Premium Pass",
        3 => "Obtain Premium Pass. Or Premium Pass plus 10 tiers",
        _ => null,
    };
}
