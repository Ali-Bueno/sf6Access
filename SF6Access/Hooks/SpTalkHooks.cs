using System;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Reads Battle Hub / World Tour NPC dialogue (the "Special Talk" system), the same
/// way arcade cutscene subtitles are read. Each spoken line is a
/// app.worldtour.SpTalkSubtitlesData with the speaker name and dialogue as message
/// Guids (mTextNameGuid / mTextDialogueGuid); the game advances lines through
/// app.worldtour.SpTalkCtrl.SubtitlesProgress.ChangePage(SpTalkSubtitlesData), which
/// this hooks to catch every line.
///
/// The hook only stores the line; the Guids are resolved on the next frame (game
/// thread) and announced — but only when the in-game Subtitles option is enabled,
/// matching the cutscene-subtitle behaviour.
/// </summary>
public class SpTalkHooks
{
    private static readonly object _lock = new();
    private static ManagedObject _pendingData;
    private static bool _pending;

    private static string _lastDialog;

    [PluginEntryPoint]
    public static void Initialize()
    {
        try
        {
            var td = TDB.Get().FindType("app.worldtour.SpTalkCtrl.SubtitlesProgress");
            var changePage = td?.GetMethod("ChangePage(app.worldtour.SpTalkSubtitlesData)");
            if (changePage == null)
            {
                API.LogError("[SF6Access] SpTalkHooks: SubtitlesProgress.ChangePage not found");
                return;
            }

            changePage.AddHook(false).AddPre(args =>
            {
                try
                {
                    lock (_lock)
                    {
                        _pendingData = ManagedObject.ToManagedObject(args[1]);
                        _pending = true;
                    }
                }
                catch (Exception ex) { API.LogError($"[SF6Access] SpTalk hook error: {ex.Message}"); }
                return PreHookResult.Continue;
            });

            API.LogInfo("[SF6Access] SpTalkHooks initialized (ChangePage hooked)");
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] SpTalkHooks init error: {ex.Message}");
        }
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        ManagedObject data;
        lock (_lock)
        {
            if (!_pending) return;
            _pending = false;
            data = _pendingData;
            _pendingData = null;
        }
        if (data == null) return;

        string dialogue = FlowHelper.CleanTags(FlowHelper.ResolveGuidField(data, "mTextDialogueGuid"));
        if (string.IsNullOrEmpty(dialogue) || dialogue == _lastDialog) return;
        _lastDialog = dialogue;

        // Follow the in-game Subtitles option (the dialogue is voiced); tracked above
        // so toggling back on doesn't re-read the line that was just skipped.
        if (!FlowHelper.AreSubtitlesEnabled()) return;

        string name = FlowHelper.CleanTags(FlowHelper.ResolveGuidField(data, "mTextNameGuid"));
        string announcement = string.IsNullOrEmpty(name) ? dialogue : $"{name}: {dialogue}";
        API.LogInfo($"[SF6Access] NPC talk: {announcement}");
        ScreenReaderService.Speak(announcement);
    }
}
