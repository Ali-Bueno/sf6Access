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

    private const int VK_F8 = 0x77;
    private const int VK_F9 = 0x78;
    private static bool _lastKeyState;
    private static bool _lastF8State;
    private static bool _isDumping;

    // F8 auto-dump: every new flow param type that appears gets its fields and
    // the on-screen texts appended to one session file — no F9 per menu needed.
    // On by default so end users always have a dump to send when they hit an
    // inaccessible menu; F8 turns it off.
    private static bool _autoDumpEnabled = true;
    private static string _autoDumpPath;
    private static int _frameCount;
    private static readonly HashSet<string> _autoDumped = new();
    private static readonly Queue<(string typeName, int dueFrame)> _autoDumpQueue = new();

    /// <summary>reframework/data under the running game's folder, created on demand.</summary>
    public static string DumpDir
    {
        get
        {
            if (_dumpDir == null)
            {
                string gameRoot = Path.GetDirectoryName(Environment.ProcessPath)
                    ?? Directory.GetCurrentDirectory();
                _dumpDir = Path.Combine(gameRoot, "reframework", "data");
            }
            Directory.CreateDirectory(_dumpDir);
            return _dumpDir;
        }
    }
    private static string _dumpDir;

    [Callback(typeof(UpdateBehavior), CallbackType.Pre)]
    public static void OnUpdate()
    {
        _frameCount++;
        bool keyDown = (GetAsyncKeyState(VK_F9) & 0x8000) != 0;

        if (keyDown && !_lastKeyState && !_isDumping)
        {
            _isDumping = true;
            try
            {
                string path = DumpEverything();
                ScreenReaderService.Speak("Full dump complete");
                API.LogInfo($"[SF6Access] Full dump saved to {path}");
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

        bool f8Down = (GetAsyncKeyState(VK_F8) & 0x8000) != 0;
        if (f8Down && !_lastF8State)
        {
            _autoDumpEnabled = !_autoDumpEnabled;
            ScreenReaderService.Speak(_autoDumpEnabled ? "Auto dump enabled" : "Auto dump disabled");
            API.LogInfo($"[SF6Access] Auto dump {(_autoDumpEnabled ? "enabled" : "disabled")}");
        }
        _lastF8State = f8Down;

        ProcessAutoDumpQueue();
    }

    /// <summary>Queue a dump of a newly appeared flow param (called by FlowTracker).</summary>
    public static void QueueAutoDump(string paramTypeName)
    {
        if (!_autoDumpEnabled || string.IsNullOrEmpty(paramTypeName)) return;
        if (!paramTypeName.StartsWith("app.") || paramTypeName.Contains("BaseParam_Create")) return;
        if (!_autoDumped.Add(paramTypeName)) return;

        // Wait ~1.5s so the screen finishes initializing before reading it
        _autoDumpQueue.Enqueue((paramTypeName, _frameCount + 90));
    }

    private static void ProcessAutoDumpQueue()
    {
        if (_autoDumpQueue.Count == 0 || _isDumping) return;
        if (_autoDumpQueue.Peek().dueFrame > _frameCount) return;

        var (typeName, _) = _autoDumpQueue.Dequeue();
        try
        {
            WriteAutoDump(typeName);
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] Auto dump failed for {typeName}: {ex.Message}");
        }
    }

    private static void WriteAutoDump(string typeName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== AUTO DUMP {typeName} - {DateTime.Now:HH:mm:ss} ===");

        var param = FlowHelper.FindFlowParam(typeName);
        if (param != null)
            DumpFieldsWithValues(sb, param.GetTypeDefinition(), param, "  ");
        else
            sb.AppendLine("  [param no longer active]");

        sb.AppendLine();
        sb.AppendLine("--- ON-SCREEN TEXTS ---");
        try
        {
            string lastOwner = null;
            foreach (var t in GuiTextReader.ReadSceneTexts(visibleOnly: false))
            {
                if (t.Owner != lastOwner)
                {
                    sb.AppendLine($"--- GUI: {t.Owner ?? "(unknown)"} ---");
                    lastOwner = t.Owner;
                }
                string shown = string.IsNullOrEmpty(t.Text) && !string.IsNullOrEmpty(t.Raw) ? $"[raw] {t.Raw}" : t.Text;
                sb.AppendLine($"  {t.Name}{(t.Visible ? "" : " [hidden]")} = {shown}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[texts error: {ex.Message}]");
        }
        sb.AppendLine();

        Directory.CreateDirectory(DumpDir);
        _autoDumpPath ??= Path.Combine(DumpDir, $"sf6access_autodump_{DateTime.Now:HHmmss}.txt");
        File.AppendAllText(_autoDumpPath, sb.ToString());
        API.LogInfo($"[SF6Access] Auto dump appended: {typeName}");
    }

    private static string DumpEverything()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== SF6 FULL DUMP - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        sb.AppendLine();

        // 1. Active UIFlowManager handles (most useful for debugging)
        DumpFlowHandles(sb);

        // 2. All on-screen GUI texts (reveals text of screens without Param fields)
        DumpGuiTexts(sb);

        // 3. Managed singletons with fields
        DumpManagedSingletons(sb);

        // 4. Native singletons (names only)
        DumpNativeSingletons(sb);

        // 5. TDB UI type scan
        DumpTDBScan(sb);

        // Timestamped file so consecutive dumps don't overwrite each other
        string path = Path.Combine(DumpDir, $"sf6access_dump_{DateTime.Now:HHmmss}.txt");
        Directory.CreateDirectory(DumpDir);
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    // ==================== GUI TEXTS ====================

    private static void DumpGuiTexts(StringBuilder sb)
    {
        sb.AppendLine("========== ON-SCREEN GUI TEXTS ==========");
        sb.AppendLine();

        try
        {
            var texts = GuiTextReader.ReadSceneTexts(visibleOnly: false);
            sb.AppendLine($"Total texts: {texts.Count}");
            sb.AppendLine();

            string lastOwner = null;
            foreach (var t in texts)
            {
                if (t.Owner != lastOwner)
                {
                    sb.AppendLine($"--- GUI: {t.Owner ?? "(unknown)"} ---");
                    lastOwner = t.Owner;
                }
                string visTag = t.Visible ? "" : " [hidden]";
                // Show the raw message when tags were stripped to nothing —
                // reveals inline input-icon tag formats
                string shown = string.IsNullOrEmpty(t.Text) && !string.IsNullOrEmpty(t.Raw) ? $"[raw] {t.Raw}" : t.Text;
                sb.AppendLine($"  {t.Name}{visTag} = {shown}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[GuiTexts error: {ex.Message}]");
        }
        sb.AppendLine();
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
