using System;
using System.Collections.Generic;
using System.IO;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the battle notice banners (HARD KNOCKDOWN, LOCK, FORCED DOWN…) in the
/// game's own localized text, for the types switched on in the user's config
/// file. Hooked at app.UIPartsBattleNotice.ActivateNotice(NoticeInfo,
/// NoticeSettingData, float) — the moment a banner is shown — which carries the
/// displayed type (NoticeSettingData.NoticeUIType) and its text
/// (NoticeSettingData.MessageId). Most notices already have a game sound, so by
/// default only the silent ones are read.
/// </summary>
public class BattleNoticeHooks
{
    private const string CONFIG_FILE = "SF6Access.notices.txt";

    // app.UIBattleHud_Notice.eNoticeUIType, in enum order (value = index)
    private static readonly string[] NoticeKeys =
    {
        "first_attack", "reversal", "throw_escape", "counter", "punish_counter", "stun",
        "hard_knockdown", "crush", "lock", "cross_up", "forced_down", "armor_break",
    };

    // The notices with no sound of their own (tester, 2026-10-16)
    private static readonly HashSet<string> DefaultOn = new() { "hard_knockdown", "lock", "forced_down" };

    private static string _configPath;
    private static DateTime _configStamp;
    private static HashSet<string> _enabled = new(DefaultOn);

    // Rollback re-simulation can show the same banner again
    private static (int team, int type, uint frame) _last = (-1, -1, 0);

    [PluginEntryPoint]
    public static void Initialize()
    {
        try
        {
            _configPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? "",
                "reframework", "plugins", "managed", CONFIG_FILE);
            if (!File.Exists(_configPath)) WriteDefaultConfig();

            var method = TDB.Get().FindType("app.UIPartsBattleNotice")?.GetMethod("ActivateNotice");
            if (method == null)
            {
                API.LogInfo("[SF6Access] UIPartsBattleNotice.ActivateNotice not found, notices not read");
                return;
            }
            method.AddHook(false).AddPre(args =>
            {
                // args[1] = this, args[2] = NoticeInfo, args[3] = NoticeSettingData
                try { OnNotice(ManagedObject.ToManagedObject(args[2]), ManagedObject.ToManagedObject(args[3])); }
                catch (Exception ex) { API.LogError($"[SF6Access] Battle notice hook error: {ex.Message}"); }
                return PreHookResult.Continue;
            });
            API.LogInfo("[SF6Access] BattleNoticeHooks initialized");
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] BattleNoticeHooks init error: {ex.Message}");
        }
    }

    private static void OnNotice(ManagedObject info, ManagedObject setting)
    {
        int type = FlowHelper.ReadIntField(setting, "NoticeUIType", -1);
        var key = (FlowHelper.ReadIntField(info, "TeamId", -1), type, FlowHelper.ReadUIntField(info, "Frame"));
        if (key == _last) return;
        _last = key;

        if (type < 0 || type >= NoticeKeys.Length || !IsEnabled(NoticeKeys[type])) return;

        string text = FlowHelper.ResolveGuidField(setting, "MessageId");
        if (string.IsNullOrWhiteSpace(text)) return;
        API.LogInfo($"[SF6Access] Battle notice [{NoticeKeys[type]}]: {text}");
        ScreenReaderService.Speak(text, interrupt: false);
    }

    /// <summary>Reads the config again whenever the file was edited, so a change
    /// applies without restarting the game.</summary>
    private static bool IsEnabled(string key)
    {
        try
        {
            var stamp = File.GetLastWriteTimeUtc(_configPath);
            if (stamp != _configStamp)
            {
                _configStamp = stamp;
                var enabled = new HashSet<string>();
                foreach (var line in File.ReadAllLines(_configPath))
                {
                    var parts = line.Split('=', 2);
                    if (parts.Length == 2 && !line.TrimStart().StartsWith("#") &&
                        parts[1].Trim().Equals("on", StringComparison.OrdinalIgnoreCase))
                        enabled.Add(parts[0].Trim().ToLowerInvariant());
                }
                _enabled = enabled;
                API.LogInfo($"[SF6Access] Battle notices on: {string.Join(", ", enabled)}");
            }
        }
        catch { }
        return _enabled.Contains(key);
    }

    private static void WriteDefaultConfig()
    {
        var lines = new List<string>
        {
            "# SF6Access - battle notices read aloud (the banners shown during a fight).",
            "# on = read, off = silent. Changes apply without restarting the game.",
            "# Most notices already have a game sound, so only the silent ones start on.",
        };
        foreach (var key in NoticeKeys) lines.Add($"{key}={(DefaultOn.Contains(key) ? "on" : "off")}");
        File.WriteAllLines(_configPath, lines);
    }
}
