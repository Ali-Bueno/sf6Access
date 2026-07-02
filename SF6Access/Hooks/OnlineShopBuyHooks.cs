using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the shop purchase dialog (app.UIFlowOnlineShopGoodsBuy) — used when
/// buying the Premium Pass / tier skips and any other shop good. Announces the
/// product and price on open (GUI view "OnlineShopBuyDialog") and the focused
/// choice (Use Fighter Coins / Cancel) as the cursor moves through ChoiceList.
/// The generic GroupFocus reader only saw an empty ChoiceList here.
/// Migrated to ScreenAdapter (IsActive kept for MainMenuHooks).
/// </summary>
public sealed class OnlineShopBuyHooks : SingleParamScreenAdapter
{
    private static OnlineShopBuyHooks _self;

    /// <summary>Consumed by MainMenuHooks to suppress the generic focus reader.</summary>
    public static bool IsActive => _self != null && _self.Active;

    private const string DIALOG_GUI = "OnlineShopBuyDialog";

    protected override string ParamType => "app.UIFlowOnlineShopGoodsBuy.UIFlowParam";

    private string _lastChoice;

    public OnlineShopBuyHooks()
    {
        _self = this;
        SearchInterval = 15;
        ReadInterval = 6;
    }

    protected override void OnBind()
    {
        _lastChoice = null;
        API.LogInfo("[SF6Access] Shop buy dialog opened");
        AnnounceProduct();
    }

    protected override void OnExit()
    {
        _lastChoice = null;
        API.LogInfo("[SF6Access] Shop buy dialog closed");
    }

    protected override void Poll() => PollChoice();

    /// <summary>Read the product name + price from the dialog GUI on open.</summary>
    private void AnnounceProduct()
    {
        var texts = ReadDialogTexts();
        if (texts.Count == 0) return;

        var parts = new List<string>();
        if (texts.TryGetValue("e_productname", out var product) && !string.IsNullOrEmpty(product))
            parts.Add(product);
        if (texts.TryGetValue("e_text_count", out var count) && !string.IsNullOrEmpty(count) && count != "1")
            parts.Add($"x{count}");
        if (texts.TryGetValue("e_text_total", out var total) && !string.IsNullOrEmpty(total))
        {
            // e_coin_num_used is the (negative) coin cost; a ticket cost otherwise
            bool coins = texts.TryGetValue("e_coin_num_used", out var c) && c != "0" && !string.IsNullOrEmpty(c);
            parts.Add(coins ? $"Price {total} Fighter Coins" : $"Price {total}");
        }

        // Append the initially-focused choice so it does not interrupt the
        // product line a frame later (the choice poll speaks with interrupt,
        // which was cancelling "Premium Rewards. Price 100" instantly)
        var (idx, choice) = ReadChoice();
        if (!string.IsNullOrEmpty(choice))
        {
            parts.Add(choice);
            _lastChoice = $"{idx}|{choice}";
        }
        if (parts.Count == 0) return;

        string text = string.Join(". ", parts);
        API.LogInfo($"[SF6Access] Shop buy: {text}");
        ScreenReaderService.Speak(text, interrupt: false);
    }

    /// <summary>Announce the focused choice (Use Fighter Coins / Cancel...).</summary>
    private void PollChoice()
    {
        var (idx, text) = ReadChoice();
        if (string.IsNullOrEmpty(text)) return;

        string key = $"{idx}|{text}";
        if (key == _lastChoice) return;
        _lastChoice = key;

        API.LogInfo($"[SF6Access] Shop choice [{idx}]: {text}");
        ScreenReaderService.Speak(text);
    }

    /// <summary>The focused ChoiceList option (index + text).</summary>
    private (int idx, string text) ReadChoice()
    {
        var choiceList = FlowHelper.GetObjectField(Param, "ChoiceList");
        if (choiceList == null) return (-1, null);

        int idx = FlowHelper.CallInt(choiceList, "get_SelectedIndex", -1);
        string text = FlowHelper.ReadSelectedItemText(choiceList);

        // Fallback: read the focused child row directly
        if (string.IsNullOrEmpty(text) && idx >= 0)
        {
            var children = FlowHelper.GetObjectField(choiceList, "_Children");
            var child = FlowHelper.GetListItem(children, idx);
            var control = FlowHelper.GetObjectField(child, "Control")
                ?? FlowHelper.Call(child, "get_Control") as ManagedObject;
            text = GuiTextReader.ReadControlTextJoined(control);
        }
        return (idx, string.IsNullOrEmpty(text) ? null : text.Trim());
    }

    private static Dictionary<string, string> ReadDialogTexts()
    {
        var result = new Dictionary<string, string>();
        try
        {
            foreach (var (owner, view) in GuiTextReader.FindGuiViews(DIALOG_GUI))
            {
                foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
                {
                    if (string.IsNullOrWhiteSpace(t.Text) || t.Name == null) continue;
                    string s = FlowHelper.ResolvePlatformTags(t.Text).Replace('\n', ' ').Trim();
                    s = FlowHelper.CleanTags(s);
                    if (!string.IsNullOrEmpty(s) && !result.ContainsKey(t.Name))
                        result[t.Name] = s;
                }
            }
        }
        catch { }
        return result;
    }
}
