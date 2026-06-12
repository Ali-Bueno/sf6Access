using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for pre-fight versus rule settings (app.menu.UIFlowVersusRuleMain).
/// Handles: setting navigation (rounds, timer, match count, commentator, ready),
/// value changes via spin controls, and commentator/caster selection.
/// </summary>
public class BattleSettingsHooks
{
    private const string RULE_PARAM_TYPE = "app.menu.UIFlowVersusRuleMain.Param";
    private const string COMMENTATOR_PARAM_TYPE = "app.UIFlowCommentatorSelect.Param";

    private static ManagedObject _ruleParam;
    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    // TDB cache
    private static Method _msgGetMethod;
    private static bool _tdbCached;

    // Navigation state
    private static int _focusedSettingIndex = -1;
    private static string _lastSpinValue = "";

    // Cached arrays from the Param
    private static ManagedObject _tateList;       // via.gui.Text[] (displays current VALUES)
    private static ManagedObject _messDataArr;    // SpinText_MessageList[] (has label Guids)
    private static ManagedObject _settingTypeArr; // SettingType[] (enum for fallback labels)

    // Commentator select sub-flow
    private static ManagedObject _commentatorSelectParam;
    private static string _lastCommentatorName = "";
    private static int _lastSelectState = -1;

    // Label cache (resolved from Guids, stays valid within same session)
    private static readonly Dictionary<int, string> _labelCache = new();

    public static bool IsInBattleSettings => _isActive;

    /// <summary>
    /// Called from MainMenuHooks when a c_setting_XX item gets focus.
    /// </summary>
    public static void OnSettingItemFocused(int settingIndex)
    {
        _focusedSettingIndex = settingIndex;
        _lastSpinValue = "";

        if (!_isActive)
            TryFindRuleParam();

        AnnounceSettingItem(settingIndex);
    }

    [PluginEntryPoint]
    public static void Initialize()
    {
        // Hook EventCursorLeft/Right on Main for value changes
        var mainTd = TDB.Get().FindType("app.menu.UIFlowVersusRuleMain.Main");
        if (mainTd != null)
        {
            foreach (var methodName in new[] { "EventCursorLeft", "EventCursorRight" })
            {
                var method = mainTd.GetMethod(methodName);
                if (method != null)
                {
                    var hook = method.AddHook(false);
                    hook.AddPost((ref ulong retval) => OnValueChanged());
                    API.LogInfo($"[SF6Access] VersusRule.Main.{methodName} hook installed");
                }
            }
        }

        // Hook commentator/caster selection changes
        HookPost("app.UIFlowCommentatorSelect.CommentatorSelect", "SelectionChanged", OnCommentatorSelectionChanged);
        HookPost("app.UIFlowCommentatorSelect.CasterSelect", "SelectionChanged", OnCommentatorSelectionChanged);

        API.LogInfo("[SF6Access] BattleSettingsHooks initialized");
    }

    private static void HookPost(string typeName, string methodName, Action callback)
    {
        var td = TDB.Get().FindType(typeName);
        if (td == null) return;
        var method = td.GetMethod(methodName);
        if (method == null) return;
        var hook = method.AddHook(false);
        hook.AddPost((ref ulong retval) => callback());
        API.LogInfo($"[SF6Access] {typeName}.{methodName} hook installed");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (!_isActive)
        {
            if (_pollCounter % POLL_SEARCH_INTERVAL != 0) return;
            TryFindRuleParam();
            return;
        }

        // Check if still active; re-bind when the game recreated the Param
        // (stale instance reads dead memory → menu goes silent on re-entry)
        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var current = FlowHelper.TrackFlowParam(RULE_PARAM_TYPE, _ruleParam, out bool changed);
            if (current == null)
            {
                API.LogInfo("[SF6Access] Battle settings ended");
                Reset();
                return;
            }
            if (changed)
                ActivateWith(current);
        }

