using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using REFrameworkNET;

namespace SF6Access.Services;

/// <summary>
/// Shared helpers for reading RE Engine UI flow state via REFramework.NET.
/// Field reads use GetField + GetDataBoxed directly because interface-declared
/// property getters do not dispatch on IL2CPP concrete types.
/// </summary>
public static partial class FlowHelper
{
    private static Field _handlesField;
    private static Method _msgGetMethod;
    private static bool _tdbCached;

    private static void CacheTDB()
    {
        if (_tdbCached) return;
        _tdbCached = true;
        _handlesField = TDB.Get().FindType("app.UIFlowManager")?.GetField("_Handles");
        _msgGetMethod = TDB.Get().FindType("via.gui.message")?.GetMethod("get(System.Guid)");
    }

    /// <summary>Find the first flow Param of the given concrete type in UIFlowManager._Handles.</summary>
    public static ManagedObject FindFlowParam(string typeFullName)
    {
        CacheTDB();
        if (_handlesField == null) return null;
        try
        {
            var flowMgr = API.GetManagedSingleton("app.UIFlowManager");
            if (flowMgr == null) return null;

            var handles = _handlesField.GetDataBoxed(typeof(object), flowMgr.GetAddress(), false) as ManagedObject;
            if (handles == null) return null;

            var td = handles.GetTypeDefinition();
            var countMethod = td?.GetMethod("get_Count");
            var getItemMethod = td?.GetMethod("get_Item(System.Int32)");
            if (countMethod == null || getItemMethod == null) return null;

            int count = Convert.ToInt32(countMethod.InvokeBoxed(typeof(int), handles, Array.Empty<object>()));
            for (int i = 0; i < count && i < 50; i++)
            {
                try
                {
                    var handle = getItemMethod.InvokeBoxed(typeof(object), handles, new object[] { i }) as ManagedObject;
                    var param = handle?.GetField("<Param>k__BackingField") as ManagedObject;
                    if (param?.GetTypeDefinition()?.FullName == typeFullName)
                        return param;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    /// <summary>Find the first flow Param whose type name starts with the given prefix.</summary>
    public static ManagedObject FindFlowParamByPrefix(string typeNamePrefix, out string foundType)
    {
        var all = FindFlowParamsByPrefix(typeNamePrefix);
        if (all.Count == 0) { foundType = null; return null; }
        foundType = all[0].typeName;
        return all[0].param;
    }

    /// <summary>All flow Params whose type name starts with the prefix, in handle order (newest last).</summary>
    public static System.Collections.Generic.List<(string typeName, ManagedObject param)> FindFlowParamsByPrefix(string typeNamePrefix)
    {
        var results = new System.Collections.Generic.List<(string, ManagedObject)>();
        CacheTDB();
        if (_handlesField == null) return results;
        try
        {
            var flowMgr = API.GetManagedSingleton("app.UIFlowManager");
            if (flowMgr == null) return results;

            var handles = _handlesField.GetDataBoxed(typeof(object), flowMgr.GetAddress(), false) as ManagedObject;
            if (handles == null) return results;

            var td = handles.GetTypeDefinition();
            var countMethod = td?.GetMethod("get_Count");
            var getItemMethod = td?.GetMethod("get_Item(System.Int32)");
            if (countMethod == null || getItemMethod == null) return results;

            int count = Convert.ToInt32(countMethod.InvokeBoxed(typeof(int), handles, Array.Empty<object>()));
            for (int i = 0; i < count && i < 50; i++)
            {
                try
                {
                    var handle = getItemMethod.InvokeBoxed(typeof(object), handles, new object[] { i }) as ManagedObject;
                    var param = handle?.GetField("<Param>k__BackingField") as ManagedObject;
                    string name = param?.GetTypeDefinition()?.FullName;
                    if (name != null && name.StartsWith(typeNamePrefix))
                        results.Add((name, param));
                }
                catch { }
            }
        }
        catch { }
        return results;
    }

    /// <summary>
    /// ALL flow Params matching ANY of the prefixes, in handle order (newest
    /// first). Per-prefix iteration loses the global ordering: the avatar
    /// arcade param outranked the newer master menu param.
    /// </summary>
    public static System.Collections.Generic.List<(string typeName, ManagedObject param)> FindFlowParamsMatchingPrefixes(string[] prefixes)
    {
        var results = new System.Collections.Generic.List<(string, ManagedObject)>();
        CacheTDB();
        if (_handlesField == null) return results;
        try
        {
            var flowMgr = API.GetManagedSingleton("app.UIFlowManager");
            if (flowMgr == null) return results;

            var handles = _handlesField.GetDataBoxed(typeof(object), flowMgr.GetAddress(), false) as ManagedObject;
            if (handles == null) return results;

            var td = handles.GetTypeDefinition();
            var countMethod = td?.GetMethod("get_Count");
            var getItemMethod = td?.GetMethod("get_Item(System.Int32)");
            if (countMethod == null || getItemMethod == null) return results;

            int count = Convert.ToInt32(countMethod.InvokeBoxed(typeof(int), handles, Array.Empty<object>()));
            for (int i = 0; i < count && i < 50; i++)
            {
                try
                {
                    var handle = getItemMethod.InvokeBoxed(typeof(object), handles, new object[] { i }) as ManagedObject;
                    var param = handle?.GetField("<Param>k__BackingField") as ManagedObject;
                    string name = param?.GetTypeDefinition()?.FullName;
                    if (name == null) continue;

                    foreach (var prefix in prefixes)
                    {
                        if (name.StartsWith(prefix))
                        {
                            results.Add((name, param));
                            break;
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
        return results;
    }

    /// <summary>First (newest) flow Param per prefix, in a single pass over _Handles.</summary>
    public static System.Collections.Generic.Dictionary<string, (string typeName, ManagedObject param)> FindFirstFlowParamsByPrefixes(string[] prefixes)
    {
        var found = new System.Collections.Generic.Dictionary<string, (string, ManagedObject)>();
        CacheTDB();
        if (_handlesField == null) return found;
        try
        {
            var flowMgr = API.GetManagedSingleton("app.UIFlowManager");
            if (flowMgr == null) return found;

            var handles = _handlesField.GetDataBoxed(typeof(object), flowMgr.GetAddress(), false) as ManagedObject;
            if (handles == null) return found;

            var td = handles.GetTypeDefinition();
            var countMethod = td?.GetMethod("get_Count");
            var getItemMethod = td?.GetMethod("get_Item(System.Int32)");
            if (countMethod == null || getItemMethod == null) return found;

            int count = Convert.ToInt32(countMethod.InvokeBoxed(typeof(int), handles, Array.Empty<object>()));
            for (int i = 0; i < count && i < 50 && found.Count < prefixes.Length; i++)
            {
                try
                {
                    var handle = getItemMethod.InvokeBoxed(typeof(object), handles, new object[] { i }) as ManagedObject;
                    var param = handle?.GetField("<Param>k__BackingField") as ManagedObject;
                    string name = param?.GetTypeDefinition()?.FullName;
                    if (name == null) continue;

                    foreach (var prefix in prefixes)
                    {
                        if (!found.ContainsKey(prefix) && name.StartsWith(prefix))
                            found[prefix] = (name, param);
                    }
                }
                catch { }
            }
        }
        catch { }
        return found;
    }

    /// <summary>
    /// Find flow Params for several types in one pass, keeping _Handles order
    /// (index 0 = newest / topmost). Lets multi-screen adapters arbitrate which
    /// of several COEXISTING Params owns the screen: backed-out screens can
    /// linger in the handles (RestoreFlow), so a fixed priority goes stale —
    /// the first watched type in handle order is the active one.
    /// </summary>
    public static System.Collections.Generic.List<(string typeName, ManagedObject param)>
        FindFlowParamsOrdered(string[] typeFullNames)
    {
        var found = new System.Collections.Generic.List<(string, ManagedObject)>();
        var seen = new System.Collections.Generic.HashSet<string>();
        CacheTDB();
        if (_handlesField == null) return found;
        try
        {
            var flowMgr = API.GetManagedSingleton("app.UIFlowManager");
            if (flowMgr == null) return found;

            var handles = _handlesField.GetDataBoxed(typeof(object), flowMgr.GetAddress(), false) as ManagedObject;
            if (handles == null) return found;

            var td = handles.GetTypeDefinition();
            var countMethod = td?.GetMethod("get_Count");
            var getItemMethod = td?.GetMethod("get_Item(System.Int32)");
            if (countMethod == null || getItemMethod == null) return found;

            int count = Convert.ToInt32(countMethod.InvokeBoxed(typeof(int), handles, Array.Empty<object>()));
            for (int i = 0; i < count && i < 50; i++)
            {
                try
                {
                    var handle = getItemMethod.InvokeBoxed(typeof(object), handles, new object[] { i }) as ManagedObject;
                    var param = handle?.GetField("<Param>k__BackingField") as ManagedObject;
                    string name = param?.GetTypeDefinition()?.FullName;
                    if (name == null) continue;

                    foreach (var target in typeFullNames)
                    {
                        if (name == target && seen.Add(target))
                            found.Add((name, param));
                    }
                }
                catch { }
            }
        }
        catch { }
        return found;
    }

    /// <summary>Find flow Params for several types in a single pass over _Handles.</summary>
    public static System.Collections.Generic.Dictionary<string, ManagedObject> FindFlowParams(string[] typeFullNames)
    {
        var found = new System.Collections.Generic.Dictionary<string, ManagedObject>();
        CacheTDB();
        if (_handlesField == null) return found;
        try
        {
            var flowMgr = API.GetManagedSingleton("app.UIFlowManager");
            if (flowMgr == null) return found;

            var handles = _handlesField.GetDataBoxed(typeof(object), flowMgr.GetAddress(), false) as ManagedObject;
            if (handles == null) return found;

            var td = handles.GetTypeDefinition();
            var countMethod = td?.GetMethod("get_Count");
            var getItemMethod = td?.GetMethod("get_Item(System.Int32)");
            if (countMethod == null || getItemMethod == null) return found;

            int count = Convert.ToInt32(countMethod.InvokeBoxed(typeof(int), handles, Array.Empty<object>()));
            for (int i = 0; i < count && i < 50; i++)
            {
                try
                {
                    var handle = getItemMethod.InvokeBoxed(typeof(object), handles, new object[] { i }) as ManagedObject;
                    var param = handle?.GetField("<Param>k__BackingField") as ManagedObject;
                    string name = param?.GetTypeDefinition()?.FullName;
                    if (name == null) continue;

                    foreach (var target in typeFullNames)
                    {
                        if (name == target && !found.ContainsKey(target))
                            found[target] = param;
                    }
                }
                catch { }
            }
        }
        catch { }
        return found;
    }

    /// <summary>
    /// Track the live instance of a flow Param across menu re-entries: re-find
    /// it in _Handles and report when the instance changed. The game recreates
    /// Params when a menu is reopened, while our ManagedObject reference pins
    /// the old (dead) one — every child object cached from it must be re-cached
    /// when this reports a change. Returns null when the flow ended.
    /// </summary>
    public static ManagedObject TrackFlowParam(string typeFullName, ManagedObject cached, out bool instanceChanged)
    {
        var current = FindFlowParam(typeFullName);
        instanceChanged = current != null && (cached == null || AddressOf(current) != AddressOf(cached));
        return current;
    }

    /// <summary>Object address, 0 when unavailable (object freed or null).</summary>
    public static ulong AddressOf(ManagedObject obj)
    {
        try { return obj?.GetAddress() ?? 0; }
        catch { return 0; }
    }

    /// <summary>Read an object field, trying the plain name and the auto-property backing field.</summary>
    public static ManagedObject GetObjectField(ManagedObject obj, string name)
    {
        if (obj == null) return null;
        try { var v = obj.GetField(name) as ManagedObject; if (v != null) return v; } catch { }
        try { return obj.GetField($"<{name}>k__BackingField") as ManagedObject; } catch { }
        return null;
    }

    public static int ReadIntField(ManagedObject obj, string name, int fallback = -1)
    {
        if (obj == null) return fallback;
        try
        {
            var td = obj.GetTypeDefinition();
            var field = td?.GetField($"<{name}>k__BackingField") ?? td?.GetField(name);
            if (field != null)
                return Convert.ToInt32(field.GetDataBoxed(typeof(int), obj.GetAddress(), false));
        }
        catch { }
        return fallback;
    }

    /// <summary>Read an 8-bit (byte/sbyte enum) field as int. Reading a byte via
    /// the int path would pull in the adjacent field's bytes, so the type must match.</summary>
    public static int ReadByteField(ManagedObject obj, string name, int fallback = 0)
    {
        if (obj == null) return fallback;
        try
        {
            var td = obj.GetTypeDefinition();
            var field = td?.GetField($"<{name}>k__BackingField") ?? td?.GetField(name);
            if (field != null)
                return Convert.ToInt32(field.GetDataBoxed(typeof(byte), obj.GetAddress(), false));
        }
        catch { }
        return fallback;
    }

    /// <summary>Read a 16-bit (short) field as int. Reading a short via the int
    /// path would pull in the adjacent field's bytes, so the type must match.</summary>
    public static int ReadShortField(ManagedObject obj, string name, int fallback = 0)
    {
        if (obj == null) return fallback;
        try
        {
            var td = obj.GetTypeDefinition();
            var field = td?.GetField($"<{name}>k__BackingField") ?? td?.GetField(name);
            if (field != null)
                return Convert.ToInt32(field.GetDataBoxed(typeof(short), obj.GetAddress(), false));
        }
        catch { }
        return fallback;
    }

    public static float ReadFloatField(ManagedObject obj, string name, float fallback = float.NaN)
    {
        if (obj == null) return fallback;
        try
        {
            var td = obj.GetTypeDefinition();
            var field = td?.GetField(name) ?? td?.GetField($"<{name}>k__BackingField");
            if (field != null)
                return Convert.ToSingle(field.GetDataBoxed(typeof(float), obj.GetAddress(), false));
        }
        catch { }
        return fallback;
    }

    public static string ReadStringField(ManagedObject obj, string name)
    {
        if (obj == null) return null;
        try
        {
            var td = obj.GetTypeDefinition();
            var field = td?.GetField(name) ?? td?.GetField($"<{name}>k__BackingField");
            if (field != null)
                return field.GetDataBoxed(typeof(string), obj.GetAddress(), false) as string;
        }
        catch { }
        return null;
    }

    public static bool ReadBoolField(ManagedObject obj, string name)
    {
        if (obj == null) return false;
        try
        {
            var td = obj.GetTypeDefinition();
            var field = td?.GetField(name) ?? td?.GetField($"<{name}>k__BackingField");
            if (field != null)
            {
                var raw = field.GetDataBoxed(typeof(bool), obj.GetAddress(), false);
                return raw != null && Convert.ToBoolean(raw);
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// The training-mode display-settings record (app.training.TrainingData.TM_DisplaySetting)
    /// holding the user's on/off toggles (Is_FrameMeter_View, Is_DS_AD_View, etc.).
    /// TrainingManager._tData is only populated during a live training session
    /// (null on menus), so returns null outside training. Falls back to the
    /// display func's training-data reference.
    /// </summary>
    public static ManagedObject GetTrainingDisplaySetting()
    {
        try
        {
            var mgr = API.GetManagedSingleton("app.training.TrainingManager");
            if (mgr == null) return null;
            var tData = GetObjectField(mgr, "_tData");
            if (tData == null)
            {
                var func = GetObjectField(mgr, "DisplayFunc");
                tData = GetObjectField(func, "_tData");
            }
            return GetObjectField(tData, "DisplaySetting");
        }
        catch { return null; }
    }

    /// <summary>Call a method declared on the object's own class (not an interface). Returns null on failure.</summary>
    public static object Call(ManagedObject obj, string methodName, params object[] args)
    {
        if (obj == null) return null;
        try { return (obj as IObject)?.Call(methodName, args); }
        catch { return null; }
    }

    public static int CallInt(ManagedObject obj, string methodName, int fallback = -1)
    {
        var result = Call(obj, methodName);
        if (result == null) return fallback;
        try { return Convert.ToInt32(result); } catch { return fallback; }
    }

    private static bool IsArray(ManagedObject obj)
    {
        try { return obj?.GetTypeDefinition()?.FullName?.EndsWith("[]") == true; }
        catch { return false; }
    }

    public static int GetListCount(ManagedObject list)
    {
        if (list == null) return 0;
        var result = Call(list, IsArray(list) ? "get_Length" : "get_Count");
        if (result == null) return 0;
        try { return Convert.ToInt32(result); } catch { return 0; }
    }

    public static ManagedObject GetListItem(ManagedObject list, int index)
    {
        if (list == null) return null;
        return Call(list, IsArray(list) ? "Get" : "get_Item", index) as ManagedObject;
    }

    /// <summary>
    /// Read the on-screen text of the row a UIParts list/grid actually has
    /// selected, via get_SelectedItem. Index-based child reads proved
    /// unreliable: _Children order can be REVERSED relative to SelectedIndex
    /// (verified with the language list, announced bottom-to-top).
    /// </summary>
    public static string ReadSelectedItemText(ManagedObject listObj)
    {
        if (listObj == null) return null;
        try
        {
            var item = Call(listObj, "get_SelectedItem") as ManagedObject;
            if (item == null) return null;

            string text = FormatRowTexts(GuiTextReader.ReadControlTexts(item), 6);
            if (!string.IsNullOrEmpty(text)) return text;

            // Some grids render labels as images and keep the text hidden
            // (master select panels) — fall back to the first hidden texts
            return FormatRowTexts(GuiTextReader.ReadControlTexts(item, visibleOnly: false), 4);
        }
        catch { return null; }
    }

    /// <summary>
    /// Join row texts for announcement: drops duplicate segments (custom room
    /// tables show "Looking for a fight!" twice) and reorders known row kinds —
    /// room table tiles put the booth number and mode before the rule string,
    /// replay rows put players and win/loss before the timestamp (tree order
    /// read only "18:35. 11/6/2026. 0" with the segment cap).
    /// </summary>
    public static string FormatRowTexts(
        System.Collections.Generic.List<GuiTextReader.GuiText> texts, int maxSegments)
    {
        if (texts == null || texts.Count == 0) return null;

        bool isTableTile = false, isReplayRow = false, isCharaRow = false;
        foreach (var t in texts)
        {
            if (t.Name == "e_txt_num") isTableTile = true;
            else if (t.Name == "e_result_") isReplayRow = true;
            else if (t.Name == "e_txt_chara") isCharaRow = true;
        }

        if (isTableTile || isReplayRow || isCharaRow)
        {
            // Stable sort: keep tree order within the same rank
            var indexed = new System.Collections.Generic.List<(int rank, int order, GuiTextReader.GuiText text)>();
            for (int i = 0; i < texts.Count; i++)
            {
                int rank = isReplayRow ? ReplayElementRank(texts[i].Name)
                    : isCharaRow ? CharaElementRank(texts[i].Name)
                    : TableElementRank(texts[i].Name);
                if (rank < 0) continue;
                indexed.Add((rank, i, texts[i]));
            }
            indexed.Sort((a, b) => a.rank != b.rank ? a.rank.CompareTo(b.rank) : a.order.CompareTo(b.order));

            texts = new System.Collections.Generic.List<GuiTextReader.GuiText>();
            foreach (var entry in indexed) texts.Add(entry.text);

            // These rows carry more meaningful segments than a generic row
            if (maxSegments < 10) maxSegments = 10;
        }

        var parts = new System.Collections.Generic.List<string>();
        foreach (var t in texts)
        {
            if (string.IsNullOrWhiteSpace(t.Text)) continue;
            string trimmed = t.Text.Trim();
            if (parts.Contains(trimmed)) continue;
            parts.Add(trimmed);
            if (parts.Count >= maxSegments) break;
        }
        return parts.Count > 0 ? string.Join(". ", parts) : null;
    }

    private static int TableElementRank(string name) => name switch
    {
        "e_txt_num" => 0,   // booth number
        "e_txt_mode" => 1,  // 1v1 / training
        "e_txt_rule" => 3,  // rounds/timer/wins string
        _ => 2,             // player names, statuses
    };

    private static int ReplayElementRank(string name) => name switch
    {
        "e_text_fid_num_" => 0, // player name (with their LP and result below)
        "e_text_lp_num" => 0,
        "e_result_" => 0,       // win/loss
        "e_time" => 1,
        "e_day" => 1,
        "e_play_num" => -1,     // bare view count reads as a stray "0" — drop
        _ => 2,
    };

    // Character-specific training settings rows (character, skill name, values)
    private static int CharaElementRank(string name) => name switch
    {
        "e_txt_chara" => 0,     // RYU
        "e_txt_name" => 1,      // Denjin Charge
        "e_txt_0" => 2,         // current / default value
        "e_text_value" => -1,   // gauge segments, dozens of bare "0"s — drop
        _ => 3,
    };

    /// <summary>
    /// Read the on-screen text of a UIParts list/grid row: child part at the
    /// given index, then all visible texts under its Control joined together.
    /// Prefer ReadSelectedItemText when reading the focused row.
    /// </summary>
    public static string ReadListRowText(ManagedObject partsObj, int index)
    {
        if (partsObj == null || index < 0) return null;
        try
        {
            var children = GetObjectField(partsObj, "_Children");
            var child = GetListItem(children, index);
            if (child == null) return null;

            var control = GetObjectField(child, "Control")
                ?? Call(child, "get_Control") as ManagedObject;
            return GuiTextReader.ReadControlTextJoined(control);
        }
        catch { return null; }
    }

    /// <summary>Read displayed text from a via.gui.Text (or similar) component.</summary>
    public static string ReadGuiText(ManagedObject textObj)
    {
        if (textObj == null) return null;
        foreach (var m in new[] { "get_Message", "get_Text", "get_String" })
        {
            var text = Call(textObj, m) as string;
            if (!string.IsNullOrEmpty(text)) return CleanTags(text);
        }
        return null;
    }

    /// <summary>Resolve a localized message Guid with a timeout (via.gui.message.get crashes for some Guids).</summary>
    public static string ResolveGuid(REFrameworkNET.ValueType guidVt)
    {
        CacheTDB();
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
                return CleanTags(task.Result);
        }
        catch { }
        return null;
    }

    private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<int, string>> _enumNameCache = new();

    /// <summary>Constant name for an enum value, resolved from the TDB (e.g.
    /// app.InputAssign.Digital.Id 8 → "BTL_Y"). Null when unknown.</summary>
    public static string ResolveEnumName(string enumTypeName, int value)
    {
        try
        {
            if (!_enumNameCache.TryGetValue(enumTypeName, out var map))
            {
                map = new System.Collections.Generic.Dictionary<int, string>();
                _enumNameCache[enumTypeName] = map;

                var fields = TDB.Get().FindType(enumTypeName)?.GetFields();
                if (fields != null)
                {
                    foreach (var f in fields)
                    {
                        if (f.Name == "value__") continue;
                        try
                        {
                            var raw = f.GetDataBoxed(typeof(int), 0, false);
                            if (raw != null) map[Convert.ToInt32(raw)] = f.Name;
                        }
                        catch { }
                    }
                }
            }
            return map.TryGetValue(value, out var name) ? name : null;
        }
        catch { return null; }
    }

    private static Method _getItemNameMethod;
    private static bool _itemNameCached;
    private static ManagedObject _inventoryManager;

    /// <summary>
    /// Localized inventory item name via app.InventoryManager.GetName
    /// (e.g. a Battle Pass / mail reward). Null when it cannot be resolved.
    /// </summary>
    public static string ResolveItemName(int category, uint id)
    {
        try
        {
            if (!_itemNameCached)
            {
                _itemNameCached = true;
                var invType = TDB.Get().FindType("app.InventoryManager");
                _getItemNameMethod = invType?.GetMethod("GetName(app.network.api.Enum.ItemCategory, System.UInt32)")
                    ?? invType?.GetMethod("GetName(app.ItemCategory, System.UInt32)")
                    ?? invType?.GetMethod("GetName");
            }
            if (_getItemNameMethod == null) return null;

            _inventoryManager ??= API.GetManagedSingleton("app.InventoryManager");
            if (_inventoryManager == null) return null;

            var name = _getItemNameMethod.InvokeBoxed(
                typeof(string), _inventoryManager, new object[] { category, id }) as string;
            return CleanTags(name);
        }
        catch { return null; }
    }

    /// <summary>Resolve a Guid stored in a field of the given object.</summary>
    public static string ResolveGuidField(ManagedObject obj, string fieldName)
    {
        if (obj == null) return null;
        try
        {
            var td = obj.GetTypeDefinition();
            var field = td?.GetField($"<{fieldName}>k__BackingField") ?? td?.GetField(fieldName);
            if (field == null) return null;

            var raw = field.GetDataBoxed(typeof(Guid), obj.GetAddress(), false);
            if (raw is REFrameworkNET.ValueType vt)
                return ResolveGuid(vt);
        }
        catch { }
        return null;
    }

    /// <summary>
    /// The raw 16-byte Guid in a field as a stable hex identity key (NOT the
    /// localized message it points to), or null when the field is absent or the
    /// Guid is all-zero. Use this to dedup on the line/message identity when the
    /// resolved text can vary frame-to-frame (e.g. subtitle typewriter reveal).
    /// </summary>
    public static string ReadGuidKey(ManagedObject obj, string fieldName)
    {
        if (obj == null) return null;
        try
        {
            var td = obj.GetTypeDefinition();
            var field = td?.GetField($"<{fieldName}>k__BackingField") ?? td?.GetField(fieldName);
            if (field == null) return null;

            var raw = field.GetDataBoxed(typeof(Guid), obj.GetAddress(), false);
            if (raw is REFrameworkNET.ValueType vt)
            {
                ulong addr = vt.GetAddress();
                if (addr == 0) return null;
                var bytes = new byte[16];
                Marshal.Copy((IntPtr)(long)addr, bytes, 0, 16);
                return Array.TrueForAll(bytes, b => b == 0) ? null : BitConverter.ToString(bytes);
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Return only the segments of newText not present in oldText
    /// ("Search range. Worldwide" → "Search range. Limited" yields "Limited").
    /// Falls back to the full new text when nothing differs segment-wise.
    /// </summary>
    public static string DiffSegments(string oldText, string newText)
    {
        if (string.IsNullOrEmpty(oldText) || string.IsNullOrEmpty(newText)) return newText;
        try
        {
            var oldSet = new System.Collections.Generic.HashSet<string>(
                oldText.Split(new[] { ". " }, StringSplitOptions.RemoveEmptyEntries));

            var changed = new System.Collections.Generic.List<string>();
            foreach (var part in newText.Split(new[] { ". " }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!oldSet.Contains(part))
                    changed.Add(part);
            }
            return changed.Count > 0 ? string.Join(". ", changed) : newText;
        }
        catch { return newText; }
    }

    private static ManagedObject _tableDataMgr;

    /// <summary>
    /// Resolve a localized World Tour master message from a master id, e.g.
    /// "StyleNameID" → "Luke's Style", "MasterUINameID" → "Luke". The master's
    /// name is rendered as a texture in-game (no text element), so this table
    /// lookup is the only way to recover it as speakable text. Read through the
    /// MasterProfileUserDataDict (a managed dictionary) rather than the native
    /// TryGetMasterProfileUserData(out ...), whose out-param invoke access-violates.
    /// </summary>
    public static string ResolveMasterMessage(uint masterId, string messageFieldName)
    {
        if (masterId == 0) return null;
        try
        {
            _tableDataMgr ??= API.GetManagedSingleton("app.TableDataManager");
            if (_tableDataMgr == null) return null;

            // Interface property getters don't dispatch on IL2CPP concrete types —
            // read the backing field directly.
            var dict = GetObjectField(_tableDataMgr, "MasterProfileUserDataDict");
            if (dict == null) return null;

            // Missing key throws a managed KeyNotFoundException (caught), not an AV.
            // The dict value is a RecordHolder<MasterProfileUserDataRecord> wrapper,
            // so unwrap to the actual record before reading the message field.
            var holder = Call(dict, "get_Item", masterId) as ManagedObject;
            if (holder == null) return null;

            var record = UnwrapRecord(holder, "MasterProfileUserDataRecord");
            if (record == null) return null;

            var msg = GetObjectField(record, messageFieldName);
            return ResolveGuidField(msg, "GUID");
        }
        catch { return null; }
    }

    private static ManagedObject _wtMasterMgr;
    private static Method _getFighterNameMethod;
    private static bool _fighterNameCached;
    private static Method _getMasterIdFromStyle;
    private static bool _masterIdFromStyleCached;

    /// <summary>
    /// Resolve a World Tour style id to its master's localized fighter name
    /// ("Ryu") via WTMasterManager.GetMasterIdFromStyleId → ResolveMasterFighterName.
    /// Used to name the currently-equipped style without entering its list.
    /// </summary>
    public static string ResolveStyleFighterName(uint styleId)
    {
        if (styleId == 0) return null;
        try
        {
            _wtMasterMgr ??= API.GetManagedSingleton("app.worldtour.WTMasterManager");
            if (_wtMasterMgr == null) return null;

            if (!_masterIdFromStyleCached)
            {
                _masterIdFromStyleCached = true;
                _getMasterIdFromStyle = TDB.Get().FindType("app.worldtour.WTMasterManager")
                    ?.GetMethod("GetMasterIdFromStyleId(System.UInt32)");
            }
            if (_getMasterIdFromStyle == null) return null;

            var res = _getMasterIdFromStyle.InvokeBoxed(typeof(uint), _wtMasterMgr,
                new object[] { styleId });
            uint masterId = res == null ? 0u : Convert.ToUInt32(res);
            return ResolveMasterFighterName(masterId);
        }
        catch { return null; }
    }

    /// <summary>
    /// Localized fighter name from a CHARA_ID (a byte enum), e.g. 1 → "Ryu",
    /// via app.IDScriptExtensions.GetFighterNameText. Shares the cached method
    /// with <see cref="ResolveMasterFighterName"/>.
    /// </summary>
    public static string ResolveFighterName(int charaId)
    {
        if (charaId <= 0) return null;
        try
        {
            if (!_fighterNameCached)
            {
                _fighterNameCached = true;
                _getFighterNameMethod = TDB.Get().FindType("app.IDScriptExtensions")
                    ?.GetMethod("GetFighterNameText(app.CHARA_ID)");
            }
            if (_getFighterNameMethod == null) return null;

            string name = _getFighterNameMethod.InvokeBoxed(typeof(string), null,
                new object[] { (byte)charaId }) as string;
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch { return null; }
    }

    /// <summary>
    /// Resolve a World Tour master's name as the underlying fighter's localized
    /// name ("Ryu"). The master's own name message is itself a texture WLTAG (no
    /// text), but WTMasterManager maps master id → udWTMasterUserData.FighterId
    /// (a CHARA_ID), which IDScriptExtensions.GetFighterNameText renders as text.
    /// </summary>
    public static string ResolveMasterFighterName(uint masterId)
    {
        if (masterId == 0) return null;
        try
        {
            _wtMasterMgr ??= API.GetManagedSingleton("app.worldtour.WTMasterManager");
            if (_wtMasterMgr == null) return null;

            var map = GetObjectField(_wtMasterMgr, "MasterDataMap");
            if (map == null) return null;

            var userData = Call(map, "get_Item", masterId) as ManagedObject;
            if (userData == null) return null;

            int fighterId = ReadIntField(userData, "FighterId");
            if (fighterId < 0) return null;

            if (!_fighterNameCached)
            {
                _fighterNameCached = true;
                _getFighterNameMethod = TDB.Get().FindType("app.IDScriptExtensions")
                    ?.GetMethod("GetFighterNameText(app.CHARA_ID)");
            }
            if (_getFighterNameMethod == null) return null;

            string name = _getFighterNameMethod.InvokeBoxed(typeof(string), null,
                new object[] { (byte)fighterId }) as string;
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch { return null; }
    }

    private static ManagedObject _wlCmdWordList;
    private static bool _wlCmdWordListCached;

    // WLTAG word-type (Arg0) whose entries are World Tour master names. Those
    // render as textures, so the word-list exchange returns nothing — fall back
    // to resolving the master id (Arg1) to the underlying fighter's name.
    private const uint WLTAG_WORDTYPE_MASTER = 2;

    /// <summary>
    /// Resolve the &lt;WLTAG CmdNo Arg0 Arg1&gt; tags of a raw GUI message to their
    /// localized text via app.MessageManager's registered WLCmdWordList command
    /// (the same word-list exchange the renderer runs: Arg0 = word type,
    /// Arg1 = message id). Used for texts the game composes only at render time
    /// (World Tour perk tooltips, master names). Unresolvable tags are stripped
    /// along with any other formatting tags.
    /// </summary>
    public static string ResolveWLTags(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        if (!raw.Contains("<WLTAG")) return CleanTags(raw);
        try
        {
            string resolved = Regex.Replace(raw, @"<WLTAG\b([^>]*)>", m =>
            {
                string attrs = m.Groups[1].Value;
                if (!TryReadTagAttr(attrs, "Arg0", out uint arg0) ||
                    !TryReadTagAttr(attrs, "Arg1", out uint arg1))
                    return "";

                string text = ResolveWLTagWordList(arg0, arg1);
                if (string.IsNullOrWhiteSpace(text) && arg0 == WLTAG_WORDTYPE_MASTER)
                    text = ResolveMasterFighterName(arg1);
                return string.IsNullOrWhiteSpace(text) ? "" : " " + text.Trim() + " ";
            });
            resolved = CleanTags(resolved);
            return Regex.Replace(resolved, @"[ \t]{2,}", " ").Trim();
        }
        catch { return CleanTags(raw); }
    }

    private static bool TryReadTagAttr(string attrs, string name, out uint value)
    {
        value = 0;
        var m = Regex.Match(attrs, name + @"\s*=\s*""(\d+)""");
        return m.Success && uint.TryParse(m.Groups[1].Value, out value);
    }

    /// <summary>Localized word-list text for a WLTAG (wordType, messageId) pair,
    /// via the WLCmdWordList command registered in app.MessageManager.WLTagCmdRegister
    /// (a static field). Null when the command or entry is unavailable.</summary>
    private static string ResolveWLTagWordList(uint wordType, uint messageId)
    {
        try
        {
            if (!_wlCmdWordListCached)
            {
                _wlCmdWordListCached = true;
                var field = TDB.Get().FindType("app.MessageManager")?.GetField("WLTagCmdRegister");
                var register = field?.GetDataBoxed(typeof(object), 0, true) as ManagedObject;
                int count = GetListCount(register);
                for (int i = 0; i < count; i++)
                {
                    var cmd = GetListItem(register, i);
                    if (cmd?.GetTypeDefinition()?.FullName?.Contains("WLCmdWordList") == true)
                    {
                        _wlCmdWordList = cmd;
                        break;
                    }
                }
            }
            if (_wlCmdWordList == null) return null;
            return Call(_wlCmdWordList, "CmdWordList", wordType, messageId) as string;
        }
        catch { return null; }
    }

    /// <summary>
    /// Unwrap a generic RecordHolder&lt;T&gt; to its inner record: return the first
    /// managed field whose type name contains <paramref name="recordTypeName"/>.
    /// </summary>
    private static ManagedObject UnwrapRecord(ManagedObject holder, string recordTypeName)
    {
        if (holder == null) return null;
        try
        {
            var td = holder.GetTypeDefinition();
            var fields = td?.GetFields();
            if (fields == null) return null;
            foreach (var f in fields)
            {
                string ftype = f.Type?.FullName;
                if (ftype != null && ftype.Contains(recordTypeName))
                {
                    var v = holder.GetField(f.Name) as ManagedObject;
                    if (v != null) return v;
                }
            }
        }
        catch { }
        return null;
    }

    private static Method _platformMsgMethod;
    private static bool _platformMsgCached;

    /// <summary>
    /// Resolve platform message tags before tag stripping: the Steam-store
    /// confirmation dialog's whole message is "&lt;PLATMSG Arg0 = "65"&gt;",
    /// which CleanTags would silently erase.
    /// </summary>
    public static string ResolvePlatformTags(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("<PLATMSG")) return text;
        try
        {
            if (!_platformMsgCached)
            {
                _platformMsgCached = true;
                _platformMsgMethod = TDB.Get().FindType("app.MessageManager")
                    ?.GetMethod("ExchangePlatformMessage(System.String)");
            }
            if (_platformMsgMethod == null) return text;

            return Regex.Replace(text, @"<PLATMSG\s*([^>]*)>", match =>
            {
                try
                {
                    var resolved = _platformMsgMethod.InvokeBoxed(typeof(string), null,
                        new object[] { match.Groups[1].Value }) as string;
                    return resolved ?? "";
                }
                catch { return ""; }
            });
        }
        catch { return text; }
    }


    // Display language option (app.Option TypeId DispLanguage = 611); value is
    // the index in the game's language list, in its options-menu order:
    // 0 Japanese, 1 English, 2 French, 3 Italian, 4 German, 5 Spanish,
    // 6 Russian, 7 Polish, 8 Portuguese (BR), 9 Korean, 10 Traditional Chinese,
    // 11 Simplified Chinese, 12 Arabic, 13 Latin American Spanish (added later).
    // Anchors 1/5/8/13 are runtime-confirmed; the rest follow the menu order.
    private const int DISP_LANGUAGE_TYPE_ID = 611;
    private static CommandWords _words;
    private static UiLang _wordsLang = (UiLang)(-1);
    private static long _wordsCheckedTick;
    private static ManagedObject _optionManager;

    /// <summary>Display-language bucket. The member order is the LocalizedText
    /// table index — keep both in sync.</summary>
    public enum UiLang { En, Es, Pt, Ja, Fr, It, De, Ru, Pl, Ko, ZhHant, ZhHans, Ar }

    /// <summary>Current display language, from the app.Option DispLanguage value.</summary>
    public static UiLang GetDisplayLang()
    {
        try
        {
            _optionManager ??= API.GetManagedSingleton("app.OptionManager");
            var result = Call(_optionManager, "GetOptionValue", DISP_LANGUAGE_TYPE_ID);
            int lang = result != null ? Convert.ToInt32(result) : -1;
            return lang switch
            {
                0 => UiLang.Ja,
                2 => UiLang.Fr,
                3 => UiLang.It,
                4 => UiLang.De,
                5 or 13 => UiLang.Es,
                6 => UiLang.Ru,
                7 => UiLang.Pl,
                8 => UiLang.Pt,
                9 => UiLang.Ko,
                10 => UiLang.ZhHant,
                11 => UiLang.ZhHans,
                12 => UiLang.Ar,
                _ => UiLang.En,
            };
        }
        catch { return UiLang.En; }
    }

    private static CommandWords CurrentWords()
    {
        long now = System.Environment.TickCount64;
        if (_words != null && now - _wordsCheckedTick < 10000) return _words;
        _wordsCheckedTick = now;

        try
        {
            var lang = GetDisplayLang();
            if (_words == null || lang != _wordsLang)
            {
                _wordsLang = lang;
                _words = BuildWords();
                API.LogInfo($"[SF6Access] Command vocabulary switched (display language={lang})");
            }
        }
        catch { _words ??= WordsEn; }
        return _words;
    }

    private static string SpeakMotion(string digits, CommandWords words)
    {
        if (words.Motions.TryGetValue(digits, out var known)) return known;

        var parts = new System.Collections.Generic.List<string>();
        foreach (char c in digits)
        {
            int d = c - '0';
            if (d >= 0 && d <= 9) parts.Add(words.Directions[d]);
        }
        return parts.Count > 0 ? string.Join(", ", parts) : digits;
    }

    /// <summary>
    /// Replace inline input tags with readable names instead of erasing them:
    /// tutorials and combo trials embed commands as &lt;INPT id="BTL_X"&gt;,
    /// &lt;CMD _236&gt; (numpad motion) and &lt;ICON p&gt; tags.
    /// Formatting tags (COLOR etc.) still vanish via CleanTags.
    /// </summary>
    public static string SpeakableIcons(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('<')) return text;
        try
        {
            var words = CurrentWords();
            return Regex.Replace(text, @"<(INPT|ICON|KEY|PAD|CMD)\b([^>]*)>", match =>
            {
                string attrs = match.Groups[2].Value;

                // id="BTL_X" attribute form (INPT tags)
                var idMatch = Regex.Match(attrs, @"id\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
                string token = idMatch.Success
                    ? idMatch.Groups[1].Value
                    : attrs.Replace("\"", "").Trim();
                if (string.IsNullOrEmpty(token)) return " ";

                return " " + SpeakToken(token, words) + " ";
            }, RegexOptions.IgnoreCase);
        }
        catch { return text; }
    }

    /// <summary>Spoken name for an input/icon token ("BTL_Y" → "triangle"),
    /// in the current display language.</summary>
    public static string SpeakInputToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try { return SpeakToken(token, CurrentWords()); }
        catch { return token; }
    }

    private static string SpeakToken(string token, CommandWords words)
    {
        if (words.Inputs.TryGetValue(token, out var inputName))
            return inputName;

        // Motion in numpad notation: "_236", "236"
        string motion = token.TrimStart('_');
        if (motion.Length > 0 && Regex.IsMatch(motion, @"^\d+$"))
            return SpeakMotion(motion, words);

        string iconKey = token.ToLowerInvariant();
        if (IconAliases.TryGetValue(iconKey, out var canonical)) iconKey = canonical;
        if (words.Icons.TryGetValue(iconKey, out var iconName))
            return iconName;

        // PS button glyphs: "BtnR1" → "R1"
        if (token.StartsWith("Btn", StringComparison.OrdinalIgnoreCase))
            return token.Substring(3);

        // Keyboard glyphs: "KeyR" → "R"
        if (token.StartsWith("Key", StringComparison.OrdinalIgnoreCase) && token.Length > 3)
            return token.Substring(3);

        // Menu-local input ids: "UITrainingOptionX" → "X"
        foreach (var prefix in new[] { "UITrainingOption", "ShortCutButton" })
        {
            if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && token.Length > prefix.Length)
                return token.Substring(prefix.Length);
        }

        return token.Replace("BTL_", "").Replace('_', ' ');
    }

    public static string CleanTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Regex.Replace(text, @"<[^>]+>", "").Trim();
    }

    private static Method _getOptionOnOff;
    private static bool _subtitleOptCached;
    // app.Option.ValueType.SubTitleDisplay — the "Subtitles" on/off display option.
    private const int SUBTITLE_DISPLAY_OPTION = 450;

    /// <summary>
    /// True when the game's Subtitles option is enabled. Read via
    /// app.Option.GetOptionValueOnOff(SubTitleDisplay) so dialogue/subtitle readers
    /// follow the player's setting. Fails OPEN (returns true) if the option cannot be
    /// read, so accessibility is never silently lost.
    /// </summary>
    public static bool AreSubtitlesEnabled()
    {
        try
        {
            if (!_subtitleOptCached)
            {
                _subtitleOptCached = true;
                _getOptionOnOff = TDB.Get().FindType("app.Option")
                    ?.GetMethod("GetOptionValueOnOff(app.Option.ValueType)");
            }
            if (_getOptionOnOff == null) return true;
            var r = _getOptionOnOff.InvokeBoxed(typeof(bool), null, new object[] { SUBTITLE_DISPLAY_OPTION });
            return r is bool b ? b : true;
        }
        catch { return true; }
    }
}
