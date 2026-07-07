using REFrameworkNET;

namespace SF6Access.Services;

/// <summary>
/// On-demand currency readouts. The World Tour money (Zenny) comes from
/// WTPlayerManager.LocalPlayerData.Wallet.Money — Wallet/Money are getter
/// properties (no backing field), so the field reads fall back to the get_
/// accessors. Currency names are kept as the game's proper nouns.
/// </summary>
public static class CurrencyReader
{
    /// <summary>The avatar's Zenny, or int.MinValue when unavailable.</summary>
    public static int ReadZenny()
    {
        try
        {
            var mgr = API.GetManagedSingleton("app.worldtour.WTPlayerManager") as ManagedObject;
            var playerData = FlowHelper.GetObjectField(mgr, "LocalPlayerData")
                ?? FlowHelper.Call(mgr, "get_LocalPlayerData") as ManagedObject;
            var wallet = FlowHelper.GetObjectField(playerData, "Wallet")
                ?? FlowHelper.Call(playerData, "get_Wallet") as ManagedObject;
            if (wallet == null) return int.MinValue;

            int money = FlowHelper.ReadIntField(wallet, "Money", int.MinValue);
            if (money == int.MinValue) money = FlowHelper.CallInt(wallet, "get_Money", int.MinValue);
            return money;
        }
        catch { return int.MinValue; }
    }

    /// <summary>
    /// Announce the avatar's Zenny (World Tour / avatar shops). Prefers the
    /// on-screen money text of the given GUI owner (its FIRST e_text_total —
    /// ShopBg / ui50201 render the money there): the Wallet getter can
    /// access-violate when the shortcut button doubles as a game action in the
    /// same frame (log-confirmed c0000005 → spoke "0 Zenny" on pad presses).
    /// </summary>
    public static void AnnounceZenny(string guiOwner = null)
    {
        string shown = guiOwner != null ? ReadFirstTotalText(guiOwner) : null;
        string msg;
        if (!string.IsNullOrEmpty(shown))
        {
            msg = $"{shown} Zenny";
        }
        else
        {
            int money = ReadZenny();
            if (money == int.MinValue)
            {
                API.LogInfo("[SF6Access] Zenny readout: wallet not found");
                return;
            }
            msg = $"{money} Zenny";
        }
        API.LogInfo($"[SF6Access] Currency: {msg}");
        ScreenReaderService.Speak(msg, interrupt: true);
    }

    /// <summary>The Zenny amount shown by a GUI owner (its first e_text_total —
    /// ShopBg / ui50201 render the money there), or null.</summary>
    public static string ReadShownZenny(string guiOwner) => ReadFirstTotalText(guiOwner);

    private static string ReadFirstTotalText(string guiOwner)
    {
        try
        {
            foreach (var t in GuiTextReader.ReadTextsByOwner(guiOwner))
                if (t.Name == "e_text_total" && !string.IsNullOrWhiteSpace(t.Text))
                    return t.Text.Trim();
        }
        catch { }
        return null;
    }
}
