using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using SF6Access.Services;

namespace SF6Access.Hooks;

public class MainMenuHooks
{
    [ThreadStatic] static ManagedObject _pendingAgent;
    private static ManagedObject _flowParam;
    private static string _lastMenuContext;

    // Cached TDB lookups
    private static Method _getMenuTypeMethod;
    private static Method _msgGetMethod;
    private static Field _iconMsgField;
    private static Field _itemMsgField;
    private static bool _fieldsCached;

    // Tab name mappings for top-level tabs (proper nouns, don't change across languages)
    private static readonly Dictionary<string, string> TabNames = new()
    {
        { "fg", "Fighting Ground" },
        { "bh", "Battle Hub" },
        { "wt", "World Tour" },
        { "c_SelectItem_friend", "Friends" },
        { "c_SelectItem_club", "Club" },
    };

    private static Method GetMsgMethod()
    {
        _msgGetMethod ??= TDB.Get().FindType("via.gui.message")?.GetMethod("get(System.Guid)");
        return _msgGetMethod;
    }

    private static void CacheMenuItemFields()
    {
        if (_fieldsCached) return;
        _fieldsCached = true;

        var td = TDB.Get().FindType("app.UIStartMenu.MenuItem");
        if (td == null) return;

        var fields = td.GetFields();
        if (fields == null) return;

        foreach (var f in fields)
        {
            string name = f.Name;
            if (name == null) continue;
            if (name.Contains("IconMessage")) _iconMsgField = f;
            else if (name.Contains("ItemMessage")) _itemMsgField = f;
        }
    }

    private static Method GetMenuTypeMethod()
    {
        _getMenuTypeMethod ??= TDB.Get().FindMethod("app.UIStartMenu.FlowParam", "GetFocusMenuType");
        return _getMenuTypeMethod;
    }

    private static int GetCurrentMenuTypeInt()
    {
        try
        {
            var method = GetMenuTypeMethod();
            if (method == null) return -1;
            var result = method.InvokeBoxed(typeof(int), _flowParam, new object[] { });
            return result != null ? Convert.ToInt32(result) : -1;
        }
        catch { return -1; }
    }

    // Track menu context for dialog button labeling
    public static string LastMenuContext => _lastMenuContext;

    [MethodHook(typeof(app.UIStartMenu.FlowParam), nameof(app.UIStartMenu.FlowParam.MenuItemSelectionChanged), MethodHookType.Pre)]
    static PreHookResult OnMenuSelectionPre(Span<ulong> args)
    {
        _flowParam = ManagedObject.ToManagedObject(args[1]);
        return PreHookResult.Continue;
    }

