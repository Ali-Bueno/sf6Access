using System;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>Names a verified route when the player faces its first walking leg.</summary>
public static class ExplorationRouteHooks
{
    private static readonly ScanDwell Dwell = new();
    private static long _nextPoll;
    private static PlaceRouteMemory.Target? _pendingTarget;
    private static long _pendingEpoch;
    private const long PollMs = FieldNavPath.MinQueryIntervalMs;

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        if (!FieldFeedback.CanOutput || !MapSelectionMemory.Exploring || CityListeningMode.Active)
        { ExplorationRoutes.Reset(); Dwell.Clear(); _pendingTarget = null; FieldFeedback.Forget("path-preview"); return; }
        long now = Environment.TickCount64;
        if (now < _nextPoll) return;
        _nextPoll = now + PollMs;
        ExplorationRoutes.Update();
        ExplorationRoutes.Choice? ahead = null;
        foreach (var choice in ExplorationRoutes.Current())
            if (choice.OffDegrees <= FieldDirectionService.AHEAD_DEG) { ahead = choice; break; }
        string key = ahead?.Route.Target.Key;
        if (_pendingTarget != ahead?.Route.Target || _pendingEpoch != ahead?.Epoch)
        {
            Dwell.Clear(); FieldFeedback.Forget("path-preview");
            _pendingTarget = ahead?.Route.Target; _pendingEpoch = ahead?.Epoch ?? 0;
        }
        if (!Dwell.Observe(key, now, true) || ahead is not { } selected) return;
        FieldFeedback.Say("path-preview", string.Format(
            ExplorationText.Get("path_ahead", "This way: walking route toward {0}, {1} meters"),
            selected.Route.Target.Destination.Label, Math.Max(1, (int)MathF.Round(selected.Route.Meters))),
            FieldSpeechPriority.Scan,
            onSpoken: () =>
            {
                Dwell.Spoken(key);
                FieldPlaceSweepHooks.OfferSpoken(selected.Route.Target.Destination, () => StillAhead(selected));
            }, stillValid: () => StillAhead(selected));
    }

    private static bool StillAhead(ExplorationRoutes.Choice expected)
    {
        foreach (var choice in ExplorationRoutes.Current())
            if (choice.Epoch == expected.Epoch && choice.Route.Target == expected.Route.Target
                && choice.OffDegrees <= FieldDirectionService.AHEAD_LEAVE_DEG) return true;
        return false;
    }
}
