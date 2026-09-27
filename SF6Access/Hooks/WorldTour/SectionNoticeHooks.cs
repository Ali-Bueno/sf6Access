using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// Catches the moment the game itself announces a city section (the on-screen banner
/// "Hong Hu Lu Chinatown - Plaza") and hands it to <see cref="ZoneHooks"/>, which is the
/// only speaker for zones. The banner component is not reachable through the scene
/// (never found in game), so the requests are hooked instead; the banner's own text is
/// read on the following frames, the section id is kept as the fallback.
/// </summary>
public class SectionNoticeHooks
{
    private const string MANAGER = "app.worldtour.CitySectionManager";
    private const string HUD = "app.UICityHud_SectionNotice";
    private const string REQUEST = "RequestSectionNotice";
    private const string SETUP = "SetupNotice";

    private const string F_TEXT = "mTextSection";
    private const string F_SECTION_ID = "SectionId";
    private const string F_REQUEST_SECTION = "SectionInfo";

    /// <summary>How long the banner text is waited for after a request, in frames (about a second).</summary>
    private const int TEXT_WAIT_FRAMES = 60;

    /// <summary>Frames between banner reads while waiting — each read creates engine wrappers.</summary>
    private const int TEXT_READ_EVERY = 6;

    private static uint _requestedId;
    private static ManagedObject _hud;
    private static string _staleText;
    private static int _waitFrames;
    private static int _hookCount;
    private static bool _firedLogged;

    [PluginEntryPoint]
    public static void Initialize()
    {
        Hook(MANAGER, REQUEST, args => Requested(args, infoArg: 2, hud: false));
        Hook(HUD, REQUEST, args => Requested(args, infoArg: 2, hud: true));
        Hook(HUD, SETUP, args => SetupRequested(args));
        API.LogInfo($"[SF6Access] SectionNoticeHooks initialized ({_hookCount} of 3 section notice hooks)");
    }

    private static void Hook(string type, string method, System.Action<System.Span<ulong>> onCall)
    {
        var m = TDB.Get().FindType(type)?.GetMethod(method);
        if (m == null)
        {
            API.LogWarning($"[SF6Access] SectionNoticeHooks: {type}.{method} not found");
            return;
        }
        m.AddHook(false).AddPre(args => { try { onCall(args); } catch { } return PreHookResult.Continue; });
        _hookCount++;
    }

    /// <summary>args[1] is `this`, args[infoArg] the <c>CitySectionInfo</c>.</summary>
    private static void Requested(System.Span<ulong> args, int infoArg, bool hud)
    {
        if (args.Length <= infoArg) return;
        var info = ManagedObject.ToManagedObject(args[infoArg]);
        uint id = FlowHelper.ReadUIntField(info, F_SECTION_ID);
        if (hud) Watch(ManagedObject.ToManagedObject(args[1]), id);
        else Request(id);
        LogFirst($"{(hud ? HUD : MANAGER)}.{REQUEST} section {id}");
    }

    private static void SetupRequested(System.Span<ulong> args)
    {
        if (args.Length < 3) return;
        var request = ManagedObject.ToManagedObject(args[2]);
        var info = FlowHelper.GetObjectField(request, F_REQUEST_SECTION);
        uint id = FlowHelper.ReadUIntField(info, F_SECTION_ID);
        Watch(ManagedObject.ToManagedObject(args[1]), id);
        LogFirst($"{HUD}.{SETUP} section {id}");
    }

    /// <summary>A new section replaces one still waiting (the player has already left it),
    /// and whatever the banner shows at that moment belongs to the section left.</summary>
    private static void Request(uint id)
    {
        if (id == 0 || id == _requestedId) return;
        _requestedId = id;
        if (_hud != null) _staleText = BannerText(_hud);
    }

    /// <summary>Waits for the banner to show a text it did not show when the request came.</summary>
    private static void Watch(ManagedObject hud, uint id)
    {
        if (hud != null)
        {
            if (_hud == null) _staleText = BannerText(hud);
            _hud = hud;
            _waitFrames = TEXT_WAIT_FRAMES;
        }
        Request(id);
    }

    private static string BannerText(ManagedObject hud)
    {
        try { return FlowHelper.CleanTags(FlowHelper.ReadGuiText(FlowHelper.GetObjectField(hud, F_TEXT)))?.Trim(); }
        catch { return null; }
    }

    private static void LogFirst(string what)
    {
        if (_firedLogged) return;
        _firedLogged = true;
        API.LogInfo($"[SF6Access] Section notice hook fired: {what}");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        if (_requestedId == 0 && _hud == null) return;

        // The banner's text control goes with the field (battle, loading): never read it then.
        FieldPresenceService.Refresh();
        if (!FieldPresenceService.InField || FieldPresenceService.Fighting)
        {
            _hud = null;
            _requestedId = 0;
            return;
        }

        string text = null;
        if (_hud != null)
        {
            if (--_waitFrames % TEXT_READ_EVERY != 0 && _waitFrames > 0) return;
            text = BannerText(_hud);
            if (text == _staleText) text = null;
            // Keep waiting while the banner is still being set up, then let the object go.
            if (string.IsNullOrEmpty(text) && _waitFrames > 0) return;
            _hud = null;
        }

        uint id = _requestedId;
        _requestedId = 0;
        if (string.IsNullOrEmpty(text)) text = ZoneNameService.SectionName(id);
        if (string.IsNullOrEmpty(text)) return;

        API.LogInfo($"[SF6Access] Section notice: '{text}' (section {id})");
        ZoneHooks.OnSectionNotice(id, text);
    }
}
