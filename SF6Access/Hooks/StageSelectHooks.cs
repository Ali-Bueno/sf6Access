using System;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for stage select screen.
/// Polls UIFlowStageSelect.Param to announce the selected stage.
/// </summary>
public class StageSelectHooks
{
    private static ManagedObject _stageParam;
    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;

    // Cached TDB lookups
    private static bool _tdbCached;
    private static bool _fieldsLogged;

    // State tracking
    private static string _lastStageName = "";

    // Cached message resolution
    private static Method _msgGetMethod;

    // Specific param types to match (skip Title which is just animation)
    private static readonly string[] StageParamTypes = new[]
    {
        "app.menu.UIFlowStageSelect.Param",
        "app.UIFlowGenericStageSetting.Param",
    };

    public static bool IsInStageSelect => _isActive;

    /// <summary>
    /// Called from MainMenuHooks when a stage list item gets focus (p_FGStageSelectListItem_).
    /// Triggers a read of the current stage name from the Param.
    /// </summary>
    public static void OnStageItemFocused()
    {
        if (!_isActive || _stageParam == null) return;
        ReadAndAnnounce();
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnLateUpdate()
    {
        _pollCounter++;

        if (!_isActive)
        {
            if (_pollCounter % POLL_SEARCH_INTERVAL != 0) return;
            TryFindStageParam();
            return;
        }

        try
        {
            // Check if still active periodically; re-bind when the game
            // recreated the Param (stale instance → silent on re-entry)
            if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
            {
                var current = FindStageParam();
                if (current == null)
                {
                    API.LogInfo("[SF6Access] Stage select ended");
                    _isActive = false;
                    _stageParam = null;
                    _lastStageName = "";
                    return;
                }
                if (FlowHelper.AddressOf(current) != FlowHelper.AddressOf(_stageParam))
                    BindParam(current);
            }

            // Poll for stage name changes every few frames
            if (_pollCounter % 5 == 0)
                ReadAndAnnounce();
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] StageSelect poll error: {ex.Message}");
            _isActive = false;
            _stageParam = null;
        }
    }

    private static void CacheTDB()
    {
        if (_tdbCached) return;
        _tdbCached = true;
        _msgGetMethod = TDB.Get().FindType("via.gui.message")?.GetMethod("get(System.Guid)");
    }

    private static void TryFindStageParam()
    {
        CacheTDB();
        var current = FindStageParam();
        if (current != null)
            BindParam(current);
    }

    private static ManagedObject FindStageParam()
    {
        var found = FlowHelper.FindFlowParams(StageParamTypes);
        foreach (var matchType in StageParamTypes)
        {
            if (found.TryGetValue(matchType, out var param) && param != null)
                return param;
        }
        return null;
    }

    private static void BindParam(ManagedObject param)
    {
        _stageParam = param;
        _isActive = true;
        _lastStageName = "";
        _fieldsLogged = false;
        API.LogInfo($"[SF6Access] Stage select param found: {param.GetTypeDefinition()?.FullName}");
        LogParamFields(param);
    }

    private static void LogParamFields(ManagedObject param)
    {
        if (_fieldsLogged) return;
        _fieldsLogged = true;

        try
        {
            var td = param.GetTypeDefinition();
            if (td == null) return;

            var fields = td.GetFields();
            if (fields != null)
            {
                foreach (var f in fields)
                {
                    try { API.LogInfo($"[SF6Access] StageParam field: {f.Name} ({f.Type?.FullName})"); }
                    catch { }
                }
            }

            var methods = td.GetMethods();
            if (methods != null)
            {
                foreach (var m in methods)
                {
                    try { API.LogInfo($"[SF6Access] StageParam method: {m.Name}"); }
                    catch { }
                }
            }

            // Walk parent chain
            var parent = td.ParentType;
            while (parent != null && parent.FullName != "System.Object")
            {
                API.LogInfo($"[SF6Access] StageParam parent: {parent.FullName}");
                var pFields = parent.GetFields();
                if (pFields != null)
                {
                    foreach (var f in pFields)
                    {
                        try { API.LogInfo($"[SF6Access] StageParam parent field: {f.Name} ({f.Type?.FullName})"); }
                        catch { }
                    }
                }
                parent = parent.ParentType;
            }
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] LogParamFields error: {ex.Message}");
        }
    }

    private static void ReadAndAnnounce()
    {
        string stageName = ReadStageName();
        if (!string.IsNullOrEmpty(stageName) && stageName != _lastStageName)
        {
            _lastStageName = stageName;
            API.LogInfo($"[SF6Access] Stage: {stageName}");
            ScreenReaderService.Speak(stageName);
        }
    }

    private static string ReadStageName()
    {
        if (_stageParam == null) return null;

        try
        {
            var td = _stageParam.GetTypeDefinition();
            if (td == null) return null;

            // Try known text field names
            foreach (var fieldName in new[] {
                "TextStageName", "StageName", "StageNameText",
                "TextName", "NameText", "SelectStageName",
                "mStageName", "mTextStageName" })
            {
                try
                {
                    var textObj = _stageParam.GetField(fieldName) as ManagedObject;
                    if (textObj == null) continue;

                    string text = ReadTextComponent(textObj);
                    if (!string.IsNullOrEmpty(text)) return text;
                }
                catch { }
            }

            // Try method calls
            foreach (var methodName in new[] {
                "GetStageName", "get_StageName",
                "GetSelectStageName", "get_SelectStageName",
                "GetCurrentStageName", "GetSelectedStageName" })
            {
                try
                {
                    var text = (_stageParam as IObject)?.Call(methodName) as string;
                    if (!string.IsNullOrEmpty(text)) return text.Trim();
                }
                catch { }
            }

            // Generic: scan all via.gui.Text fields
            var fields = td.GetFields();
            if (fields != null)
            {
                foreach (var f in fields)
                {
                    try
                    {
                        if (f.Type?.FullName == "via.gui.Text")
                        {
                            var textObj = f.GetDataBoxed(typeof(object), _stageParam.GetAddress(), false) as ManagedObject;
                            if (textObj == null) continue;

                            string text = ReadTextComponent(textObj);
                            if (!string.IsNullOrEmpty(text))
                            {
                                API.LogInfo($"[SF6Access] Stage text from {f.Name}: {text}");
                                return text;
                            }
                        }
                    }
                    catch { }
                }
            }

            // Try all Guid fields and resolve via message system
            if (fields != null && _msgGetMethod != null)
            {
                foreach (var f in fields)
                {
                    try
                    {
                        if (f.Type?.FullName != "System.Guid") continue;

                        var guidVal = f.GetDataBoxed(typeof(Guid), _stageParam.GetAddress(), false);
                        if (guidVal is REFrameworkNET.ValueType vt)
                        {
                            var text = _msgGetMethod.InvokeBoxed(typeof(string), null, new object[] { vt }) as string;
                            if (!string.IsNullOrEmpty(text))
                            {
                                API.LogInfo($"[SF6Access] Stage guid field {f.Name}: {text}");
                                return text.Trim();
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] ReadStageName error: {ex.Message}");
        }
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

}
