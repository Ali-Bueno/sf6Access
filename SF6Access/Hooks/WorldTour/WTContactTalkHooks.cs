using System;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// Reads what an ordinary street NPC says when you take the "Hablar" prompt —
/// the short exchanges all over World Tour and the Battle Hub.
///
/// <para><b>This is a THIRD dialogue system, not a broken one.</b> Story and
/// rival dialogue runs through <c>UIFlowSpTalkNovelMain</c> (read by
/// <see cref="SF6Access.Hooks.SpTalkNovelHooks"/>) and staged "Special Talk"
/// scenes through <c>SpTalkCtrl</c> (read by
/// <see cref="SF6Access.Hooks.SpTalkHooks"/>). Casual NPC chat uses neither: it
/// is driven by <c>app.worldtour.WTContactSystem</c>, whose script commands push
/// each line into a message log. Nothing was polling that, which is exactly why
/// the tester heard their rival fine and passers-by not at all.</para>
///
/// <para><b>The text arrives as plain strings.</b> Unlike
/// <c>SpTalkSubtitlesData</c>, which carries message Guids to resolve,
/// <c>WTContactMessageLog</c> already holds <c>Name</c> and <c>Message</c> as
/// resolved text — so there is nothing to look up and nothing to get wrong.</para>
///
/// <para>Not gated by the game's Subtitles option: that setting governs the
/// voiced cutscene-style talk, and this system has no connection to it — the
/// same reasoning that leaves the novel reader ungated.</para>
/// </summary>
public class WTContactTalkHooks
{
    private const string CONTACT_SYSTEM = "app.worldtour.WTContactSystem";
    private const string ADD_MESSAGE_LOG = "AddMessageLog(app.worldtour.WTContactDefine.WTContactMessageLog)";

    // WTContactMessageLog.ELogType — a Choice entry is a menu option, not
    // something the NPC said.
    private const int LOG_TYPE_CHOICE = 1;

    private static readonly object Lock = new();
    private static ManagedObject _pending;
    private static bool _havePending;
    private static string _lastSpoken;

    [PluginEntryPoint]
    public static void Initialize()
    {
        try
        {
            var td = TDB.Get().FindType(CONTACT_SYSTEM);
            // Full signature first; the plain name is the fallback in case the
            // parameter type spelling differs in another build.
            var add = td?.GetMethod(ADD_MESSAGE_LOG) ?? td?.GetMethod("AddMessageLog");
            if (add == null)
            {
                API.LogError($"[SF6Access] WTContactTalkHooks: {CONTACT_SYSTEM}.AddMessageLog not found");
                return;
            }

            // Dynamic hook (AddHook(false)) for IL2CPP interface dispatch, and a
            // pre-hook only — never a pre and a post on the same dynamic hook.
            add.AddHook(false).AddPre(args =>
            {
                try
                {
                    lock (Lock)
                    {
                        // args[0] is the WTContactSystem instance; args[1] the log entry.
                        _pending = ManagedObject.ToManagedObject(args[1]);
                        _havePending = true;
                    }
                }
                catch (Exception ex) { API.LogError($"[SF6Access] Contact talk hook error: {ex.Message}"); }
                return PreHookResult.Continue;
            });

            API.LogInfo("[SF6Access] WTContactTalkHooks initialized (AddMessageLog hooked)");
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] WTContactTalkHooks init error: {ex.Message}");
        }
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        ManagedObject log;
        lock (Lock)
        {
            if (!_havePending) return;
            _havePending = false;
            log = _pending;
            _pending = null;
        }
        if (log == null) return;

        // Reading happens HERE, on the game thread, not inside the hook: the hook
        // runs on whatever thread the game called from, and touching managed
        // objects there is how the mod's older hooks got into trouble.
        string message = Text(log, "Message");
        if (string.IsNullOrEmpty(message)) return;

        string name = Text(log, "Name");
        string announcement = string.IsNullOrEmpty(name) ? message : $"{name}: {message}";
        if (announcement == _lastSpoken) return;
        _lastSpoken = announcement;

        int type = FlowHelper.ReadIntField(log, "logType", 0);
        API.LogInfo($"[SF6Access] Contact talk{(type == LOG_TYPE_CHOICE ? " (choice)" : "")}: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    /// <summary>One already-resolved string off the log entry, tags stripped and
    /// line breaks flattened — the screen reader stops at a newline, so a wrapped
    /// line would otherwise be read only as far as its first break.</summary>
    private static string Text(ManagedObject log, string member)
    {
        string raw = null;
        try { raw = FlowHelper.Call(log, "get_" + member) as string; } catch { }
        if (string.IsNullOrEmpty(raw))
        {
            try { raw = log.GetField($"<{member}>k__BackingField") as string; } catch { }
            if (string.IsNullOrEmpty(raw))
            {
                try { raw = log.GetField(member) as string; } catch { }
            }
        }
        string clean = FlowHelper.CleanTags(raw);
        return string.IsNullOrEmpty(clean) ? null : clean.Replace('\n', ' ').Replace('\r', ' ').Trim();
    }
}
