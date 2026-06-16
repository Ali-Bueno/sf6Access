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
public static class FlowHelper
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

    // --- Spoken command vocabulary, per display language ---
    // Indexed by digit 0-9 (numpad notation): 2=down, 6=forward...
    private sealed class CommandWords
    {
        public string[] Directions;     // numpad 0-9
        public System.Collections.Generic.Dictionary<string, string> Motions;
        public System.Collections.Generic.Dictionary<string, string> Icons;
        public System.Collections.Generic.Dictionary<string, string> Inputs;
    }

    private static readonly CommandWords WordsEn = new()
    {
        Directions = new[] { "neutral", "down-back", "down", "down-forward", "back",
            "neutral", "forward", "up-back", "up", "up-forward" },
        Motions = new()
        {
            { "236", "quarter circle forward" }, { "214", "quarter circle back" },
            { "623", "forward, down, down-forward" }, { "421", "back, down, down-back" },
            { "41236", "half circle forward" }, { "63214", "half circle back" },
            { "236236", "double quarter circle forward" }, { "214214", "double quarter circle back" },
            { "22", "down, down" }, { "44", "back, back" }, { "66", "forward, forward" },
        },
        Icons = new()
        {
            { "+", "plus" }, { "p", "punch" }, { "k", "kick" },
            { "lp", "light punch" }, { "mp", "medium punch" }, { "hp", "heavy punch" },
            { "lk", "light kick" }, { "mk", "medium kick" }, { "hk", "heavy kick" },
            { "ls", "light attack" }, { "ms", "medium attack" }, { "hs", "heavy attack" },
            { "di", "drive impact" }, { "dp", "drive parry" }, { "tr", "throw" },
            { "sm", "special move" }, { "sa", "super art" }, { "auto", "auto" }, { "n", "neutral" },
        },
        Inputs = new()
        {
            { "BTL_LR_LSX", "left or right" }, { "BTL_UD_LSY", "up or down" },
            { "BTL_PLUS_LS_U", "up" }, { "BTL_PLUS_LS_D", "down" },
            { "BTL_PLUS_LS_L", "left" }, { "BTL_PLUS_LS_R", "right" },
            { "BTL_LS_U", "up" }, { "BTL_LS_D", "down" }, { "BTL_LS_L", "left" }, { "BTL_LS_R", "right" },
            { "BTL_X", "square" }, { "BTL_Y", "triangle" }, { "BTL_A", "cross" }, { "BTL_B", "circle" },
            { "BTL_LB", "L1" }, { "BTL_RB", "R1" }, { "BTL_LT", "L2" }, { "BTL_RT", "R2" },
            { "BTL_L3", "L3" }, { "BTL_R3", "R3" }, { "BTL_LSB", "L3" }, { "BTL_RSB", "R3" },
            { "BTL_U", "up" }, { "BTL_D", "down" }, { "BTL_L", "left" }, { "BTL_R", "right" },
            { "UIDecide", "confirm" }, { "UICancel", "cancel" },
            { "MouseL", "left click" }, { "MouseR", "right click" },
        },
    };

    private static readonly CommandWords WordsEs = new()
    {
        Directions = new[] { "neutral", "abajo-atrás", "abajo", "abajo-adelante", "atrás",
            "neutral", "adelante", "arriba-atrás", "arriba", "arriba-adelante" },
        Motions = new()
        {
            { "236", "cuarto de círculo adelante" }, { "214", "cuarto de círculo atrás" },
            { "623", "adelante, abajo, abajo-adelante" }, { "421", "atrás, abajo, abajo-atrás" },
            { "41236", "medio círculo adelante" }, { "63214", "medio círculo atrás" },
            { "236236", "dos cuartos de círculo adelante" }, { "214214", "dos cuartos de círculo atrás" },
            { "22", "abajo, abajo" }, { "44", "atrás, atrás" }, { "66", "adelante, adelante" },
        },
        Icons = new()
        {
            { "+", "más" }, { "p", "puño" }, { "k", "patada" },
            { "lp", "puño ligero" }, { "mp", "puño medio" }, { "hp", "puño fuerte" },
            { "lk", "patada ligera" }, { "mk", "patada media" }, { "hk", "patada fuerte" },
            { "ls", "ataque ligero" }, { "ms", "ataque medio" }, { "hs", "ataque fuerte" },
            { "di", "drive impact" }, { "dp", "drive parry" }, { "tr", "agarre" },
            { "sm", "golpe especial" }, { "sa", "super art" }, { "auto", "auto" }, { "n", "neutral" },
        },
        Inputs = new()
        {
            { "BTL_LR_LSX", "izquierda o derecha" }, { "BTL_UD_LSY", "arriba o abajo" },
            { "BTL_PLUS_LS_U", "arriba" }, { "BTL_PLUS_LS_D", "abajo" },
            { "BTL_PLUS_LS_L", "izquierda" }, { "BTL_PLUS_LS_R", "derecha" },
            { "BTL_LS_U", "arriba" }, { "BTL_LS_D", "abajo" }, { "BTL_LS_L", "izquierda" }, { "BTL_LS_R", "derecha" },
            { "BTL_X", "cuadrado" }, { "BTL_Y", "triángulo" }, { "BTL_A", "equis" }, { "BTL_B", "círculo" },
            { "BTL_LB", "L1" }, { "BTL_RB", "R1" }, { "BTL_LT", "L2" }, { "BTL_RT", "R2" },
            { "BTL_L3", "L3" }, { "BTL_R3", "R3" }, { "BTL_LSB", "L3" }, { "BTL_RSB", "R3" },
            { "BTL_U", "arriba" }, { "BTL_D", "abajo" }, { "BTL_L", "izquierda" }, { "BTL_R", "derecha" },
            { "UIDecide", "confirmar" }, { "UICancel", "cancelar" },
            { "MouseL", "clic izquierdo" }, { "MouseR", "clic derecho" },
        },
    };

    private static readonly CommandWords WordsPt = new()
    {
        Directions = new[] { "neutro", "baixo-trás", "baixo", "baixo-frente", "trás",
            "neutro", "frente", "cima-trás", "cima", "cima-frente" },
        Motions = new()
        {
            { "236", "quarto de círculo para frente" }, { "214", "quarto de círculo para trás" },
            { "623", "frente, baixo, baixo-frente" }, { "421", "trás, baixo, baixo-trás" },
            { "41236", "meio círculo para frente" }, { "63214", "meio círculo para trás" },
            { "236236", "dois quartos de círculo para frente" }, { "214214", "dois quartos de círculo para trás" },
            { "22", "baixo, baixo" }, { "44", "trás, trás" }, { "66", "frente, frente" },
        },
        Icons = new()
        {
            { "+", "mais" }, { "p", "soco" }, { "k", "chute" },
            { "lp", "soco leve" }, { "mp", "soco médio" }, { "hp", "soco forte" },
            { "lk", "chute leve" }, { "mk", "chute médio" }, { "hk", "chute forte" },
            { "ls", "ataque leve" }, { "ms", "ataque médio" }, { "hs", "ataque forte" },
            { "di", "drive impact" }, { "dp", "drive parry" }, { "tr", "arremesso" },
            { "sm", "golpe especial" }, { "sa", "super art" }, { "auto", "auto" }, { "n", "neutro" },
        },
        Inputs = new()
        {
            { "BTL_LR_LSX", "esquerda ou direita" }, { "BTL_UD_LSY", "cima ou baixo" },
            { "BTL_PLUS_LS_U", "cima" }, { "BTL_PLUS_LS_D", "baixo" },
            { "BTL_PLUS_LS_L", "esquerda" }, { "BTL_PLUS_LS_R", "direita" },
            { "BTL_LS_U", "cima" }, { "BTL_LS_D", "baixo" }, { "BTL_LS_L", "esquerda" }, { "BTL_LS_R", "direita" },
            { "BTL_X", "quadrado" }, { "BTL_Y", "triângulo" }, { "BTL_A", "xis" }, { "BTL_B", "círculo" },
            { "BTL_LB", "L1" }, { "BTL_RB", "R1" }, { "BTL_LT", "L2" }, { "BTL_RT", "R2" },
            { "BTL_L3", "L3" }, { "BTL_R3", "R3" }, { "BTL_LSB", "L3" }, { "BTL_RSB", "R3" },
            { "BTL_U", "cima" }, { "BTL_D", "baixo" }, { "BTL_L", "esquerda" }, { "BTL_R", "direita" },
            { "UIDecide", "confirmar" }, { "UICancel", "cancelar" },
            { "MouseL", "clique esquerdo" }, { "MouseR", "clique direito" },
        },
    };

    // Display language option (app.Option TypeId DispLanguage = 611); value is
    // the index in the language list: 1=English, 5=Spanish, 8=Portuguese (BR),
    // 13=Latin American Spanish
    private const int DISP_LANGUAGE_TYPE_ID = 611;
    private static CommandWords _words = WordsEn;
    private static long _wordsCheckedTick;
    private static ManagedObject _optionManager;

    private static CommandWords CurrentWords()
    {
        long now = System.Environment.TickCount64;
        if (now - _wordsCheckedTick < 10000) return _words;
        _wordsCheckedTick = now;

        try
        {
            _optionManager ??= API.GetManagedSingleton("app.OptionManager");
            var result = Call(_optionManager, "GetOptionValue", DISP_LANGUAGE_TYPE_ID);
            int lang = result != null ? Convert.ToInt32(result) : -1;
            var words = lang switch
            {
                5 or 13 => WordsEs,
                8 => WordsPt,
                _ => WordsEn,
            };
            if (words != _words)
                API.LogInfo($"[SF6Access] Command vocabulary switched (display language value={lang})");
            _words = words;
        }
        catch { }
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

        if (words.Icons.TryGetValue(token.ToLowerInvariant(), out var iconName))
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
}
