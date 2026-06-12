using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the training reversal move selection lists (opened from
/// the reversal rows of the training menu). One flow Param exists per move
/// category (Normal/CommandNormal/Special/SA/Recording/Common), each with a
/// focus index ("FoucsIndex" — game's own typo; "FocusIndex" on Special) and a
/// TrainingSkillData[] whose SkillMessage Guid is the localized move name.
/// </summary>
public class TrainingReversalHooks
{
    private const string PARAM_PREFIX = "app.training.UIFlowTrainingMenu_Reversal";
    private const string MAIN_PARAM = "app.training.UIFlowTrainingMenu_Reversal.Param";

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 30;
    private const int POLL_READ_INTERVAL = 5;

    private static bool _isActive;
    private static readonly List<(string typeName, ManagedObject param)> _params = new();
    private static readonly Dictionary<string, int> _lastFocus = new();
    private static int _lastTabIndex = -2;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] TrainingReversalHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            _params.Clear();
            _params.AddRange(FlowHelper.FindFlowParamsByPrefix(PARAM_PREFIX));

            bool active = _params.Count > 0;
            if (active && !_isActive)
            {
                _isActive = true;
                _lastFocus.Clear();
                _lastTabIndex = -2;
                API.LogInfo($"[SF6Access] Reversal menu active ({_params.Count} params)");
            }
            else if (!active && _isActive)
            {
                _isActive = false;
                _lastFocus.Clear();
                API.LogInfo("[SF6Access] Reversal menu ended");
            }
        }

        if (!_isActive || _pollCounter % POLL_READ_INTERVAL != 0) return;

        foreach (var (typeName, param) in _params)
        {
            if (typeName == MAIN_PARAM)
                PollTab(param);
            else
                PollMoveList(typeName, param);
        }
    }

    /// <summary>Category tab of the reversal screen (Normal/Special/SA...).</summary>
    private static void PollTab(ManagedObject param)
    {
        int tab = FlowHelper.ReadIntField(param, "TabIndex");
        if (tab < 0 || tab == _lastTabIndex) return;

        bool first = _lastTabIndex == -2;
        _lastTabIndex = tab;
        if (first) return;

        string title = null;
        try
        {
            var titles = FlowHelper.GetObjectField(param, "PrimaryTitle");
            title = FlowHelper.Call(titles, "Get", tab) as string;
        }
        catch { }

        string announcement = !string.IsNullOrEmpty(title)
            ? FlowHelper.CleanTags(title)
            : $"Tab {tab + 1}";
        API.LogInfo($"[SF6Access] Reversal tab [{tab}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    /// <summary>Move rows of one category list.</summary>
    private static void PollMoveList(string typeName, ManagedObject param)
    {
        // "FoucsIndex" is the game's own typo on most categories
        int focus = FlowHelper.ReadIntField(param, "FoucsIndex", int.MinValue);
        if (focus == int.MinValue)
            focus = FlowHelper.ReadIntField(param, "FocusIndex", int.MinValue);
        if (focus < 0) return;

        bool first = !_lastFocus.TryGetValue(typeName, out int last);
        if (!first && focus == last) return;
        _lastFocus[typeName] = focus;
        if (first) return;

        string announcement = ReadSkillName(param, focus) ?? ReadRowText(param);
        if (string.IsNullOrEmpty(announcement)) return;

        API.LogInfo($"[SF6Access] Reversal move [{typeName.Substring(PARAM_PREFIX.Length)},{focus}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    /// <summary>Localized move name from SkillData[focus].SkillMessage.</summary>
    private static string ReadSkillName(ManagedObject param, int focus)
    {
        try
        {
            var skills = FlowHelper.GetObjectField(param, "SkillData");
            var record = FlowHelper.GetListItem(skills, focus);
            return FlowHelper.ResolveGuidField(record, "SkillMessage");
        }
        catch { return null; }
    }

    /// <summary>On-screen text of the focused row in the list's group scroll.</summary>
    private static string ReadRowText(ManagedObject param)
    {
        try
        {
            var group = FlowHelper.GetObjectField(param, "_pGroupScroll");
            var child = FlowHelper.Call(group, "GetFocusChild") as ManagedObject;
            var control = FlowHelper.GetObjectField(child, "Control")
                ?? FlowHelper.Call(child, "get_Control") as ManagedObject;
            return GuiTextReader.ReadControlTextJoined(control);
        }
        catch { return null; }
    }
}
