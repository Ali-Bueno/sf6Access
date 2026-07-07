using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Announces the arcade bonus stage (car crush) results. The result panel
/// app.UI75520.Param shows Time / Score / Clear / Total as UIPartsTextureNumber —
/// numbers drawn as textures with no readable value field. The values are
/// captured by hooking SetTextureNums(int) (the game sets each part's number that
/// way) into an address→value map, then matched against the panel's four named
/// parts once the results appear. A short delay lets the count-up settle on the
/// final figures before announcing.
///
/// SingleParamScreenAdapter for the poll; the SetTextureNums capture hooks stay
/// in the static [PluginEntryPoint]. Registered in ScreenRegistry.
/// </summary>
public sealed class BonusResultHooks : SingleParamScreenAdapter
{
    private const string RESULT_PARAM = "app.UI75520.Param";
    protected override string ParamType => RESULT_PARAM;

    // 9 read ticks at the 10-frame interval = the original 90-frame delay
    // (lets the count-up settle on the final figures).
    private const int RESULT_DELAY_TICKS = 9;

    public BonusResultHooks()
    {
        SearchInterval = 10;
        ReadInterval = 10;
    }

    private int _tick;
    private bool _announced;
    private int _seenTick = -1;

    // UIPartsTextureNumber instance address -> last value the game set into it.
    // Static: the capture hooks run regardless of the adapter's state.
    private static readonly Dictionary<ulong, int> _values = new();

    [PluginEntryPoint]
    public static void Initialize()
    {
        InstallHook("SetTextureNums(System.Int32)");
        InstallHook("SetTextureNums(System.Int32, System.Int32)");
        API.LogInfo("[SF6Access] BonusResultHooks initialized");
    }

    private static void InstallHook(string signature)
    {
        try
        {
            var td = TDB.Get().FindType("app.UIPartsTextureNumber");
            var m = td?.GetMethod(signature);
            if (m == null) { API.LogInfo($"[SF6Access] {signature} not found"); return; }

            var hook = m.AddHook(false);
            hook.AddPre(args =>
            {
                try
                {
                    _values[args[1]] = (int)(long)args[2]; // args[1]=this, args[2]=val
                    if (_values.Count > 256) _values.Clear();
                }
                catch { }
                return PreHookResult.Continue;
            });
            API.LogInfo($"[SF6Access] UIPartsTextureNumber.{signature} hook installed");
        }
        catch (System.Exception ex)
        {
            API.LogError($"[SF6Access] Bonus result hook failed: {ex.Message}");
        }
    }

    protected override void OnBind()
    {
        _announced = false;
        _seenTick = -1;
    }

    protected override void OnExit()
    {
        _announced = false;
        _seenTick = -1;
    }

    protected override void Poll()
    {
        _tick++;
        try
        {
            if (_announced) return;

            int total = LookupPart(Param, "PartsTextureNumberTotalScore", out bool haveTotal);
            if (!haveTotal) return; // values not set into the parts yet

            if (_seenTick < 0) { _seenTick = _tick; return; }
            if (_tick - _seenTick < RESULT_DELAY_TICKS) return;

            // Re-read after the delay so we get the settled final figures.
            total = LookupPart(Param, "PartsTextureNumberTotalScore", out bool haveTotalFinal);
            int time = LookupPart(Param, "PartsTextureNumberTime", out bool haveTime);
            int score = LookupPart(Param, "PartsTextureNumberScore", out bool haveScore);
            int clear = LookupPart(Param, "PartsTextureNumberClear", out bool haveClear);

            _announced = true;
            var parts = new List<string>();
            if (haveScore) parts.Add($"Score {score}");
            if (haveTime) parts.Add($"Time bonus {time}");
            if (haveClear) parts.Add($"Clear bonus {clear}");
            parts.Add($"Total {total}");

            string text = string.Join(". ", parts) + ".";
            API.LogInfo($"[SF6Access] Bonus result: {text}");
            Speak(text, interrupt: false);
        }
        catch { }
    }

    private static int LookupPart(ManagedObject param, string field, out bool found)
    {
        found = false;
        var part = FlowHelper.GetObjectField(param, field);
        if (part == null) return 0;
        try
        {
            if (_values.TryGetValue(part.GetAddress(), out int v)) { found = true; return v; }
        }
        catch { }
        return 0;
    }
}
