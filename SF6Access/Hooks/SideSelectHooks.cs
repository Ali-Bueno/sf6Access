using System;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the side select screen (Human/CPU selection per side).
/// The screen is a 3-position strip navigated with left/right:
///   POS_1P  = Player 1 Human, Player 2 CPU
///   DEFAULT = Both CPU
///   POS_2P  = Player 1 CPU, Player 2 Human
/// Primary indicator: ArrPadIconCtrl[0].PlayState from P1 param.
/// </summary>
public class SideSelectHooks
{
    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    private static Field _handlesField;
    private static bool _tdbCached;

    private static ManagedObject _p1Param;
    private static ManagedObject _p2Param;
    private static string _lastPadState = "";
    private static string _lastAnnouncement = "";

    [PluginEntryPoint]
    public static void Initialize()
    {
        HookPadNavigation();
        API.LogInfo("[SF6Access] SideSelectHooks initialized");
    }

    private static void HookPadNavigation()
    {
        try
        {
            var td = TDB.Get().FindType("app.UIPartsSideSelectPadIcon");
            if (td == null) return;

            foreach (var name in new[] { "Left", "Right" })
            {
                var method = td.GetMethod(name);
                if (method == null) continue;
                var hook = method.AddHook(false);
                hook.AddPost((ref ulong retval) =>
                {
                    if (_isActive) PollState();
                });
                API.LogInfo($"[SF6Access] PadIcon.{name} hook installed");
            }
        }
        catch (Exception ex)
        {
            API.LogInfo($"[SF6Access] PadIcon hook failed: {ex.Message}");
        }
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (!_isActive)
        {
            if (_pollCounter % POLL_SEARCH_INTERVAL != 0) return;
            TryFindParam();
            return;
        }

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0 && !IsStillActive())
        {
            Reset();
            return;
        }

