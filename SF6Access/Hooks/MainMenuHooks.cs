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

            // Any focus change invalidates the previously tracked control
            FocusValueHooks.Clear();

            // If navigating back to tabs, clear option menu flag
            if (TabNames.ContainsKey(rawName))
                OptionMenuHooks.IsInOptionMenu = false;

            // Suppress FocusChanged events while in option menu (OptionMenuHooks handles those)
            if (OptionMenuHooks.IsInOptionMenu)
                return;

            // A text-entry dialog (search by name/code) is modal: the only
            // FocusChanged events are its Cancelar / Buscar buttons, which a
            // background tracker would otherwise suppress — leaving navigation
            // silent. Let this generic reader handle them.
            if (TextInputDialogHooks.IsActive)
            {
                // fall through to the c_item_N button handler below
            }
            // Suppress while dedicated menu hooks handle announcements.
            // GroupFocus only suppresses while it actually announces rows —
            // a silently-active tracker must not mute this generic reader
            // (Battle Hub room muted the avatar battle menu)
            else if (KeyConfigHooks.IsInKeyConfig || NewsHooks.IsInNewsMenu ||
                RewardHooks.IsActive || RevivalPassWarningHooks.IsActive ||
                OnlineShopBuyHooks.IsActive || CustomRoomJoinHooks.IsActive ||
                MusicPlayerHooks.IsActive ||
                CustomRoomHooks.IsInCustomRoomTop ||
                MatchingFighterSettingHooks.IsInFighterSetting ||
                DeathMatchSettingHooks.IsInDeathMatchSetting ||
                GroupFocusHooks.ShouldSuppressFocus)
            {
                // Timestamped trace of real navigation presses — measures how
                // far behind them the poll-based announcers run
                API.LogInfo($"[SF6Access] Focus (suppressed): {rawName}");

                // GroupFocus suppression is a heuristic window, not a guarantee
                // that it can read this row: queue the focused item so it gets
                // announced anyway if no row announcement follows
                if (GroupFocusHooks.ShouldSuppressFocus &&
                    !KeyConfigHooks.IsInKeyConfig && !NewsHooks.IsInNewsMenu &&
                    !RewardHooks.IsActive &&
                    !CustomRoomHooks.IsInCustomRoomTop &&
                    !MatchingFighterSettingHooks.IsInFighterSetting &&
                    !DeathMatchSettingHooks.IsInDeathMatchSetting)
                {
                    GroupFocusHooks.QueueFocusFallback(selectedItem, rawName);
                }
                return;
            }

            // Grid menu items (item0, item1, c_item_00, etc.) - resolve via FlowParam
            if (Regex.IsMatch(rawName, @"^(item\d+|c_item_\d{2,})$"))
            {
                // Start menu grid: resolve via FlowParam (MenuItemSelectionChanged
                // doesn't fire for some tabs, e.g. Fighting Ground).
                // The start menu stays ACTIVE underneath CFN/shop/tips screens,
                // so only trust its resolution when the focused item's on-screen
                // text actually matches it — otherwise this is another screen's
                // grid reusing the same item names. The probe reads only the
                // first text: a full subtree walk per focus event added lag.
                if (_flowParam != null && FlowTrackerHooks.IsFlowActive("UIStartMenu"))
                {
                    string resolved = GetStartMenuAnnouncement();
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        string firstText = GuiTextReader.ReadFirstControlText(selectedItem)?.Trim();
                        bool belongsToStartMenu = string.IsNullOrEmpty(firstText) ||
                            resolved.Contains(firstText, StringComparison.OrdinalIgnoreCase);

                        if (belongsToStartMenu)
                        {
                            if (GameStateTracker.HasChanged("menu_item", resolved))
                            {
                                API.LogInfo($"[SF6Access] Grid: {resolved}");
                                ScreenReaderService.Speak(resolved);
                            }
                            return;
                        }
                    }
                }

                // Other screens (gallery, tips, CFN, shop...) reuse these item
                // names — read the focused item's on-screen text instead
                string itemText = TryResolveItemText(selectedItem, rawName);
                if (!string.IsNullOrEmpty(itemText) && GameStateTracker.HasChanged("focus_item", itemText))
                {
                    API.LogInfo($"[SF6Access] Grid (generic): {itemText}");
                    ScreenReaderService.Speak(itemText);
                    FocusValueHooks.Track(selectedItem);
                }
                return;
            }

            // Message box buttons are announced by DialogFlowHooks polling
            // (FocusChanged doesn't fire for dialogs in every context)
            if (DialogFlowHooks.IsDialogActive && Regex.IsMatch(rawName, @"^c_item_\d$"))
                return;

            // c_item_N: used by dialog buttons AND by generic menu lists (custom rooms etc.)
            // Read the item's actual on-screen text; Yes/No only as a last resort
            if (Regex.IsMatch(rawName, @"^c_item_\d$"))
            {
                int btnIdx = rawName[rawName.Length - 1] - '0';

                // Read at most the first two texts — a button label, not a whole panel
                string btnLabel = ReadItemLabel(selectedItem);

                // Item text often lives in a sibling of the SelectItem — walk the row
                // container, but only when it looks like a button row (few texts);
                // otherwise it grabs unrelated screen text behind the dialog
                if (string.IsNullOrEmpty(btnLabel))
                {
                    try
                    {
                        var parent = (selectedItem as IObject)?.Call("get_Parent") as ManagedObject;
                        var parentTexts = GuiTextReader.ReadControlTexts(parent);
                        if (parentTexts.Count > 0 && parentTexts.Count <= 3)
                            btnLabel = parentTexts[0].Text;
                        API.LogInfo($"[SF6Access] {rawName}: subtree empty, parent has {parentTexts.Count} texts, label='{btnLabel}'");
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(btnLabel))
                    btnLabel = btnIdx == 0 ? "Yes" : "No";

                // In a text-entry dialog the name field between the buttons
                // fires no focus event, so returning to a button looked
                // unchanged to the dedup and went silent. Announce every focus
                // there — the 250 ms speech filter still drops the 2x-per-
                // navigation re-fire.
                bool forceRead = TextInputDialogHooks.IsActive;
                if (forceRead || GameStateTracker.HasChanged("focus_item", $"{rawName}|{btnLabel}"))
                {
                    API.LogInfo($"[SF6Access] Item button [{rawName}]: {btnLabel}");
                    ScreenReaderService.Speak(btnLabel);
                    FocusValueHooks.Track(selectedItem);
                }
                return;
            }

            // Events banner in the multi menu: announce the focused event
            if (rawName.Contains("nnounce") || rawName.Contains("banner", StringComparison.OrdinalIgnoreCase))
            {
                EventBannerHooks.AnnounceCurrent();
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

            // Generic menus (custom rooms, lobby forms): watch the focused control
            // so value changes (spin left/right) are announced without a focus change.
            // The arcade settings menu has its own data-driven value reader
            // (ArcadeSettingHooks) — skip the generic watcher to avoid double-reads.
            if (ArcadeSettingHooks.IsActive)
                FocusValueHooks.Clear();
            else
                FocusValueHooks.Track(selectedItem);

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

            // Key on text too: repeated control names (search form rows etc.)
            // would otherwise announce only the first row
            if (GameStateTracker.HasChanged("focus_item", $"{rawName}|{announcement}"))
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
    /// <summary>Read up to the first two visible texts under a control (item label + value).</summary>
    private static string ReadItemLabel(ManagedObject control)
    {
        try
        {
            var texts = GuiTextReader.ReadControlTexts(control);
            var parts = new System.Collections.Generic.List<string>();
            foreach (var t in texts)
            {
                if (string.IsNullOrWhiteSpace(t.Text)) continue;
                parts.Add(t.Text.Trim());
                if (parts.Count >= 2) break;
            }
            return parts.Count > 0 ? string.Join(". ", parts) : null;
        }
        catch { return null; }
    }

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

        // Fallback: read all visible texts under the item's GUI subtree
        // (covers custom room lists, lobby forms and other generic menus)
        try
        {
            string joined = GuiTextReader.ReadControlTextJoined(selectedItem);
            if (!string.IsNullOrEmpty(joined))
                return joined;
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
