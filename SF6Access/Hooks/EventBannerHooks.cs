using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the current-events banner in the multi menu
/// (UIStartMenu.FlowParam._MenuAnnounceBanner — UIPartsAnnounceBanner with a
/// _BannerList scroll list of "[Event] ..." entries). Announces the focused
/// event when navigating the banner.
///
/// ScreenAdapter; MainMenuHooks calls AnnounceCurrent() when the banner gains
/// menu focus. Registered in ScreenRegistry.
/// </summary>
public sealed class EventBannerHooks : ScreenAdapter
{
    private const string START_MENU_TYPE = "app.UIStartMenu.FlowParam";
    private static readonly string[] Types = { START_MENU_TYPE };
    public override string[] OwnedTypes => Types;

    private static EventBannerHooks _self;

    public EventBannerHooks()
    {
        SearchInterval = 60;
        ReadInterval = 5;
        _self = this;
    }

    private ManagedObject _banner;
    private ManagedObject _mainGroup;
    private int _lastFocus = -2;
    private string _lastText;

    protected override bool Locate()
    {
        var startParam = FlowHelper.FindFlowParam(START_MENU_TYPE);
        _banner = FlowHelper.GetObjectField(startParam, "_MenuAnnounceBanner");
        _mainGroup = FlowHelper.GetObjectField(startParam, "_MainGroup");
        return _banner != null;
    }

    protected override void OnDeactivate()
    {
        _banner = null;
        _mainGroup = null;
        _lastFocus = -2;
        _lastText = null;
    }

    protected override void OnPoll()
    {
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

            _lastFocus = focus;
            Announce(); // gaining focus or moving: read the current event
        }
        catch { _banner = null; }
    }

    /// <summary>True when the start menu's focused group child IS the banner.</summary>
    private bool IsBannerFocused()
    {
        try
        {
            var focusChild = FlowHelper.Call(_mainGroup, "GetFocusChild") as ManagedObject;
            if (focusChild == null || _banner == null) return false;
            return focusChild.GetAddress() == _banner.GetAddress();
        }
        catch { return false; }
    }

    /// <summary>Announce the focused event entry (called on banner focus by MainMenuHooks).</summary>
    public static void AnnounceCurrent() => _self?.Announce();

    private void Announce()
    {
        if (_banner == null) return;

        var list = FlowHelper.GetObjectField(_banner, "_BannerList");
        string text = FlowHelper.ReadSelectedItemText(list)
            ?? FlowHelper.ReadListRowText(list, _lastFocus < 0 ? 0 : _lastFocus);
        if (string.IsNullOrEmpty(text) || text == _lastText) return;
        _lastText = text;

        API.LogInfo($"[SF6Access] Event banner [{_lastFocus}]: {text}");
        Speak(text);
    }
}
