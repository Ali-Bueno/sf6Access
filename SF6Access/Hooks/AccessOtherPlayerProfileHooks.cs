using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// When you walk up to another player in the Battle Hub and open the access
/// menu (app.UIFlowAccessOtherPlayerMenu.FlowParam), the menu OPTIONS already
/// read via GroupFocusHooks, but the opponent's profile shown alongside does
/// not. This hook reads that profile once when the menu appears.
///
/// The profile lives in two HUD GUIs (verified element names in the 2026-06-16
/// auto-dump):
/// - "BattleHubContactPanel_OtherPlayer": e_text_name (the avatar name).
/// - "CFNFighterProfileSimpleTop": e_text_fid_name (CFN name), e_text_title,
///   e_text_lp_num (league points), e_text_mr_num (master rating).
/// Reading is gated on the access menu being active so the profile GUIs are not
/// picked up in unrelated CFN contexts (and to avoid Battle Hub ambient noise).
/// Migrated to ScreenAdapter.
/// </summary>
public sealed class AccessOtherPlayerProfileHooks : SingleParamScreenAdapter
{
    private const string PANEL_GUI = "BattleHubContactPanel_OtherPlayer";
    private const string PROFILE_GUI = "CFNFighterProfileSimpleTop";

    protected override string ParamType => "app.UIFlowAccessOtherPlayerMenu.FlowParam";

    public AccessOtherPlayerProfileHooks()
    {
        SearchInterval = 15;
        ReadInterval = 15;
    }

    private bool _announced;
    private int _retries;

    protected override void OnBind()
    {
        // Menu just appeared: the profile GUIs fill a few frames later — retry.
        _announced = false;
        _retries = 12;
    }

    protected override void Poll()
    {
        if (_announced) return;

        string text = BuildProfile();
        if (string.IsNullOrEmpty(text))
        {
            if (--_retries > 0) return;
            _announced = true;
            return;
        }

        _announced = true;
        API.LogInfo($"[SF6Access] Player profile: {text}");
        ScreenReaderService.Speak(text, interrupt: false);
    }

    /// <summary>
    /// "{name}. {title}. LP {lp}. MR {mr}" from the two profile HUD GUIs.
    /// Null until at least a name resolves.
    /// </summary>
    private static string BuildProfile()
    {
        string name = null, title = null, lp = null, mr = null;

        foreach (var (owner, view) in GuiTextReader.FindGuiViews(PANEL_GUI))
        {
            foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
            {
                if (t.Name == "e_text_name" && !string.IsNullOrWhiteSpace(t.Text))
                    name ??= t.Text.Trim();
            }
        }

        foreach (var (owner, view) in GuiTextReader.FindGuiViews(PROFILE_GUI))
        {
            foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
            {
                if (string.IsNullOrWhiteSpace(t.Text)) continue;
                switch (t.Name)
                {
                    case "e_text_fid_name": name ??= t.Text.Trim(); break;
                    case "e_text_title": title ??= t.Text.Trim(); break;
                    case "e_text_lp_num": lp ??= t.Text.Trim(); break;
                    case "e_text_mr_num": mr ??= t.Text.Trim(); break;
                }
            }
        }

        if (string.IsNullOrEmpty(name)) return null;

        var parts = new System.Collections.Generic.List<string> { name };
        if (!string.IsNullOrEmpty(title)) parts.Add(title);
        if (!string.IsNullOrEmpty(lp)) parts.Add($"LP {lp}");
        if (!string.IsNullOrEmpty(mr)) parts.Add($"MR {mr}");
        return string.Join(". ", parts);
    }
}
