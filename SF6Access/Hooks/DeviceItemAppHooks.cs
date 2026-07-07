using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Item app of the in-game smartphone ("View consumable and sellable items" in
/// Avatar Arcade / World Tour) — app.UIFlowUI50201.Param. Same shape as the WTM
/// pause Item tab: category tabs (PartsSimpleListTabMenu), an item grid whose
/// cells only carry owned-count numbers (PartsScrollGridItem), and the selected
/// item's name/description in the ui50201 GUI. Nothing generic reads it (the
/// grid cells are bare numbers), so tabs + selection are announced here; the
/// use-item confirm popup is the shared UIWidget_ItemConfirmWindow. G / R3
/// reads the current Zenny.
/// </summary>
public sealed class DeviceItemAppHooks : SingleParamScreenAdapter
{
    private const string GUI_OWNER = "ui50201";

    protected override string ParamType => "app.UIFlowUI50201.Param";

    public DeviceItemAppHooks()
    {
        ReadInterval = 1;   // the G/R3 shortcut needs per-frame edge detection
    }

    // Menu/GUI reads still run at the old cadence; only the shortcut is per-frame.
    private const int MENU_POLL_EVERY = 5;
    private int _frame;
    private readonly ReadoutShortcut _currencyShortcut = new();

    private readonly ItemConfirmWatcher _confirm = new();
    private int _lastTabIdx = int.MinValue;
    private int _lastItemIdx = int.MinValue;
    // Set on a tab change: the re-laid grid's item announcement must QUEUE
    // behind the tab name instead of cutting it off.
    private bool _queueNextItemAnnounce;

    protected override void OnBind()
    {
        _lastTabIdx = int.MinValue;
        _lastItemIdx = int.MinValue;
        _queueNextItemAnnounce = false;
        _confirm.Reset();
        API.LogInfo("[SF6Access] Device item app active");
    }

    protected override void OnExit() => _confirm.Reset();

    protected override void Poll()
    {
        if (_currencyShortcut.Pressed()) CurrencyReader.AnnounceZenny(GUI_OWNER);
        if (++_frame % MENU_POLL_EVERY != 0) return;

        _confirm.Poll();

        // Category tab: announce on change (the initial tab is implied by the
        // app the user just opened)
        var tab = FlowHelper.GetObjectField(Param, "PartsSimpleListTabMenu");
        int tabIdx = FlowHelper.CallInt(tab, "get_SelectedIndex");
        if (tabIdx >= 0 && tabIdx != _lastTabIdx)
        {
            bool first = _lastTabIdx == int.MinValue;
            _lastTabIdx = tabIdx;
            _lastItemIdx = int.MinValue;   // tab switch re-lays the grid
            if (!first)
            {
                string tabName = FlowHelper.ReadSelectedItemText(tab);
                if (!string.IsNullOrEmpty(tabName))
                {
                    API.LogInfo($"[SF6Access] Device item tab [{tabIdx}]: {tabName}");
                    Speak(tabName);
                    _queueNextItemAnnounce = true;
                }
                return;
            }
        }

        var grid = FlowHelper.GetObjectField(Param, "PartsScrollGridItem");
        int idx = FlowHelper.CallInt(grid, "get_SelectedIndex");
        if (idx < 0 || idx == _lastItemIdx) return;
        _lastItemIdx = idx;

        string msg = ItemGridReader.ReadSelectedItem(grid, GUI_OWNER);
        if (string.IsNullOrEmpty(msg))
        {
            _lastItemIdx = int.MinValue;   // GUI not populated yet — retry
            return;
        }
        API.LogInfo($"[SF6Access] Device item [{idx}]: {msg}");
        Speak(msg, interrupt: !_queueNextItemAnnounce);
        _queueNextItemAnnounce = false;
    }
}
