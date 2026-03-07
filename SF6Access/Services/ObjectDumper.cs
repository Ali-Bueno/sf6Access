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
