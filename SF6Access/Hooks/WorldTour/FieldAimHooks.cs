using System;
using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>A settled focus identifies one person or place, while standing or looking around on the move.</summary>
public class FieldAimHooks
{
    private const long PollMs = 100;
    private const long ResumeSettleMs = ScanDwell.DwellMs;
    private static readonly SonarFocus Focus = new(FieldDirectionService.AHEAD_DEG, FieldDirectionService.AHEAD_LEAVE_DEG);
    // A tenth of a clock sector prevents small corrections from flipping two distant names.
    private static readonly CityPlaceFocus CityFocus = new(FieldDirectionService.AHEAD_DEG,
        FieldDirectionService.AHEAD_LEAVE_DEG, FieldDirectionService.DEGREES_PER_HOUR / 10f);
    private static readonly ScanDwell Dwell = new();
    private readonly record struct Candidate(string Key, Destination Destination, float Meters);
    private static readonly List<Candidate> Candidates = new();
    private static long _nextPoll, _resumeAt;
    private static bool _paused = true;
    private static bool _cityListening;
    private static uint _city, _situation;
    private static string FocusKey => _cityListening ? CityFocus.Key : Focus.Key;

    [PluginEntryPoint]
    public static void Initialize() => API.LogInfo("[SF6Access] FieldAimHooks initialized (local people/places and optional citywide named listening)");

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();
        long now = Environment.TickCount64;
        if (!FieldFeedback.CanScan) { Pause(); return; }
        if (_paused) { _paused = false; _resumeAt = now + ResumeSettleMs; }
        if (now < _nextPoll) return;
        _nextPoll = now + PollMs;
        if (!WorldTourStateService.TryReadContext(out uint city, out uint situation))
        {
            // Do not repeatedly speak a candidate that cannot acquire a verified world
            // snapshot. An uncommitted dwell can retry once this read recovers.
            FieldFeedback.Forget("look");
            FieldPlaceSweepHooks.DiscardInvalid();
            return;
        }
        if (_cityListening != CityListeningMode.Active || _city != city || _situation != situation)
            ClearFocus();
        _cityListening = CityListeningMode.Active;
        _city = city; _situation = situation;
        var forward = FieldDirectionService.GetCameraForward();
        if (!forward.Ok)
        {
            FieldFeedback.Forget("look");
            FieldPlaceSweepHooks.DiscardInvalid();
            return;
        }
        long ignoreUntil = Math.Max(CameraAimService.OwnTurnUntil, _resumeAt);
        if (now < ignoreUntil) { ClearFocus(); return; }

        var me = FieldPresenceService.PlayerPos;
        Candidates.Clear();
        Focus.Begin();
        CityFocus.Begin();
        void Offer(string key, Destination destination, float meters)
        {
            var b = FieldDirectionService.GetBearing(forward, destination.X - me.X, destination.Z - me.Z);
            if (!b.Ok) return;
            float offDegrees = AxisLatch.OffDeg(b.Ahead, b.Right);
            if (_cityListening) CityFocus.Offer(key, offDegrees, meters);
            else Focus.Offer(key, offDegrees, meters);
            Candidates.Add(new(key, destination, meters));
        }

