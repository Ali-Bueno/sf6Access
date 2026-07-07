using System.Collections.Generic;
using System.Runtime.InteropServices;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// In-game store (app.UIFlowOnlineShop.UIFlowParam — Fighter Coins / Drive
/// Tickets shop). Adds two things on top of the generic navigation reading:
/// - the focused product's price WITH its currency ("300 Fighter Coins" /
///   "50 Drive Tickets") — the currency is only an icon on screen. Read from
///   the param's CurrentGoodsInfo (Prices/SalePrices pairs of
///   UIFlowOnlineShop.CurrencyType {FIGHTER_COIN_PAID=0, FIGHTER_COIN_FREE=1,
///   TICKET=3} + value), announced a few frames behind the row so it queues.
///   Store-jump products (Steam pricing) have no wallet price — stay quiet.
/// - the G / Square shortcut that reads the current balances, from the param's
///   UIPartsTicketText / UIPartsFighterCoinText widgets (their GetWalletMoney()
///   returns the authoritative value — the on-screen captions are icons).
/// </summary>
public sealed class OnlineShopHooks : SingleParamScreenAdapter
{
    protected override string ParamType => "app.UIFlowOnlineShop.UIFlowParam";

    public OnlineShopHooks()
    {
        ReadInterval = 1;   // the G/Square shortcut needs per-frame edge detection
    }

    // Menu/GUI reads still run at the old cadence; only the shortcut is per-frame.
    private const int MENU_POLL_EVERY = 5;
    // Queue the price behind the product row read by the generic reader.
    private const int PRICE_DELAY_FRAMES = 12;

    // app.UIFlowOnlineShop.CurrencyType
    private const int CURRENCY_FIGHTER_COIN_PAID = 0;
    private const int CURRENCY_FIGHTER_COIN_FREE = 1;
    private const int CURRENCY_TICKET = 3;

    private readonly ReadoutShortcut _currencyShortcut = new();
    private int _frame;
    private ulong _lastGoodsAddr;
    private string _pendingPrice;
    private int _pendingPriceFrame = -1;

    protected override void OnBind()
    {
        _lastGoodsAddr = 0;
        _pendingPrice = null;
        API.LogInfo("[SF6Access] Online shop active");
    }

    protected override void OnExit()
    {
        _lastGoodsAddr = 0;
        _pendingPrice = null;
    }

    protected override void Poll()
    {
        if (_currencyShortcut.Pressed()) AnnounceBalances();

        _frame++;
        if (_frame % MENU_POLL_EVERY == 0) PollGoodsPrice();

        if (_pendingPrice != null && _frame >= _pendingPriceFrame)
        {
            string price = _pendingPrice;
            _pendingPrice = null;
            API.LogInfo($"[SF6Access] Shop goods price: {price}");
            ScreenReaderService.Speak(price, interrupt: false);
        }
    }

    /// <summary>Announce the focused product's price + currency whenever the
    /// param's CurrentGoodsInfo instance changes.</summary>
    private void PollGoodsPrice()
    {
        try
        {
            var goods = FlowHelper.GetObjectField(Param, "CurrentGoodsInfo");
            ulong addr = 0;
            try { addr = goods?.GetAddress() ?? 0; } catch { }
            if (addr == _lastGoodsAddr) return;
            _lastGoodsAddr = addr;
            if (goods == null) return;

            string price = ReadGoodsPrice(goods);
            if (price == null) return;
            _pendingPrice = price;
            _pendingPriceFrame = _frame + PRICE_DELAY_FRAMES;
        }
        catch { }
    }

    /// <summary>"N Fighter Coins" / "N Drive Tickets" for a GoodsInfo — the sale
    /// price when a sale is on, the normal price otherwise. Null when the product
    /// has no wallet price (platform-store products).</summary>
    private static string ReadGoodsPrice(ManagedObject goods)
    {
        var prices = FlowHelper.GetObjectField(goods, "SalePrices");
        if (FlowHelper.GetListCount(prices) == 0 ||
            FlowHelper.ReadByteField(goods, "_IsNowSale") == 0)
            prices = FlowHelper.GetObjectField(goods, "Prices");
        if (FlowHelper.GetListCount(prices) == 0) return null;

        var entry = FlowHelper.Call(prices, "get_Item", 0);
        if (!TryDecodePriceEntry(entry, out int currency, out int value)) return null;

        string currencyName = currency == CURRENCY_TICKET ? "Drive Tickets" : "Fighter Coins";
        return $"{value} {currencyName}";
    }

