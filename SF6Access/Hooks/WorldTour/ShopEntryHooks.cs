using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// Says which shop you just walked into, and which one you just left.
///
/// <para><b>Why this matters more than it sounds.</b> Walking into a building
/// without realising it is one of the worst disorientations there is: the
/// soundscape changes, the street is gone, and nothing says what happened. A
/// sighted player gets the answer instantly from the screen.</para>
///
/// <para><b>The game announces it, we do not infer it.</b>
/// <c>app.worldtour.om.Om008000</c> — the shop OM — calls
/// <c>CallShopInSoundTrigger()</c> and <c>CallShopOutSoundTrigger()</c>
/// (<c>Om008000.cs:241/:244</c>) at exactly the moments it considers the player to
/// have gone in and come out. Those are the game's own thresholds, so the
/// announcement can never disagree with what the game thinks happened. There is no
/// <c>IsInside</c> or <c>CurrentShop</c> member anywhere to poll — searched — so
/// hooking these is not merely the cheapest route, it is the only honest one.</para>
///
/// <para><b>Which shop</b> comes from the instance that fired: its access target's
/// <c>GetDispName()</c>, the same call <see cref="AvatarFieldReader"/> has always
/// used to name NPCs and objects, resolved through the same tag cleaner. It is
/// already localized.</para>
///
/// <para><b>The hook resolves the NAME and then lets the engine object go.</b> A
/// string survives to the next frame safely; a <c>ManagedObject</c> kept across
/// frames is the finalizer-on-the-GC-thread crash this mod has paid for twice. The
/// speaking itself happens on the ordinary tick, not inside the hook.</para>
/// </summary>
public class ShopEntryHooks
{
    private const string SHOP_OM = "app.worldtour.om.Om008000";
    private const string ENTER = "CallShopInSoundTrigger";
    private const string LEAVE = "CallShopOutSoundTrigger";

    /// <summary>The member the OM publishes its interaction target on
    /// (<c>WTCityOM.cs:418</c>); the component walk is the fallback, mirroring
    /// <see cref="AvatarFieldReader"/>.</summary>
    private const string F_ACCESS_TARGET = "AccessTarget";

    private static string _pendingName;
    private static bool _pendingLeaving;
    private static bool _hooked;

    [PluginEntryPoint]
    public static void Initialize()
    {
        var td = TDB.Get().FindType(SHOP_OM);
        var enter = td?.GetMethod(ENTER);
        var leave = td?.GetMethod(LEAVE);
        if (enter == null && leave == null)
        {
            API.LogWarning($"[SF6Access] ShopEntryHooks: neither {ENTER} nor {LEAVE} found on {SHOP_OM}; " +
                           "shop entry will not be announced");
            return;
        }

        enter?.AddHook(false).AddPre(args => { Remember(args, leaving: false); return PreHookResult.Continue; });
        leave?.AddHook(false).AddPre(args => { Remember(args, leaving: true); return PreHookResult.Continue; });
        _hooked = true;
        API.LogInfo($"[SF6Access] ShopEntryHooks initialized (enter={enter != null}, leave={leave != null})");
    }

    /// <summary>Whether the enter/exit triggers were reachable at all.</summary>
    internal static bool Available => _hooked;

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        if (_pendingName == null) return;

        string name = _pendingName;
        bool leaving = _pendingLeaving;
        _pendingName = null;

        FieldPresenceService.Refresh();
        if (!FieldPresenceService.CanSpeak) return;
        // Never over the game's own voice; a doorway is not worth cutting a line off.
        if (SF6Access.Hooks.SpTalkNovelHooks.DialogueActive) return;

        ScreenReaderService.Speak(
            leaving ? LocalizedText.ShopLeaving(name) : LocalizedText.ShopEntering(name),
            interrupt: false);
        API.LogInfo($"[SF6Access] Shop {(leaving ? "exit" : "entry")}: '{name}'");
    }

    /// <summary>Read the shop's name out of the instance that fired, and keep only
    /// the string. Runs inside the hook, so it does the least it can get away with
    /// and never throws out of it.</summary>
    private static void Remember(System.Span<ulong> args, bool leaving)
    {
        try
        {
            // args[1] is `this` — the established shape for a dynamic hook in this
            // mod (see BattleInfoHooks).
            if (args.Length < 2) return;
            var om = ManagedObject.ToManagedObject(args[1]);
            if (om == null) return;

            string name = NameOf(om);
            if (string.IsNullOrEmpty(name)) return;

            _pendingName = name;
            _pendingLeaving = leaving;
        }
        catch { }
    }

    /// <summary>The shop's localized display name, from its access target — the
    /// member first, then a walk of the GameObject's components for a sibling whose
    /// type name contains <c>AccessTarget</c>, which is exactly how
    /// <see cref="AvatarFieldReader"/> names everything else in the field.</summary>
    private static string NameOf(ManagedObject om)
    {
        string name = DispName(FieldProbeService.Member(om, F_ACCESS_TARGET) as ManagedObject);
        if (!string.IsNullOrEmpty(name)) return name;

        var go = FlowHelper.Call(om, "get_GameObject") as ManagedObject;
        var components = go == null ? null : FlowHelper.Call(go, "get_Components") as ManagedObject;
        int n = FlowHelper.GetListCount(components);
        for (int i = 0; i < n; i++)
        {
            var c = FlowHelper.GetListItem(components, i);
            string t = null;
            try { t = c?.GetTypeDefinition()?.GetFullName(); } catch { }
            if (t == null || !t.Contains("AccessTarget")) continue;

            name = DispName(c);
            if (!string.IsNullOrEmpty(name)) return name;
        }
        return null;
    }

    private static string DispName(ManagedObject accessTarget)
    {
        if (accessTarget == null) return null;
        try { return FlowHelper.CleanTags(FlowHelper.Call(accessTarget, "GetDispName") as string)?.Trim(); }
        catch { return null; }
    }
}
