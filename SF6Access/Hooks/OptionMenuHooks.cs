using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

public class OptionMenuHooks
{
    private static Method _msgGetMethod;
    private static Field _titleMsgField;
    private static Field _descMsgField;
    private static Field _dataTypeField;
    private static Field _typeIdField;
    private static Field _eventTypeField;
    private static bool _fieldsCached;

    // DecideEventType of the currently focused option (3 = sub-list/dropdown)
    private static int _lastFocusedEventType = -1;

    // Collect all SwitchFocus(true) addresses per frame
    private static readonly List<ulong> _pendingAddrs = new();
    private static bool _hasPendingRead;
    private static string _lastAnnouncement;

    // Flag to suppress MainMenuHooks.FocusChanged while in option menu
    private static bool _isInOptionMenu;
    public static bool IsInOptionMenu
    {
        get => _isInOptionMenu;
        set
        {
            if (_isInOptionMenu && !value)
            {
                // Leaving option menu - reset ALL option state
                _currentTab = -1;
                _lastAnnouncement = null;
                _lastFocusedSetting = null;
                _lastFocusedTypeId = -1;
                _lastFocusedEventType = -1;
                _lastPolledValue = -1;
                _lastSubListIndex = -1;
                _lastFocusedAddr = 0;
                _optionMenuParam = null;

            }
            _isInOptionMenu = value;
        }
    }

    // Track focused option for value change polling and sub-list reading
    private static ulong _lastFocusedAddr;
    private static int _lastFocusedTypeId = -1;
    private static int _lastPolledValue = -1;
    private static ManagedObject _optionManager;
    private static ManagedObject _lastFocusedSetting;

    // Sub-list (language selection etc.) tracking
    private static bool _subListChanged;
    private static int _lastSubListIndex = -1;
    private static ManagedObject _lastSubList;

    // OptionMenuParam for ListState polling
    private static ManagedObject _optionMenuParam;
    private static Field _listStateField;

    // Tab detection
    private static int _currentTab = -1;
    private static readonly string[] OptionTabNames = {
        "General", "Interface", "Battle", "Field", "Audio", "Language", "Graphic"
    };

    // Cache resolved titles/descriptions by Setting address
    private static readonly Dictionary<ulong, string> _titleCache = new();
    private static readonly Dictionary<ulong, string> _descCache = new();

    [PluginEntryPoint]
    public static void Initialize()
    {
        var td = TDB.Get().FindType("app.UIPartsOptionUnit");
        if (td == null)
        {
            API.LogError("[SF6Access] UIPartsOptionUnit type not found");
            return;
        }

        var switchFocus = td.GetMethod("SwitchFocus");
        if (switchFocus == null)
        {
            API.LogError("[SF6Access] SwitchFocus method not found");
            return;
        }

        var hook = switchFocus.AddHook(false);
        hook.AddPre(args =>
        {
            try
            {
                bool isFocus = args[2] != 0;
                if (!isFocus) return PreHookResult.Continue;

                IsInOptionMenu = true;
                _pendingAddrs.Add(args[1]);
                _hasPendingRead = true;
            }
            catch (Exception ex)
            {
                API.LogError($"[SF6Access] SwitchFocus error: {ex.Message}");
            }

            return PreHookResult.Continue;
        });

        // Hook SimpleList for navigation within open dropdowns
        // Use OptionMenuParam.ListState to detect when sub-list is open
        var simpleListTd = TDB.Get().FindType("app.UIPartsSimpleList");
        if (simpleListTd != null)
        {
            var invokeChanged = simpleListTd.GetMethod("InvokeSelectionChanged");
            if (invokeChanged != null)
            {
                var slHook = invokeChanged.AddHook(false);
                slHook.AddPre(args =>
                {
                    try
                    {
                        if (!_isInOptionMenu || _lastFocusedSetting == null)
                            return PreHookResult.Continue;

                        // Poll ListState from OptionMenuParam
                        if (!IsSubListOpen())
                            return PreHookResult.Continue;

                        var simpleList = ManagedObject.ToManagedObject(args[1]);
                        if (simpleList == null) return PreHookResult.Continue;

                        // Don't read SelectedIndex here: in this PRE hook the
                        // selection is not updated yet (announced previous row,
                        // user picked Portuguese thinking it was Spanish).
                        // ProcessSubListChange reads it fresh on the next update.
                        _lastSubList = simpleList;
                        _subListChanged = true;
                    }
                    catch { }
                    return PreHookResult.Continue;
                });
                API.LogInfo("[SF6Access] SimpleList selection hook installed");
            }
        }

        API.LogInfo("[SF6Access] Option menu hook installed (dynamic)");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        // Handle focus changes
        if (_hasPendingRead)
        {
            _hasPendingRead = false;
            _lastSubListIndex = -1;
            _subListChanged = false;
            ProcessFocusChange();
            return;
        }

        // Handle sub-list selection changes (language list etc.)
        if (_subListChanged)
        {
            _subListChanged = false;
            ProcessSubListChange();
        }

        // Poll for value changes on the focused option
        PollValueChange();
    }

