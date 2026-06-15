using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Announces the arcade bonus stage (car crush) results. The result panel
/// app.UI75520.Param shows Time / Score / Clear / Total as UIPartsTextureNumber —
/// numbers drawn as textures with no readable value field. The values are
/// captured by hooking SetTextureNums(int) (the game sets each part's number that
/// way) into an address→value map, then matched against the panel's four named
/// parts once the results appear. A short delay lets the count-up settle on the
/// final figures before announcing.
/// </summary>
public class BonusResultHooks
{
    private const string RESULT_PARAM = "app.UI75520.Param";

    private static int _pollCounter;
    private const int POLL_INTERVAL = 10;
    private const int RESULT_DELAY = 90; // let the count-up settle

    private static bool _announced;
    private static int _seenFrame = -1;

    // UIPartsTextureNumber instance address -> last value the game set into it
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

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;
        if (_pollCounter % POLL_INTERVAL != 0) return;

        try
        {
            var param = FlowHelper.FindFlowParam(RESULT_PARAM);
            if (param == null)
            {
                _announced = false;
                _seenFrame = -1;
                return;
            }
            if (_announced) return;

            int total = LookupPart(param, "PartsTextureNumberTotalScore", out bool haveTotal);
            if (!haveTotal) return; // values not set into the parts yet

            if (_seenFrame < 0) { _seenFrame = _pollCounter; return; }
            if (_pollCounter - _seenFrame < RESULT_DELAY) return;

            // Re-read after the delay so we get the settled final figures.
            total = LookupPart(param, "PartsTextureNumberTotalScore", out bool _ht);
            int time = LookupPart(param, "PartsTextureNumberTime", out bool haveTime);
            int score = LookupPart(param, "PartsTextureNumberScore", out bool haveScore);
            int clear = LookupPart(param, "PartsTextureNumberClear", out bool haveClear);

            _announced = true;
            var parts = new List<string>();
            if (haveScore) parts.Add($"Score {score}");
            if (haveTime) parts.Add($"Time bonus {time}");
            if (haveClear) parts.Add($"Clear bonus {clear}");
            parts.Add($"Total {total}");

            string text = string.Join(". ", parts) + ".";
            API.LogInfo($"[SF6Access] Bonus result: {text}");
            ScreenReaderService.Speak(text, interrupt: false);
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
