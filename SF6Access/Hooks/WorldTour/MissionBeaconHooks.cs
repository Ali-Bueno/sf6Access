using System;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>Route steering by sound; speech reports contact, traversal needs and route availability.</summary>
public class MissionBeaconHooks
{
    internal const float ARRIVED_M = 4f; // Existing proximity band; does not confirm interaction.
    private const long ResolveMs = FieldNavPath.MinQueryIntervalMs;
    // Let a rate-limited query recover, but do not keep pointing at an old corner on a ladder.
    private const long FixLifetimeMs = 4 * ResolveMs;
    private const int CueNearGapMs = 100, CueFarGapMs = 500; // Existing steering cadence, retained.
    private static readonly HomingCue Cue = new("mission beacon.mp3", ARRIVED_M, 60f, CueNearGapMs, CueFarGapMs);
    private static readonly RouteQuerySchedule Schedule = new(ResolveMs);
    private static long _fixAt;
    private static string _key, _lastState, _spokenState, _pendingStatus;
    private static bool _haveFix, _atContact, _contactNow, _ladderOffered, _active;
    private static BeaconDestination _fix;

    internal static bool Homing => _haveFix;
    internal static bool Guiding { get; private set; }
    internal static bool NeedsObstacleDiscovery => Guiding && !_fix.Searching &&
        (_fix.NoRoute || _fix.Blocked || _fix.AtFoot || _fix.Closest);

    [PluginEntryPoint]
    public static void Initialize() => API.LogInfo("[SF6Access] MissionBeaconHooks initialized (quiet route guidance)");

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();
        if (!FieldFeedback.CanOutput)
        {
            if (_active) Reset();
            _active = false;
            return;
        }
        _active = true;
        long now = Environment.TickCount64;
        // Navigation samples never wait for the reader: speech must not freeze the steering target.
        if (Schedule.Due(now))
        {
            bool waiting = Resolve(now);
            Schedule.Resolved(now, waiting, FieldNavPath.NextQueryTick, LevelRoute.BackgroundSearch);
        }
        if (Guiding && Schedule.MayPump) LevelRoute.Pump();
        if (now - _fixAt > FixLifetimeMs) Hush();
        Ping();
    }

    private static bool Resolve(long now)
    {
        var dest = BeaconDestination.Resolve();
        if (!dest.Ok)
        {
            if (Guiding || _key != null) Reset();
            return false;
        }
        Guiding = true;
        var player = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());
        if (!player.ok) { Hush(); return dest.WaitingForQuery; }
        if (dest.Key != _key)
        {
            Hush(); ForgetWords();
            _key = dest.Key; _lastState = _spokenState = _pendingStatus = null;
            _atContact = _ladderOffered = false;
            _fix = default;
        }
        if (dest.Holding) return dest.WaitingForQuery;
        _fix = dest; _fixAt = dest.SampledAt;
        float distance = MathF.Sqrt(FieldAccessCatalog.DistanceSq(dest.TargetX, dest.TargetY, dest.TargetZ,
            player.x, player.y, player.z));
        bool contact = dest.InContact || (!dest.IsTracked && !dest.IsApproach && !dest.Ladder && FieldActionReader.AtMissionContact());
        _contactNow = contact;
        if (contact)
        {
            Hush();
            FieldFeedback.Forget("route-state");
            if (!_atContact)
                FieldFeedback.Say("route-contact", dest.IsTracked
                    ? string.Format(ExplorationText.Get("destination_contact", "{0} within interaction range"), dest.Word)
                    : ExplorationText.Get("mission_contact", "Objective within interaction range"),
                    FieldSpeechPriority.Interaction, onSpoken: () => _atContact = true, stillValid: () => _contactNow);
            return dest.WaitingForQuery;
        }
        FieldFeedback.Forget("route-contact");
        _atContact = false;
        _haveFix = !dest.Searching && !dest.Unconfirmed;
        bool near = distance <= (RouteNotice.IsNear(_lastState) ? 2 * ARRIVED_M : ARRIVED_M);
        string state = RouteNotice.State(dest, near);
        if (state != _lastState)
        {
            string previous = _lastState;
            _lastState = state;
            _spokenState = null;
            FieldFeedback.Forget("route-state");
            _pendingStatus = RouteNotice.Text(state, previous, dest, distance, player.y);
            if (_pendingStatus != null) API.LogInfo("[SF6Access] Mission navigation: " + _pendingStatus);
        }
        // An explicit readout may defer this notice. Commit only when heard, and retry only while still true.
        if (_pendingStatus != null && _spokenState != state)
            FieldFeedback.Say("route-state", _pendingStatus, FieldSpeechPriority.Route,
                onSpoken: () => _spokenState = state, stillValid: () => _lastState == state && !_contactNow);
        if (dest.Searching) return dest.WaitingForQuery;
        AnnounceLadderGrip(dest, near, player.x, player.y, player.z);
        return dest.WaitingForQuery;
    }

    private static void AnnounceLadderGrip(in BeaconDestination dest, bool near, float x, float y, float z)
    {
        if (!dest.Ladder || !near)
        { _ladderOffered = false; FieldFeedback.Forget("route-ladder"); return; }
        LadderAccess.Diagnose(dest.LadderAccess, dest.BottomRungM, x, y, z);
        bool offered = LadderAccess.Offered(dest.LadderAccess);
        if (offered && !_ladderOffered)
            FieldFeedback.Say("route-ladder", RouteNotice.LadderOffered(), FieldSpeechPriority.Interaction,
                onSpoken: () => _ladderOffered = true,
                stillValid: () => _fix.Ladder && LadderAccess.Offered(_fix.LadderAccess));
        if (!offered) FieldFeedback.Forget("route-ladder");
        if (!offered) _ladderOffered = false;
    }

    private static void Ping()
    {
        if (!_haveFix || !Cue.Due) return;
        var player = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());
        if (!player.ok) return;
        float direct = MathF.Sqrt(FieldAccessCatalog.DistanceSq(_fix.TargetX, _fix.TargetY, _fix.TargetZ,
            player.x, player.y, player.z));
        Cue.Sound(FieldDirectionService.GetCameraForward(), _fix.X - player.x, _fix.Z - player.z,
            _fix.RouteM > 0 ? _fix.RouteM : direct, route: true);
    }

    private static void Hush() { _haveFix = false; Cue.Reset(); }
    internal static void Cancel() => Reset();
    private static void ForgetWords()
    {
        FieldFeedback.Forget("route-state");
        FieldFeedback.Forget("route-contact"); FieldFeedback.Forget("route-ladder");
    }
    private static void Reset()
    {
        Hush(); ForgetWords(); Guiding = false;
        _key = _lastState = _spokenState = _pendingStatus = null; _fix = default; Schedule.Reset();
        _atContact = _contactNow = _ladderOffered = false;
        BeaconDestination.Reset();
    }
}
