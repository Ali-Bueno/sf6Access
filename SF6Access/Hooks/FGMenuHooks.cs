using System;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Hooks Fighting Ground menu navigation.
/// FG uses UIFlowFGMainMenuList.Param with tateText[] for item names.
/// MainChanged(bool) fires when navigation changes.
/// </summary>
public class FGMenuHooks
{
    private const string FG_PARAM_TYPE = "app.menu.UIFlowFGMainMenuList.Param";

    private static ManagedObject _fgParam;
    private static string _lastAnnounced;

    // The item name is held back briefly so GuideTextHooks can fold the
    // InputGuide description into a SINGLE "name. description" announcement
    // (testers lost the title when the separate description replaced it). If no
    // description arrives within the window, OnUpdate speaks the name alone.
    private static string _pendingName;
    private static long _pendingSince;
    private const long PENDING_COMBINE_MS = 350;

    /// <summary>
    /// GuideTextHooks calls this when it is about to announce an InputGuide
    /// description: returns the freshly-focused FG item name (once) so the two
    /// are spoken together, or null when there is nothing pending.
    /// </summary>
    public static string ConsumePendingName()
    {
        if (_pendingName == null) return null;
        if (System.Environment.TickCount64 - _pendingSince > PENDING_COMBINE_MS + 400)
        {
            _pendingName = null;
            return null;
        }
        string name = _pendingName;
        _pendingName = null;
        return name;
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        if (_pendingName == null) return;
        if (System.Environment.TickCount64 - _pendingSince < PENDING_COMBINE_MS) return;

        // No description folded it in within the window — speak the name alone
        string name = _pendingName;
        _pendingName = null;
        API.LogInfo($"[SF6Access] FG item (no description): {name}");
        ScreenReaderService.Speak(name);
    }

    /// <summary>Called from MainMenuHooks when c_SubMenu_item focus changes (vertical navigation)</summary>
    public static void OnSubMenuItemFocused()
    {
        if (_fgParam == null) return;
        ReadCurrentFGItem();
    }

    /// <summary>
    /// Capture the live Param from a hook's `this` argument. When the menu is
    /// re-entered the game creates a NEW Param — clear the dedupe state so the
    /// first item is announced again (it stayed silent when the cursor landed
    /// on the same item as last time).
    /// </summary>
    private static void CaptureParam(ulong thisAddr)
    {
        var param = ManagedObject.ToManagedObject(thisAddr);
        if (param != null && FlowHelper.AddressOf(param) != FlowHelper.AddressOf(_fgParam))
            _lastAnnounced = null;
        _fgParam = param;
    }

    [PluginEntryPoint]
    public static void Initialize()
    {
        // Hook MainChanged on the FG Param to detect navigation
        var paramTd = TDB.Get().FindType("app.menu.UIFlowFGMainMenuList.Param");
        if (paramTd == null)
        {
            API.LogError("[SF6Access] FG Param type not found");
            return;
        }

        var mainChanged = paramTd.GetMethod("MainChanged");
        if (mainChanged != null)
        {
            var hook = mainChanged.AddHook(false);
            hook.AddPre(args =>
            {
                // Capture the Param instance (args[1] = this)
                CaptureParam(args[1]);
                return PreHookResult.Continue;
            });
            hook.AddPost((ref ulong retval) =>
            {
                ReadCurrentFGItem();
            });
            API.LogInfo("[SF6Access] FG MainChanged hook installed");
        }

        // Also hook SetSubPos for vertical navigation within a category
        var setSubPos = paramTd.GetMethod("SetSubPos");
        if (setSubPos != null)
        {
            var hook = setSubPos.AddHook(false);
            hook.AddPre(args =>
            {
                CaptureParam(args[1]);
                return PreHookResult.Continue;
            });
            hook.AddPost((ref ulong retval) =>
            {
                ReadCurrentFGItem();
            });
            API.LogInfo("[SF6Access] FG SetSubPos hook installed");
        }

        // Hook Right/Left for category switching
        foreach (var methodName in new[] { "Right", "Left" })
        {
            var method = paramTd.GetMethod(methodName);
            if (method == null) continue;
            var hook = method.AddHook(false);
            hook.AddPre(args =>
            {
                CaptureParam(args[1]);
                return PreHookResult.Continue;
            });
            hook.AddPost((ref ulong retval) =>
            {
                ReadCurrentFGItem();
            });
            API.LogInfo($"[SF6Access] FG {methodName} hook installed");
        }
    }

