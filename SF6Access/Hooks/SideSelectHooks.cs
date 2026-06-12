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
    private const string PARAM_TYPE = "app.UIFlowSideSelect.Param";

    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

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

        // Re-bind when the game recreated the Params (stale instances read
        // dead memory → side select goes silent on re-entry)
        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var (p1, p2) = FindParams();
            if (p1 == null && p2 == null)
            {
                Reset();
                return;
            }
            if (FlowHelper.AddressOf(p1) != FlowHelper.AddressOf(_p1Param) ||
                FlowHelper.AddressOf(p2) != FlowHelper.AddressOf(_p2Param))
                Bind(p1, p2);
        }

        if (_pollCounter % POLL_READ_INTERVAL == 0)
            PollState();
    }

    private static void TryFindParam()
    {
        var (p1, p2) = FindParams();
        if (p1 == null && p2 == null) return;
        Bind(p1, p2);
    }

    private static (ManagedObject p1, ManagedObject p2) FindParams()
    {
        ManagedObject p1 = null, p2 = null;
        foreach (var (_, param) in FlowHelper.FindFlowParamsByPrefix(PARAM_TYPE))
        {
            int userIndex = ReadIntField(param, "UserIndex");
            if (userIndex == 0) p1 ??= param;
            else if (userIndex == 1) p2 ??= param;
        }
        return (p1, p2);
    }

    private static void Bind(ManagedObject p1, ManagedObject p2)
    {
        _p1Param = p1;
        _p2Param = p2;
        _isActive = true;
        _lastPadState = "";
        _lastAnnouncement = "";
        API.LogInfo($"[SF6Access] SideSelect found (P1={p1 != null}, P2={p2 != null})");
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
