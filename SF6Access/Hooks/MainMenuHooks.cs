using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using SF6Access.Services;

namespace SF6Access.Hooks;

public class MainMenuHooks
{
    [ThreadStatic] static ManagedObject _pendingAgent;
    private static ManagedObject _flowParam;

    // Cached TDB lookups
    private static Method _getMenuTypeMethod;
    private static Method _msgGetMethod;
    private static Field _iconMsgField;
    private static Field _itemMsgField;
    private static bool _fieldsCached;

    // Tab name mappings for top-level tabs that have no localized text
    private static readonly Dictionary<string, string> TabNames = new()
    {
        { "fg", "Fighting Ground" },
        { "bh", "Battle Hub" },
        { "wt", "World Tour" },
        { "c_SelectItem_friend", "Friends" },
        { "c_SelectItem_club", "Club" },
    };

    // MenuType enum int -> readable name
    private static readonly Dictionary<int, string> MenuTypeNames = new()
    {
        { 0, "Profile" },
        { 1, "CFN" },
        { 2, "News" },
        { 3, "Rewards" },
        { 4, "Shop" },
        { 5, "Options" },
        { 6, "Gallery" },
        { 7, "Tips" },
        { 8, "Player List" },
        { 9, "Server List" },
        { 10, "Custom Room" },
        { 11, "Tournament" },
        { 12, "Main Menu" },
        { 13, "Exit to Desktop" },
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
        if (td == null)
        {
            API.LogWarning("[SF6Access] MenuItem type not found in TDB");
            return;
        }
        var fields = td.GetFields();
        if (fields == null) return;
        foreach (var f in fields)
        {
            string name = f.Name;
            if (name != null && name.Contains("IconMessage")) _iconMsgField = f;
            if (name != null && name.Contains("ItemMessage")) _itemMsgField = f;
        }
        API.LogInfo($"[SF6Access] MenuItem fields - icon: {_iconMsgField?.Name}, item: {_itemMsgField?.Name}");
    }

    private static Method GetMenuTypeMethod()
    {
        _getMenuTypeMethod ??= TDB.Get().FindMethod("app.UIStartMenu.FlowParam", "GetFocusMenuType");
        return _getMenuTypeMethod;
    }

    // Capture FlowParam instance when grid menu selection changes
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

            var selectedItem = (focusItem as IObject)?.Call("get_SelectedItem") as ManagedObject;
            if (selectedItem == null) return;

            string rawName = null;
            try { rawName = (selectedItem as IObject)?.Call("get_Name") as string; } catch { }

            if (string.IsNullOrEmpty(rawName)) return;

            API.LogInfo($"[SF6Access] FocusChanged raw: '{rawName}'");

            // Skip grid menu items - they're handled by MenuItemSelectionChanged hook
            if (Regex.IsMatch(rawName, @"^(item\d+|c_item_\d+)$"))
                return;

            // Map known tab names to readable text
            string announcement = TabNames.TryGetValue(rawName, out var mapped) ? mapped : rawName;

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

        string name = null;
        string description = null;

        // Strategy 1: Get MenuType enum and resolve name
        name = GetNameFromMenuType();

        // Strategy 2: Try reading Guid fields from MenuItem value type
        if (string.IsNullOrEmpty(name))
            name = GetNameFromMenuItemGuids();

        // Get the description
        try
        {
            description = (_flowParam as IObject)?.Call("GetFocusMenuItemMessage") as string;
            description = CleanTags(description);
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] GetFocusMenuItemMessage error: {ex.Message}");
        }

        if (!string.IsNullOrEmpty(name) && name == description)
            return name;
        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(description))
            return $"{name}. {description}";
        if (!string.IsNullOrEmpty(name))
            return name;
        return description;
    }

    private static string GetNameFromMenuType()
    {
        try
        {
            var method = GetMenuTypeMethod();
            if (method == null)
            {
                API.LogWarning("[SF6Access] GetFocusMenuType method not found");
                return null;
            }

            var result = method.InvokeBoxed(typeof(int), _flowParam, new object[] { });
            if (result == null)
            {
                API.LogInfo("[SF6Access] GetFocusMenuType returned null");
                return null;
            }

            int menuTypeInt = Convert.ToInt32(result);

            if (MenuTypeNames.TryGetValue(menuTypeInt, out var name))
            {
                API.LogInfo($"[SF6Access] MenuType: {menuTypeInt} -> {name}");
                return name;
            }

            API.LogInfo($"[SF6Access] MenuType: {menuTypeInt} (unknown)");
            return $"Menu {menuTypeInt}";
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] GetNameFromMenuType error: {ex.Message}");
            return null;
        }
    }

    private static string GetNameFromMenuItemGuids()
    {
        try
        {
            var menuItem = (_flowParam as IObject)?.Call("GetFocusMenuItem");
            if (menuItem == null)
            {
                API.LogInfo("[SF6Access] GetFocusMenuItem returned null");
                return null;
            }

            var menuItemMo = menuItem as ManagedObject;
            if (menuItemMo == null)
            {
                API.LogInfo($"[SF6Access] MenuItem is not ManagedObject, type: {menuItem.GetType().Name}");
                return null;
            }

            var msgMethod = GetMsgMethod();
            if (msgMethod == null) return null;

            ulong addr = menuItemMo.GetAddress();
            API.LogInfo($"[SF6Access] MenuItem address: 0x{addr:X}");

            // Try IconMessageId
            if (_iconMsgField != null)
            {
                try
                {
                    var guid = (Guid)_iconMsgField.GetDataBoxed(typeof(Guid), addr, false);
                    API.LogInfo($"[SF6Access] IconMessageId Guid: {guid}");
                    if (guid != Guid.Empty)
                    {
                        var text = msgMethod.InvokeBoxed(typeof(string), null, new object[] { guid }) as string;
                        if (!string.IsNullOrEmpty(text))
                            return CleanTags(text);
                    }
                }
                catch (Exception ex)
                {
                    API.LogError($"[SF6Access] IconMessageId read error: {ex.Message}");
                }
            }

            // Try ItemMessageId
            if (_itemMsgField != null)
            {
                try
                {
                    var guid = (Guid)_itemMsgField.GetDataBoxed(typeof(Guid), addr, false);
                    API.LogInfo($"[SF6Access] ItemMessageId Guid: {guid}");
                    if (guid != Guid.Empty)
                    {
                        var text = msgMethod.InvokeBoxed(typeof(string), null, new object[] { guid }) as string;
                        if (!string.IsNullOrEmpty(text))
                            return CleanTags(text);
                    }
                }
                catch (Exception ex)
                {
                    API.LogError($"[SF6Access] ItemMessageId read error: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] GetNameFromMenuItemGuids error: {ex.Message}");
        }

        return null;
    }

    private static string FormatEnumName(string enumName)
    {
        if (string.IsNullOrEmpty(enumName)) return null;
        // EXIT_TO_DESKTOP -> Exit To Desktop
        var parts = enumName.Split('_');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
                parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1).ToLower();
        }
        return string.Join(" ", parts);
    }

    private static string CleanTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string cleaned = Regex.Replace(text, @"<[^>]+>", "").Trim();
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        return cleaned.Trim();
    }
}