    // Language names by via.Language enum value for fallback
    private static readonly string[] LanguageNames = {
        "Japanese", "English", "French", "Italian", "German", "Spanish",
        "Portuguese", "Russian", "Polish", "Dutch", "Norwegian", "Swedish",
        "Chinese (Traditional)", "Chinese (Simplified)", "Korean", "Finnish",
        "Thai", "Czech", "Hungarian", "Indonesian", "Vietnamese", "Romanian",
        "Turkish", "Arabic", "Hindi", "Hebrew"
    };

    private static void ProcessSubListChange()
    {
        if (_lastFocusedSetting == null || _lastSubList == null) return;

        // Read the index now — the selection has been applied by this point
        int idx = -1;
        try
        {
            var idxObj = (_lastSubList as IObject)?.Call("get_SelectedIndex");
            if (idxObj != null) idx = Convert.ToInt32(idxObj);
        }
        catch { }
        if (idx < 0 || idx == _lastSubListIndex) return;
        _lastSubListIndex = idx;

        try
        {
            string label = null;
            try
            {
                label = (_lastFocusedSetting as IObject)?.Call("GetValueMessage", _lastSubListIndex) as string;
                label = CleanTags(label);
            }
            catch { }

            // Read the focused row's on-screen text (matches game language and
            // the list's real display order — the hardcoded table did not)
            if (string.IsNullOrEmpty(label))
                label = ReadSubListRowText(_lastSubListIndex);

            // Last resort for language options: known language names table
            if (string.IsNullOrEmpty(label) && _lastFocusedTypeId > 0)
            {
                // TypeIds 610-640 are language options
                int typeGroup = _lastFocusedTypeId / 100;
                if (typeGroup == 6 && _lastSubListIndex >= 0 && _lastSubListIndex < LanguageNames.Length)
                    label = LanguageNames[_lastSubListIndex];
            }

            if (!string.IsNullOrEmpty(label))
            {
                API.LogInfo($"[SF6Access] Sub-list item [{_lastSubListIndex}]: {label}");
                ScreenReaderService.Speak(label);
            }
            else
            {
                API.LogInfo($"[SF6Access] Sub-list index {_lastSubListIndex} (no label, typeId={_lastFocusedTypeId})");
            }
        }
        catch { }
    }