        // Poll for value changes and commentator select
        if (_pollCounter % POLL_READ_INTERVAL == 0)
        {
            PollSpinValueChange();
            PollCommentatorSelect();
        }
    }

    private static void Reset()
    {
        _isActive = false;
        _ruleParam = null;
        _tateList = null;
        _messDataArr = null;
        _settingTypeArr = null;
        _labelCache.Clear();
        _commentatorSelectParam = null;
        _focusedSettingIndex = -1;
        _lastSpinValue = "";
        _lastCommentatorName = "";
        _lastSelectState = -1;
    }

    // --- Param Discovery ---

    private static void CacheTDB()
    {
        if (_tdbCached) return;
        _tdbCached = true;
        _msgGetMethod = TDB.Get().FindType("via.gui.message")?.GetMethod("get(System.Guid)");
    }

    private static void TryFindRuleParam()
    {
        CacheTDB();

        var result = FlowHelper.FindFlowParam(RULE_PARAM_TYPE);
        if (result == null) return;

        ActivateWith(result);
    }

    private static void ActivateWith(ManagedObject param)
    {
        _ruleParam = param;
        _isActive = true;
        _focusedSettingIndex = -1;
        _lastSpinValue = "";
        _labelCache.Clear();

        // Cache the arrays from the Param
        _tateList = GetField(_ruleParam, "tateList");
        _messDataArr = GetField(_ruleParam, "mRuleSettingMessData");
        _settingTypeArr = GetField(_ruleParam, "ArrSettingType");

        API.LogInfo($"[SF6Access] VersusRule param found (tateList={_tateList != null}, messData={_messDataArr != null}, settingType={_settingTypeArr != null})");
    }

    // --- Setting Item Navigation ---

    private static void AnnounceSettingItem(int settingIndex)
    {
        if (_ruleParam == null)
        {
            API.LogInfo($"[SF6Access] Setting item {settingIndex} (no rule param yet)");
            return;
        }

        string label = ReadSettingLabel(settingIndex);
        string value = ReadSettingValue(settingIndex);

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(label)) parts.Add(label);
        if (!string.IsNullOrEmpty(value)) parts.Add(value);

        string announcement = parts.Count > 0 ? string.Join(": ", parts) : $"Setting {settingIndex}";
        _lastSpinValue = value ?? "";

        API.LogInfo($"[SF6Access] Battle setting [{settingIndex}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    /// <summary>
    /// Read the LABEL for a setting from mRuleSettingMessData[index].Text Guid.
    /// Falls back to ArrSettingType enum or hardcoded names.
    /// </summary>
    private static string ReadSettingLabel(int index)
    {
        if (_labelCache.TryGetValue(index, out string cached))
            return cached;

        // Resolve label Guid from SpinText_MessageList.Text
        if (_messDataArr != null && _msgGetMethod != null)
        {
            try
            {
                var msgEntry = (_messDataArr as IObject)?.Call("Get", index) as ManagedObject;
                if (msgEntry != null)
                {
                    // Text property = single Guid for the setting label
                    string label = null;
                    try
                    {
                        var guidObj = (msgEntry as IObject)?.Call("get_Text");
                        if (guidObj is REFrameworkNET.ValueType guidVt)
                            label = ResolveGuid(guidVt);
                    }
                    catch { }

                    // Try backing field
                    if (string.IsNullOrEmpty(label))
                    {
                        try
                        {
                            var td = msgEntry.GetTypeDefinition();
                            var textField = td?.GetField("<Text>k__BackingField") ?? td?.GetField("Text");
                            if (textField != null)
                            {
                                var raw = textField.GetDataBoxed(typeof(Guid), msgEntry.GetAddress(), false);
                                if (raw is REFrameworkNET.ValueType vt)
                                    label = ResolveGuid(vt);
                            }
                        }
                        catch { }
                    }

                    if (!string.IsNullOrEmpty(label))
                    {
                        _labelCache[index] = label;
                        return label;
                    }
                }
            }
            catch { }
        }

        // Fallback: use ArrSettingType enum
        return GetSettingTypeLabel(index);
    }

    private static string GetSettingTypeLabel(int index)
    {
        return $"Setting {index}";
    }

    /// <summary>
    /// Read the current VALUE for a setting from tateList[index] (via.gui.Text).
    /// tateList contains the text controls that display the current value on screen.
    /// </summary>
    private static string ReadSettingValue(int index)
    {
        if (_tateList == null) return null;

        try
        {
            var textObj = (_tateList as IObject)?.Call("Get", index) as ManagedObject;
            if (textObj != null)
            {
                string text = ReadTextComponent(textObj);
                if (!string.IsNullOrEmpty(text)) return text;
            }
        }
        catch { }

        return null;
    }

    // --- Value Change Detection ---

    private static void OnValueChanged()
    {
        if (!_isActive || _focusedSettingIndex < 0) return;

        // Re-read value after left/right cursor
        string value = ReadSettingValue(_focusedSettingIndex);
        if (!string.IsNullOrEmpty(value) && value != _lastSpinValue)
        {
            _lastSpinValue = value;
            API.LogInfo($"[SF6Access] Value changed [{_focusedSettingIndex}]: {value}");
            ScreenReaderService.Speak(value);
        }
    }

    private static void PollSpinValueChange()
    {
        if (_focusedSettingIndex < 0 || _tateList == null) return;

        string value = ReadSettingValue(_focusedSettingIndex);
        if (string.IsNullOrEmpty(value)) return;

        if (value != _lastSpinValue)
        {
            _lastSpinValue = value;
            API.LogInfo($"[SF6Access] Value changed (poll) [{_focusedSettingIndex}]: {value}");
            ScreenReaderService.Speak(value);
        }
    }

    // --- Commentator/Caster Selection ---

    private static void OnCommentatorSelectionChanged()
    {
        ReadCommentatorTitle();
    }

    private static void PollCommentatorSelect()
    {
        if (_commentatorSelectParam == null)
        {
            if (_pollCounter % POLL_SEARCH_INTERVAL != 0) return;
            _commentatorSelectParam = FlowHelper.FindFlowParam(COMMENTATOR_PARAM_TYPE);
            if (_commentatorSelectParam != null)
            {
                _lastCommentatorName = "";
                _lastSelectState = -1;
                API.LogInfo("[SF6Access] Commentator select param found");
            }
            return;
        }

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var current = FlowHelper.TrackFlowParam(COMMENTATOR_PARAM_TYPE, _commentatorSelectParam, out bool changed);
            if (current == null || changed)
            {
                _commentatorSelectParam = current;
                _lastCommentatorName = "";
                _lastSelectState = -1;
                if (current == null) return;
            }
        }

        ReadCommentatorTitle();
    }

    private static void ReadCommentatorTitle()
    {
        if (_commentatorSelectParam == null)
        {
            _commentatorSelectParam = FlowHelper.FindFlowParam(COMMENTATOR_PARAM_TYPE);
            if (_commentatorSelectParam == null) return;
        }

        try
        {
            var titleText = GetProperty(_commentatorSelectParam, "TitleText");
            if (titleText == null) return;

            string name = ReadTextComponent(titleText);
            if (string.IsNullOrEmpty(name)) return;

            // Check state (Commentator=0 vs Caster=1)
            string prefix = "";
            try
            {
                var stateObj = (_commentatorSelectParam as IObject)?.Call("get_SelectState");
                int state = stateObj != null ? Convert.ToInt32(stateObj) : 0;
                if (state != _lastSelectState)
                {
                    _lastSelectState = state;
                    prefix = state == 0 ? "Commentator: " : "Caster: ";
                }
            }
            catch { }

            string announcement = prefix + name;
            if (announcement != _lastCommentatorName)
            {
                _lastCommentatorName = announcement;
                API.LogInfo($"[SF6Access] {announcement}");
                ScreenReaderService.Speak(announcement);
            }
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] CommentatorSelect error: {ex.Message}");
            _commentatorSelectParam = null;
        }
    }

    // --- Utilities ---

    private static ManagedObject GetField(ManagedObject obj, string fieldName)
    {
        if (obj == null) return null;
        try
        {
            return obj.GetField(fieldName) as ManagedObject;
        }
        catch { }
        try
        {
            return obj.GetField($"<{fieldName}>k__BackingField") as ManagedObject;
        }
        catch { }
        return null;
    }

    private static ManagedObject GetProperty(ManagedObject obj, string propName)
    {
        if (obj == null) return null;
        try
        {
            return (obj as IObject)?.Call($"get_{propName}") as ManagedObject;
        }
        catch { }
        try
        {
            return obj.GetField($"<{propName}>k__BackingField") as ManagedObject;
        }
        catch { }
        return null;
    }

    private static string ReadTextComponent(ManagedObject textObj)
    {
        foreach (var m in new[] { "get_Message", "get_Text", "get_String" })
        {
            try
            {
                var text = (textObj as IObject)?.Call(m) as string;
                if (!string.IsNullOrEmpty(text)) return text.Trim();
            }
            catch { }
        }
        return null;
    }

    private static string ResolveGuid(REFrameworkNET.ValueType guidVt)
    {
        if (_msgGetMethod == null || guidVt == null) return null;

        ulong vtAddr = guidVt.GetAddress();
        if (vtAddr == 0) return null;

        bool allZero = true;
        for (int i = 0; i < 16; i++)
        {
            if (Marshal.ReadByte((IntPtr)(long)(vtAddr + (ulong)i)) != 0)
            { allZero = false; break; }
        }
        if (allZero) return null;

        try
        {
            var task = Task.Run(() =>
            {
                try { return _msgGetMethod.InvokeBoxed(typeof(string), null, new object[] { guidVt }) as string; }
                catch { return null; }
            });

            if (task.Wait(TimeSpan.FromMilliseconds(200)))
                return task.Result?.Trim();
        }
        catch { }
        return null;
    }
}
