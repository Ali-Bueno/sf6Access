using System;
using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Tracks active UIFlow handles to detect game state transitions
/// (character select, stage select, news, training, etc.) and announce them.
/// Also logs flow names for research/discovery.
/// </summary>
public class FlowTrackerHooks
{
    private static ManagedObject _flowManager;
    private static Field _handlesField;
    private static int _frameCounter;
    private static bool _initialized;

    // Track known active flows to detect transitions
    private static readonly HashSet<string> _activeFlows = new();
    private static readonly HashSet<string> _announcedFlows = new();

    /// <summary>Current active game context (most recent interesting flow)</summary>
    public static string CurrentContext { get; private set; }

    /// <summary>True when an active flow's type name contains the given fragment (polled every ~0.5s).</summary>
    public static bool IsFlowActive(string nameContains)
    {
        foreach (var flow in _activeFlows)
        {
            if (flow.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // Map flow type names to readable announcements
    private static readonly Dictionary<string, string> FlowAnnouncements = new()
    {
        // Character select
        { "UIFlowFighterSelect", "Character Select" },
        { "UIFlowCharaSelect", "Character Select" },
        // Stage select
        { "UIFlowStageSelect", "Stage Select" },
        // Training / Tutorial
        { "UIFlowTrainingMenu", "Training Menu" },
        { "UIFlowTrainingTop", "Training" },
        // Option menu
        { "UIFlowOption", "Options" },
        { "UIFlowSystemOption", "Options" },
        // Information / News
        { "UIFlowInformation", "Information" },
        { "UIFlowNews", "News" },
        { "UIFlowNotice", "Notices" },
        // Multi menu
        { "UIFlowMultiMenuTop", "Multi Menu" },
        // Gallery
        { "UIFlowGallery", "Gallery" },
        // Shop
        { "UIFlowShop", "Shop" },
        // Arcade
        { "UIFlowArcade", "Arcade" },
        // Versus
        { "UIFlowVs", "Versus" },
        // Battle Settings (pre-fight)
        { "UIFlowMatchingSetting", "Battle Settings" },
        { "UIFlowVersusRuleMain", "Versus Rules" },
        { "UIFlowCommentatorSelect", "Commentator Select" },
        { "UIFlowCommentatorSetting", "Commentator Settings" },
        // Side select (Player/CPU)
        { "UIFlowSideSelect", "Side Select" },
        // Battle Hub
        { "UIFlowBattleHub", "Battle Hub" },
        // Replay
        { "UIFlowReplay", "Replay" },
        // Profile
        { "UIFlowProfile", "Profile" },
        { "UICFNFightersProfile", "Profile" },
        // Rewards (fighting pass)
        { "UIFlowReward", "Rewards" },
        // Avatar status menu (equip / moves)
        { "UIStatusMenu", "Status Menu" },
        // Character guides
        { "UI11413", "Character Guides" },
        // Fighting Ground main
        { "UIFlowFGMainMenu", "Fighting Ground" },
        // Results
        { "UIFlowBattleResult", "Results" },
        { "UIFlowResult", "Results" },
        // Custom Room
        { "UIFlowCustomRoom", "Custom Room" },
        // World Tour
        { "UIFlowWorldTour", "World Tour" },
        // Boot title screen (the prompt renders as an image; despite the
        // game saying "any button", only these inputs actually advance). The
        // keyboard default is F, not X (tester-confirmed).
        { "UIFlowTitle", "Title screen. Press F on keyboard, A on Xbox controller, or Cross on PlayStation controller" },
    };

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        // Poll every 30 frames (~0.5s at 60fps)
        if (++_frameCounter % 30 != 0) return;

        if (!_initialized)
        {
            TryInitialize();
            return;
        }

        try
        {
            PollActiveFlows();
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] FlowTracker error: {ex.Message}");
            _flowManager = null;
            _initialized = false;
        }
    }

    private static void TryInitialize()
    {
        try
        {
            _flowManager = API.GetManagedSingleton("app.UIFlowManager");
            if (_flowManager == null) return;

            _handlesField = _flowManager.GetTypeDefinition()?.GetField("_Handles");
            if (_handlesField == null)
            {
                _flowManager = null;
                return;
            }

            _initialized = true;
            API.LogInfo("[SF6Access] FlowTracker initialized");
        }
        catch { }
    }

    private static void PollActiveFlows()
    {
        if (_flowManager == null || _handlesField == null) return;

        var handles = _handlesField.GetDataBoxed(typeof(object), _flowManager.GetAddress(), false) as ManagedObject;
        if (handles == null) return;

        var countMethod = handles.GetTypeDefinition()?.GetMethod("get_Count");
        var getItemMethod = handles.GetTypeDefinition()?.GetMethod("get_Item(System.Int32)");
        if (countMethod == null || getItemMethod == null) return;

        var countObj = countMethod.InvokeBoxed(typeof(int), handles, Array.Empty<object>());
        int count = countObj != null ? Convert.ToInt32(countObj) : 0;

        var currentFlows = new HashSet<string>();

        for (int i = 0; i < count && i < 30; i++)
        {
            try
            {
                var handle = getItemMethod.InvokeBoxed(typeof(object), handles, new object[] { i }) as ManagedObject;
                if (handle == null) continue;

                var td = handle.GetTypeDefinition();
                string typeName = td?.FullName ?? td?.Name;
                if (string.IsNullOrEmpty(typeName)) continue;

                // Extract the short flow name (after last dot)
                string shortName = typeName;
                int lastDot = typeName.LastIndexOf('.');
                if (lastDot >= 0 && lastDot < typeName.Length - 1)
                    shortName = typeName.Substring(lastDot + 1);

                // Also check inner type names (e.g., "UIFlowManager.Handle" wrapping a flow)
                // Try to get the actual flow type from the handle
                string flowTypeName = TryGetFlowTypeName(handle);
                if (!string.IsNullOrEmpty(flowTypeName))
                    shortName = flowTypeName;

                currentFlows.Add(shortName);
            }
            catch { }
        }

        // Detect new flows (appeared since last poll)
        foreach (var flow in currentFlows)
        {
            if (!_activeFlows.Contains(flow))
            {
                OnFlowStarted(flow);
            }
        }

        // Detect ended flows
        foreach (var flow in _activeFlows)
        {
            if (!currentFlows.Contains(flow))
            {
                OnFlowEnded(flow);
            }
        }

        _activeFlows.Clear();
        foreach (var f in currentFlows)
            _activeFlows.Add(f);
    }

    private static bool _fieldsDiscovered;
    private static string[] _handleFieldNames;

    private static string TryGetFlowTypeName(ManagedObject handle)
    {
        // Discover Handle fields once
        if (!_fieldsDiscovered)
        {
            _fieldsDiscovered = true;
            try
            {
                var td = handle.GetTypeDefinition();
                if (td != null)
                {
                    var fields = td.GetFields();
                    if (fields != null)
                    {
                        var names = new List<string>();
                        foreach (var f in fields)
                        {
                            try { names.Add(f.Name); } catch { }
                        }
                        _handleFieldNames = names.ToArray();
                        API.LogInfo($"[SF6Access] Handle fields: {string.Join(", ", _handleFieldNames)}");
                    }
                }
            }
            catch { }
        }

        // Try all known field patterns that might contain the flow object
        // Handle has <Element>k__BackingField which is the actual UIFlow
        string[] fieldNames = _handleFieldNames ?? new[] {
            "<Element>k__BackingField", "Element", "_Element",
            "Flow", "_Flow", "<Flow>k__BackingField",
        };

        foreach (var fieldName in fieldNames)
        {
            try
            {
                var flowObj = handle.GetField(fieldName) as ManagedObject;
                if (flowObj == null) continue;
                var td = flowObj.GetTypeDefinition();
                string name = td?.FullName;
                if (string.IsNullOrEmpty(name)) continue;

                // Skip wrapper types - look for the actual UIFlow type
                if (name == "app.UIFlowManager.Handle" || name == "app.UIFlowManager.Element")
                {
                    // Element wraps the actual flow - try to get the concrete type
                    // The Element's runtime type might be a subclass with a more specific name
                    // or it might have its own fields pointing to the flow
                    continue;
                }

                // Full type name: short names made distinct flows indistinguishable
                return name;
            }
            catch { }
        }

        // Fallback: identify the flow by its Param's FULL type name — short
        // names collapsed distinct flows into "Param"/"UIFlowParam" in the log,
        // which made new menus impossible to identify from session logs
        try
        {
            var param = handle.GetField("<Param>k__BackingField") as ManagedObject;
            if (param != null)
            {
                string name = param.GetTypeDefinition()?.FullName;
                if (!string.IsNullOrEmpty(name) && name != "app.UIFlowParamBase")
                    return name;
            }
        }
        catch { }

        return null;
    }

    private static void OnFlowStarted(string flowName)
    {
        API.LogInfo($"[SF6Access] Flow started: {flowName}");

        // F8 auto-dump mode: capture every new screen without pressing F9
        ObjectDumper.QueueAutoDump(flowName);

        // Check if we have an announcement for this flow
        string announcement = FindAnnouncement(flowName);
        if (announcement != null)
        {
            CurrentContext = announcement;
            if (!_announcedFlows.Contains(flowName))
            {
                _announcedFlows.Add(flowName);
                ScreenReaderService.Speak(announcement);
                API.LogInfo($"[SF6Access] Announced flow: {announcement}");
            }
        }
    }

    private static void OnFlowEnded(string flowName)
    {
        API.LogInfo($"[SF6Access] Flow ended: {flowName}");
        _announcedFlows.Remove(flowName);

        string announcement = FindAnnouncement(flowName);
        if (announcement != null && CurrentContext == announcement)
            CurrentContext = null;
    }

    private static string FindAnnouncement(string flowName)
    {
        // The title-screen prompt is a full instruction sentence — localized
        // (the mode NAMES in the table are brand names the game itself keeps
        // in English across languages).
        if (flowName.Contains("UIFlowTitle", StringComparison.OrdinalIgnoreCase))
            return LocalizedText.TitleScreenPrompt();

        // Exact match first (titles localize via screen.* lang keys; the
        // table holds the English fallbacks)
        if (FlowAnnouncements.TryGetValue(flowName, out var exact))
            return LangFile.GetByText("screen", exact);

        // Partial match (flow name contains a key)
        foreach (var kvp in FlowAnnouncements)
        {
            if (flowName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return LangFile.GetByText("screen", kvp.Value);
        }

        return null;
    }
}
