using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Ranked/casual match standby screen (app.UIFlowMatchingSetting). Announces
/// league points on entry, tab changes, and any on-screen value text change
/// (BGM, commentator, controller, outfit…). Focused-row reading is handled by
/// the generic FocusChanged path plus the group poller for each settings tab.
///
/// Built on the ScreenAdapter foundation: the base class owns the poll lifecycle
/// and stale-Param re-entry; this class just wires the archetype readers
/// (TabWatcher, ValueTextWatcher, GroupFocusPoller). Registered in ScreenRegistry.
/// </summary>
public sealed class MatchingSettingHooks : SingleParamScreenAdapter
{
    protected override string ParamType => "app.UIFlowMatchingSetting.Param";

    public MatchingSettingHooks()
    {
        SearchInterval = 60;
        ReadInterval = 10;
    }

    // Value texts shown across the settings tabs — announced when they change.
    private readonly ValueTextWatcher _values = new(
        "MatchingSetting",
        "TextOperation", "TextPreset", "TextAnimType", "TextSkin", "TextEffect",
        "TextEffectColor", "TextSound", "TextSoundType", "TextController", "TextBgm",
        "TextCommentatorEnable", "TextCommentator", "TextCaster", "TextCheerOnline",
        "TextCommentatorVolume", "TextCommentatorSubtitles", "TextSide", "TextBattleHud",
        "TextLeaguePoint", "TextMasterLeaguePoint", "TextCirtifiedCount");

    private readonly TabWatcher _tab = new("MatchingSetting");

    // Row groups of every settings tab. The Param's own Group wraps whole tab
    // panels — the poller's segment cap skips those, and the nested groups read
    // the actual focused row.
    private readonly GroupFocusPoller _focus = new(
        "MatchingSetting", announceFirst: false,
        new GroupFocusPoller.Source(null, "Group"),
        new GroupFocusPoller.Source("MatchingSettingMatching", "mGroup"),
        new GroupFocusPoller.Source("MatchingSettingBattle", "mGroup"),
        new GroupFocusPoller.Source("MatchingSettingBattle", "mSimpleList"),
        new GroupFocusPoller.Source("FighterProfileSetting", "mGroup"),
        new GroupFocusPoller.Source("FighterProfileSetting", "mTopList"));

    protected override void OnBind()
    {
        _tab.Bind(FlowHelper.GetObjectField(Param, "TabList"));
        _focus.Reset();
        _values.Bind(Param);
        API.LogInfo("[SF6Access] MatchingSetting active");

        // League/rank info shown on the matching tab, announced on entry.
        string league = _values.Compose(
            "TextLeaguePoint", "TextMasterLeaguePoint", "TextCirtifiedCount");
        if (league != null)
            ScreenReaderService.Speak(league, interrupt: false);
    }

    protected override void OnExit()
    {
        API.LogInfo("[SF6Access] MatchingSetting ended");
        _tab.Reset();
        _focus.Reset();
        _values.Reset();
    }

    protected override void Poll()
    {
        _tab.Poll();
        _focus.Poll(Param);
        _values.Poll();
    }
}
