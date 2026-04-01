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
/// F12 key: scans TDB for UI-related types and dumps to file for accessibility research.
/// </summary>
public static class TDBScanner
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_F12 = 0x7B;
    private static bool _lastKeyState;
    private static bool _isDumping;

    private static readonly string DumpPath = Path.Combine(
        @"D:\games\steam\steamapps\common\Street Fighter 6\reframework\data",
        "sf6access_tdb_scan.txt"
    );

    // Patterns to search for in type names (case-insensitive matching)
    private static readonly string[] SearchPatterns = {
        // News and information
        "News", "Notice", "Information", "Bulletin", "Announce",
        // Tutorials and training
        "Tutorial", "Training",
        // UI flows (main navigation)
        "UIFlow",
        // UI components
        "UIPartsTab", "UIPartsScroll", "UIPartsText", "UIPartsList",
        // Character and stage select
        "CharaSelect", "CharacterSelect", "StageSelect", "CharaList",
        // Battle HUD and results
        "BattleHud", "BattleResult", "RoundResult",
        // Option menu
        "UIOption", "OptionMenu", "OptionFlow",
        // Multi menu (CFN, shop, etc.)
        "MultiMenu", "UIFlowMulti",
        // Text and message display
        "UIFlowText", "MessageWindow", "TextScroll",
    };

    [Callback(typeof(UpdateBehavior), CallbackType.Pre)]
    public static void OnUpdate()
    {
        bool keyDown = (GetAsyncKeyState(VK_F12) & 0x8000) != 0;

        if (keyDown && !_lastKeyState && !_isDumping)
        {
            _isDumping = true;
            try
            {
                ScanTypes();
                ScreenReaderService.Speak("TDB scan complete");
                API.LogInfo($"[SF6Access] TDB scan saved to {DumpPath}");
            }
            catch (Exception ex)
            {
                API.LogError($"[SF6Access] TDB scan failed: {ex.Message}");
                ScreenReaderService.Speak("TDB scan failed");
            }
            finally
            {
                _isDumping = false;
            }
        }

        _lastKeyState = keyDown;
    }

    private static void ScanTypes()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== SF6 TDB Type Scan - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        sb.AppendLine();

        var tdb = TDB.Get();
        uint numTypes = tdb.GetNumTypes();
        sb.AppendLine($"Total types in TDB: {numTypes}");
        sb.AppendLine();

        // Group results by search pattern
        var results = new Dictionary<string, List<string>>();
        foreach (var pattern in SearchPatterns)
            results[pattern] = new List<string>();

        // Scan all types by index
        uint numTypesCount = tdb.GetNumTypes();
        for (uint idx = 0; idx < numTypesCount; idx++)
        {
            try
            {
                var td = tdb.GetType(idx);
                if (td == null) continue;
                string fullName = td.FullName;
                if (string.IsNullOrEmpty(fullName)) continue;

                foreach (var pattern in SearchPatterns)
                {
                    if (fullName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        results[pattern].Add(fullName);
                        break; // Only categorize under first matching pattern
                    }
                }
            }
            catch { }
        }

        // Output results grouped by pattern
        foreach (var pattern in SearchPatterns)
        {
            var matches = results[pattern];
            if (matches.Count == 0) continue;

            matches.Sort();
            sb.AppendLine($"========== {pattern} ({matches.Count} types) ==========");
            foreach (var name in matches)
                sb.AppendLine($"  {name}");
            sb.AppendLine();
        }

        // Dump detailed info for key types
        sb.AppendLine("========== DETAILED TYPE INFO ==========");
        sb.AppendLine();

        string[] detailTypes = {
            // Flow manager handle structure
            "app.UIFlowManager.Handle", "app.UIFlowManager.Element",
            // Fighting Ground menu
            "app.menu.UIFlowFGMainMenuList", "app.menu.UIFlowFGMainMenuList.Param",
            "app.menu.FGMainMenuData",
            // Character/stage select
            "app.UIFlowFighterSelectSettingBase",
            "app.UIFlowMatchingFighterSetting", "app.UIFlowMatchingFighterSetting.Param",
            "app.UIFlowGenericStageSetting", "app.UIFlowGenericStageSettingBase",
            "app.UIFlowSideSelect", "app.SelectFighterUIDataManager",
            // News/info/mail
            "app.UIFlowNotice", "app.UIFlowEntryNotice",
            "app.UIFlowMailBox", "app.UIFlowTextList",
            "app.UIFlowSelectFighterInformation",
            // Training
            "app.training.UIFlowTrainingMenu",
            "app.training.UIFlowTrainingMenu.Param",
            "app.training.TrainingMenuData",
            // Option sub-list/dropdown
            "app.UIOptionSettingMenu", "app.UIOptionSettingMenu.Flow_SubList",
            "app.UIOptionSettingMenu.Flow_Base", "app.UIOptionSettingMenu.OptionMenuParam",
            "app.UIOptionSettingMenu.eListState",
            "app.UIPartsSimpleList",
            // Sub-menu / start menu
            "app.UIStartMenu.FlowParam", "app.UIStartMenu.MenuItem",
            "app.UIStartMenu.MenuType",
        };

        foreach (var typeName in detailTypes)
        {
            var td = tdb.FindType(typeName);
            if (td == null) continue;

            sb.AppendLine($"--- {typeName} ---");
            var parent = td.ParentType;
            if (parent != null)
                sb.AppendLine($"  Parent: {parent.FullName}");

            // Fields
            var fields = td.GetFields();
            if (fields != null && fields.Count > 0)
            {
                sb.AppendLine("  Fields:");
                foreach (var f in fields)
                {
                    try
                    {
                        var ft = f.Type;
                        string ftName = ft?.FullName ?? "?";
                        string staticTag = f.IsStatic() ? " [static]" : "";
                        sb.AppendLine($"    {ftName} {f.Name}{staticTag}");
                    }
                    catch { }
                }
            }

            // Methods
            var methods = td.GetMethods();
            if (methods != null && methods.Count > 0)
            {
                sb.AppendLine("  Methods:");
                foreach (var m in methods)
                {
                    try
                    {
                        var retType = m.ReturnType;
                        string retName = retType?.FullName ?? "void";
                        bool isStatic = m.IsStatic();
                        var parms = m.GetParameters();
                        var pStr = new StringBuilder();
                        if (parms != null)
                        {
                            for (int i = 0; i < parms.Count; i++)
                            {
                                if (i > 0) pStr.Append(", ");
                                var p = parms[i];
                                pStr.Append($"{p.Type?.FullName ?? "?"} {p.Name ?? $"arg{i}"}");
                            }
                        }
                        string sTag = isStatic ? " [static]" : "";
                        sb.AppendLine($"    {retName} {m.Name}({pStr}){sTag}");
                    }
                    catch { }
                }
            }

            sb.AppendLine();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(DumpPath));
        File.WriteAllText(DumpPath, sb.ToString());
    }
}
