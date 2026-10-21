using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>Tap R3/V to choose a spoken target or cancel active guidance; hold it to open destinations.</summary>
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

    /// <summary>R3/V tap = choose or cancel; hold = the destination picker.</summary>
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
        API.LogInfo("[SF6Access] MapAimHooks initialized (tap R3 / V = select or cancel destination; hold = destination picker; Shift+V = route guidance on/off)");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();
        if (!FieldPresenceService.CanSpeak || SF6Access.Hooks.SpTalkNovelHooks.DialogueActive || PadGuideHooks.Active)
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
        // A city can still be unknown on the first playable frame after loading.
        ForgetOtherCity();
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
        else if (gesture == HoldTapDetector.Gesture.Tap) HandleTap();
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
            bool followingMission = !MapSelectionMemory.Exploring && !MapSelectionMemory.Current.Ok;
            MapSelectionMemory.Reset("another city");
            if (followingMission) MapSelectionMemory.FollowMission();
            CameraAimService.Reset();
            LevelRoute.Reset();
            BeaconDestination.Reset();
            FieldNavPath.Reset();
            AccessPointReader.Reset();
            ApproachFinder.Reset();
            PlaceCache.Reset();
            FieldAccessCatalog.Reset();
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

    /// <summary>Cancellation wins over selection for every active destination, including the tracked mission.</summary>
    private static void HandleTap()
    {
        // Cancellation requires no native target read and must not consume a new candidate.
        var selected = MapSelectionMemory.Current;
        bool followingMission = !selected.Ok && !MapSelectionMemory.Exploring;
        bool hasGuidance = selected.Ok || followingMission;
        string reason = selected.Ok ? "selected-destination" : followingMission ? "tracked-mission" : "no-spoken-candidate";
        Destination place = default;
        bool candidate = !hasGuidance && FieldFeedback.CanExplore
            && FieldPlaceSweepHooks.TryTakeCandidate(out place, out reason);
        var action = LookRetargetPolicy.Decide(selected.Ok, followingMission, candidate);
        // One diagnostic per deliberate press, so a future failed selection can distinguish
        // protected modes, no spoken candidate, live identity failures and actual look-away.
        API.LogInfo($"[SF6Access] R3 {action}: selected='{selected.Label}', mission={followingMission}, lost={selected.Lost}, pending={selected.Pending}, candidate='{place.Label}', {reason}");
        if (action == LookTapAction.Cancel)
        {
            MapSelectionMemory.Explore();
            CameraAimService.Release();
            MissionBeaconHooks.Cancel();
            FieldAimHooks.RearmAfterCancel();
            ScreenReaderService.Speak(DestinationText.BeaconCancelled(), interrupt: true);
            return;
        }
        if (action == LookTapAction.Choose)
        {
            if (place.Action == DestinationAction.FollowMission) MapSelectionMemory.FollowMission();
            else MapSelectionMemory.Remember(place);
        }
        Aim();
    }

    private static void Aim()
    {
        var mgr = WorldTourStateService.GetAvatarManager();
        var me = AvatarFieldReader.ReadPlayerPos(mgr);
        if (!me.ok) return;

        // A newly chosen target starts aimed at its checked walking leg.
        var dest = BeaconDestination.Resolve();
        if (!dest.Ok)
        {
            ScreenReaderService.Speak(MapSelectionMemory.Exploring
                ? DestinationText.ExploreNoSelection() : LocalizedText.AimNoSelection(), interrupt: true);
            return;
        }
        if (dest.TargetLost)
        {
            ScreenReaderService.Speak(RouteNotice.Text(RouteNotice.State(dest, false), null, dest, 0f, me.y), interrupt: true);
            return;
        }
        if (dest.Holding)
        {
            ScreenReaderService.Speak(DestinationText.RouteRefreshing(), interrupt: true);
            return;
        }
        if (dest.Unconfirmed)
        {
            ScreenReaderService.Speak(DestinationText.RouteUnavailable(), interrupt: true);
            return;
        }
        if (dest.Searching)
        {
            ScreenReaderService.Speak(RouteNotice.SearchingNow(), interrupt: true);
            return;
        }

        float tx = dest.TargetX - me.x, ty = dest.TargetY - me.y, tz = dest.TargetZ - me.z;
        float straight = (float)System.Math.Sqrt(tx * tx + ty * ty + tz * tz);

        // How far there is to go: the WALKED length when the route measured one,
        // because that is the number that predicts the walk.
        int meters = (int)System.Math.Round(dest.RouteM > 0f ? dest.RouteM : straight);

        // No walkable route: still turn toward it (a bearing is better than nothing)
        // but SAY that this is a direction, not a way. Marching a blind player at a
        // rooftop's foundations is exactly what this feature must never do.
        if (dest.NoRoute || FieldNavPath.Enabled && !dest.ViaRoute)
        {
            bool aimed = CameraAimService.AimAt(me.x, me.z, dest.X, dest.Z);
            string status = dest.NoRoute ? LocalizedText.AimNoRoute(dest.Word, meters)
                : DestinationText.RouteUnconfirmed(dest.Word, meters);
            if (!aimed)
            {
                int bearing = FieldDirectionService.ClockHour(FieldDirectionService.GetCameraForward(),
                    dest.X - me.x, dest.Z - me.z);
                if (bearing > 0) status += ". " + LocalizedText.ClockShort(bearing, meters);
            }
            ScreenReaderService.Speak(status, interrupt: true);
            API.LogInfo($"[SF6Access] Unconfirmed walking route to '{dest.Word}' ({meters} m); direction only");
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
