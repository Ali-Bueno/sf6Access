using System;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>A sparse nearby breakable hint when a route needs an obstacle action.</summary>
public class BreakableBeaconHooks
{
    // An occasional discovery, not a second direction to follow beside the route.
    private const long HintGapMs = 6000;
    private const long EmptyRetryMs = 1000;
    private static long _nextAt;

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();
        if (!FieldFeedback.CanOutput || !MissionBeaconHooks.NeedsObstacleDiscovery) return;
        long now = Environment.TickCount64;
        if (now < _nextAt) return;
        var me = FieldPresenceService.PlayerPos;
        var target = BreakableCatalog.Nearest(me.X, me.Y, me.Z, FieldAccessCatalog.RangeM);
        if (target is not { } b) { _nextAt = now + EmptyRetryMs; return; }
        string key = WorldSonarHooks.BreakableKey(b);
        if (!FieldFeedback.CueAvailable(FieldCueLane.Nearby, key)) return;
        var bearing = FieldDirectionService.GetBearing(FieldDirectionService.GetCameraForward(), b.X - me.X, b.Z - me.Z);
        if (!bearing.Ok || bearing.Behind) { _nextAt = now + EmptyRetryMs; return; }
        float volume = .25f * (FieldFeedback.SpeechActive ? FieldCuePolicy.SpeechGain : 1f);
        var duration = AudioService.PlayWorldObject(WorldObjectKind.Breakable, bearing.Right, volume);
        FieldFeedback.ReserveCue(FieldCueLane.Nearby, key, duration);
        _nextAt = now + HintGapMs;
    }
}
