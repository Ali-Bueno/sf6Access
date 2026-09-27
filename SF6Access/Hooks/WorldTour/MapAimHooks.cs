using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// <b>R3 (or V): point me down the next leg of the route to where I am going.</b>
/// Holding it instead opens the destination picker (<see cref="DestinationPickerHooks"/>).
///
/// <para><b>Press, walk, press again.</b> Each press turns the camera down the NEXT
/// LEG OF THE ROUTE — not at the destination in a straight line — so the player
/// walks that leg, presses again at the corner, and repeats until they arrive.
/// That is the whole interaction, and it is why the aim comes from
/// <see cref="BeaconDestination"/> rather than from the raw target: that is the one
/// place that already knows both what to follow and which way the walkable route
/// actually leaves from here.</para>
///
/// <para><b>What it follows.</b> A point the player picked on the phone map if
/// there is one — a mission, a shop, a master, an NPC, a fast-travel point, their
/// own placed marker, all captured by <see cref="MapSelectionMemory"/> as the map
/// reader speaks them — and otherwise the STORY OBJECTIVE, so the key is useful
/// without opening the map at all. Reaching a picked point clears it, and the key
/// goes back to following the mission; without that the player could never hand the
/// beacon back short of leaving the city.</para>
///
/// <para><b>It replaced a modal radar</b> (2026-09-09). That radar built its own
/// browsable list from the HUD minimap, and the minimap turned out to be
/// unreachable in game — neither <c>app.UIMiniMapWindow</c> nor its owner
/// <c>app.UICityHud_CityMiniMap</c> appears in the scene, so the list was always
/// empty. The map the player already browses was the better selector all along:
/// no second list to learn, no buttons to steal from the game, and it demonstrably
/// works.</para>
///
/// <para><b>It always answers.</b> If the camera cannot be turned — see
/// <see cref="CameraAimService"/>, which refuses rather than guessing which euler
/// component is the yaw — the direction is SPOKEN as a clock hour instead. A press
/// never produces silence.</para>
/// </summary>
public class MapAimHooks
{
    /// <summary>Keyboard V, mirroring the pad button. Chosen when this was the
    /// tracker's key and kept, so muscle memory survives the redesign.</summary>
    private const int VK_V = 0x56;

    /// <summary>The pad button, by its engine name — <c>via.hid.GamePadButton</c>
    /// resolves it to a bit at runtime, so no hex is written into the mod.</summary>
    private const string BUTTON_ENUM = "via.hid.GamePadButton";
    private const string BTN_AIM = "RStickPush";   // R3

    private static ReadoutShortcut _shortcut;

    /// <summary>R3/V tap = aim (on release), hold = the destination picker.</summary>
    private static HoldTapDetector _gesture;

    /// <summary>Shift+V: the route pathfinder's kill switch. Keyboard only — there is
    /// no chord on a pad, and this is a rescue control, not a control the player is
    /// expected to reach for. It lives here because this hook owns V.</summary>
    private static readonly ReadoutShortcut RouteSwitch =
        new(VK_V, ReadoutShortcut.PAD_NONE, shift: true);

