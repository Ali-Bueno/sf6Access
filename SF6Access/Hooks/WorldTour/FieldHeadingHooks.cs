using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>Settled compass directions while turning, plus the current facing in the Z readout.</summary>
public class FieldHeadingHooks
{
    private const long PollMs = 100;
    private const long ResumeSettleMs = 1000; // Ignore the camera blending back after menus and dialogue.
    private static readonly CameraTurnDetector Turn = new(FieldDirectionService.DEGREES_PER_HOUR / 2.0);
    private static readonly HeadingAnnouncement Heading = new(FieldHeadingService.SECTORS);
    private static bool _wasInField;
    private static bool _paused = true;
    private static long _nextPoll, _resumeAt;
    private static uint _city;

    public static string CurrentFacing()
    {
        float heading = FieldHeadingService.HeadingDegrees(FieldDirectionService.GetCameraForward());
        return float.IsNaN(heading) ? null : FieldHeadingService.SectorName(FieldHeadingService.Sector(heading));
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();
        if (_wasInField && !FieldPresenceService.InField) FieldHeadingService.Reset();
        _wasInField = FieldPresenceService.InField;
        if (!FieldFeedback.CanOutput) { Pause(); return; }
        long now = System.Environment.TickCount64;
        if (_paused) { _paused = false; _resumeAt = now + ResumeSettleMs; }
        if (now < _nextPoll) return;
        _nextPoll = now + PollMs;
        uint city = WorldTourStateService.CityId;
        if (city != 0 && _city != 0 && city != _city)
        {
            FieldHeadingService.Reset();
            Heading.Reset();
            Turn.Reset();
        }
        if (city != 0) _city = city;

        var forward = FieldDirectionService.GetCameraForward();
        if (!forward.Ok) { Heading.Reset(); Turn.Reset(); FieldFeedback.Forget("heading"); return; }
        long ignoreUntil = System.Math.Max(_resumeAt, CameraAimService.OwnTurnUntil);
        bool deliberate = Turn.Sample(forward.X, forward.Z, now, ignoreUntil);
        if (now < ignoreUntil) { Heading.Reset(); FieldFeedback.Forget("heading"); return; }
        int sector = Heading.Observe(FieldHeadingService.HeadingDegrees(forward), deliberate, now);
        if (sector < 0) { FieldFeedback.Forget("heading"); return; }
        FieldFeedback.Say("heading", FieldHeadingService.SectorName(sector), FieldSpeechPriority.Heading,
            onSpoken: () => Heading.Spoke(sector, System.Environment.TickCount64),
            stillValid: () => FieldFeedback.CanOutput && Heading.IsCurrent(sector));
    }

    private static void Pause()
    {
        if (_paused) return;
        _paused = true;
        Heading.Reset();
        Turn.Reset();
        FieldFeedback.Forget("heading");
    }
}
