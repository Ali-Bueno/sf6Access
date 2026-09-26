using System;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>Automatic context actions and local access discovery, without choosing a goal or moving.</summary>
public class FieldExplorationHooks
{
    private static readonly NavigationNoticeMemory Memory = new();
    private const long ReaderHoldMs = 1200; // Same grace as the existing field readers.
    private static long _nextSample, _nextSpeech;
    private static uint _city, _section;
    private static bool _inField, _failed;
    private static readonly float[] AlignedTone = { AudioService.NoteMi, AudioService.NoteLa };

    [PluginEntryPoint]
    public static void Initialize()
    {
        var method = TDB.Get().FindType("app.UIHud_PlayerActionGuide")?.GetMethod("FlowEvent_LateUpdate");
        method?.AddHook(false).AddPre(args =>
        {
            try
            {
                FieldPresenceService.Refresh();
                if (args.Length > 1 && FieldPresenceService.CanSpeak
                    && !SpTalkNovelHooks.DialogueActive && !PadGuideHooks.Active)
                    FieldActionReader.Capture(ManagedObject.ToManagedObject(args[1]));
            }
            catch (Exception ex) { LogFailure(ex); }
            return PreHookResult.Continue;
        });
        // The requests that raise a prompt: args[1] = the HUD, args[2] = GuideDef.
        int requests = 0;
        foreach (string name in new[] { "RequestShowFromPlayer", "RequestShowFromOM" })
        {
            var request = TDB.Get().FindType("app.UIHud_PlayerActionGuide")?.GetMethod(name);
            if (request == null) continue;
            requests++;
            request.AddHook(false).AddPre(args =>
            {
                try
                {
                    if (args.Length > 2 && FieldPresenceService.CanSpeak) FieldActionReader.NoteRequest((int)(long)args[2]);
                }
                catch (Exception ex) { LogFailure(ex); }
                return PreHookResult.Continue;
            });
        }
        API.LogInfo($"[SF6Access] Exploration initialized, automatic action HUD hook={method != null}, request hooks={requests}");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        try { Update(); }
        catch (Exception ex) { LogFailure(ex); }
    }

    private static void Update()
    {
        FieldPresenceService.Refresh();
        if (!FieldPresenceService.CanSpeak)
        {
            if (_inField) Reset();
            _inField = false;
            return;
        }
        _inField = true;
        if (SpTalkNovelHooks.DialogueActive || PadGuideHooks.Active) return;
        long now = Environment.TickCount64;
        if (now < _nextSample) return;
        _nextSample = now + FieldActionReader.SampleMs;
        uint city = WorldTourStateService.CityId, section = WorldTourStateService.CurrentSectionId;
        if (city != _city || (section != 0 && section != _section))
        { Reset(); _city = city; _section = section; }
        Memory.Prune(now);
        var player = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());
        if (!player.ok) return;
        // Keep discovery samples fresh even while another reader owns speech.
        var accesses = FieldAccessCatalog.Nearby(player.x, player.y, player.z, now);
        if (now < _nextSpeech || now - ScreenReaderService.LastInterruptTick < ReaderHoldMs) return;
        foreach (var prompt in FieldActionReader.Current(now))
        {
            if (!Memory.Wants(prompt.Key, prompt.Text, 0, true, now)) continue;
            Say(prompt.Key, prompt.Text, ExplorationText.Available(prompt.Text), 0, true, now);
            return;
        }
        var forward = FieldDirectionService.GetCameraForward();
        FieldAccessCatalog.Access? best = null;
        float nearest = FieldAccessCatalog.RangeM;
        foreach (var access in accesses)
        {
            float distance = MathF.Sqrt(FieldAccessCatalog.DistanceSq(access.X, access.Y, access.Z,
                player.x, player.y, player.z));
            int hour = FieldDirectionService.ClockHour(forward, access.X - player.x, access.Z - player.z);
            if (distance > nearest || !Memory.Wants(access.Key, access.Text, distance, hour == 12, now)) continue;
            best = access; nearest = distance;
        }
        if (best is not { } selected) return;
        int clock = FieldDirectionService.ClockHour(forward, selected.X - player.x, selected.Z - player.z);
        string words = clock > 0 ? LocalizedText.AtClockMeters(selected.Text, clock, (int)MathF.Round(nearest))
                                : LocalizedText.AtMeters(selected.Text, (int)MathF.Round(nearest));
        Say(selected.Key, selected.Text, words, nearest, clock == 12, now);
        if (clock == 12) AudioService.PlayTone(AlignedTone);
    }

    private static void Say(string key, string state, string text, float distance, bool aligned, long now)
    {
        ScreenReaderService.Speak(text, interrupt: true);
        Memory.Spoke(key, state, distance, aligned, now);
        _nextSpeech = now + NavigationNoticeMemory.RepeatFloorMs;
        API.LogInfo("[SF6Access] Exploration: " + text);
    }

    private static void Reset()
    { Memory.Clear(); FieldActionReader.Reset(); FieldAccessCatalog.Reset(); _nextSpeech = 0; }

    private static void LogFailure(Exception ex)
    {
        if (_failed) return;
        _failed = true;
        API.LogWarning($"[SF6Access] Exploration read failed: {ex.GetType().Name}: {ex.Message}");
    }
}
