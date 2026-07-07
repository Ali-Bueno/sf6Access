using System;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for stage select screen.
/// Polls UIFlowStageSelect.Param to announce the selected stage.
///
/// ScreenAdapter (multi-Param, priority order). MainMenuHooks routes stage-item
/// focus events here via the static OnStageItemFocused(). Registered in
/// ScreenRegistry.
/// </summary>
public sealed class StageSelectHooks : ScreenAdapter
{
    // Specific param types to match (skip Title which is just animation)
    private static readonly string[] StageParamTypes =
    {
        "app.menu.UIFlowStageSelect.Param",
        "app.UIFlowGenericStageSetting.Param",
    };

    public override string[] OwnedTypes => StageParamTypes;

    // BGM selection (Q/E on the stage select screen toggles Stage BGM /
    // Character BGM / a specific track). The value lives in the "StageSelect"
    // GUI's e_text_bgm element, not in a Param field — cache the view and poll.
    private const string BGM_GUI = "StageSelect";
    private const string BGM_TEXT = "e_text_bgm";

    private static StageSelectHooks _self;
    public static bool IsInStageSelect => _self != null && _self.Active;

    /// <summary>
    /// Called from MainMenuHooks when a stage list item gets focus (p_FGStageSelectListItem_).
    /// Triggers a read of the current stage name from the Param.
    /// </summary>
    public static void OnStageItemFocused()
    {
        var self = _self;
        if (self == null || !self.Active || self._stageParam == null) return;
        self.ReadAndAnnounce();
    }

    public StageSelectHooks()
    {
        SearchInterval = 60;
        ReadInterval = 5;
        _self = this;
    }

    private ManagedObject _stageParam;

    // Cached TDB lookups
    private static bool _tdbCached;
    private static Method _msgGetMethod;
    private bool _fieldsLogged;

    // State tracking
    private string _lastStageName = "";

    private readonly System.Collections.Generic.List<(string owner, ManagedObject view)> _bgmViews = new();
    private string _lastBgm = "";

    protected override bool Locate()
    {
        CacheTDB();
        var found = FlowHelper.FindFlowParams(StageParamTypes);
        ManagedObject current = null;
        foreach (var matchType in StageParamTypes)
        {
            if (found.TryGetValue(matchType, out current) && current != null) break;
        }
        if (current == null)
        {
            _stageParam = null;
            return false;
        }

        if (_stageParam == null || FlowHelper.AddressOf(current) != FlowHelper.AddressOf(_stageParam))
            BindParam(current);
        else
        {
            // Refresh the BGM GUI view (re-created with the screen)
            _bgmViews.Clear();
            foreach (var v in GuiTextReader.FindGuiViews(BGM_GUI))
                _bgmViews.Add(v);
        }
        return true;
    }

    protected override void OnDeactivate()
    {
        API.LogInfo("[SF6Access] Stage select ended");
        _stageParam = null;
        _lastStageName = "";
        _bgmViews.Clear();
        _lastBgm = "";
    }

    protected override void OnPoll()
    {
        try
        {
            ReadAndAnnounce();
            PollBgm();
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] StageSelect poll error: {ex.Message}");
        }
    }

    private static void CacheTDB()
    {
        if (_tdbCached) return;
        _tdbCached = true;
        _msgGetMethod = TDB.Get().FindType("via.gui.message")?.GetMethod("get(System.Guid)");
    }

    private void BindParam(ManagedObject param)
    {
        _stageParam = param;
        _lastStageName = "";
        _fieldsLogged = false;
        _lastBgm = "";
        _bgmViews.Clear();
        foreach (var v in GuiTextReader.FindGuiViews(BGM_GUI))
            _bgmViews.Add(v);
        API.LogInfo($"[SF6Access] Stage select param found: {param.GetTypeDefinition()?.FullName}");
        LogParamFields(param);
    }

    /// <summary>
    /// Announce the BGM selection (Q/E) when it changes. The current choice is
    /// the StageSelect GUI's e_text_bgm element ("Stage BGM", "Character BGM",
    /// a track name...).
    /// </summary>
    private void PollBgm()
    {
        foreach (var (owner, view) in _bgmViews)
        {
            try
            {
                foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
                {
                    if (t.Name != BGM_TEXT) continue;
                    string bgm = t.Text?.Trim();
                    if (string.IsNullOrEmpty(bgm) || bgm == _lastBgm) return;

                    bool first = string.IsNullOrEmpty(_lastBgm);
                    _lastBgm = bgm;
                    if (first) return; // Don't announce the initial value

                    API.LogInfo($"[SF6Access] Stage BGM: {bgm}");
                    Speak(bgm);
                    return;
                }
            }
            catch { }
        }
    }

    private void LogParamFields(ManagedObject param)
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

    private void ReadAndAnnounce()
    {
        string stageName = ReadStageName();
        if (!string.IsNullOrEmpty(stageName) && stageName != _lastStageName)
        {
            _lastStageName = stageName;
            API.LogInfo($"[SF6Access] Stage: {stageName}");
            Speak(stageName);
        }
    }

    private string ReadStageName()
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

            // Battle Hub generic stage setting holds the stage name only in its
            // GUI ("GenericStageSetting_BH", element e_text_stage), not in any
            // Param field — read that element directly. Scoped to the stage GUI
            // so the Battle Hub's ambient on-screen text is never picked up.
            foreach (var (owner, view) in GuiTextReader.FindGuiViews("StageSetting"))
            {
                foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
                {
                    if (t.Name == "e_text_stage" && !string.IsNullOrWhiteSpace(t.Text))
                        return t.Text.Replace('\n', ' ').Trim();
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
