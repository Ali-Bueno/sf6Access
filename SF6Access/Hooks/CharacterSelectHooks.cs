using System;
using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for character select screen.
/// Hooks SelectedFighterCtrl.SetFighterSetting to detect fighter changes per player.
/// Buffers announcements within a frame so both players are read together.
/// </summary>
public class CharacterSelectHooks
{
    // Track last fighter ID per player slot to detect real changes
    private static readonly Dictionary<int, uint> _lastFighterIdPerPlayer = new();

    // Buffer announcements within a frame to combine P1+P2
    private static readonly List<string> _pendingAnnouncements = new();

    // Cached TDB
    private static Method _getFighterNameMethod;

    [PluginEntryPoint]
    public static void Initialize()
    {
        _getFighterNameMethod = TDB.Get().FindType("app.IDScriptExtensions")
            ?.GetMethod("GetFighterNameText(app.CHARA_ID)");

        if (_getFighterNameMethod == null)
        {
            API.LogError("[SF6Access] GetFighterNameText method not found");
            return;
        }

        var selFighterTd = TDB.Get().FindType("app.menu.SelectedFighterCtrl");
        if (selFighterTd == null)
        {
            API.LogError("[SF6Access] SelectedFighterCtrl type not found");
            return;
        }

        var setMethod = selFighterTd.GetMethod("SetFighterSetting(System.Int32, System.UInt32, System.Int32, System.Int32)");
        if (setMethod == null)
        {
            API.LogError("[SF6Access] SetFighterSetting method not found");
            return;
        }

        var hook = setMethod.AddHook(false);
        hook.AddPre(args =>
        {
            try
            {
                int playerNo = (int)(long)args[2];
                uint fid = (uint)(long)args[3];

                // Only queue if this player's fighter actually changed
                if (_lastFighterIdPerPlayer.TryGetValue(playerNo, out uint lastFid) && lastFid == fid)
                    return PreHookResult.Continue;

                _lastFighterIdPerPlayer[playerNo] = fid;

                string name = ResolveFighterName(fid);
                if (!string.IsNullOrEmpty(name))
                {
                    string announcement = $"{name} Player {playerNo + 1}";
                    API.LogInfo($"[SF6Access] Fighter P{playerNo + 1}: {name} (fid={fid})");
                    _pendingAnnouncements.Add(announcement);
                }
            }
            catch (Exception ex)
            {
                API.LogError($"[SF6Access] SetFighterSetting hook error: {ex.Message}");
            }
            return PreHookResult.Continue;
        });

        API.LogInfo("[SF6Access] CharSelect SetFighterSetting hook installed");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnLateUpdate()
    {
        if (_pendingAnnouncements.Count == 0) return;

        string combined = string.Join(". ", _pendingAnnouncements);
        _pendingAnnouncements.Clear();
        ScreenReaderService.Speak(combined);
    }

    private static string ResolveFighterName(uint fid)
    {
        if (_getFighterNameMethod == null) return null;

        try
        {
            byte charaId = (byte)fid;
            return _getFighterNameMethod.InvokeBoxed(
                typeof(string), null, new object[] { charaId }) as string;
        }
        catch
        {
            return $"Fighter {fid}";
        }
    }

    /// <summary>Reset tracked state (e.g., when leaving character select)</summary>
    public static void Reset()
    {
        _lastFighterIdPerPlayer.Clear();
        _pendingAnnouncements.Clear();
    }
}
