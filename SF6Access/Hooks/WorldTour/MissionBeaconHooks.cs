using System;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>Automatic route bearing, route-state notices and contact-based arrival.</summary>
public class MissionBeaconHooks
{
    internal const float ARRIVED_M = 4f; // Existing proximity band; no longer confirms interaction.
    private const long ReaderHoldMs = 1200;
    private const long ResolveMs = 500; // Two route refreshes/sec, independent of frame rate.
    private const long RepeatMs = 15000;
    private const long DirectionHoldMs = 4000;
    // A route is followed by ear, so even far away a turn must be heard within a step or two
    // (tester 2026-09-26: at 2.5 s it was "impossible to follow"). The cadence still speeds up near.
    private const int CueNearGapMs = 100, CueFarGapMs = 500;
    private static readonly HomingCue Cue = new("mission beacon.mp3", ARRIVED_M, 60f, CueNearGapMs, CueFarGapMs);
    private static long _nextResolve, _nextWords, _nextTurn;
    private static string _key, _lastState;
    private static int _lastHour;
    private static int _lastTurnPhase;
    private static float _lastMeters;
    private static bool _haveFix, _atContact, _ladderOffered;
    private static BeaconDestination _fix;
    internal static bool Homing => _haveFix;

    [PluginEntryPoint]
    public static void Initialize() => API.LogInfo("[SF6Access] MissionBeaconHooks initialized (automatic route status and contact arrival)");

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();
        if (!FieldPresenceService.CanSpeak) { Reset(); return; }
        if (PadGuideHooks.Active || SpTalkNovelHooks.DialogueActive) { Hush(); return; }
        LevelRoute.Pump();
        long now = Environment.TickCount64;
        // The cue is a short sound, not speech: it keeps playing under other readers' lines.
        Ping();
        if (now - ScreenReaderService.LastInterruptTick < ReaderHoldMs) return;
        if (now >= _nextResolve)
        {
            _nextResolve = now + ResolveMs;
            Resolve(now);
        }
    }

    private static void Resolve(long now)
    {
        var dest = BeaconDestination.Resolve();
        if (!dest.Ok) { Reset(); return; }
        var player = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());
        if (!player.ok) { Hush(); return; }
        if (dest.Key != _key)
        { _key = dest.Key; _lastState = null; _lastHour = 0; _lastTurnPhase = 0; _nextWords = _nextTurn = 0; _atContact = false; }
        if (dest.Holding) return;
        float distance = MathF.Sqrt(FieldAccessCatalog.DistanceSq(dest.TargetX, dest.TargetY, dest.TargetZ,
            player.x, player.y, player.z));
        bool contact = !dest.IsTracked && !dest.IsApproach && !dest.Ladder && FieldActionReader.AtMissionContact();
        if (contact)
        {
            Hush();
            if (!_atContact) ScreenReaderService.Speak(ExplorationText.Get("mission_contact", "Objective within interaction range"), interrupt: true);
            _atContact = true;
            return;
        }
        _atContact = false;
        _fix = dest; _haveFix = !dest.Searching;
        // Leaving a "near" state takes twice the distance, so standing at the edge does not repeat it.
        bool near = distance <= (RouteNotice.IsNear(_lastState) ? 2 * ARRIVED_M : ARRIVED_M);
        string state = RouteNotice.State(dest, near);
        bool stateChanged = state != _lastState;
        string previous = _lastState;
        _lastState = state;
        if (stateChanged)
        {
            string status = RouteNotice.Text(state, previous, dest, distance, player.y);
            if (status != null)
            {
                ScreenReaderService.Speak(status, interrupt: true);
                API.LogInfo("[SF6Access] Mission navigation: " + status);
                _nextWords = now + DirectionHoldMs;
                return;
            }
        }
        if (dest.Searching) return;
        if (AnnounceLadderGrip(dest, near, player.x, player.y, player.z)) { _nextWords = now + DirectionHoldMs; return; }
        var forward = FieldDirectionService.GetCameraForward();
        float toCorner = MathF.Sqrt(FieldAccessCatalog.DistanceSq(dest.X, player.y, dest.Z,
            player.x, player.y, player.z));
        bool turnNow = toCorner <= FieldRayCaster.CapsuleRadius();
        int turnPhase = dest.Turn * (turnNow ? 2 : 1);
        if (turnPhase != 0 && turnPhase != _lastTurnPhase
            && (now >= _nextTurn || (turnNow && Math.Abs(_lastTurnPhase) == 1)))
        {
            string side = dest.Turn > 0 ? ExplorationText.Get("right", "right") : ExplorationText.Get("left", "left");
            string instruction = turnNow
                ? string.Format(ExplorationText.Get("turn_now", "Turn {0} now"), side)
                : string.Format(ExplorationText.Get("turn_in", "Turn {0} in {1} meters"), side,
                    Math.Max(1, (int)MathF.Round(toCorner)));
            ScreenReaderService.Speak(instruction, interrupt: false);
            _lastTurnPhase = turnPhase; _nextWords = _nextTurn = now + DirectionHoldMs;
            return;
        }
        if (turnPhase == 0) _lastTurnPhase = 0;
        int hour = FieldDirectionService.ClockHour(forward, dest.X - player.x, dest.Z - player.z);
        float meters = dest.RouteM > 0 ? dest.RouteM : distance;
        bool changed = hour != _lastHour || meters < _lastMeters / 2 || meters > _lastMeters * 2;
        if (now < _nextWords || (!changed && now < _nextWords + RepeatMs - DirectionHoldMs)) return;
        string what = RouteNotice.Heading(dest);
        string text = hour > 0 ? LocalizedText.AtClockMeters(what, hour, (int)MathF.Round(meters))
                              : LocalizedText.AtMeters(what, (int)MathF.Round(meters));
        ScreenReaderService.Speak(text, interrupt: false);
        _lastHour = hour; _lastMeters = meters; _nextWords = now + DirectionHoldMs;
    }

    /// <summary>At a planned ladder: says once when the game starts offering it, and logs what the game offers.</summary>
    private static bool AnnounceLadderGrip(in BeaconDestination dest, bool near, float x, float y, float z)
    {
        if (!dest.Ladder || !near) { _ladderOffered = false; return false; }
        LadderAccess.Diagnose(dest.LadderAccess, dest.BottomRungM, x, y, z);
        bool offered = LadderAccess.Offered(dest.LadderAccess);
        bool fresh = offered && !_ladderOffered;
        _ladderOffered = offered;
        if (fresh) ScreenReaderService.Speak(RouteNotice.LadderOffered(), interrupt: true);
        return fresh;
    }

    private static void Ping()
    {
        if (!_haveFix || !Cue.Due) return;
        var player = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());
        if (!player.ok) return;
        float direct = MathF.Sqrt(FieldAccessCatalog.DistanceSq(_fix.TargetX, _fix.TargetY, _fix.TargetZ,
            player.x, player.y, player.z));
        // Continue to guide the final approach until contact, including inside the old 4 m band.
        Cue.Sound(FieldDirectionService.GetCameraForward(), _fix.X - player.x, _fix.Z - player.z,
            _fix.RouteM > 0 ? _fix.RouteM : direct);
    }

    private static void Hush() { _haveFix = false; Cue.Reset(); }
    private static void Reset()
    {
        Hush(); _key = _lastState = null; _nextWords = 0; _atContact = _ladderOffered = false; _lastTurnPhase = 0;
        BeaconDestination.Reset();
    }
}