    /// <summary>
    /// Decode one Prices entry into (currency, value). The entries are
    /// (CurrencyType, int) pairs; a boxed value-tuple is read from raw memory
    /// (two ints), a managed record through its named fields. The currency side
    /// is identified by its enum range {0,1,3} — logged when neither side fits,
    /// so an unexpected shape shows up in the log instead of a wrong readout.
    /// </summary>
    private static bool TryDecodePriceEntry(object entry, out int currency, out int value)
    {
        currency = -1;
        value = 0;
        try
        {
            int a, b;
            if (entry is ManagedObject mo)
            {
                // System.Tuple<CurrencyType,int> (log-confirmed): private
                // m_Item1/m_Item2 fields with get_Item1/get_Item2 getters
                a = FlowHelper.ReadIntField(mo, "m_Item1", int.MinValue);
                b = FlowHelper.ReadIntField(mo, "m_Item2", int.MinValue);
                if (a == int.MinValue || b == int.MinValue)
                {
                    a = FlowHelper.CallInt(mo, "get_Item1", int.MinValue);
                    b = FlowHelper.CallInt(mo, "get_Item2", int.MinValue);
                }
                if (a == int.MinValue || b == int.MinValue)
                {
                    a = FlowHelper.ReadIntField(mo, "Currency", int.MinValue);
                    b = FlowHelper.ReadIntField(mo, "Price", int.MinValue);
                }
                if (a == int.MinValue || b == int.MinValue)
                {
                    API.LogInfo($"[SF6Access] Unknown shop price entry type: {mo.GetTypeDefinition()?.FullName}");
                    return false;
                }
            }
            else if (entry is REFrameworkNET.ValueType vt)
            {
                ulong addr = vt.GetAddress();
                if (addr == 0) return false;
                a = Marshal.ReadInt32((System.IntPtr)(long)addr);
                b = Marshal.ReadInt32((System.IntPtr)(long)addr, 4);
            }
            else
            {
                if (entry != null)
                    API.LogInfo($"[SF6Access] Unknown shop price entry: {entry.GetType().Name}");
                return false;
            }

            bool aIsCurrency = a is CURRENCY_FIGHTER_COIN_PAID or CURRENCY_FIGHTER_COIN_FREE or CURRENCY_TICKET;
            bool bIsCurrency = b is CURRENCY_FIGHTER_COIN_PAID or CURRENCY_FIGHTER_COIN_FREE or CURRENCY_TICKET;
            if (aIsCurrency && !bIsCurrency) { currency = a; value = b; return b > 0; }
            if (bIsCurrency && !aIsCurrency) { currency = b; value = a; return a > 0; }

            API.LogInfo($"[SF6Access] Ambiguous shop price entry: {a}, {b}");
            return false;
        }
        catch { return false; }
    }

    private void AnnounceBalances()
    {
        var parts = new List<string>();
        int tickets = ReadBalance("TicketText");
        int coins = ReadBalance("FighterCoinText");
        if (tickets != int.MinValue) parts.Add($"Drive Tickets {tickets}");
        if (coins != int.MinValue) parts.Add($"Fighter Coins {coins}");
        // Hub goods shop: its gear also costs Zenny, shown by the ShopBg GUI —
        // ShopHooks defers to this single announcement there
        string zenny = CurrencyReader.ReadShownZenny("ShopBg");
        if (!string.IsNullOrEmpty(zenny)) parts.Add($"{zenny} Zenny");
        if (parts.Count == 0)
        {
            API.LogInfo("[SF6Access] Online shop balances not found");
            return;
        }

        string msg = string.Join(". ", parts);
        API.LogInfo($"[SF6Access] Currency: {msg}");
        ScreenReaderService.Speak(msg, interrupt: true);
    }

    private int ReadBalance(string field)
    {
        var widget = FlowHelper.GetObjectField(Param, field);
        return widget == null ? int.MinValue
            : FlowHelper.CallInt(widget, "GetWalletMoney", int.MinValue);
    }
}
