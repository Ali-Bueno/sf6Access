using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the ranked "edit character" screen opened with T from the
/// match standby screen (app.UIFlowMatchingFighterSetting). The Param's own
/// Group/TabList only wrap whole tab panels — the actual rows live in nested
/// parts per tab: fighter capture settings (spin rows), title plate grid and
/// new-challenger customize (spin rows).
/// </summary>
public class MatchingFighterSettingHooks
{
    private const string PARAM_TYPE = "app.UIFlowMatchingFighterSetting.Param";

    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    private static ManagedObject _param;

    private static readonly GroupFocusPoller FocusPoller = new(
        "MatchingFighterSetting", announceFirst: true,
        new GroupFocusPoller.Source(null, "Group"),
        new GroupFocusPoller.Source(null, "TabList", isList: true),
        new GroupFocusPoller.Source("MatchingFighterSetting", "mGroup"),
        new GroupFocusPoller.Source("MatchingTitleSetting", "mGroup"),
        new GroupFocusPoller.Source("MatchingTitleSetting", "mScrollGrid", isList: true),
        new GroupFocusPoller.Source("MatchingTitleSetting", "mTabScrollList", isList: true),
        new GroupFocusPoller.Source("MatchingNewChallengerCustomize", "mGroup"));

    public static bool IsInFighterSetting => _isActive;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] MatchingFighterSettingHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (!_isActive)
        {
            if (_pollCounter % POLL_SEARCH_INTERVAL != 0) return;
            TryActivate();
            return;
        }

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var current = FlowHelper.TrackFlowParam(PARAM_TYPE, _param, out bool changed);
            if (current == null)
            {
                Reset();
                return;
            }
            if (changed)
            {
                // Menu was recreated — re-bind the param
                _param = current;
                FocusPoller.Reset();
            }
        }

        if (_pollCounter % POLL_READ_INTERVAL == 0)
            FocusPoller.Poll(_param);
    }

    private static void TryActivate()
    {
        var param = FlowHelper.FindFlowParam(PARAM_TYPE);
        if (param == null) return;

        _param = param;
        FocusPoller.Reset();
        _isActive = true;
        API.LogInfo("[SF6Access] MatchingFighterSetting active");
    }

    private static void Reset()
    {
        API.LogInfo("[SF6Access] MatchingFighterSetting ended");
        _isActive = false;
        _param = null;
        FocusPoller.Reset();
    }
}
