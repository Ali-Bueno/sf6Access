using System;
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
    private static Method _msgGetMethod;
    private static Field _iconMsgField;
    private static Field _itemMsgField;
    private static bool _fieldsCached;

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
            if (name != null && name.Contains("IconMessage")) _iconMsgField = f;
            if (name != null && name.Contains("ItemMessage")) _itemMsgField = f;
        }
        API.LogInfo($"[SF6Access] MenuItem fields - icon: {_iconMsgField?.Name}, item: {_itemMsgField?.Name}");
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
            if (!string.IsNullOrEmpty(announcement) && GameStateTracker.HasChanged("focus_item", announcement))
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

            string announcement = null;
            try { announcement = (selectedItem as IObject)?.Call("get_Name") as string; } catch { }

            if (string.IsNullOrEmpty(announcement)) return;

            // Log for debugging
            API.LogInfo($"[SF6Access] FocusChanged raw: '{announcement}'");

            if (GameStateTracker.HasChanged("focus_item", announcement))
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

        // Get the focused MenuItem and read its Guid fields directly
        try
        {
            var menuItem = (_flowParam as IObject)?.Call("GetFocusMenuItem") as ManagedObject;
            if (menuItem != null)
            {
                var msgMethod = GetMsgMethod();
                if (msgMethod != null)
                {
                    // Try IconMessageId field (the item name)
                    if (_iconMsgField != null)
                    {
                        try
                        {
                            var guid = (Guid)_iconMsgField.GetDataBoxed(typeof(Guid), menuItem.GetAddress(), false);
                            if (guid != Guid.Empty)
                                name = msgMethod.InvokeBoxed(typeof(string), null, new object[] { guid }) as string;
                        }
                        catch { }
                    }

                    // Try ItemMessageId field if icon didn't work
                    if (string.IsNullOrEmpty(name) && _itemMsgField != null)
                    {
                        try
                        {
                            var guid = (Guid)_itemMsgField.GetDataBoxed(typeof(Guid), menuItem.GetAddress(), false);
                            if (guid != Guid.Empty)
                                name = msgMethod.InvokeBoxed(typeof(string), null, new object[] { guid }) as string;
                        }
                        catch { }
                    }

                    name = CleanTags(name);
                }
            }
        }
        catch { }

        // Get the description
        try
        {
            description = (_flowParam as IObject)?.Call("GetFocusMenuItemMessage") as string;
            description = CleanTags(description);
        }
        catch { }

        // If name equals description, don't duplicate
        if (!string.IsNullOrEmpty(name) && name == description)
            return name;
        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(description))
            return $"{name}. {description}";
        if (!string.IsNullOrEmpty(name))
            return name;
        return description;
    }

    private static string CleanTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string cleaned = Regex.Replace(text, @"<[^>]+>", "").Trim();
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        return cleaned.Trim();
    }
}
