using System;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Announces the control-type display toggle (Classic / Modern / Dynamic) on the
/// Fighting Ground guide list screens. The game cycles it with L2/R2 (or Z/C on
/// keyboard) and only plays a sound.
///
/// Two distinct mechanisms exist:
/// - Tutorials (app.esports.UI11410.Param): a TabInputType tab strip whose tabs
///   render as images, so there is no readable label — read the
///   CurrentSelectTabInputType enum and speak a localized name. The change fires
///   EventInputTypeChanged() / UpdateInputTypeChanged().
/// - Character Guides (UI11413) and Combo Trials (UI11414): a TextControlType
///   via.gui.Text label refreshed by UpdateControlType() — read that label.
/// </summary>
public class TutorialControlTypeHooks
{
    // UI11410.TabInputType: Classic=0, Modern=1, Dynamic=2.
    private static readonly string[][] ControlTypeNames =
    {
        new[] { "Classic", "Modern", "Dynamic" },   // En
        new[] { "Clásico", "Moderno", "Dinámico" }, // Es
        new[] { "Clássico", "Moderno", "Dinâmico" }, // Pt
    };

    // Screens that expose a TextControlType label refreshed by UpdateControlType().
    private static readonly string[] LabelParamTypes =
    {
        "app.esports.UI11413.Param",
        "app.esports.UI11414.Param",
    };

    private const string TutorialParamType = "app.esports.UI11410.Param";

    private static ulong _pendingThis;
    private static ulong _lastInstance;
    private static string _lastAnnounced;

    [PluginEntryPoint]
    public static void Initialize()
    {
        // Tutorials screen: TabInputType enum, no readable label.
        var tutTd = TDB.Get().FindType(TutorialParamType);
        foreach (var methodName in new[] { "EventInputTypeChanged", "UpdateInputTypeChanged" })
        {
            var method = tutTd?.GetMethod(methodName);
            if (method == null) continue;
            var hook = method.AddHook(false);
            hook.AddPre(args => { _pendingThis = args[1]; return PreHookResult.Continue; });
            hook.AddPost((ref ulong retval) => AnnounceTabInputType(_pendingThis));
            API.LogInfo($"[SF6Access] {TutorialParamType}.{methodName} hook installed");
        }

        // Character Guides / Combo Trials: TextControlType label.
        foreach (var typeName in LabelParamTypes)
        {
            var method = TDB.Get().FindType(typeName)?.GetMethod("UpdateControlType");
            if (method == null)
            {
                API.LogError($"[SF6Access] {typeName}.UpdateControlType not found");
                continue;
            }
            var hook = method.AddHook(false);
            hook.AddPre(args => { _pendingThis = args[1]; return PreHookResult.Continue; });
            hook.AddPost((ref ulong retval) => AnnounceLabel(_pendingThis));
            API.LogInfo($"[SF6Access] {typeName}.UpdateControlType hook installed");
        }
    }

    /// <summary>Tutorials (UI11410): read the CurrentSelectTabInputType enum.</summary>
    private static void AnnounceTabInputType(ulong thisAddr)
    {
        if (thisAddr == 0) return;
        try
        {
            var param = ManagedObject.ToManagedObject(thisAddr);
            if (param == null) return;

            int value = FlowHelper.ReadByteField(param, "CurrentSelectTabInputType", -1);
            if (value < 0 || value > 2) return;

            var names = ControlTypeNames[(int)FlowHelper.GetDisplayLang()];
            Announce(thisAddr, names[value]);
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] Tutorial control type read error: {ex.Message}");
        }
    }

    /// <summary>Character Guides / Combo Trials: read the TextControlType label.</summary>
    private static void AnnounceLabel(ulong thisAddr)
    {
        if (thisAddr == 0) return;
        try
        {
            var param = ManagedObject.ToManagedObject(thisAddr);
            if (param == null) return;

            string text = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(param, "TextControlType"));
            if (string.IsNullOrWhiteSpace(text)) return;
            Announce(thisAddr, text.Replace('\n', ' ').Trim());
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] Control type label read error: {ex.Message}");
        }
    }

    private static void Announce(ulong thisAddr, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // A new screen instance: let the current type be announced again
        // (re-entry should still report which display mode is active).
        if (thisAddr != _lastInstance)
        {
            _lastInstance = thisAddr;
            _lastAnnounced = null;
        }

        if (text == _lastAnnounced) return;
        _lastAnnounced = text;

        API.LogInfo($"[SF6Access] Control type display: {text}");
        // Queue after the focused item read; the toggle does not move focus,
        // so on a deliberate L2/R2 press nothing competes and it speaks promptly.
        ScreenReaderService.Speak(text, interrupt: false);
    }
}
