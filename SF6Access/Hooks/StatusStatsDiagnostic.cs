using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// DEV TOOL (F6): deep-dump the avatar Status menu stats panel so the real,
/// authoritative stat labels can be found. The on-screen stat numbers are
/// unlabeled GUI values (the labels are textures), so this dumps:
///   - the type definitions (fields + methods) of the stat widgets
///     (UIPartsPlayerEquipStatus / UIPartsPlayerStatusSet / UIPartsBuffListWindow),
///   - their full via.gui element tree (icon element names reveal each stat),
///   - the WTPlayerData status fields.
/// Open the Status menu Gear tab and press F6. Remove this file once the stats
/// readout is implemented.
/// </summary>
public static class StatusStatsDiagnostic
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_F6 = 0x75;
    private static bool _lastKey;

    private const string STATUS_PARAM_TYPE = "app.UIStatusMenu.StatusMenuParam";
    private const string EQUIP_PARAM_TYPE = "app.UIStatusMenu_Equip.Param";

    [Callback(typeof(UpdateBehavior), CallbackType.Pre)]
    public static void OnUpdate()
    {
        bool down = (GetAsyncKeyState(VK_F6) & 0x8000) != 0;
        if (down && !_lastKey)
        {
            try
            {
                string path = Dump();
                ScreenReaderService.Speak("Stats dump complete");
                API.LogInfo($"[SF6Access] Stats diagnostic saved to {path}");
            }
            catch (Exception ex)
            {
                API.LogError($"[SF6Access] Stats diagnostic failed: {ex.Message}");
                ScreenReaderService.Speak("Stats dump failed");
            }
        }
        _lastKey = down;
    }

    private static string Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== SF6 STATUS STATS DIAGNOSTIC - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        sb.AppendLine();

        var statusParam = FlowHelper.FindFlowParam(STATUS_PARAM_TYPE);
        var equipParam = FlowHelper.FindFlowParam(EQUIP_PARAM_TYPE);
        sb.AppendLine($"statusParam: {(statusParam != null ? "found" : "null")}");
        sb.AppendLine($"equipParam:  {(equipParam != null ? "found" : "null")}");
        sb.AppendLine();

        // --- Stat widgets on the equip param ---
        if (equipParam != null)
        {
            DumpWidget(sb, equipParam, "mEquipStatus");
            DumpWidget(sb, equipParam, "mPerkStatus");
        }

        // --- Stat widget on the status param ---
        if (statusParam != null)
        {
            DumpWidget(sb, statusParam, "PlayerStatusSet");

            // --- WTPlayerData status data ---
            sb.AppendLine("########## WTPlayerData (PlayerData) ##########");
            var playerData = FlowHelper.GetObjectField(statusParam, "PlayerData");
            if (playerData != null)
                DumpObject(sb, playerData, "  ", 0, maxDepth: 2);
            else
                sb.AppendLine("  [null]");
            sb.AppendLine();
        }

        // Confirm/attention popups (e.g. "Switch moves?") — dump the full control
        // tree (with PlayState) of any dialog/attention GUI so its Yes/No buttons
        // and their selection state can be located.
        sb.AppendLine("########## DIALOG / ATTENTION GUIs ##########");
        foreach (var keyword in new[] { "Attention", "Dialog", "Confirm", "MessageBox" })
        {
            foreach (var (owner, view) in GuiTextReader.FindGuiViews(keyword))
            {
                sb.AppendLine($"--- GUI: {owner} ---");
                GuiTextReader.DumpControlTree(view, sb, 1);
                sb.AppendLine();
            }
        }

        string path = Path.Combine(ObjectDumper.DumpDir, $"sf6access_statstats_{DateTime.Now:HHmmss}.txt");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    private static void DumpWidget(StringBuilder sb, ManagedObject owner, string fieldName)
    {
        sb.AppendLine($"########## {fieldName} ##########");
        var widget = FlowHelper.GetObjectField(owner, fieldName);
        if (widget == null)
        {
            sb.AppendLine("  [null]");
            sb.AppendLine();
            return;
        }

        var td = widget.GetTypeDefinition();
        sb.AppendLine($"  type: {td?.FullName}");
        sb.AppendLine();

        sb.AppendLine("  --- FIELDS ---");
        DumpFields(sb, td, widget, "  ");
        sb.AppendLine();

        sb.AppendLine("  --- METHODS ---");
        DumpMethods(sb, td, "  ");
        sb.AppendLine();

        // GUI element tree (icon/texture names reveal each stat label)
        sb.AppendLine("  --- GUI TREE ---");
        var control = GetWidgetControl(widget);
        if (control != null)
            GuiTextReader.DumpControlTree(control, sb, 1);
        else
            sb.AppendLine("  [no control found]");
        sb.AppendLine();
    }

    /// <summary>Try the common accessors a UIParts/UIWidget exposes for its root control.</summary>
    private static ManagedObject GetWidgetControl(ManagedObject widget)
    {
        foreach (var getter in new[] { "get_Control", "get_GUI", "get_View", "get_Panel", "get_RootControl" })
        {
            var c = FlowHelper.Call(widget, getter) as ManagedObject;
            if (c != null) return ResolveView(c);
        }
        foreach (var field in new[] { "Control", "_control", "mControl", "GUI", "_gui", "View" })
        {
            var c = FlowHelper.GetObjectField(widget, field);
            if (c != null) return ResolveView(c);
        }
        return null;
    }

    /// <summary>If the accessor returned a via.gui.GUI, descend to its View control.</summary>
    private static ManagedObject ResolveView(ManagedObject obj)
    {
        try
        {
            string type = obj.GetTypeDefinition()?.FullName;
            if (type == "via.gui.GUI")
                return FlowHelper.Call(obj, "get_View") as ManagedObject ?? obj;
        }
        catch { }
        return obj;
    }

    private static void DumpFields(StringBuilder sb, TypeDefinition td, ManagedObject obj, string indent)
    {
        if (td == null) return;
        try
        {
            var fields = td.GetFields();
            if (fields == null) return;
            foreach (var f in fields)
            {
                try
                {
                    bool isStatic = f.IsStatic();
                    string value;
                    try
                    {
                        var raw = f.GetDataBoxed(typeof(object), obj.GetAddress(), isStatic);
                        value = raw?.ToString() ?? "null";
                        if (value.Length > 120) value = value.Substring(0, 120) + "...";
                    }
                    catch { value = "(read error)"; }
                    sb.AppendLine($"{indent}{f.Type?.FullName ?? "?"} {f.Name}{(isStatic ? " [static]" : "")} = {value}");
                }
                catch { }
            }
        }
        catch { }
    }

    private static void DumpMethods(StringBuilder sb, TypeDefinition td, string indent)
    {
        if (td == null) return;
        try
        {
            var methods = td.GetMethods();
            if (methods == null) return;
            foreach (var m in methods)
            {
                try
                {
                    var parms = m.GetParameters();
                    var p = new StringBuilder();
                    if (parms != null)
                        for (int i = 0; i < parms.Count; i++)
                        {
                            if (i > 0) p.Append(", ");
                            p.Append($"{parms[i].Type?.FullName ?? "?"} {parms[i].Name ?? $"p{i}"}");
                        }
                    sb.AppendLine($"{indent}{m.ReturnType?.FullName ?? "void"} {m.Name}({p}){(m.IsStatic() ? " [static]" : "")}");
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>Recursively dump an object's fields and nested managed objects up to maxDepth.</summary>
    private static void DumpObject(StringBuilder sb, ManagedObject obj, string indent, int depth, int maxDepth)
    {
        if (obj == null || depth > maxDepth) return;
        var td = obj.GetTypeDefinition();
        if (td == null) return;

        try
        {
            var fields = td.GetFields();
            if (fields == null) return;
            foreach (var f in fields)
            {
                try
                {
                    bool isStatic = f.IsStatic();
                    string typeName = f.Type?.FullName ?? "?";
                    object raw;
                    try { raw = f.GetDataBoxed(typeof(object), obj.GetAddress(), isStatic); }
                    catch { sb.AppendLine($"{indent}{typeName} {f.Name} = (read error)"); continue; }

                    if (raw is ManagedObject child && depth < maxDepth &&
                        typeName.StartsWith("app.") && !typeName.EndsWith("[]"))
                    {
                        sb.AppendLine($"{indent}{typeName} {f.Name}:");
                        DumpObject(sb, child, indent + "  ", depth + 1, maxDepth);
                    }
                    else
                    {
                        string value = raw?.ToString() ?? "null";
                        if (value.Length > 120) value = value.Substring(0, 120) + "...";
                        sb.AppendLine($"{indent}{typeName} {f.Name} = {value}");
                    }
                }
                catch { }
            }
        }
        catch { }
    }
}