    [MethodHook(typeof(app.UIStartMenu.FlowParam), nameof(app.UIStartMenu.FlowParam.MenuItemSelectionChanged), MethodHookType.Post)]
    static void OnMenuSelectionPost(ref ulong retval)
    {
        try
        {
            if (_flowParam == null) return;

            // User is back in main menu grid, no longer in options
            OptionMenuHooks.IsInOptionMenu = false;

            string announcement = GetStartMenuAnnouncement();
            if (!string.IsNullOrEmpty(announcement) && GameStateTracker.HasChanged("menu_item", announcement))
            {
                API.LogInfo($"[SF6Access] Menu: {announcement}");
                ScreenReaderService.Speak(announcement);
            }
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] MenuItemSelectionChanged error: {ex.Message}");
        }
    }

    [MethodHook(typeof(app.UIAgent), nameof(app.UIAgent.FocusChanged), MethodHookType.Pre)]
    static PreHookResult OnFocusChangedPre(Span<ulong> args)
    {
        _pendingAgent = ManagedObject.ToManagedObject(args[1]);
        return PreHookResult.Continue;
    }

    [MethodHook(typeof(app.UIAgent), nameof(app.UIAgent.FocusChanged), MethodHookType.Post)]
    static void OnFocusChangedPost(ref ulong retval)
    {
        try
        {
            if (_pendingAgent == null) return;

            var agent = _pendingAgent;
            _pendingAgent = null;

            var focusItem = (agent as IObject)?.Call("GetFocusItem") as ManagedObject;
            if (focusItem == null) return;

            ManagedObject selectedItem = null;
            try { selectedItem = (focusItem as IObject)?.Call("get_SelectedItem") as ManagedObject; }
            catch { }
            if (selectedItem == null) return;

            string rawName = null;
            try { rawName = (selectedItem as IObject)?.Call("get_Name") as string; } catch { }
            if (string.IsNullOrEmpty(rawName)) return;

            // If navigating back to tabs, clear option menu flag
            if (TabNames.ContainsKey(rawName))
                OptionMenuHooks.IsInOptionMenu = false;

            // Suppress FocusChanged events while in option menu (OptionMenuHooks handles those)
            if (OptionMenuHooks.IsInOptionMenu)
                return;

            // Grid menu items (item0, item1, c_item_00, etc.) - resolve via FlowParam
            if (Regex.IsMatch(rawName, @"^(item\d+|c_item_\d{2,})$"))
            {
                // MenuItemSelectionChanged handles this when FlowParam is active
                // But some tabs (Fighting Ground) don't fire MenuItemSelectionChanged,
                // so we try FlowParam here as backup
                if (_flowParam != null)
                {
                    string resolved = GetStartMenuAnnouncement();
                    if (!string.IsNullOrEmpty(resolved) && GameStateTracker.HasChanged("menu_item", resolved))
                    {
                        API.LogInfo($"[SF6Access] Grid: {resolved}");
                        ScreenReaderService.Speak(resolved);
                    }
                }
                // If _flowParam is null, MenuItemSelectionChanged will handle it
                return;
            }

            // Dialog buttons: c_item_0 = first button, c_item_1 = second button
            if (Regex.IsMatch(rawName, @"^c_item_\d$"))
            {
                int btnIdx = rawName[rawName.Length - 1] - '0';
                string btnLabel = btnIdx == 0 ? "Yes" : "No";
                if (GameStateTracker.HasChanged("focus_item", rawName))
                {
                    API.LogInfo($"[SF6Access] Dialog button: {btnLabel}");
                    ScreenReaderService.Speak(btnLabel);
                }
                return;
            }

            // Sub-menu items (c_SubMenu_item0, etc.) - FG vertical navigation
            if (Regex.IsMatch(rawName, @"^c_SubMenu_item\d+$"))
            {
                FGMenuHooks.OnSubMenuItemFocused();
                return;
            }

            // Stage select list items
            if (rawName.Contains("StageSelectListItem"))
            {
                StageSelectHooks.OnStageItemFocused();
                return;
            }

            // Battle settings items (c_setting_00, c_setting_01, etc.)
            if (Regex.IsMatch(rawName, @"^c_setting_\d+$"))
            {
                int settingIdx = int.Parse(rawName.Substring("c_setting_".Length));
                BattleSettingsHooks.OnSettingItemFocused(settingIdx);
                return;
            }

            // Try to resolve localized text for unknown UI items (mail items, etc.)
            string resolvedText = TryResolveItemText(selectedItem, rawName);

            // Map known tab names to readable text
            string announcement = TabNames.TryGetValue(rawName, out var mapped) ? mapped
                : !string.IsNullOrEmpty(resolvedText) ? resolvedText : rawName;

            // Try to get description for tabs (fg, bh, wt)
            if (TabNames.ContainsKey(rawName))
            {
                string tabDesc = TryGetTabDescription(rawName);
                if (!string.IsNullOrEmpty(tabDesc) && tabDesc != announcement)
                    announcement = $"{announcement}. {tabDesc}";
            }

            if (GameStateTracker.HasChanged("focus_item", rawName))
            {
                API.LogInfo($"[SF6Access] Focus: {announcement}");
                ScreenReaderService.Speak(announcement);
            }
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] FocusChanged error: {ex.Message}");
            _pendingAgent = null;
        }
    }

    private static string GetStartMenuAnnouncement()
    {
        if (_flowParam == null) return null;

        CacheMenuItemFields();

        int menuType = GetCurrentMenuTypeInt();
        string name = null;
        string description = null;

        // Always resolve localized name fresh (reflects current game language)
        if (menuType >= 0)
            name = ResolveMenuItemName();

        // Track context for dialog labeling
        _lastMenuContext = name;


        // Get the description
        try
        {
            description = (_flowParam as IObject)?.Call("GetFocusMenuItemMessage") as string;
            description = CleanTags(description);
        }
        catch { }

        if (!string.IsNullOrEmpty(name) && name == description)
            return name;
        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(description))
            return $"{name}. {description}";
        if (!string.IsNullOrEmpty(name))
            return name;
        return description;
    }

    private static string ResolveMenuItemName()
    {
        var fp = _flowParam;
        var task = Task.Run(() => GetLocalizedNameFromMenuItem(fp));

        if (task.Wait(TimeSpan.FromMilliseconds(200)))
            return task.Result;

        return null;
    }

    private static string GetLocalizedNameFromMenuItem(ManagedObject fp = null)
    {
        try
        {
            fp ??= _flowParam;
            var menuItem = (fp as IObject)?.Call("GetFocusMenuItem") as ManagedObject;
            if (menuItem == null) return null;

            ulong addr = menuItem.GetAddress();
            if (addr == 0) return null;

            var msgMethod = GetMsgMethod();
            if (msgMethod == null) return null;

            string text = TryReadGuidAndResolve(_iconMsgField, addr, msgMethod);
            if (!string.IsNullOrEmpty(text)) return text;

            text = TryReadGuidAndResolve(_itemMsgField, addr, msgMethod);
            if (!string.IsNullOrEmpty(text)) return text;
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] GetLocalizedNameFromMenuItem error: {ex.Message}");
        }

        return null;
    }

    private static string TryReadGuidAndResolve(Field guidField, ulong structAddr, Method msgGetMethod)
    {
        if (guidField == null) return null;

        try
        {
            var rawValue = guidField.GetDataBoxed(typeof(Guid), structAddr, false);
            if (rawValue is not REFrameworkNET.ValueType vt) return null;

            ulong vtAddr = vt.GetAddress();
            if (vtAddr == 0) return null;

            // Check if Guid is all zeros (empty)
            bool allZero = true;
            for (int i = 0; i < 16; i++)
            {
                if (Marshal.ReadByte((IntPtr)(long)(vtAddr + (ulong)i)) != 0)
                { allZero = false; break; }
            }
            if (allZero) return null;

            var text = msgGetMethod.InvokeBoxed(typeof(string), null, new object[] { vt }) as string;
            if (!string.IsNullOrEmpty(text))
            {
                API.LogInfo($"[SF6Access] Resolved {guidField.Name}: {CleanTags(text)}");
                return CleanTags(text);
            }
        }
        catch { }

        return null;
    }

    // GuideDescriptionMessage IDs for tab descriptions
    private static readonly Dictionary<string, int> TabDescriptionIds = new()
    {
        { "fg", 267 },  // Fighting Ground
        { "bh", 266 },  // Battle Hub
        { "wt", 247 },  // World Tour
    };

    private static Method _guidDescMethod;
    private static ManagedObject _tableDataMgr;

    private static string TryGetTabDescription(string rawName)
    {
        if (!TabDescriptionIds.TryGetValue(rawName, out int gdmId))
            return null;

        try
        {
            // Lazy init
            _tableDataMgr ??= API.GetManagedSingleton("app.TableDataManager");
            _guidDescMethod ??= TDB.Get().FindType("app.TableDataManager")
                ?.GetMethod("TryGetGuideDescriptionMessage(app.MsgDef.GuideDescriptionMessage, System.Guid)");

            if (_tableDataMgr == null || _guidDescMethod == null) return null;

            var msgGet = GetMsgMethod();
            if (msgGet == null) return null;

            var guidVt = TDB.Get().FindType("System.Guid").CreateValueType();
            var args = new object[] { gdmId, guidVt };
            var result = _guidDescMethod.InvokeBoxed(typeof(bool), _tableDataMgr, args);

            if (result is bool b && b)
            {
                var outGuid = args[1] as REFrameworkNET.ValueType ?? guidVt;
                var text = msgGet.InvokeBoxed(typeof(string), null, new object[] { outGuid }) as string;
                text = CleanTags(text);
                if (!string.IsNullOrEmpty(text))
                    return text;
            }
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] TryGetTabDescription error: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Try to read localized text from a SelectItem's GUI element.
    /// Works for mail items, news items, and other dynamic UI elements.
    /// </summary>
    private static string TryResolveItemText(ManagedObject selectedItem, string rawName)
    {
        if (selectedItem == null) return null;

        // Skip known patterns that are handled elsewhere
        if (TabNames.ContainsKey(rawName)) return null;

        try
        {
            // Try get_Message or get_Text on the SelectItem
            foreach (var methodName in new[] { "get_Message", "get_Text", "get_Label" })
            {
                try
                {
                    var text = (selectedItem as IObject)?.Call(methodName) as string;
                    text = CleanTags(text);
                    if (!string.IsNullOrEmpty(text))
                        return text;
                }
                catch { }
            }
        }
        catch { }

        return null;
    }

    private static string CleanTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Regex.Replace(Regex.Replace(text, @"<[^>]+>", "").Trim(), @"\s+", " ").Trim();
    }
}
