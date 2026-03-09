using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;

namespace SF6Access.Services;

public static class ObjectDumper
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_F9 = 0x78;
    private const int VK_F10 = 0x79;

    private static bool _lastKeyState;
    private static bool _lastF10State;
    private static bool _isDumping;

    private static readonly string DumpPath = Path.Combine(
        @"D:\games\steam\steamapps\common\Street Fighter 6\reframework\data",
        "sf6access_dump.txt"
    );

    private static readonly string OptionDumpPath = Path.Combine(
        @"D:\games\steam\steamapps\common\Street Fighter 6\reframework\data",
        "sf6access_options_dump.txt"
    );

    [Callback(typeof(UpdateBehavior), CallbackType.Pre)]
    public static void OnUpdate()
    {
        bool keyDown = (GetAsyncKeyState(VK_F9) & 0x8000) != 0;

        if (keyDown && !_lastKeyState && !_isDumping)
        {
            _isDumping = true;
            try
            {
                DumpAllObjects();
                ScreenReaderService.Speak("Object dump complete");
                API.LogInfo($"[SF6Access] Object dump saved to {DumpPath}");
            }
            catch (Exception ex)
            {
                API.LogError($"[SF6Access] Dump failed: {ex.Message}");
                ScreenReaderService.Speak("Dump failed");
            }
            finally
            {
                _isDumping = false;
            }
        }

        _lastKeyState = keyDown;

        bool f10Down = (GetAsyncKeyState(VK_F10) & 0x8000) != 0;

        if (f10Down && !_lastF10State && !_isDumping)
        {
            _isDumping = true;
            try
            {
                DumpOptionTypes();
                ScreenReaderService.Speak("Option dump complete");
                API.LogInfo($"[SF6Access] Option dump saved to {OptionDumpPath}");
            }
            catch (Exception ex)
            {
                API.LogError($"[SF6Access] Option dump failed: {ex.Message}");
                ScreenReaderService.Speak("Option dump failed");
            }
            finally
            {
                _isDumping = false;
            }
        }

        _lastF10State = f10Down;
    }

    private static void DumpAllObjects()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== SF6 Object Dump - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        sb.AppendLine();

        DumpManagedSingletons(sb);
        sb.AppendLine();
        DumpNativeSingletons(sb);

        Directory.CreateDirectory(Path.GetDirectoryName(DumpPath));
        File.WriteAllText(DumpPath, sb.ToString());
    }

    private static void DumpManagedSingletons(StringBuilder sb)
    {
        sb.AppendLine("========== MANAGED SINGLETONS ==========");
        sb.AppendLine();

        List<ManagedSingleton> singletons;
        try
        {
            singletons = API.GetManagedSingletons();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[ERROR getting managed singletons: {ex.Message}]");
            return;
        }

        foreach (var singleton in singletons)
        {
            try
            {
                var obj = singleton.Instance;
                if (obj == null) continue;

                var td = obj.GetTypeDefinition();
                if (td == null) continue;

                string typeName = td.FullName ?? td.Name ?? "(unknown)";
                sb.AppendLine($"--- {typeName} ---");

                DumpFields(sb, td, obj, "  ");
                DumpMethods(sb, td, "  ");
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  [ERROR: {ex.Message}]");
                sb.AppendLine();
            }
        }
    }

    private static void DumpNativeSingletons(StringBuilder sb)
    {
        sb.AppendLine("========== NATIVE SINGLETONS ==========");
        sb.AppendLine();

        List<NativeSingleton> singletons;
        try
        {
            singletons = API.GetNativeSingletons();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[ERROR getting native singletons: {ex.Message}]");
            return;
        }

        foreach (var singleton in singletons)
        {
            try
            {
                var obj = singleton.Instance;
                if (obj == null) continue;

                var td = obj.GetTypeDefinition();
                if (td == null) continue;

                string typeName = td.FullName ?? td.Name ?? "(unknown)";
                sb.AppendLine($"--- {typeName} ---");
                DumpMethods(sb, td, "  ");
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  [ERROR: {ex.Message}]");
                sb.AppendLine();
            }
        }
    }

    private static void DumpFields(StringBuilder sb, TypeDefinition td, ManagedObject obj, string indent)
    {
        try
        {
            var fields = td.GetFields();
            if (fields == null || fields.Count == 0) return;

            sb.AppendLine($"{indent}Fields:");
            foreach (var field in fields)
            {
                try
                {
                    string fieldName = field.Name ?? "(null)";
                    var fieldType = field.Type;
                    string fieldTypeName = fieldType?.FullName ?? fieldType?.Name ?? "?";
                    bool isStatic = field.IsStatic();

                    string value = "(not read)";
                    try
                    {
                        var rawValue = field.GetDataBoxed(typeof(object), obj.GetAddress(), false);
                        value = rawValue?.ToString() ?? "null";
                        if (value.Length > 200) value = value.Substring(0, 200) + "...";
                    }
                    catch
                    {
                        value = "(read error)";
                    }

                    string staticTag = isStatic ? " [static]" : "";
                    sb.AppendLine($"{indent}  {fieldTypeName} {fieldName}{staticTag} = {value}");
                }
                catch
                {
                    // Skip unreadable fields
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"{indent}  [Fields error: {ex.Message}]");
        }
    }

    private static void DumpOptionTypes()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== SF6 Option Types Dump - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        sb.AppendLine();

        // Dump TabType enum
        DumpEnumType(sb, "app.Option.TabType");
        DumpEnumType(sb, "app.Option.ValueType");
        DumpEnumType(sb, "app.Option.GroupType");
        DumpEnumType(sb, "app.Option.SupportModeType");

        // Dump OptionUnitBase type definition
        DumpTypeDefinition(sb, "app.Option.OptionUnitBase");
        DumpTypeDefinition(sb, "app.Option.OptionValueUnit");
        DumpTypeDefinition(sb, "app.Option.OptionGroupUnit");
        DumpTypeDefinition(sb, "app.Option.OptionValueData");
        DumpTypeDefinition(sb, "app.Option.OptionSettingUserData");
        DumpTypeDefinition(sb, "app.Option.OptionSettingUnit");
        DumpTypeDefinition(sb, "app.Option.OptionValueSetting");
        DumpTypeDefinition(sb, "app.Option.OptionSettingUserData.TabUnitList");

        // Try to enumerate actual option data from OptionManager
        sb.AppendLine("========== OPTION MANAGER LIVE DATA ==========");
        sb.AppendLine();
        try
        {
            var optMgr = API.GetManagedSingleton("app.OptionManager");
            if (optMgr == null)
            {
                sb.AppendLine("[OptionManager singleton not found]");
            }
            else
            {
                // Try each TabType value (0-20 range)
                var getListMethod = TDB.Get().FindType("app.OptionManager")
                    ?.GetMethod("GetOptionUnitList(app.Option.TabType)");

                if (getListMethod != null)
                {
                    for (int tab = 0; tab <= 20; tab++)
                    {
                        try
                        {
                            var list = getListMethod.InvokeBoxed(typeof(object), optMgr, new object[] { tab }) as ManagedObject;
                            if (list == null) continue;

                            var countProp = list.GetTypeDefinition()?.GetMethod("get_Count");
                            if (countProp == null) continue;

                            var count = countProp.InvokeBoxed(typeof(int), list, new object[] { });
                            int cnt = count != null ? Convert.ToInt32(count) : 0;
                            if (cnt == 0) continue;

                            sb.AppendLine($"--- TabType {tab}: {cnt} units ---");

                            var getItemMethod = list.GetTypeDefinition()?.GetMethod("get_Item(System.Int32)");
                            if (getItemMethod == null) continue;

                            for (int i = 0; i < cnt && i < 50; i++)
                            {
                                try
                                {
                                    var unit = getItemMethod.InvokeBoxed(typeof(object), list, new object[] { i }) as ManagedObject;
                                    if (unit == null) continue;

                                    var unitTd = unit.GetTypeDefinition();
                                    string typeName = unitTd?.FullName ?? "?";
                                    sb.AppendLine($"  [{i}] Type: {typeName}");

                                    // Try to get Setting object and dump its fields
                                    try
                                    {
                                        var setting = (unit as IObject)?.Call("get_Setting") as ManagedObject;
                                        if (setting != null)
                                        {
                                            var settingTd = setting.GetTypeDefinition();
                                            sb.AppendLine($"    Setting type: {settingTd?.FullName}");
                                            if (settingTd != null)
                                                DumpFields(sb, settingTd, setting, "      ");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        sb.AppendLine($"    Setting error: {ex.Message}");
                                    }

                                    // For OptionValueUnit, also get ValueType and current value
                                    if (typeName.Contains("OptionValueUnit"))
                                    {
                                        try
                                        {
                                            var vt = (unit as IObject)?.Call("get_ValueType");
                                            var val = (unit as IObject)?.Call("get_Value");
                                            sb.AppendLine($"    ValueType: {vt}, Value: {val}");

                                            // Get ValueSetting and dump it
                                            var vs = (unit as IObject)?.Call("get_ValueSetting") as ManagedObject;
                                            if (vs != null)
                                            {
                                                var vsTd = vs.GetTypeDefinition();
                                                sb.AppendLine($"    ValueSetting type: {vsTd?.FullName}");
                                                if (vsTd != null)
                                                    DumpFields(sb, vsTd, vs, "      ");
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            sb.AppendLine($"    ValueUnit error: {ex.Message}");
                                        }
                                    }

                                    // For OptionGroupUnit, get GroupType
                                    if (typeName.Contains("OptionGroupUnit"))
                                    {
                                        try
                                        {
                                            var gt = (unit as IObject)?.Call("get_GroupType");
                                            sb.AppendLine($"    GroupType: {gt}");
                                        }
                                        catch (Exception ex)
                                        {
                                            sb.AppendLine($"    GroupUnit error: {ex.Message}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    sb.AppendLine($"  [{i}] Error: {ex.Message}");
                                }
                            }
                            sb.AppendLine();
                        }
                        catch { }
                    }
                }
                else
                {
                    sb.AppendLine("[GetOptionUnitList method not found]");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[Error enumerating options: {ex.Message}]");
        }

        // Search for UI types related to options
        sb.AppendLine();
        sb.AppendLine("========== OPTION UI TYPES (TDB Search) ==========");
        sb.AppendLine();
        string[] typesToSearch = {
            "app.UIOptionFlow", "app.UIOptionAgent", "app.UIOptionView",
            "app.UIOptionCtrl", "app.UIOptionMenu", "app.UIOptionPage",
            "app.UISystemOptionFlow", "app.UIOptionFlowParam",
            "app.Option.OptionFlow", "app.Option.OptionAgent",
        };
        foreach (var name in typesToSearch)
        {
            var td = TDB.Get().FindType(name);
            if (td != null)
            {
                sb.AppendLine($"--- {name} ---");
                DumpMethods(sb, td, "  ");
                sb.AppendLine();
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(OptionDumpPath));
        File.WriteAllText(OptionDumpPath, sb.ToString());
    }

    private static void DumpEnumType(StringBuilder sb, string typeName)
    {
        var td = TDB.Get().FindType(typeName);
        if (td == null)
        {
            sb.AppendLine($"[Enum {typeName} not found]");
            return;
        }

        sb.AppendLine($"=== Enum: {typeName} ===");
        var fields = td.GetFields();
        if (fields != null)
        {
            foreach (var f in fields)
            {
                try
                {
                    string name = f.Name ?? "(null)";
                    if (name == "value__") continue;
                    // Try to read the enum value
                    string val = "(?)";
                    try
                    {
                        var raw = f.GetDataBoxed(typeof(int), 0, true);
                        val = raw?.ToString() ?? "?";
                    }
                    catch { }
                    sb.AppendLine($"  {name} = {val}");
                }
                catch { }
            }
        }
        sb.AppendLine();
    }

    private static void DumpTypeDefinition(StringBuilder sb, string typeName)
    {
        var td = TDB.Get().FindType(typeName);
        if (td == null)
        {
            sb.AppendLine($"[Type {typeName} not found]");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"=== Type: {typeName} ===");
        var parent = td.ParentType;
        if (parent != null)
            sb.AppendLine($"  Parent: {parent.FullName}");

        var fields = td.GetFields();
        if (fields != null && fields.Count > 0)
        {
            sb.AppendLine("  Fields:");
            foreach (var f in fields)
            {
                try
                {
                    string fname = f.Name ?? "(null)";
                    var ft = f.Type;
                    string ftName = ft?.FullName ?? ft?.Name ?? "?";
                    bool isStatic = f.IsStatic();
                    string staticTag = isStatic ? " [static]" : "";
                    sb.AppendLine($"    {ftName} {fname}{staticTag}");
                }
                catch { }
            }
        }

        DumpMethods(sb, td, "  ");
        sb.AppendLine();
    }

    private static void DumpMethods(StringBuilder sb, TypeDefinition td, string indent)
    {
        try
        {
            var methods = td.GetMethods();
            if (methods == null || methods.Count == 0) return;

            sb.AppendLine($"{indent}Methods:");
            foreach (var method in methods)
            {
                try
                {
                    string methodName = method.Name ?? "(null)";
                    var returnType = method.ReturnType;
                    string returnTypeName = returnType?.FullName ?? returnType?.Name ?? "void";
                    bool isStatic = method.IsStatic();

                    var parameters = method.GetParameters();
                    var paramStr = new StringBuilder();
                    if (parameters != null)
                    {
                        for (int i = 0; i < parameters.Count; i++)
                        {
                            if (i > 0) paramStr.Append(", ");
                            var p = parameters[i];
                            string pType = p.Type?.FullName ?? p.Type?.Name ?? "?";
                            string pName = p.Name ?? $"arg{i}";
                            paramStr.Append($"{pType} {pName}");
                        }
                    }

                    string staticTag = isStatic ? " [static]" : "";
                    sb.AppendLine($"{indent}  {returnTypeName} {methodName}({paramStr}){staticTag}");
                }
                catch
                {
                    // Skip unreadable methods
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"{indent}  [Methods error: {ex.Message}]");
        }
    }
}
