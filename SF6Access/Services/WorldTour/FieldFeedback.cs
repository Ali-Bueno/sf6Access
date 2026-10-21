using System;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Hooks;
using SF6Access.Hooks.WorldTour;

namespace SF6Access.Services.WorldTour;

/// <summary>Shared speech and cue admission for automatic field navigation.</summary>
public static class FieldFeedback
{
    private static readonly FieldSpeechPolicy Speech = new();
    private static readonly FieldCuePolicy Cues = new();
    private static long _lastExternalSequence;

    public static bool CanOutput => FieldPresenceService.CanSpeak && !SpTalkNovelHooks.DialogueActive
        && !PadGuideHooks.Active && !DestinationPickerHooks.IsOpen;
    public static bool CanExplore => CanOutput && (MapSelectionMemory.Exploring ||
        (!MapSelectionMemory.Current.Ok && !MissionTargetService.Find().Ok));
    public static bool CanScan => CanOutput && (!CityListeningMode.Active || ReadoutShortcut.IsGameForeground());
    public static bool SpeechActive => Speech.Busy(Environment.TickCount64)
        || Environment.TickCount64 < ScreenReaderService.ProtectedUntil || ScreenReaderService.ExternalSpeaking;

    public static bool Say(string source, string text, FieldSpeechPriority priority,
        long lifetimeMs = 2500, Action onSpoken = null, Func<bool> stillValid = null)
    {
        if (!CanOutput) return false;
        SynchronizeExternal();
        return Speech.Offer(source, text, priority, Environment.TickCount64, lifetimeMs, onSpoken, stillValid);
    }

    public static void Forget(string source) => Speech.Forget(source);

    public static bool CueAvailable(FieldCueLane lane, string source, bool duringSpeech = true)
    {
        long now = Environment.TickCount64;
        return CanOutput && Cues.Available(now, lane, source) && (duringSpeech || !SpeechActive);
    }

    public static void ReserveCue(FieldCueLane lane, string source, TimeSpan duration)
    {
        Cues.Reserve(Environment.TickCount64, lane, source, duration);
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        FieldPresenceService.Refresh();
        if (!CanOutput) { Speech.Clear(); return; }
        SynchronizeExternal();
        long now = Environment.TickCount64;
        long protectedUntil = ScreenReaderService.ExternalSpeaking
            ? Math.Max(ScreenReaderService.ProtectedUntil, now + FieldSpeechPolicy.CoalesceMs)
            : ScreenReaderService.ProtectedUntil;
        if (!Speech.TryTake(now, protectedUntil, out var notice)) return;
        ScreenReaderService.SpeakAutomatic(notice.Text);
        notice.OnSpoken?.Invoke();
    }

    private static void SynchronizeExternal()
    {
        // A requested readout supersedes pending field chatter, rather than postponing it.
        if (_lastExternalSequence != ScreenReaderService.ExternalSequence)
        {
            _lastExternalSequence = ScreenReaderService.ExternalSequence;
            Speech.Clear();
        }
    }
}
