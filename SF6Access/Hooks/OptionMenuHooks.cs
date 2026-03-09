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
    private static bool _fieldsCached;

    // Collect all SwitchFocus(true) addresses per frame
    private static readonly List<ulong> _pendingAddrs = new();
    private static bool _hasPendingRead;
    private static string _lastAnnouncement;

    // Track focused option for value change polling and sub-list reading
    private static ulong _lastFocusedAddr;
    private static int _lastFocusedTypeId = -1;
    private static int _lastPolledValue = -1;
    private static ManagedObject _optionManager;
    private static ManagedObject _lastFocusedSetting;

    // Sub-list (language selection etc.) tracking
    private static bool _subListChanged;
    private static int _lastSubListIndex = -1;

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

                _pendingAddrs.Add(args[1]);
                _hasPendingRead = true;
            }
            catch (Exception ex)
            {
                API.LogError($"[SF6Access] SwitchFocus error: {ex.Message}");
            }

            return PreHookResult.Continue;
        });

        // Hook UIPartsSimpleList for sub-list navigation (language selection etc.)
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
                        // Only handle if we have a focused value option
                        if (_lastFocusedSetting == null || _lastFocusedTypeId <= 0)
                            return PreHookResult.Continue;

                        var simpleList = ManagedObject.ToManagedObject(args[1]);
                        if (simpleList == null) return PreHookResult.Continue;

                        var idxObj = (simpleList as IObject)?.Call("get_SelectedIndex");
                        int idx = idxObj != null ? Convert.ToInt32(idxObj) : -1;

                        if (idx >= 0 && idx != _lastSubListIndex)
                        {
                            _lastSubListIndex = idx;
                            _subListChanged = true;
                        }
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
            _lastSubListIndex = -1; // Reset sub-list tracking on new focus
            ProcessFocusChange();
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

    private static void ProcessSubListChange()
    {
        if (_lastFocusedSetting == null || _lastSubListIndex < 0) return;

        try
        {
            string label = null;
            try
            {
                label = (_lastFocusedSetting as IObject)?.Call("GetValueMessage", _lastSubListIndex) as string;
                label = CleanTags(label);
            }
            catch { }

            if (!string.IsNullOrEmpty(label))
            {
                API.LogInfo($"[SF6Access] Sub-list item: {label}");
                ScreenReaderService.Speak(label);
            }
        }
        catch { }
    }

    private static void ProcessFocusChange()
    {
        try
        {
            foreach (var addr in _pendingAddrs)
            {
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

                        if (string.IsNullOrEmpty(valueLabel))
                            valueLabel = currentValue.ToString();
                    }
                }

                var parts = new List<string>();
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

            // Get the setting to resolve the value label
            if (_lastFocusedSetting == null) return;

            string valueLabel = null;
            try
            {
                valueLabel = (_lastFocusedSetting as IObject)?.Call("GetValueMessage", currentValue) as string;
                valueLabel = CleanTags(valueLabel);
            }
            catch { }

            if (string.IsNullOrEmpty(valueLabel))
                valueLabel = currentValue.ToString();

            _lastAnnouncement = null; // Reset so next focus reads full
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
