using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the training shortcut settings menu (app.UIFlowShortcutSetting).
/// Param fields (verified via F9 dump): _MenuList (UIPartsGroupScroll with _FocusIndex)
/// and ShortcutData (app.ShortcutSettingData[] with ItemMessage/GuideMessage Guids).
/// </summary>
public class ShortcutSettingHooks
{
    private const string PARAM_TYPE = "app.UIFlowShortcutSetting.Param";

    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    private static ManagedObject _param;
    private static ManagedObject _menuList;
    private static ManagedObject _shortcutData;
    private static int _lastFocusIndex = -2;

    public static bool IsInShortcutSetting => _isActive;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] ShortcutSettingHooks initialized");
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

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0 && FlowHelper.FindFlowParam(PARAM_TYPE) == null)
        {
            Reset();
            return;
        }

        if (_pollCounter % POLL_READ_INTERVAL == 0)
            PollFocus();
    }

    private static void TryActivate()
    {
        var param = FlowHelper.FindFlowParam(PARAM_TYPE);
        if (param == null) return;

        _param = param;
        _menuList = FlowHelper.GetObjectField(param, "_MenuList");
        _shortcutData = FlowHelper.GetObjectField(param, "ShortcutData");
        _lastFocusIndex = -2;
        _isActive = true;

        API.LogInfo($"[SF6Access] Shortcut settings active (menuList={_menuList != null}, " +
            $"data={FlowHelper.GetListCount(_shortcutData)} items)");

        PollFocus();
    }

    private static void PollFocus()
    {
        if (_menuList == null) return;

        int focusIdx = FlowHelper.ReadIntField(_menuList, "_FocusIndex");
        if (focusIdx < 0 || focusIdx == _lastFocusIndex) return;

        bool first = _lastFocusIndex == -2;
        _lastFocusIndex = focusIdx;
        if (first) return;

        AnnounceItem(focusIdx);
    }

    private static void AnnounceItem(int index)
    {
        var data = FlowHelper.GetListItem(_shortcutData, index);
        if (data == null)
        {
            API.LogInfo($"[SF6Access] Shortcut item [{index}]: no data");
            return;
        }

        string name = FlowHelper.ResolveGuidField(data, "ItemMessage");
        string guide = FlowHelper.ResolveGuidField(data, "GuideMessage");

        string announcement = name;
        if (!string.IsNullOrEmpty(guide) && guide != name)
            announcement = string.IsNullOrEmpty(announcement) ? guide : $"{announcement}. {guide}";

        if (string.IsNullOrEmpty(announcement))
        {
            announcement = $"Shortcut {index + 1}";
        }

        API.LogInfo($"[SF6Access] Shortcut [{index}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    private static void Reset()
    {
        API.LogInfo("[SF6Access] Shortcut settings ended");
        _isActive = false;
        _param = null;
        _menuList = null;
        _shortcutData = null;
        _lastFocusIndex = -2;
    }
}
