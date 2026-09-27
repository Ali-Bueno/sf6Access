using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Keeps the field's controls from reaching the avatar while a mod menu owns them, with
/// the game's own lock: <c>app.InputManager.RequestMaskLowerAndEqualLayers(Action)</c>
/// (static, lasts one frame — so it fails safe). Confirmed working in game 2026-09-09;
/// see <c>docs/sf6-screens.md</c> § "Taking the field's input".
/// </summary>
public static class FieldInputMask
{
    private const string MANAGER = "app.InputManager";
    private const string METHOD = "RequestMaskLowerAndEqualLayers(app.InputLayerPriority)";
    private const string PRIORITY_ENUM = "app.InputLayerPriority";

    private static Method _mask;
    private static object[] _args;
    private static bool _resolved;

    /// <summary>Masks the field for the next frame; call every frame the menu is open.</summary>
    public static void Hold()
    {
        if (!_resolved) Resolve();
        if (_mask == null) return;
        try { _mask.InvokeBoxed(null, null, _args); }
        catch { }
    }

    private static void Resolve()
    {
        _resolved = true;
        int action = FieldProbeService.EnumValue(PRIORITY_ENUM, "Action");
        _mask = action < 0 ? null : TDB.Get().FindType(MANAGER)?.GetMethod(METHOD);
        _args = new object[] { action };
        API.LogInfo(_mask == null
            ? $"[SF6Access] Field input mask unavailable ({MANAGER}.{METHOD}, Action={action})"
            : $"[SF6Access] Field input mask bound ({MANAGER}.{METHOD}, Action={action})");
    }
}