    private static void ReadCurrentFGItem()
    {
        if (_fgParam == null) return;

        try
        {
            // Try GetSelectData().GetModeTypeName() approach, then tateText
            string name = TryReadFromSelectData() ?? TryReadFromTateText();

            // Backing out of nested submenus fast leaves _fgParam pointing at a
            // dead Param (the game recreated it), so reads return null and only
            // GuideTextHooks' description was heard — the lost-title bug. Re-find
            // the live Param and retry once.
            if (string.IsNullOrEmpty(name))
            {
                var live = FlowHelper.FindFlowParam(FG_PARAM_TYPE);
                if (live != null && FlowHelper.AddressOf(live) != FlowHelper.AddressOf(_fgParam))
                {
                    _fgParam = live;
                    _lastAnnounced = null;
                    name = TryReadFromSelectData() ?? TryReadFromTateText();
                }
            }

            if (!string.IsNullOrEmpty(name) && name != _lastAnnounced)
            {
                _lastAnnounced = name;
                // Hold the name so the description can be folded in (see
                // ConsumePendingName / OnUpdate). Don't speak it here.
                _pendingName = name;
                _pendingSince = System.Environment.TickCount64;
                API.LogInfo($"[SF6Access] FG item (pending combine): {name}");
            }
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] FG read error: {ex.Message}");
        }
    }

    private static string TryReadFromSelectData()
    {
        try
        {
            var selectData = (_fgParam as IObject)?.Call("GetSelectData") as ManagedObject;
            if (selectData == null) return null;

            // Try to get a name/title from ModeTypeData
            var td = selectData.GetTypeDefinition();
            if (td == null) return null;

            // Try reading string fields
            foreach (var fname in new[] { "Name", "Title", "ModeName", "CategoryName" })
            {
                try
                {
                    var val = selectData.GetField(fname) as string;
                    if (!string.IsNullOrEmpty(val)) return val;
                }
                catch { }
            }

            // Try reading Guid fields and resolving
            var msgGet = TDB.Get().FindType("via.gui.message")?.GetMethod("get(System.Guid)");
            if (msgGet == null) return null;

            var fields = td.GetFields();
            if (fields == null) return null;
            foreach (var f in fields)
            {
                try
                {
                    if (f.Type?.FullName != "System.Guid") continue;
                    var guidVal = f.GetDataBoxed(typeof(Guid), selectData.GetAddress(), false);
                    if (guidVal is REFrameworkNET.ValueType vt)
                    {
                        var text = msgGet.InvokeBoxed(typeof(string), null, new object[] { vt }) as string;
                        if (!string.IsNullOrEmpty(text))
                        {
                            API.LogInfo($"[SF6Access] FG resolved {f.Name}: {text}");
                            return text;
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    private static string TryReadFromTateText()
    {
        try
        {
            // Read modeSelect to know which index is selected
            var modeSelectField = _fgParam.GetTypeDefinition()?.GetField("modeSelect");
            if (modeSelectField == null) return null;

            var rawIdx = modeSelectField.GetDataBoxed(typeof(uint), _fgParam.GetAddress(), false);
            int idx = rawIdx != null ? Convert.ToInt32(rawIdx) : -1;
            if (idx < 0) return null;

            // Read tateText array
            var tateTextField = _fgParam.GetTypeDefinition()?.GetField("tateText");
            if (tateTextField == null) return null;

            var tateArr = tateTextField.GetDataBoxed(typeof(object), _fgParam.GetAddress(), false) as ManagedObject;
            if (tateArr == null) return null;

            // Get array length and item
            var lenMethod = tateArr.GetTypeDefinition()?.GetMethod("get_Length");
            if (lenMethod == null) return null;
            int len = Convert.ToInt32(lenMethod.InvokeBoxed(typeof(int), tateArr, Array.Empty<object>()));
            if (idx >= len) return null;

            var getMethod = tateArr.GetTypeDefinition()?.GetMethod("Get");
            if (getMethod == null) return null;

            var textObj = getMethod.InvokeBoxed(typeof(object), tateArr, new object[] { idx }) as ManagedObject;
            if (textObj == null) return null;

            // via.gui.Text has get_Message() or get_Text() to read displayed text
            foreach (var mName in new[] { "get_Message", "get_Text", "get_String" })
            {
                try
                {
                    var text = (textObj as IObject)?.Call(mName) as string;
                    if (!string.IsNullOrEmpty(text)) return text;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }
}
