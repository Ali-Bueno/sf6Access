using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility hooks for World Tour character creation screen.
/// Hooks property setters on UIFlowUI61000.Param to detect category changes.
/// F11 dumps detailed avatar creation state for debugging.
/// </summary>
public class AvatarCreateHooks
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_F11 = 0x7A;
    private static bool _lastF11State;
    private static bool _isDumping;

    private static Method _msgGetMethod;

    // Pending category changes
    private static int _pendingMainCategory = -1;
    private static bool _hasPendingMainCategory;
    private static int _pendingMiddleCategory = -1;
    private static bool _hasPendingMiddleCategory;
    private static ulong _lastParamAddr;

    // State tracking
    private static int _lastMainCategory = -1;
    private static int _lastMiddleCategory = -1;

    // Guid cache
    private static readonly Dictionary<string, string> _guidCache = new();

    private static readonly string DumpPath = Path.Combine(
        @"D:\games\steam\steamapps\common\Street Fighter 6\reframework\data",
        "sf6access_avatar_dump.txt"
    );

    // Main category names matching MainCategoryType enum
    private static readonly string[] MainCategoryNames =
    {
        "Type", "Preset", "Body", "Face", "Body Paint",
        "Face Paint", "Color", "Voice", "Recipe"
    };

    // Sub-category names per main category
    private static readonly Dictionary<int, string[]> SubCategoryNames = new()
    {
        [0] = new[] { "Body Type", "Gender Identity" }, // TYPE
        [1] = new[] { "Face Preset", "Body Preset", "Random", "Blend" }, // PRESET
        [2] = new[] { "Height", "Upper Body", "Lower Body", "Build", "Skin Color", "Body Hair" }, // BODY
        [3] = new[] { "Face Shape", "Hair", "Eyes", "Pupils", "Eyelashes", "Eyebrows", "Nose", "Mouth", "Ears", "Beard", "Age", "Contour", "Expression" }, // FACE
        [4] = new[] { "Slot 1", "Slot 2", "Slot 3", "Slot 4", "Slot 5" }, // BODY_PAINT
        [5] = new[] { "Slot 1", "Slot 2", "Slot 3", "Slot 4", "Slot 5" }, // FACE_PAINT
        [6] = new[] { "Skin", "Body Hair", "Hair", "Pupils", "Eyelashes", "Eyebrows", "Beard", "Body Paint", "Face Paint" }, // COLOR
        [7] = new[] { "Voice" }, // VOICE
        [8] = new[] { "Recipe" }, // RECIPE
    };

    [PluginEntryPoint]
    public static void Initialize()
    {
        var paramTd = TDB.Get().FindType("app.worldtour.UIFlowUI61000.Param");
        if (paramTd == null)
        {
            API.LogWarning("[SF6Access] UIFlowUI61000.Param not found");
            return;
        }

        // List all methods on Param for debugging
        var methods = paramTd.GetMethods();
        if (methods != null)
        {
            API.LogInfo($"[SF6Access] UIFlowUI61000.Param has {methods.Count} methods");
            foreach (var m in methods)
            {
                string mName = m.Name ?? "?";
                if (mName.Contains("Category") || mName.Contains("MainCategory") ||
                    mName.Contains("MiddleCategory") || mName.Contains("set_") ||
                    mName.Contains("Select") || mName.Contains("Focus"))
                {
                    API.LogInfo($"[SF6Access]   Param method: {m.ReturnType?.FullName ?? "void"} {mName}");
                }
            }
        }

        // Hook set_CurrentMainCategory
        var setMainCat = paramTd.GetMethod("set_CurrentMainCategory");
        if (setMainCat != null)
        {
            var hook = setMainCat.AddHook(false);
            hook.AddPre(args =>
            {
                try
                {
                    _lastParamAddr = args[1];
                    _pendingMainCategory = (int)args[2];
                    _hasPendingMainCategory = true;
                    API.LogInfo($"[SF6Access] set_CurrentMainCategory({args[2]}) on {args[1]:X}");
                }
                catch (Exception ex)
                {
                    API.LogError($"[SF6Access] set_CurrentMainCategory error: {ex.Message}");
                }
                return PreHookResult.Continue;
            });
            API.LogInfo("[SF6Access] set_CurrentMainCategory hook installed");
        }
        else
        {
            API.LogWarning("[SF6Access] set_CurrentMainCategory not found");
        }

        // Hook set_CurrentMiddleCategory
        var setMidCat = paramTd.GetMethod("set_CurrentMiddleCategory");
        if (setMidCat != null)
        {
            var hook = setMidCat.AddHook(false);
            hook.AddPre(args =>
            {
                try
                {
                    _lastParamAddr = args[1];
                    _pendingMiddleCategory = (int)args[2];
                    _hasPendingMiddleCategory = true;
                    API.LogInfo($"[SF6Access] set_CurrentMiddleCategory({args[2]}) on {args[1]:X}");
                }
                catch (Exception ex)
                {
                    API.LogError($"[SF6Access] set_CurrentMiddleCategory error: {ex.Message}");
                }
                return PreHookResult.Continue;
            });
            API.LogInfo("[SF6Access] set_CurrentMiddleCategory hook installed");
        }
        else
        {
            API.LogWarning("[SF6Access] set_CurrentMiddleCategory not found");
        }


        API.LogInfo("[SF6Access] Avatar creation hooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        // F11: dump avatar state
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

        // Process main category change
        if (_hasPendingMainCategory)
        {
            _hasPendingMainCategory = false;
            if (_pendingMainCategory != _lastMainCategory)
            {
                _lastMainCategory = _pendingMainCategory;
                _lastMiddleCategory = -1; // Reset sub on main change
                string name = GetMainCategoryName(_pendingMainCategory);
                API.LogInfo($"[SF6Access] Announcing main category: {name}");
                ScreenReaderService.Speak(name);
            }
        }

        // Process middle category change
        if (_hasPendingMiddleCategory)
        {
            _hasPendingMiddleCategory = false;
            if (_pendingMiddleCategory != _lastMiddleCategory)
            {
                _lastMiddleCategory = _pendingMiddleCategory;
                string name = GetSubCategoryName(_lastMainCategory, _pendingMiddleCategory);
                API.LogInfo($"[SF6Access] Announcing sub-category: {name}");
                ScreenReaderService.Speak(name);
            }
        }
    }

    private static string GetMainCategoryName(int index)
    {
        // Try localized name from Param.MainCategoryNameMessageId
        if (_lastParamAddr != 0)
        {
            try
            {
                var param = ManagedObject.ToManagedObject(_lastParamAddr);
                if (param != null)
                {
                    var guidArray = (param as IObject)?.Call("get_MainCategoryNameMessageId") as ManagedObject;
                    if (guidArray != null)
                    {
                        var guid = (guidArray as IObject)?.Call("Get", index);
                        if (guid is REFrameworkNET.ValueType vt)
                        {
                            string text = ResolveGuid(vt, $"main_{index}");
                            if (!string.IsNullOrEmpty(text))
                                return text;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                API.LogError($"[SF6Access] Localized main category error: {ex.Message}");
            }
        }

        // English fallback
        if (index >= 0 && index < MainCategoryNames.Length)
            return MainCategoryNames[index];
        return $"Category {index}";
    }

    private static string GetSubCategoryName(int mainIndex, int subIndex)
    {
        // Try localized from UIGroupBase on the MiddleBaseParam
        // (would require finding the UIGroupBase instance - complex)
        // For now use English fallback

        if (mainIndex >= 0 && SubCategoryNames.TryGetValue(mainIndex, out var subs))
        {
            if (subIndex >= 0 && subIndex < subs.Length)
                return subs[subIndex];
        }
        return $"Item {subIndex + 1}";
    }

    private static string ResolveGuid(REFrameworkNET.ValueType vt, string cacheKey)
    {
        if (_guidCache.TryGetValue(cacheKey, out var cached))
            return cached;

        ulong vtAddr = vt.GetAddress();
        if (vtAddr == 0) return null;

        bool allZero = true;
        for (int i = 0; i < 16; i++)
        {
            if (Marshal.ReadByte((IntPtr)(long)(vtAddr + (ulong)i)) != 0)
            { allZero = false; break; }
        }
        if (allZero) return null;

        _msgGetMethod ??= TDB.Get().FindType("via.gui.message")?.GetMethod("get(System.Guid)");
        if (_msgGetMethod == null) return null;

        try
        {
            var task = Task.Run(() =>
            {
                try { return _msgGetMethod.InvokeBoxed(typeof(string), null, new object[] { vt }) as string; }
                catch { return null; }
            });

            if (task.Wait(TimeSpan.FromMilliseconds(200)))
            {
                string text = CleanTags(task.Result);
                if (!string.IsNullOrEmpty(text))
                    _guidCache[cacheKey] = text;
                return text;
            }
        }
        catch { }

        return null;
    }

    private static string CleanTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Regex.Replace(Regex.Replace(text, @"<[^>]+>", "").Trim(), @"\s+", " ").Trim();
    }

    private static void DumpAvatarState()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== SF6 Avatar State Dump - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        sb.AppendLine();

        // Dump UIFlowUI61000.Param methods
        var paramTd = TDB.Get().FindType("app.worldtour.UIFlowUI61000.Param");
        if (paramTd != null)
        {
            sb.AppendLine("=== UIFlowUI61000.Param Methods ===");
            var methods = paramTd.GetMethods();
            if (methods != null)
            {
                foreach (var m in methods)
                {
                    try
                    {
                        var parms = m.GetParameters();
                        var pList = new List<string>();
                        if (parms != null)
                            foreach (var p in parms)
                                pList.Add($"{p.Type?.FullName ?? "?"} {p.Name ?? "?"}");
                        sb.AppendLine($"  {m.ReturnType?.FullName ?? "void"} {m.Name}({string.Join(", ", pList)})");
                    }
                    catch { }
                }
            }
        }

        // If we have a param address, dump its live state
        if (_lastParamAddr != 0)
        {
            sb.AppendLine();
            sb.AppendLine($"=== Live Param State (addr={_lastParamAddr:X}) ===");
            try
            {
                var param = ManagedObject.ToManagedObject(_lastParamAddr);
                if (param != null)
                {
                    try
                    {
                        var mc = (param as IObject)?.Call("get_CurrentMainCategory");
                        sb.AppendLine($"  CurrentMainCategory = {mc}");
                    }
                    catch (Exception ex) { sb.AppendLine($"  CurrentMainCategory error: {ex.Message}"); }

                    try
                    {
                        var mc2 = (param as IObject)?.Call("get_CurrentMiddleCategory");
                        sb.AppendLine($"  CurrentMiddleCategory = {mc2}");
                    }
                    catch (Exception ex) { sb.AppendLine($"  CurrentMiddleCategory error: {ex.Message}"); }

                    try
                    {
                        var active = (param as IObject)?.Call("get_IsActivate");
                        sb.AppendLine($"  IsActivate = {active}");
                    }
                    catch (Exception ex) { sb.AppendLine($"  IsActivate error: {ex.Message}"); }
                }
            }
            catch (Exception ex) { sb.AppendLine($"  Param read error: {ex.Message}"); }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("  No Param address captured yet (hooks haven't fired)");
        }

        // Dump MiddleBaseParam methods
        var middleTd = TDB.Get().FindType("app.worldtour.UIFlowWTAvatarCreateDefaultMiddle.MiddleBaseParam");
        if (middleTd != null)
        {
            sb.AppendLine();
            sb.AppendLine("=== MiddleBaseParam Methods ===");
            var methods = middleTd.GetMethods();
            if (methods != null)
            {
                foreach (var m in methods)
                {
                    try
                    {
                        var parms = m.GetParameters();
                        var pList = new List<string>();
                        if (parms != null)
                            foreach (var p in parms)
                                pList.Add($"{p.Type?.FullName ?? "?"} {p.Name ?? "?"}");
                        sb.AppendLine($"  {m.ReturnType?.FullName ?? "void"} {m.Name}({string.Join(", ", pList)})");
                    }
                    catch { }
                }
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(DumpPath)!);
        File.WriteAllText(DumpPath, sb.ToString());
        API.LogInfo($"[SF6Access] Avatar state dump saved to {DumpPath}");
    }
}