    /// <summary>Localized tab name from the options tab bar (mTabList children).</summary>
    private static string ReadTabName(int tabIndex)
    {
        try
        {
            // Local only — assigning _optionMenuParam here would skip the
            // _listStateField initialization in IsSubListOpen and break dropdowns
            var param = _optionMenuParam ?? FlowHelper.FindFlowParam("app.UIOptionSettingMenu.OptionMenuParam");
            if (param == null) return null;

            var tabList = param.GetField("<mTabList>k__BackingField") as ManagedObject
                ?? param.GetField("mTabList") as ManagedObject;
            var children = tabList?.GetField("_Children") as ManagedObject;
            var child = (children as IObject)?.Call("get_Item", tabIndex) as ManagedObject;
            var control = child?.GetField("<Control>k__BackingField") as ManagedObject
                ?? (child as IObject)?.Call("get_Control") as ManagedObject;

            string name = GuiTextReader.ReadControlTextJoined(control);
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch { return null; }
    }

    /// <summary>Read the sub-list row's displayed text (child part first, list texts fallback).</summary>
    private static string ReadSubListRowText(int idx)
    {
        if (_lastSubList == null || idx < 0) return null;
        try
        {
            var children = (_lastSubList as IObject)?.Call("get__Children") as ManagedObject
                ?? _lastSubList.GetField("_Children") as ManagedObject;
            if (children != null)
            {
                var child = (children as IObject)?.Call("get_Item", idx) as ManagedObject;
                var control = child?.GetField("<Control>k__BackingField") as ManagedObject
                    ?? (child as IObject)?.Call("get_Control") as ManagedObject;
                string text = GuiTextReader.ReadControlTextJoined(control);
                if (!string.IsNullOrEmpty(text)) return text;
            }

            // Uniform single-text rows: Nth text under the list control
            var listControl = _lastSubList.GetField("<Control>k__BackingField") as ManagedObject
                ?? (_lastSubList as IObject)?.Call("get_Control") as ManagedObject;
            var texts = GuiTextReader.ReadControlTexts(listControl);
            if (idx < texts.Count) return texts[idx].Text;
        }
        catch { }
        return null;
    }

    private static void ProcessFocusChange()
    {
        try
        {
            // Iterate in reverse: last SwitchFocus event is the actual focused item
            for (int i = _pendingAddrs.Count - 1; i >= 0; i--)
            {
                var addr = _pendingAddrs[i];
                if (addr == 0) continue;

                var optUnit = ManagedObject.ToManagedObject(addr);
                if (optUnit == null) continue;

                ManagedObject setting = null;
                try { setting = (optUnit as IObject)?.Call("get_Setting") as ManagedObject; }
                catch { }

                if (setting == null) continue;

                CacheSettingFields();

                int dataType = GetIntField(setting, _dataTypeField);
                int typeId = GetIntField(setting, _typeIdField);

                string title = ResolveGuidField(setting, _titleMsgField, _titleCache);
                string description = ResolveGuidField(setting, _descMsgField, _descCache);

                // Get current value from OptionManager if this is a value option
                string valueLabel = null;
                if (dataType == 1 && typeId > 0)
                {
                    int currentValue = GetOptionValue(typeId);
                    if (currentValue >= 0)
                    {
                        try
                        {
                            valueLabel = (setting as IObject)?.Call("GetValueMessage", currentValue) as string;
                            valueLabel = CleanTags(valueLabel);
                        }
                        catch { }

                        // Only show raw number for true sliders (no ValueMessageList)
                        // Don't show raw number for enums whose label failed to resolve
                        if (string.IsNullOrEmpty(valueLabel))
                        {
                            if (GetValueMessageCount(setting) == 0)
                                valueLabel = currentValue.ToString();
                        }
                    }
                }

                // Detect tab change from TypeId ranges (100s = General, 200s = Interface, etc.)
                string tabPrefix = null;
                if (typeId >= 100)
                {
                    int tab = typeId / 100 - 1;
                    if (tab >= 0 && tab < OptionTabNames.Length && tab != _currentTab)
                    {
                        _currentTab = tab;
                        // Localized tab name from the on-screen tab bar; English fallback
                        tabPrefix = ReadTabName(tab) ?? OptionTabNames[tab];
                        API.LogInfo($"[SF6Access] Option tab: {tabPrefix}");
                    }
                }

                var parts = new List<string>();
                if (tabPrefix != null)
                    parts.Add(tabPrefix);
                if (!string.IsNullOrEmpty(title))
                    parts.Add(title);
                if (!string.IsNullOrEmpty(valueLabel))
                    parts.Add(valueLabel);
                if (!string.IsNullOrEmpty(description) && description != title)
                    parts.Add(description);

                string announcement = parts.Count > 0 ? string.Join(". ", parts) : null;
                if (!string.IsNullOrEmpty(announcement) && announcement != _lastAnnouncement)
                {
                    _lastAnnouncement = announcement;
                    _lastFocusedAddr = addr;
                    _lastFocusedSetting = setting;
                    _lastFocusedTypeId = (dataType == 1) ? typeId : -1;
                    _lastFocusedEventType = GetIntField(setting, _eventTypeField);
                    _lastPolledValue = (dataType == 1 && typeId > 0) ? GetOptionValue(typeId) : -1;
                    API.LogInfo($"[SF6Access] Option: {announcement}");
                    ScreenReaderService.Speak(announcement);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] Option read error: {ex.Message}");
        }
        finally
        {
            _pendingAddrs.Clear();
        }
    }

    private static void PollValueChange()
    {
        if (_lastFocusedTypeId <= 0) return;

        try
        {
            int currentValue = GetOptionValue(_lastFocusedTypeId);
            if (currentValue < 0 || currentValue == _lastPolledValue) return;

            _lastPolledValue = currentValue;

            if (_lastFocusedSetting == null) return;

            string valueLabel = null;
            try
            {
                valueLabel = (_lastFocusedSetting as IObject)?.Call("GetValueMessage", currentValue) as string;
                valueLabel = CleanTags(valueLabel);
            }
            catch { }

            if (string.IsNullOrEmpty(valueLabel))
            {
                if (GetValueMessageCount(_lastFocusedSetting) == 0)
                    valueLabel = currentValue.ToString();
            }

            if (string.IsNullOrEmpty(valueLabel)) return;

            _lastAnnouncement = null;
            API.LogInfo($"[SF6Access] Value changed: {valueLabel}");
            ScreenReaderService.Speak(valueLabel);
        }
        catch { }
    }

    private static int GetOptionValue(int typeId)
    {
        try
        {
            _optionManager ??= API.GetManagedSingleton("app.OptionManager");
            if (_optionManager == null) return -1;

            var result = (_optionManager as IObject)?.Call("GetOptionValue", typeId);
            return result != null ? Convert.ToInt32(result) : -1;
        }
        catch { return -1; }
    }

    private static int GetIntField(ManagedObject obj, Field field)
    {
        if (field == null || obj == null) return -1;
        try
        {
            ulong addr = obj.GetAddress();
            if (addr == 0) return -1;
            var raw = field.GetDataBoxed(typeof(int), addr, false);
            return raw != null ? Convert.ToInt32(raw) : -1;
        }
        catch { return -1; }
    }

    /// <summary>
    /// Check if the option menu currently has a sub-list dropdown open
    /// by reading OptionMenuParam.ListState == SubList (1).
    /// </summary>
    private static bool IsSubListOpen()
    {
        try
        {
            // Find OptionMenuParam if we don't have it
            if (_optionMenuParam == null)
            {
                var flowMgr = API.GetManagedSingleton("app.UIFlowManager");
                if (flowMgr == null) return false;

                var handlesField = flowMgr.GetTypeDefinition()?.GetField("_Handles");
                if (handlesField == null) return false;

                var handles = handlesField.GetDataBoxed(typeof(object), flowMgr.GetAddress(), false) as ManagedObject;
                if (handles == null) return false;

                var countMethod = handles.GetTypeDefinition()?.GetMethod("get_Count");
                var getItemMethod = handles.GetTypeDefinition()?.GetMethod("get_Item(System.Int32)");
                if (countMethod == null || getItemMethod == null) return false;

                int count = Convert.ToInt32(countMethod.InvokeBoxed(typeof(int), handles, Array.Empty<object>()));
                for (int i = 0; i < count && i < 20; i++)
                {
                    try
                    {
                        var handle = getItemMethod.InvokeBoxed(typeof(object), handles, new object[] { i }) as ManagedObject;
                        if (handle == null) continue;
                        var param = handle.GetField("<Param>k__BackingField") as ManagedObject;
                        if (param?.GetTypeDefinition()?.FullName == "app.UIOptionSettingMenu.OptionMenuParam")
                        {
                            _optionMenuParam = param;
                            _listStateField = param.GetTypeDefinition().GetField("<ListState>k__BackingField");
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (_optionMenuParam == null || _listStateField == null)
                return false;

            // Read ListState: SubList=1, RadioButtonList=2 (both are open dropdowns)
            var raw = _listStateField.GetDataBoxed(typeof(int), _optionMenuParam.GetAddress(), false);
            int state = raw != null ? Convert.ToInt32(raw) : 0;
            return state == 1 || state == 2;
        }
        catch
        {
            _optionMenuParam = null;
            return false;
        }
    }

    /// <summary>
    /// Returns number of entries in ValueMessageList.
    /// 0 means it's a numeric/slider (show raw number).
    /// > 0 means it's an enum (don't show raw number if label fails).
    /// </summary>
    private static int GetValueMessageCount(ManagedObject setting)
    {
        try
        {
            var msgList = (setting as IObject)?.Call("GetValueMessageList") as ManagedObject;
            if (msgList != null)
            {
                var countMethod = msgList.GetTypeDefinition()?.GetMethod("get_Count");
                if (countMethod != null)
                {
                    var cnt = countMethod.InvokeBoxed(typeof(int), msgList, Array.Empty<object>());
                    if (cnt != null) return Convert.ToInt32(cnt);
                }
            }
        }
        catch { }
        return 0;
    }

    private static void CacheSettingFields()
    {
        if (_fieldsCached) return;
        _fieldsCached = true;

        var td = TDB.Get().FindType("app.Option.OptionSettingUnit");
        if (td == null) return;

        _titleMsgField = td.GetField("TitleMessage");
        _descMsgField = td.GetField("DescriptionMessage");
        _dataTypeField = td.GetField("_DataType");
        _typeIdField = td.GetField("TypeId");
        _eventTypeField = td.GetField("EventType");
    }

    private static string ResolveGuidField(ManagedObject setting, Field guidField, Dictionary<ulong, string> cache)
    {
        if (guidField == null) return null;

        try
        {
            ulong addr = setting.GetAddress();
            if (addr == 0) return null;

            if (cache.TryGetValue(addr, out var cached))
                return cached;

            var rawValue = guidField.GetDataBoxed(typeof(Guid), addr, false);
            if (rawValue is not REFrameworkNET.ValueType vt) return null;

            ulong vtAddr = vt.GetAddress();
            if (vtAddr == 0) return null;

            bool allZero = true;
            for (int i = 0; i < 16; i++)
            {
                if (Marshal.ReadByte((IntPtr)(long)(vtAddr + (ulong)i)) != 0)
                { allZero = false; break; }
            }
            if (allZero) return null;

            var msgMethod = GetMsgMethod();
            if (msgMethod == null) return null;

            var task = Task.Run(() =>
            {
                try { return msgMethod.InvokeBoxed(typeof(string), null, new object[] { vt }) as string; }
                catch { return null; }
            });

            string text = null;
            if (task.Wait(TimeSpan.FromMilliseconds(200)))
                text = CleanTags(task.Result);

            if (!string.IsNullOrEmpty(text))
                cache[addr] = text;

            return text;
        }
        catch { return null; }
    }

    private static Method GetMsgMethod()
    {
        _msgGetMethod ??= TDB.Get().FindType("via.gui.message")?.GetMethod("get(System.Guid)");
        return _msgGetMethod;
    }

    private static string CleanTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Regex.Replace(Regex.Replace(text, @"<[^>]+>", "").Trim(), @"\s+", " ").Trim();
    }
}