        if (_pollCounter % POLL_READ_INTERVAL == 0)
            PollState();
    }

    private static void CacheTDB()
    {
        if (_tdbCached) return;
        _tdbCached = true;
        _handlesField = TDB.Get().FindType("app.UIFlowManager")?.GetField("_Handles");
    }

    private static void TryFindParam()
    {
        CacheTDB();
        if (_handlesField == null) return;

        var flowMgr = API.GetManagedSingleton("app.UIFlowManager");
        if (flowMgr == null) return;

        var handles = _handlesField.GetDataBoxed(typeof(object), flowMgr.GetAddress(), false) as ManagedObject;
        if (handles == null) return;

        var countMethod = handles.GetTypeDefinition()?.GetMethod("get_Count");
        var getItemMethod = handles.GetTypeDefinition()?.GetMethod("get_Item(System.Int32)");
        if (countMethod == null || getItemMethod == null) return;

        int count = Convert.ToInt32(countMethod.InvokeBoxed(typeof(int), handles, Array.Empty<object>()));

        for (int i = 0; i < count && i < 30; i++)
        {
            try
            {
                var handle = getItemMethod.InvokeBoxed(typeof(object), handles, new object[] { i }) as ManagedObject;
                if (handle == null) continue;
                var param = handle.GetField("<Param>k__BackingField") as ManagedObject;
                if (param?.GetTypeDefinition()?.FullName != "app.UIFlowSideSelect.Param") continue;

                int userIndex = ReadIntField(param, "UserIndex");
                if (userIndex == 0) _p1Param = param;
                else if (userIndex == 1) _p2Param = param;
            }
            catch { }
        }

        if (_p1Param == null && _p2Param == null) return;

        _isActive = true;
        _lastPadState = "";
        _lastAnnouncement = "";
        API.LogInfo($"[SF6Access] SideSelect found (P1={_p1Param != null}, P2={_p2Param != null})");
        PollState();
    }

    private static void PollState()
    {
        // Read PadIconCtrl PlayState from both params — use whichever has a meaningful state
        string padPs = ReadPadCtrlFromAny();
        if (padPs == null) return;

        // Ignore animation states — only react to stable positions
        if (padPs != "POS_1P" && padPs != "POS_2P" && padPs != "DEFAULT")
            return;

        if (padPs == _lastPadState) return;

        string prevPad = _lastPadState;
        _lastPadState = padPs;

        string announcement;
        if (padPs == "POS_1P")
        {
            announcement = "Player 1: Human";
        }
        else if (padPs == "POS_2P")
        {
            announcement = "Player 2: Human";
        }
        else
        {
            // DEFAULT = both CPU. Announce based on which side just changed.
            if (prevPad == "POS_1P")
                announcement = "Player 1: CPU";
            else if (prevPad == "POS_2P")
                announcement = "Player 2: CPU";
            else
                announcement = "CPU";
        }

        if (announcement == _lastAnnouncement) return;
        _lastAnnouncement = announcement;

        API.LogInfo($"[SF6Access] {announcement} (PadCtrl={padPs})");
        ScreenReaderService.Speak(announcement);
    }

    private static string ReadPadCtrlFromAny()
    {
        // Check both params' PadIconCtrl — return the first non-DEFAULT/non-empty state,
        // or DEFAULT if that's what both show
        string best = null;
        foreach (var param in new[] { _p1Param, _p2Param })
        {
            if (param == null) continue;
            var padArr = GetField(param, "ArrPadIconCtrl");
            if (padArr == null) continue;

            int len = GetArrayLength(padArr);
            for (int i = 0; i < len && i < 3; i++)
            {
                var padCtrl = GetArrayElement(padArr, i);
                if (padCtrl == null) continue;
                string ps = CallString(padCtrl, "get_PlayState");
                if (string.IsNullOrEmpty(ps)) continue;

                // Prefer POS_1P or POS_2P over DEFAULT
                if (ps == "POS_1P" || ps == "POS_2P")
                    return ps;

                if (best == null)
                    best = ps;
            }
        }
        return best;
    }

    // --- Utilities ---

    private static string CallString(ManagedObject obj, string methodName)
    {
        try { return (obj as IObject)?.Call(methodName) as string; }
        catch { return null; }
    }

    private static ManagedObject GetField(ManagedObject obj, string name)
    {
        try { return obj.GetField(name) as ManagedObject; } catch { }
        try { return obj.GetField($"<{name}>k__BackingField") as ManagedObject; } catch { }
        return null;
    }

    private static int ReadIntField(ManagedObject obj, string name)
    {
        try
        {
            var td = obj.GetTypeDefinition();
            var field = td?.GetField($"<{name}>k__BackingField") ?? td?.GetField(name);
            if (field != null)
                return Convert.ToInt32(field.GetDataBoxed(typeof(int), obj.GetAddress(), false));
        }
        catch { }
        return -1;
    }

    private static int GetArrayLength(ManagedObject arr)
    {
        foreach (var m in new[] { "get_Length", "get_Count" })
        {
            try
            {
                var result = (arr as IObject)?.Call(m);
                if (result != null) return Convert.ToInt32(result);
            }
            catch { }
        }
        return 0;
    }

    private static ManagedObject GetArrayElement(ManagedObject arr, int index)
    {
        try { return (arr as IObject)?.Call("Get", index) as ManagedObject; }
        catch { return null; }
    }

    private static bool IsStillActive()
    {
        try
        {
            var flowMgr = API.GetManagedSingleton("app.UIFlowManager");
            if (flowMgr == null) return false;
            var handles = _handlesField?.GetDataBoxed(typeof(object), flowMgr.GetAddress(), false) as ManagedObject;
            if (handles == null) return false;
            var countMethod = handles.GetTypeDefinition()?.GetMethod("get_Count");
            var getItemMethod = handles.GetTypeDefinition()?.GetMethod("get_Item(System.Int32)");
            if (countMethod == null || getItemMethod == null) return false;
            int count = Convert.ToInt32(countMethod.InvokeBoxed(typeof(int), handles, Array.Empty<object>()));
            for (int i = 0; i < count && i < 30; i++)
            {
                try
                {
                    var handle = getItemMethod.InvokeBoxed(typeof(object), handles, new object[] { i }) as ManagedObject;
                    if (handle == null) continue;
                    var param = handle.GetField("<Param>k__BackingField") as ManagedObject;
                    if (param?.GetTypeDefinition()?.FullName == "app.UIFlowSideSelect.Param")
                        return true;
                }
                catch { }
            }
        }
        catch { }
        return false;
    }

    private static void Reset()
    {
        API.LogInfo("[SF6Access] SideSelect ended");
        _isActive = false;
        _p1Param = null;
        _p2Param = null;
        _lastPadState = "";
        _lastAnnouncement = "";
    }
}
