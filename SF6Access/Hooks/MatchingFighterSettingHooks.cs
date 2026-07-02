using SF6Access.Services;
using SF6Access.Services.Ui;
using REFrameworkNET;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the ranked "edit character" screen opened with T from the
/// match standby screen (app.UIFlowMatchingFighterSetting). The Param's own
/// Group/TabList only wrap whole tab panels — the actual rows live in nested
/// parts per tab: fighter capture settings (spin rows), title plate grid and
/// new-challenger customize (spin rows).
/// Migrated to ScreenAdapter (IsInFighterSetting kept for MainMenuHooks).
/// </summary>
public sealed class MatchingFighterSettingHooks : SingleParamScreenAdapter
{
    private static MatchingFighterSettingHooks _self;

    /// <summary>Consumed by MainMenuHooks to suppress the generic focus reader.</summary>
    public static bool IsInFighterSetting => _self != null && _self.Active;

    protected override string ParamType => "app.UIFlowMatchingFighterSetting.Param";

    private readonly GroupFocusPoller _focus = new(
        "MatchingFighterSetting", announceFirst: true,
        new GroupFocusPoller.Source(null, "Group"),
        new GroupFocusPoller.Source(null, "TabList", isList: true),
        new GroupFocusPoller.Source("MatchingFighterSetting", "mGroup"),
        new GroupFocusPoller.Source("MatchingTitleSetting", "mGroup"),
        new GroupFocusPoller.Source("MatchingTitleSetting", "mScrollGrid", isList: true),
        new GroupFocusPoller.Source("MatchingTitleSetting", "mTabScrollList", isList: true),
        new GroupFocusPoller.Source("MatchingNewChallengerCustomize", "mGroup"));

    public MatchingFighterSettingHooks()
    {
        _self = this;
        SearchInterval = 60;
        ReadInterval = 5;
    }

    protected override void OnBind()
    {
        _focus.Reset();
        API.LogInfo("[SF6Access] MatchingFighterSetting active");
    }

    protected override void OnExit()
    {
        _focus.Reset();
        API.LogInfo("[SF6Access] MatchingFighterSetting ended");
    }

    protected override void Poll() => _focus.Poll(Param);
}
