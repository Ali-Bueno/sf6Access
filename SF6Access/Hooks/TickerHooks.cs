using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Reads ticker toast notifications (app.UIFlowTicker) — "item obtained",
/// invitations, rank changes and other transient system messages.
/// The displayed text lives in UIPartsTicker.Text (via.gui.Text).
/// </summary>
public class TickerHooks
{
    private const string PARAM_TYPE = "app.UIFlowTicker.UIFlowParam";

    private static int _pollCounter;
    private const int POLL_INTERVAL = 30;

    private static ManagedObject _tickerParam;
    private static ManagedObject _tickerText;
    private static ManagedObject _tickerControl;
    private static string _lastText;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] TickerHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        if (++_pollCounter % POLL_INTERVAL != 0) return;

        // Track the live param: the game recreates it across scene changes and
        // the cached Text component would silently read a dead object forever
        _tickerParam = FlowHelper.TrackFlowParam(PARAM_TYPE, _tickerParam, out bool changed);
        if (_tickerParam == null)
        {
            _tickerText = null;
            _tickerControl = null;
            return;
        }

        if (changed || _tickerText == null)
        {
            var ticker = FlowHelper.GetObjectField(_tickerParam, "Ticker");
            _tickerText = FlowHelper.GetObjectField(ticker, "Text")
                ?? FlowHelper.Call(ticker, "get_Text") as ManagedObject;
            _tickerControl = FlowHelper.GetObjectField(ticker, "Control")
                ?? FlowHelper.Call(ticker, "get_Control") as ManagedObject;

            if (_tickerText != null)
                API.LogInfo($"[SF6Access] Ticker found (control={_tickerControl != null})");
            if (_tickerText == null) return;
        }

        string text;
        try
        {
            // The toast often splits message and item name across several texts —
            // read the whole ticker subtree, fall back to the single Text component
            text = GuiTextReader.ReadControlTextJoined(_tickerControl)
                ?? FlowHelper.ReadGuiText(_tickerText);
        }
        catch
        {
            _tickerText = null; // Re-discover next time
            _tickerControl = null;
            return;
        }

        if (string.IsNullOrEmpty(text) || text == _lastText)
        {
            if (string.IsNullOrEmpty(text)) _lastText = null; // Toast closed; allow repeats
            return;
        }
        _lastText = text;

        API.LogInfo($"[SF6Access] Ticker: {text}");
        ScreenReaderService.Speak(text, interrupt: false);
    }
}
