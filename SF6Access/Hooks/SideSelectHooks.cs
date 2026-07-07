using System;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the side select screen (Human/CPU selection per side).
/// The screen is a 3-position strip navigated with left/right:
///   POS_1P  = Player 1 Human, Player 2 CPU
///   DEFAULT = Both CPU
///   POS_2P  = Player 1 CPU, Player 2 Human
/// Primary indicator: ArrPadIconCtrl[0].PlayState from P1 param.
///
/// ScreenAdapter for the poll; the PadIcon Left/Right hooks (event-routed
/// immediate re-read) stay in the static [PluginEntryPoint] and call into the
/// registered instance. Registered in ScreenRegistry.
/// </summary>
public sealed class SideSelectHooks : ScreenAdapter
{
    private const string PARAM_TYPE = "app.UIFlowSideSelect.Param";
    private static readonly string[] Types = { PARAM_TYPE };
    public override string[] OwnedTypes => Types;

    private static SideSelectHooks _self;

    public SideSelectHooks()
    {
        SearchInterval = 60;
        ReadInterval = 5;
        _self = this;
    }

    private ManagedObject _p1Param;
    private ManagedObject _p2Param;
    private string _lastPadState = "";
    private string _lastAnnouncement = "";

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
                    var self = _self;
                    if (self != null && self.Active) self.PollState();
                });
                API.LogInfo($"[SF6Access] PadIcon.{name} hook installed");
            }
        }
        catch (Exception ex)
        {
            API.LogInfo($"[SF6Access] PadIcon hook failed: {ex.Message}");
        }
    }

    protected override bool Locate()
    {
        var (p1, p2) = FindParams();
        if (p1 == null && p2 == null)
        {
            _p1Param = null;
            _p2Param = null;
            return false;
        }

        // Re-bind when the game recreated the Params (stale instances read
        // dead memory → side select goes silent on re-entry)
        if (FlowHelper.AddressOf(p1) != FlowHelper.AddressOf(_p1Param) ||
            FlowHelper.AddressOf(p2) != FlowHelper.AddressOf(_p2Param))
            Bind(p1, p2);
        return true;
    }

    protected override void OnDeactivate()
    {
        API.LogInfo("[SF6Access] SideSelect ended");
        _p1Param = null;
        _p2Param = null;
        _lastPadState = "";
        _lastAnnouncement = "";
    }

    protected override void OnPoll() => PollState();

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

    private void Bind(ManagedObject p1, ManagedObject p2)
    {
        _p1Param = p1;
        _p2Param = p2;
        _lastPadState = "";
        _lastAnnouncement = "";
        API.LogInfo($"[SF6Access] SideSelect found (P1={p1 != null}, P2={p2 != null})");
        PollState();
    }

    private void PollState()
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
        Speak(announcement);
    }

    private string ReadPadCtrlFromAny()
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
}