    private static bool _wasInField;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] MapAimHooks initialized (tap R3 / V = face the destination, hold = destination picker, Shift+V = route guidance on/off)");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();
        if (!FieldPresenceService.CanSpeak)
        {
            // A press held into a fight or menu must not aim when control comes back.
            _gesture?.Swallow();
            if (_wasInField)
            {
                _wasInField = false;
                // Calibration survives menus, the phone and fights (it used to be lost each time,
                // and R3 then only spoke until the player turned the camera again).
                CameraAimService.Release();
                FieldNavPath.Reset();
                AccessPointReader.Reset();
                ApproachFinder.Reset();
            }
            return;
        }
        if (!_wasInField) ForgetOtherCity();
        _wasInField = true;

        // Built on first use, not in a static initialiser: the button's bit has to
        // be read out of the TDB, which is not ready when the plugin loads.
        _shortcut ??= new ReadoutShortcut(VK_V, PadFlag());
        _gesture ??= new HoldTapDetector(ReadoutShortcut.SystemHoldDelayMs());

        // Both are polled every frame, even though only one can act: each keeps its
        // own edge state, and a skipped detector would invent a press later.
        bool route = RouteSwitch.Pressed();
        var gesture = _gesture.Update(_shortcut.Down(), System.Environment.TickCount64);
        // The picker owns R3/V while open; the press that closes it must not aim on release.
        if (DestinationPickerHooks.IsOpen) _gesture.Swallow();
        else if (route) ToggleRouteGuidance();
        else if (gesture == HoldTapDetector.Gesture.Tap) { TakeLookedAtPlace(); Aim(); }
        else if (gesture == HoldTapDetector.Gesture.Hold) DestinationPickerHooks.Open();

        // Holds the yaw for its brief window against the controller's own update.
        CameraAimService.Tick();
    }

    /// <summary>A city's coordinates are not the next city's: the destination is forgotten
    /// when control comes back in a different city. A menu, the phone or a street fight
    /// keeps it — the destination picker's choice has to survive them.</summary>
    private static void ForgetOtherCity()
    {
        uint city = WorldTourStateService.CityId;
        if (city == 0) return;
        if (_city != 0 && city != _city)
        {
            MapSelectionMemory.Reset("another city");
            CameraAimService.Reset();
        }
        _city = city;
    }

    private static uint _city;

    /// <summary>Turn route guidance off (or back on). With it on, the beacon aims at
    /// the next bend of a real walkable route instead of straight through the scenery;
    /// with it off, every beacon is the straight-line beacon it always was.</summary>
    private static void ToggleRouteGuidance()
    {
        FieldNavPath.Enabled = !FieldNavPath.Enabled;
        ScreenReaderService.Speak(
            FieldNavPath.Enabled ? LocalizedText.PathfinderOn() : LocalizedText.PathfinderOff(),
            interrupt: true);
        API.LogInfo($"[SF6Access] Route guidance {(FieldNavPath.Enabled ? "ON" : "off")} — " +
                    FieldNavPath.RouteDescription);
    }

    /// <summary>A place the look sweep has just named, still faced, becomes the destination
    /// (<see cref="FieldPlaceSweepHooks"/>): look, hear it, tap R3 — no menu.</summary>
    private static void TakeLookedAtPlace()
    {
        if (!FieldPlaceSweepHooks.TryTakeCandidate(out var place)) return;
        if (place.Action == DestinationAction.FollowMission)
            MapSelectionMemory.Reset("back to the story objective");
        else
            MapSelectionMemory.Remember(place.Label, place.X, place.Y, place.Z);
    }

    private static void Aim()
    {
        var mgr = WorldTourStateService.GetAvatarManager();
        var me = AvatarFieldReader.ReadPlayerPos(mgr);
        if (!me.ok) return;

        // The same decision the beacon makes every second: the map pick if there is
        // one, else the story objective, and — with route guidance on — an aim point
        // that is the next bend of a real walkable route rather than a bearing
        // through a building. Asking it here is what makes a press mean "point me
        // down the next leg", and what makes press-walk-press converge.
        var dest = BeaconDestination.Resolve();
        if (!dest.Ok)
        {
            ScreenReaderService.Speak(LocalizedText.AimNoSelection(), interrupt: true);
            return;
        }
        if (dest.Searching)
        {
            ScreenReaderService.Speak(RouteNotice.SearchingNow(), interrupt: true);
            return;
        }

        float tx = dest.TargetX - me.x, ty = dest.TargetY - me.y, tz = dest.TargetZ - me.z;
        float straight = (float)System.Math.Sqrt(tx * tx + ty * ty + tz * tz);

        // Arrived at a point the player chose: let it go, so the next press follows
        // the mission again. The story objective is never cleared — it is not ours
        // to finish.
        if (dest.IsTracked && !dest.Ladder && !dest.IsApproach && straight <= MissionBeaconHooks.ARRIVED_M)
        {
            MapSelectionMemory.Reset("arrived");
            ScreenReaderService.Speak(LocalizedText.TrackerArrived(dest.Word), interrupt: true);
            return;
        }

        // How far there is to go: the WALKED length when the route measured one,
        // because that is the number that predicts the walk.
        int meters = (int)System.Math.Round(dest.RouteM > 0f ? dest.RouteM : straight);

        // No walkable route: still turn toward it (a bearing is better than nothing)
        // but SAY that this is a direction, not a way. Marching a blind player at a
        // rooftop's foundations is exactly what this feature must never do.
        if (dest.NoRoute)
        {
            CameraAimService.AimAt(me.x, me.z, dest.X, dest.Z);
            ScreenReaderService.Speak(LocalizedText.AimNoRoute(dest.Word, meters), interrupt: true);
            API.LogInfo($"[SF6Access] No walkable route to '{dest.Word}' ({meters} m); said so");
            return;
        }

        if (CameraAimService.AimAt(me.x, me.z, dest.X, dest.Z))
        {
            // An approach is not the thing: say so, or the player arrives and wonders
            // why the objective is still twelve metres above them.
            ScreenReaderService.Speak(
                dest.IsApproach ? LocalizedText.AimApproach(dest.Word, meters)
                                : LocalizedText.AimTurned(dest.Word, meters),
                interrupt: true);
            API.LogInfo($"[SF6Access] Aimed at '{dest.Word}' ({meters} m" +
                        (dest.ViaRoute ? ", via the route's next bend" : ", straight line") +
                        $") using {CameraAimService.Status}");
            return;
        }

        // The camera would not turn. Say which way instead — the same clock-hour
        // vocabulary every other World Tour reader uses, and pointed at the same
        // leg of the route the turn would have used.
        int hour = FieldDirectionService.ClockHour(FieldDirectionService.GetCameraForward(),
                                                   dest.X - me.x, dest.Z - me.z);
        ScreenReaderService.Speak(
            hour > 0 ? LocalizedText.AimUnavailable(dest.Word, hour, meters)
                     : LocalizedText.AtMeters(dest.Word, meters),
            interrupt: true);
        API.LogInfo($"[SF6Access] Could not aim ({CameraAimService.Status}); spoke the bearing instead");
    }

    /// <summary>R3's bit, or 0 — which <see cref="ReadoutShortcut"/> reads as
    /// "keyboard only", so a missing enum member costs the pad binding and nothing
    /// else.</summary>
    private static uint PadFlag()
    {
        int value = FieldProbeService.EnumValue(BUTTON_ENUM, BTN_AIM);
        if (value <= 0)
        {
            API.LogWarning($"[SF6Access] MapAimHooks: {BUTTON_ENUM}.{BTN_AIM} not found; V only");
            return ReadoutShortcut.PAD_NONE;
        }
        API.LogInfo($"[SF6Access] MapAimHooks: R3 = {value:X}");
        return (uint)value;
    }
}
