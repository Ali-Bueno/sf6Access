using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the current-events banner in the multi menu
/// (UIStartMenu.FlowParam._MenuAnnounceBanner — UIPartsAnnounceBanner with a
/// _BannerList scroll list of "[Event] ..." entries). Announces the focused
/// event when navigating the banner.
/// </summary>
public class EventBannerHooks
{
    private const string START_MENU_TYPE = "app.UIStartMenu.FlowParam";

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    private static ManagedObject _banner;
    private static ManagedObject _mainGroup;
    private static int _lastFocus = -2;
    private static string _lastText;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] EventBannerHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var startParam = FlowHelper.FindFlowParam(START_MENU_TYPE);
            _banner = FlowHelper.GetObjectField(startParam, "_MenuAnnounceBanner");
            _mainGroup = FlowHelper.GetObjectField(startParam, "_MainGroup");
            if (_banner == null) { _lastFocus = -2; _lastText = null; }
        }

        if (_banner == null || _pollCounter % POLL_READ_INTERVAL != 0) return;

        try
        {
            // The banner auto-rotates on a timer — only announce while the
            // menu focus is actually ON the banner, or it talks over the
            // other menu options
            if (!IsBannerFocused())
            {
                _lastFocus = -2;
                return;
            }

            // The banner exposes FocusIndex AND an inner _BannerList whose
            // SelectedIndex moves when browsing events — watch both
            int focus = FlowHelper.CallInt(_banner, "get_FocusIndex");
            if (focus < 0)
            {
                var list = FlowHelper.GetObjectField(_banner, "_BannerList");
                focus = FlowHelper.CallInt(list, "get_SelectedIndex");
            }
            if (focus < 0 || focus == _lastFocus) return;

            bool first = _lastFocus == -2;
            _lastFocus = focus;
            if (first)
            {
                AnnounceCurrent(); // gaining focus: read the current event
                return;
            }

            AnnounceCurrent();
        }
        catch { _banner = null; }
    }

    /// <summary>True when the start menu's focused group child IS the banner.</summary>
    private static bool IsBannerFocused()
    {
        try
        {
            var focusChild = FlowHelper.Call(_mainGroup, "GetFocusChild") as ManagedObject;
            if (focusChild == null || _banner == null) return false;
            return focusChild.GetAddress() == _banner.GetAddress();
        }
        catch { return false; }
    }

    /// <summary>Announce the focused event entry (also callable on banner focus).</summary>
    public static void AnnounceCurrent()
    {
        if (_banner == null) return;

        var list = FlowHelper.GetObjectField(_banner, "_BannerList");
        string text = FlowHelper.ReadSelectedItemText(list)
            ?? FlowHelper.ReadListRowText(list, _lastFocus < 0 ? 0 : _lastFocus);
        if (string.IsNullOrEmpty(text) || text == _lastText) return;
        _lastText = text;

        API.LogInfo($"[SF6Access] Event banner [{_lastFocus}]: {text}");
        ScreenReaderService.Speak(text);
    }
}
