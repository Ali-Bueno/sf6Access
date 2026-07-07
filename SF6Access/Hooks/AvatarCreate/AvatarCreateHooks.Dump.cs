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
/// F11 research dump for the avatar creator. Kept as a static [Callback] so
/// the tool works even when the screen is inactive (its handle listing helps
/// World Tour research). Dumps the main param's key fields, the current child
/// flow's full field/method list, and the live charaEditParam color values.
/// </summary>
public sealed partial class AvatarCreateHooks
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_F11 = 0x7A;
    private static bool _lastF11State;
    private static bool _isDumping;

    private static string DumpPath =>
        Path.Combine(ObjectDumper.DumpDir, "sf6access_avatar_dump.txt");

    private static Field _handlesField;
    private static bool _tdbCached;

    internal static ManagedObject GetFlowHandles()
    {
        var flowMgr = API.GetManagedSingleton("app.UIFlowManager");
        if (flowMgr == null) return null;

        if (!_tdbCached)
        {
            _tdbCached = true;
            _handlesField = TDB.Get().FindType("app.UIFlowManager")?.GetField("_Handles");
        }
        if (_handlesField == null) return null;

        ulong mgrAddr = flowMgr.GetAddress();
        if (mgrAddr == 0) return null;
        return _handlesField.GetDataBoxed(typeof(ManagedObject), mgrAddr, false) as ManagedObject;
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnDumpKey()
    {
        bool f11Down = (GetAsyncKeyState(VK_F11) & 0x8000) != 0;
        if (f11Down && !_lastF11State && !_isDumping)
        {
            _isDumping = true;
            try
            {
                DumpAvatarState();
                ScreenReaderService.Speak("Avatar dump complete");
            }
            catch (Exception ex)
            {
                API.LogError($"[SF6Access] Avatar dump failed: {ex.Message}");
            }
            finally { _isDumping = false; }
        }
        _lastF11State = f11Down;
    }

    private static void DumpAvatarState()
    {
        var self = _self;
        var sb = new StringBuilder();
        sb.AppendLine($"=== SF6 Avatar State Dump - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        sb.AppendLine($"AvatarParam: {(self?._avatarParam != null ? "found" : "null")}");
        sb.AppendLine($"IsActive: {self?.Active == true}");
        sb.AppendLine($"LastMainCategory: {self?._lastMainCategory}");
        sb.AppendLine($"LastMiddleCategory: {self?._lastMiddleCategory}");
        sb.AppendLine($"ChildFlowType: {self?._lastChildFlowType}");
        sb.AppendLine();

        DumpFlowHandles(sb);

        if (self?._avatarParam != null)
        {
            DumpParamFields(sb, self._avatarParam, "Avatar Param (key fields)",
                f => f.Contains("Category") || f.Contains("Middle") || f.Contains("Select") ||
                     f.Contains("Guide") || f.Contains("Paint") || f.Contains("Zeny"));
            DumpEditParamColors(sb, self._avatarParam);
        }

        if (self?._childFlowParam != null)
            DumpChildFlow(sb, self._childFlowParam, self._lastChildFlowType);

        Directory.CreateDirectory(Path.GetDirectoryName(DumpPath)!);
        File.WriteAllText(DumpPath, sb.ToString());
        API.LogInfo($"[SF6Access] Avatar state dump saved to {DumpPath}");
    }

    private static void DumpFlowHandles(StringBuilder sb)
    {
        sb.AppendLine("=== UIFlowManager Handles (worldtour only, index 0 = newest) ===");
        try
        {
            var handles = GetFlowHandles();
            if (handles == null) { sb.AppendLine("  null"); return; }
            int count = FlowHelper.GetListCount(handles);
            for (int i = 0; i < count; i++)
            {
                try
                {
                    var handle = FlowHelper.GetListItem(handles, i);
                    var param = FlowHelper.Call(handle, "GetParam") as ManagedObject;
                    string paramType = param?.GetTypeDefinition()?.FullName ?? "null";
                    if (!paramType.Contains("worldtour")) continue;
                    sb.AppendLine($"  [{i}] {paramType} (IsEnd={FlowHelper.Call(handle, "get_IsEnd")})");
                }
                catch { }
            }
        }
        catch (Exception ex) { sb.AppendLine($"  Error: {ex.Message}"); }
    }

    private static void DumpParamFields(StringBuilder sb, ManagedObject obj, string title,
        Func<string, bool> nameFilter)
    {
        sb.AppendLine();
        sb.AppendLine($"=== {title} ===");
        try
        {
            var td = obj.GetTypeDefinition();
            ulong addr = obj.GetAddress();
            var fields = td?.GetFields();
            if (fields == null) return;
            foreach (var f in fields)
            {
                string fname = f.Name ?? "";
                if (!nameFilter(fname)) continue;
                try
                {
                    var val = f.GetDataBoxed(typeof(object), addr, false);
                    sb.AppendLine($"    {fname} = {val}");
                }
                catch (Exception ex) { sb.AppendLine($"    {fname} ERROR: {ex.Message}"); }
            }
        }
        catch (Exception ex) { sb.AppendLine($"  Fields error: {ex.Message}"); }
    }

    /// <summary>The live charaEditParam colors (raw rgba + spoken names).</summary>
    private static void DumpEditParamColors(StringBuilder sb, ManagedObject rootParam)
    {
        sb.AppendLine();
        sb.AppendLine("=== charaEditParam colors ===");
        try
        {
            var presetData = FlowHelper.Call(rootParam, "get_MyEditPresetParam") as ManagedObject
                             ?? FlowHelper.GetObjectField(rootParam, "MyEditPresetParam");
            var edit = FlowHelper.GetObjectField(presetData, "EditParam");
            if (edit == null) { sb.AppendLine("  EditParam null"); return; }

            foreach (var fieldName in new[]
            {
                "FaceColor", "PaintColor", "HairColor", "HairColor2", "HairColor3", "HairColor4",
                "ChestHairColor", "BackHairColor", "ArmHairColor", "LegHairColor"
            })
            {
                uint? rgba = FlowHelper.ReadColorField(edit, fieldName);
                sb.AppendLine(rgba != null
                    ? $"    {fieldName} = #{rgba.Value:X8} ({ColorNamer.NameRgba(rgba.Value)})"
                    : $"    {fieldName} = unreadable");
            }
            foreach (var (owner, field) in new[]
            {
                ("eye_r", "iris_col"), ("eye_l", "iris_col"),
                ("eye_r", "sclera_col"), ("eye_l", "sclera_col"),
                ("brows", "colL"), ("brows", "colR"),
                ("lash", "colorup"), ("lash", "colordown"),
            })
            {
                var ownerObj = FlowHelper.GetObjectField(edit, owner);
                uint? rgba = FlowHelper.ReadColorField(ownerObj, field);
                sb.AppendLine(rgba != null
                    ? $"    {owner}.{field} = #{rgba.Value:X8} ({ColorNamer.NameRgba(rgba.Value)})"
                    : $"    {owner}.{field} = unreadable");
            }
        }
        catch (Exception ex) { sb.AppendLine($"  Error: {ex.Message}"); }
    }

    private static void DumpChildFlow(StringBuilder sb, ManagedObject child, string typeName)
    {
        sb.AppendLine();
        sb.AppendLine($"=== Child Flow: {typeName} ===");
        try
        {
            var td = child.GetTypeDefinition();
            ulong addr = child.GetAddress();
            var fields = td?.GetFields();
            if (fields != null)
            {
                sb.AppendLine($"  Fields ({fields.Count}):");
                foreach (var f in fields)
                {
                    try
                    {
                        var val = f.GetDataBoxed(typeof(object), addr, false);
                        string valStr = val?.ToString() ?? "null";
                        if (valStr.Length > 100) valStr = valStr[..100] + "...";
                        sb.AppendLine($"    {f.Type?.FullName ?? "?"} {f.Name} = {valStr}");
                    }
                    catch (Exception ex) { sb.AppendLine($"    {f.Name} ERROR: {ex.Message}"); }
                }
            }
        }
        catch (Exception ex) { sb.AppendLine($"  Error: {ex.Message}"); }
    }
}