        if (!_cityListening)
        foreach (var other in AvatarFieldReader.ReadOthers(WorldTourStateService.GetAvatarManager()))
        {
            if (other.Dist > FieldBeaconHooks.HOME_RANGE_M) continue;
            string name = AvatarNameCache.NameOf(other);
            if (string.IsNullOrEmpty(name)) continue;
            var position = AvatarFieldReader.ReadWorldPos(other.Avatar);
            if (!position.ok) continue;
            // A copied point and name may outlive the poll; the engine wrapper may not.
            var destination = new Destination(name, null, DestinationCategory.People,
                position.x, position.y, position.z, Actor: LiveDestinationReader.ForAvatar(other));
            Offer("person:" + other.Avatar.GetAddress(), destination, other.Dist);
        }
        foreach (var place in PlaceCache.Places())
        {
            var d = place.Destination;
            // Access state is sampled live below rather than using the slower place snapshot.
            if (d.Category == DestinationCategory.Access || d.Action == DestinationAction.FollowMission) continue;
            float meters = d.FlatDistanceTo(me.X, me.Z);
            if (_cityListening ? !CityPlaceFocus.Includes(d) : meters > 4f * FieldBeaconHooks.HOME_RANGE_M) continue;
            if (d.Category == DestinationCategory.Masters && meters <= FieldBeaconHooks.HOME_RANGE_M) continue;
            Offer(place.Key, d, meters);
        }
        if (!_cityListening)
        foreach (var access in FieldAccessCatalog.Nearby(me.X, me.Y, me.Z, now))
        {
            float meters = MathF.Sqrt(FieldAccessCatalog.DistanceSq(access.X, access.Y, access.Z, me.X, me.Y, me.Z));
            if (meters > FieldBeaconHooks.HOME_RANGE_M) continue;
            Offer(access.Key, new Destination(access.Text, null, DestinationCategory.Access,
                access.X, access.Y, access.Z), meters);
        }
        if (!_cityListening)
        foreach (var b in BreakableCatalog.All())
        {
            float meters = b.DistanceFrom(me.X, me.Y, me.Z);
            if (meters > FieldBeaconHooks.HOME_RANGE_M) continue;
            var d = new Destination(ExplorationText.Get("breakable", "Breakable object"), null,
                DestinationCategory.Places, b.X, b.Y, b.Z);
            Offer(WorldSonarHooks.BreakableKey(b), d, meters);
        }
        Focus.End();
        CityFocus.End();
        // Ownership belongs to the last name actually spoken, not to whichever raw
        // candidate currently wins the narrow scan cone.
        FieldPlaceSweepHooks.DiscardInvalid();
        string previous = Dwell.PendingKey;
        bool ready = Dwell.Observe(FocusKey, now, deliberate: true);
        if (previous != FocusKey)
        {
            FieldFeedback.Forget("look");
        }
        if (!ready) { FieldFeedback.Forget("look"); return; }
        foreach (var candidate in Candidates)
        {
            if (candidate.Key != FocusKey) continue;
            var offered = candidate;
            FieldFeedback.Say("look", LocalizedText.AtMeters(offered.Destination.Label, (int)Math.Round(offered.Meters)),
                FieldSpeechPriority.Scan, onSpoken: () =>
                {
                    if (RememberSpoken(offered, city, situation))
                    {
                        Dwell.Spoken(offered.Key);
                        if (_cityListening)
                        {
                            CityFocus.Spoken(offered.Key);
                            API.LogInfo($"[SF6Access] City listening focus: '{offered.Destination.Label}', {offered.Meters:0} m, city {city}");
                        }
                    }
                    else FieldPlaceSweepHooks.Clear();
                }, stillValid: () => FieldFeedback.CanScan && FocusKey == offered.Key
                    && _cityListening == CityListeningMode.Active && SameContext(city, situation));
            break;
        }
    }

    internal static bool IsFocused(string key) => FieldFeedback.CanScan && FocusKey == key;

    internal static bool TryCityFocus(out Destination destination, out string key)
    {
        destination = default; key = null;
        if (!CityListeningMode.Active || !_cityListening || !CityFocus.CanSound || !FieldFeedback.CanScan
            || !SameContext(_city, _situation)) return false;
        foreach (var candidate in Candidates)
        {
            if (candidate.Key != CityFocus.Key) continue;
            destination = candidate.Destination; key = candidate.Key;
            return true;
        }
        return false;
    }

    private static bool RememberSpoken(Candidate spoken, uint city, uint situation)
    {
        // Queued words belong to the world in which the target was observed. Never
        // silently anchor an old static point in whichever world becomes readable later.
        if (!SameContext(city, situation)) return false;
        bool cityListening = _cityListening;
        // One and a half clock sectors admits ordinary corrections after hearing a name.
        var facing = new SpokenFocusGate(FieldDirectionService.DEGREES_PER_HOUR + FieldDirectionService.AHEAD_DEG,
            ScanDwell.DwellMs);
        var actor = spoken.Destination.Actor.IsValid ? new ActorTrackingState(spoken.Destination) : null;
        long nextProof = 0;
        FieldPlaceSweepHooks.OfferTracked(spoken.Destination, taking =>
        {
            var target = spoken.Destination;
            SpokenDestinationMemory.Check Result(SpokenCandidateState state, string reason) => new(state, target, reason);
            if (!FieldFeedback.CanScan) return Result(SpokenCandidateState.Invalid, "field-paused");
            if (cityListening != CityListeningMode.Active)
                return Result(SpokenCandidateState.Invalid, "listening-mode-changed");
            if (cityListening && CityFocus.Key != spoken.Key)
                return Result(SpokenCandidateState.Invalid, "city-focus-changed");
            bool currentReadable = WorldTourStateService.TryReadContext(out uint currentCity, out uint currentSituation);
            if (!currentReadable)
                return Result(SpokenCandidateState.Pending, "world-context-unreadable");
            if (city != currentCity)
                return Result(SpokenCandidateState.Invalid, "city-changed");
            if (situation != currentSituation)
                return Result(SpokenCandidateState.Invalid, "situation-changed");
            long now = Environment.TickCount64;
            if (actor != null)
            {
                // Keep only plain observations; a tap always proves the exact identity again.
                if (taking || now >= nextProof)
                {
                    actor.Observe(LiveDestinationReader.Read(spoken.Destination.Actor));
                    nextProof = now + FieldActionReader.SampleMs;
                }
                if (actor.Lost) return Result(SpokenCandidateState.Invalid, "actor-lost-or-replaced");
                if (actor.Pending) return Result(SpokenCandidateState.Pending, "actor-unreadable");
                target = actor.Current;
            }
            else
            {
                bool present = false;
                foreach (var candidate in Candidates)
                    if (candidate.Key == spoken.Key) { target = candidate.Destination; present = true; break; }
                if (!present) return Result(SpokenCandidateState.Invalid, "target-not-observed");
            }
            var me = FieldPresenceService.PlayerPos;
            if (taking)
            {
                var position = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());
                if (!position.ok) return Result(SpokenCandidateState.Pending, "player-position-unreadable");
                me = (position.x, position.y, position.z);
            }
            var bearing = FieldDirectionService.GetBearing(FieldDirectionService.GetCameraForward(), target.X - me.X, target.Z - me.Z);
            if (!bearing.Ok) return Result(SpokenCandidateState.Pending, "bearing-unreadable");
            float off = AxisLatch.OffDeg(bearing.Ahead, bearing.Right);
            var state = facing.Observe(off, now);
            string reason = (state == SpokenCandidateState.Current ? "current" : "look-away")
                + ":" + off.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "deg";
            return Result(state, reason);
        });
        return true;
    }

    private static bool SameContext(uint city, uint situation) =>
        WorldTourStateService.TryReadContext(out uint currentCity, out uint currentSituation)
        && city == currentCity && situation == currentSituation;

    internal static void RearmAfterCancel()
    {
        Dwell.Clear();
        CityFocus.Clear();
        FieldFeedback.Forget("look");
        FieldPlaceSweepHooks.Clear();
    }

    private static void ClearFocus()
    {
        Focus.Clear(); CityFocus.Clear(); Candidates.Clear(); Dwell.Clear();
        FieldFeedback.Forget("look");
        FieldPlaceSweepHooks.Clear();
    }

    private static void Pause()
    {
        if (_paused) return;
        _paused = true;
        ClearFocus();
    }
}
