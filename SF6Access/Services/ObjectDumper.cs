using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;

namespace SF6Access.Services;

/// <summary>
/// F9: unified dump of all game state to a single file for accessibility research.
/// Includes: UIFlowManager handles, managed singletons, and TDB UI type scan.
/// </summary>
public static class ObjectDumper
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_F9 = 0x78;
    private static bool _lastKeyState;
    private static bool _isDumping;

    private static readonly string DumpPath = Path.Combine(
        @"D:\games\steam\steamapps\common\Street Fighter 6\reframework\data",
        "sf6access_dump.txt"
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
                DumpEverything();
                ScreenReaderService.Speak("Full dump complete");
                API.LogInfo($"[SF6Access] Full dump saved to {DumpPath}");
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
    }

    private static void DumpEverything()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== SF6 FULL DUMP - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        sb.AppendLine();

        // 1. Active UIFlowManager handles (most useful for debugging)
        DumpFlowHandles(sb);

        // 2. Managed singletons with fields
        DumpManagedSingletons(sb);

        // 3. Native singletons (names only)
        DumpNativeSingletons(sb);

        // 4. TDB UI type scan
        DumpTDBScan(sb);

        Directory.CreateDirectory(Path.GetDirectoryName(DumpPath));
        File.WriteAllText(DumpPath, sb.ToString());
    }

    // ==================== FLOW HANDLES ====================

    private static void DumpFlowHandles(StringBuilder sb)
    {
        sb.AppendLine("========== ACTIVE UI FLOW HANDLES ==========");
        sb.AppendLine();

        try
        {
            var flowMgr = API.GetManagedSingleton("app.UIFlowManager");
            if (flowMgr == null)
            {
                sb.AppendLine("[UIFlowManager not found]");
                sb.AppendLine();
                return;
            }

            var handlesField = flowMgr.GetTypeDefinition()?.GetField("_Handles");
            if (handlesField == null)
            {
                sb.AppendLine("[_Handles field not found]");
                sb.AppendLine();
                return;
            }

            var handles = handlesField.GetDataBoxed(typeof(object), flowMgr.GetAddress(), false) as ManagedObject;
            if (handles == null)
            {
                sb.AppendLine("[_Handles is null]");
                sb.AppendLine();
                return;
            }

            var countMethod = handles.GetTypeDefinition()?.GetMethod("get_Count");
            var getItemMethod = handles.GetTypeDefinition()?.GetMethod("get_Item(System.Int32)");
            if (countMethod == null || getItemMethod == null)
            {
                sb.AppendLine("[Count/GetItem methods not found]");
                sb.AppendLine();
                return;
            }

            int count = Convert.ToInt32(countMethod.InvokeBoxed(typeof(int), handles, Array.Empty<object>()));
            sb.AppendLine($"Total handles: {count}");
            sb.AppendLine();

            for (int i = 0; i < count && i < 50; i++)
            {
                try
                {
                    var handle = getItemMethod.InvokeBoxed(typeof(object), handles, new object[] { i }) as ManagedObject;
                    if (handle == null) continue;

                    var param = handle.GetField("<Param>k__BackingField") as ManagedObject;
                    var element = handle.GetField("<Element>k__BackingField") as ManagedObject;

                    string paramType = param?.GetTypeDefinition()?.FullName ?? "null";
                    string elemType = element?.GetTypeDefinition()?.FullName ?? "null";

                    sb.AppendLine($"--- Handle [{i}] ---");
                    sb.AppendLine($"  Param:   {paramType}");
                    sb.AppendLine($"  Element: {elemType}");

                    // Dump param fields in detail
                    if (param != null && paramType != "null" &&
                        !paramType.Contains("BaseParam_Create"))
                    {
                        var ptd = param.GetTypeDefinition();
                        DumpFieldsWithValues(sb, ptd, param, "  ");
                    }

                    sb.AppendLine();
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  [Handle {i} error: {ex.Message}]");
                    sb.AppendLine();
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[FlowHandles error: {ex.Message}]");
            sb.AppendLine();
        }
    }

    // ==================== MANAGED SINGLETONS ====================

    private static void DumpManagedSingletons(StringBuilder sb)
    {
        sb.AppendLine("========== MANAGED SINGLETONS ==========");
        sb.AppendLine();

        try
        {
            var singletons = API.GetManagedSingletons();

            foreach (var singleton in singletons)
            {
                try
                {
                    var obj = singleton.Instance;
                    if (obj == null) continue;

                    var td = obj.GetTypeDefinition();
                    string typeName = td?.FullName ?? "(unknown)";
                    sb.AppendLine($"--- {typeName} ---");

                    DumpFieldsWithValues(sb, td, obj, "  ");
                    DumpMethodSignatures(sb, td, "  ");
                    sb.AppendLine();
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  [ERROR: {ex.Message}]");
                    sb.AppendLine();
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[ERROR getting singletons: {ex.Message}]");
            sb.AppendLine();
        }
    }

    // ==================== NATIVE SINGLETONS ====================

    private static void DumpNativeSingletons(StringBuilder sb)
    {
        sb.AppendLine("========== NATIVE SINGLETONS ==========");
        sb.AppendLine();

        try
        {
            var singletons = API.GetNativeSingletons();

            foreach (var singleton in singletons)
            {
                try
                {
                    var obj = singleton.Instance;
                    if (obj == null) continue;

                    var td = obj.GetTypeDefinition();
                    string typeName = td?.FullName ?? "(unknown)";
                    sb.AppendLine($"  {typeName}");
                }
                catch { }
            }
            sb.AppendLine();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[ERROR: {ex.Message}]");
            sb.AppendLine();
        }
    }

    // ==================== TDB UI TYPE SCAN ====================

    private static readonly string[] TDBPatterns = {
        "UIFlow", "UIPartsTab", "UIPartsScroll", "UIPartsList", "UIPartsSpin",
        "CharaSelect", "CharacterSelect", "StageSelect",
        "BattleHud", "BattleResult", "RoundResult",
        "UIOption", "OptionMenu", "OptionFlow",
        "Training", "Tutorial",
        "News", "Notice", "Information",
        "MultiMenu", "UIFlowMulti",
        "MatchingSetting", "VersusRule", "CommentatorSelect",
    };

    private static void DumpTDBScan(StringBuilder sb)
    {
        sb.AppendLine("========== TDB UI TYPES ==========");
        sb.AppendLine();

        var tdb = TDB.Get();
        uint numTypes = tdb.GetNumTypes();
        sb.AppendLine($"Total TDB types: {numTypes}");
        sb.AppendLine();

        var results = new Dictionary<string, List<string>>();
        foreach (var p in TDBPatterns)
            results[p] = new List<string>();

        for (uint idx = 0; idx < numTypes; idx++)
        {
            try
            {
                var td = tdb.GetType(idx);
                if (td == null) continue;
                string fullName = td.FullName;
                if (string.IsNullOrEmpty(fullName)) continue;

                foreach (var pattern in TDBPatterns)
                {
                    if (fullName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        results[pattern].Add(fullName);
                        break;
                    }
                }
            }
            catch { }
        }

        foreach (var pattern in TDBPatterns)
        {
            var matches = results[pattern];
            if (matches.Count == 0) continue;

            matches.Sort();
            sb.AppendLine($"--- {pattern} ({matches.Count} types) ---");
            foreach (var name in matches)
                sb.AppendLine($"  {name}");
            sb.AppendLine();
        }
    }

    // ==================== HELPERS ====================

    private static void DumpFieldsWithValues(StringBuilder sb, TypeDefinition td, ManagedObject obj, string indent)
    {
        if (td == null) return;

        try
        {
            var fields = td.GetFields();
            if (fields == null || fields.Count == 0) return;

            foreach (var field in fields)
            {
                try
                {
                    string fieldName = field.Name ?? "(null)";
                    string fieldTypeName = field.Type?.FullName ?? "?";
                    bool isStatic = field.IsStatic();

                    string value = "";
                    try
                    {
                        var rawValue = field.GetDataBoxed(typeof(object), obj.GetAddress(), isStatic);
                        value = rawValue?.ToString() ?? "null";
                        if (value.Length > 200) value = value.Substring(0, 200) + "...";
                    }
                    catch { value = "(read error)"; }

                    string staticTag = isStatic ? " [static]" : "";
                    sb.AppendLine($"{indent}{fieldTypeName} {fieldName}{staticTag} = {value}");
                }
                catch { }
            }
        }
        catch { }
    }

    private static void DumpMethodSignatures(StringBuilder sb, TypeDefinition td, string indent)
    {
        if (td == null) return;

        try
        {
            var methods = td.GetMethods();
            if (methods == null || methods.Count == 0) return;

            sb.AppendLine($"{indent}Methods:");
            foreach (var m in methods)
            {
                try
                {
                    string retType = m.ReturnType?.FullName ?? "void";
                    string name = m.Name ?? "?";
                    bool isStatic = m.IsStatic();

                    var parms = m.GetParameters();
                    var pStr = new StringBuilder();
                    if (parms != null)
                    {
                        for (int i = 0; i < parms.Count; i++)
                        {
                            if (i > 0) pStr.Append(", ");
                            pStr.Append($"{parms[i].Type?.FullName ?? "?"} {parms[i].Name ?? $"p{i}"}");
                        }
                    }

                    string sTag = isStatic ? " [static]" : "";
                    sb.AppendLine($"{indent}  {retType} {name}({pStr}){sTag}");
                }
                catch { }
            }
        }
        catch { }
    }
}
