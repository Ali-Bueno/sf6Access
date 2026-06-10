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

    public static string CleanTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Regex.Replace(text, @"<[^>]+>", "").Trim();
    }
}
