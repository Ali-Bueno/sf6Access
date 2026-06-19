using System;
using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Announces the emote / pose / cheer YOU play with your Battle Hub / World Tour
/// avatar. The local avatar's emote is triggered through
/// app.worldtour.avatar.AvatarInputController.SetEmote(uint id) (and SetEmoteHold
/// for held ones); the id is resolved to a localized name via
/// app.helper.hGUI.GetEmoteName(uint).
///
/// SCOPE: this is the LOCAL player only — AvatarInputController is driven by your
/// input. Emotes played by NEARBY players go through a networked avatar-animation
/// path (not this controller) and are NOT covered here; that needs a capture while
/// another player emotes to find the remote trigger. Fixed phrases / stamps / text
/// are handled separately by [[SocialChatHooks]].
///
/// The id is logged raw on every trigger so the id↔name mapping can be confirmed
/// (SetEmote may carry a bank id that GetEmoteName does not accept directly).
/// </summary>
public class AvatarEmoteHooks
{
    private static Method _getEmoteName;          // hGUI.GetEmoteName(uint) — fallback
    private static Method _tryGetEmoteNameMessage; // TableDataManager.TryGetEquipEmoteNameMessage(uint, out string)
    private static ManagedObject _tableDataMgr;

    private static readonly List<string> _pending = new();
    private static readonly object _lock = new();

    // SetEmoteHold fires repeatedly while held — drop repeats of the same id
    // within a short window so a held emote is announced once.
    private static uint _lastId;
    private static long _lastTick;
    private const long REPEAT_MS = 1500;

    [PluginEntryPoint]
    public static void Initialize()
    {
        try
        {
            _getEmoteName = TDB.Get().FindType("app.helper.hGUI")?.GetMethod("GetEmoteName(System.UInt32)");
            _tryGetEmoteNameMessage = TDB.Get().FindType("app.TableDataManager")
                ?.GetMethod("TryGetEquipEmoteNameMessage(System.UInt32, System.String)");

            var td = TDB.Get().FindType("app.worldtour.avatar.AvatarInputController");
            if (td == null)
            {
                API.LogError("[SF6Access] AvatarEmoteHooks: AvatarInputController not found");
                return;
            }

            bool any = false;
            foreach (var sig in new[] { "SetEmote(System.UInt32)", "SetEmoteHold(System.UInt32)" })
            {
                var m = td.GetMethod(sig);
                if (m == null) continue;
                m.AddHook(false).AddPre(args =>
                {
                    try { OnSetEmote((uint)(long)args[1]); }
                    catch (Exception ex) { API.LogError($"[SF6Access] AvatarEmote hook error: {ex.Message}"); }
                    return PreHookResult.Continue;
                });
                any = true;
            }

            API.LogInfo($"[SF6Access] AvatarEmoteHooks initialized (hooks installed={any}, resolver={_getEmoteName != null})");
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] AvatarEmoteHooks init error: {ex.Message}");
        }
    }

    private static void OnSetEmote(uint id)
    {
        long now = Environment.TickCount64;
        if (id == _lastId && now - _lastTick < REPEAT_MS) { _lastTick = now; return; }
        _lastId = id;
        _lastTick = now;

        // NOTE: emotes are ExActions; SetEmote's id is an ExAction/animation id that
        // does not map to a localized emote name through any reachable table (tried
        // ManageId / inventory item id / wheel MotionBankId — all different id spaces).
        // So this currently resolves to null and announces nothing; kept as the capture
        // point if an ExActionId->name path is ever found. See memory social-chat-readout.
        string name = ResolveEmoteName(id);
        if (string.IsNullOrWhiteSpace(name)) return;

        lock (_lock) _pending.Add(name);
    }

    private static string ResolveEmoteName(uint id)
    {
        if (id == 0) return null;

        // Most promising: SetEmote's id is too large to be a MotionBankId (those are
        // ~20000) and is not a ManageId either — it looks like the emote's INVENTORY
        // item id. InventoryManager.Emote (EmoteInventory) resolves item ids by name.
        string invName = ResolveViaInventory(id);
        if (!string.IsNullOrWhiteSpace(invName)) return invName;

        // Next: TableDataManager.TryGetEquipEmoteNameMessage(id, out message).
        // REFramework.NET writes the out-string back into the args array.
        if (_tryGetEmoteNameMessage != null)
        {
            try
            {
                _tableDataMgr ??= API.GetManagedSingleton("app.TableDataManager");
                if (_tableDataMgr != null)
                {
                    var args = new object[] { id, null };
                    var ok = _tryGetEmoteNameMessage.InvokeBoxed(typeof(bool), _tableDataMgr, args);
                    if (ok is bool b && b)
                    {
                        string msg = FlowHelper.CleanTags(args[1] as string);
                        if (!string.IsNullOrWhiteSpace(msg)) return msg;
                    }
                }
            }
            catch { }
        }

        // Fallback: hGUI.GetEmoteName(uint).
        if (_getEmoteName != null)
        {
            try
            {
                string name = _getEmoteName.InvokeBoxed(typeof(string), null, new object[] { id }) as string;
                if (!string.IsNullOrWhiteSpace(name)) return FlowHelper.CleanTags(name);
            }
            catch { }
        }
        return null;
    }

    private static Method _getEmoteInventory;   // app.InventoryManager.get_Emote() [static]
    private static Method _emoteInvGetName;      // EmoteInventory.GetName(uint)
    private static ManagedObject _emoteInventory;
    private static bool _invCached;

    /// <summary>Resolve an emote item id to its name via InventoryManager's EmoteInventory.</summary>
    private static string ResolveViaInventory(uint id)
    {
        try
        {
            if (!_invCached)
            {
                _invCached = true;
                _getEmoteInventory = TDB.Get().FindType("app.InventoryManager")?.GetMethod("get_Emote");
                _emoteInvGetName = TDB.Get().FindType("app.InventoryManager.EmoteInventory")?.GetMethod("GetName(System.UInt32)");
            }
            if (_getEmoteInventory == null || _emoteInvGetName == null) return null;

            _emoteInventory ??= _getEmoteInventory.InvokeBoxed(typeof(object), null, System.Array.Empty<object>()) as ManagedObject;
            if (_emoteInventory == null) return null;

            string name = _emoteInvGetName.InvokeBoxed(typeof(string), _emoteInventory, new object[] { id }) as string;
            return string.IsNullOrWhiteSpace(name) ? null : FlowHelper.CleanTags(name);
        }
        catch { return null; }
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        string[] batch;
        lock (_lock)
        {
            if (_pending.Count == 0) return;
            batch = _pending.ToArray();
            _pending.Clear();
        }

        foreach (var line in batch)
            ScreenReaderService.Speak(line, interrupt: false);
    }
}
