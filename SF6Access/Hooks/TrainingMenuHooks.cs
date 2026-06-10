using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the training mode pause menu (app.training.TrainingManager).
/// Polls PrimaryIndex/SecondaryIndex for navigation and announces the focused
/// item from CurrentMenuData (_MessageID name + guide message Guids).
/// </summary>
public class TrainingMenuHooks
{
    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    private static ManagedObject _manager;
    private static int _lastPrimary = -1;
    private static int _lastSecondary = -1;

    public static bool IsInTrainingMenu => _isActive;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] TrainingMenuHooks initialized");
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

        if (_pollCounter % POLL_READ_INTERVAL != 0) return;

        if (!IsMenuOpen())
        {
            Reset();
            return;
        }

        PollNavigation();
    }

    private static void TryActivate()
    {
        try
        {
            _manager = API.GetManagedSingleton("app.training.TrainingManager");
        }
        catch { _manager = null; }

        if (_manager == null || !IsMenuOpen()) return;

        _lastPrimary = -1;
        _lastSecondary = -1;
        _isActive = true;
        API.LogInfo("[SF6Access] Training menu opened");

        PollNavigation();
    }

    private static bool IsMenuOpen()
    {
        var result = FlowHelper.Call(_manager, "get_IsMenuOpening");
        return result is bool b && b;
    }

    private static void PollNavigation()
    {
        int primary = FlowHelper.CallInt(_manager, "get_PrimaryIndex");
        int secondary = FlowHelper.CallInt(_manager, "get_SecondaryIndex");

        if (primary == _lastPrimary && secondary == _lastSecondary) return;

        bool first = _lastPrimary == -1 && _lastSecondary == -1;
        _lastPrimary = primary;
        _lastSecondary = secondary;
        if (first) return;

        AnnounceCurrentItem();
    }

    private static void AnnounceCurrentItem()
    {
        var data = FlowHelper.Call(_manager, "get_CurrentMenuData") as ManagedObject;
        if (data == null) return;

        string name = FlowHelper.ResolveGuidField(data, "_MessageID");
        string sub = FlowHelper.ResolveGuidField(data, "_SubMessageID");
        string guide = FlowHelper.ResolveGuidField(data, "_GuideMessage")
                    ?? FlowHelper.ResolveGuidField(data, "_GuideMessageID");

        string announcement = name;
        if (!string.IsNullOrEmpty(sub) && sub != name)
            announcement = string.IsNullOrEmpty(announcement) ? sub : $"{announcement} {sub}";
        if (!string.IsNullOrEmpty(guide) && guide != announcement)
            announcement = string.IsNullOrEmpty(announcement) ? guide : $"{announcement}. {guide}";

        if (string.IsNullOrEmpty(announcement)) return;

        API.LogInfo($"[SF6Access] Training menu [{_lastPrimary},{_lastSecondary}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    private static void Reset()
    {
        API.LogInfo("[SF6Access] Training menu closed");
        _isActive = false;
        _lastPrimary = -1;
        _lastSecondary = -1;
    }
}
