using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Reads ticker toast notifications (app.UIFlowTicker) — "item obtained",
/// invitations, rank changes and other transient system messages. The displayed
/// text lives in UIPartsTicker.Text (via.gui.Text). Migrated to ScreenAdapter.
/// </summary>
public sealed class TickerHooks : SingleParamScreenAdapter
{
    protected override string ParamType => "app.UIFlowTicker.UIFlowParam";

    public TickerHooks()
    {
        SearchInterval = 30;
        ReadInterval = 30;
    }

    private ManagedObject _text;
    private ManagedObject _control;
    private string _lastText;

    protected override void OnBind()
    {
        var ticker = FlowHelper.GetObjectField(Param, "Ticker");
        _text = FlowHelper.GetObjectField(ticker, "Text")
            ?? FlowHelper.Call(ticker, "get_Text") as ManagedObject;
        _control = FlowHelper.GetObjectField(ticker, "Control")
            ?? FlowHelper.Call(ticker, "get_Control") as ManagedObject;
        if (_text != null)
            API.LogInfo($"[SF6Access] Ticker found (control={_control != null})");
    }

    protected override void OnExit()
    {
        _text = null;
        _control = null;
        _lastText = null;
    }

    protected override void Poll()
    {
        if (_text == null) OnBind();   // re-discover after a read failure
        if (_text == null) return;

        string text;
        try
        {
            // The toast often splits message and item name across several texts —
            // read the whole ticker subtree, fall back to the single Text component.
            text = GuiTextReader.ReadControlTextJoined(_control)
                ?? FlowHelper.ReadGuiText(_text);
        }
        catch
        {
            _text = null; // Re-discover next time
            _control = null;
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
