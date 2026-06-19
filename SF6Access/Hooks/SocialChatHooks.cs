using System;
using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Announces socials in the Battle Hub / lobbies so a blind player can "talk"
/// through fixed phrases, stamps and chat: every message that hits the chat
/// feed (your own and nearby players') passes through app.ChatManager.addLog,
/// which this hooks. It reads the ChatInfo (speaker + content), resolves the
/// social to readable text by FormatType (Normal text / Stamp / fixed-phrase
/// Template), and announces "{speaker}: {text}".
///
/// FormatType (app.network.api.Enum.FormatType): None=0, Normal=1 (literal text),
/// Stamp=2 (id → StampIdToMessage), Template=3 (fixed phrase → FixedPhraseIdToMessage),
/// MessageId=4 (localized id). Hook fires off the main thread, so announcements
/// are queued and spoken from LateUpdate.
/// </summary>
public class SocialChatHooks
{
    private static Method _stampIdToMessage;
    private static Method _messageToStampId;
    private static Method _fixedPhraseIdToMessage;
    private static Method _messageToFixedPhraseId;

    private static readonly List<string> _pending = new();
    private static readonly object _lock = new();

    // Recently announced message UniqueIds, so the same entry isn't re-read
    // (addLog can be invoked more than once per message during log rebuilds).
    private static readonly HashSet<string> _seen = new();
    private static readonly Queue<string> _seenOrder = new();
    private const int SEEN_MAX = 64;

    [PluginEntryPoint]
    public static void Initialize()
    {
        try
        {
            var td = TDB.Get().FindType("app.ChatManager");
            if (td == null)
            {
                API.LogError("[SF6Access] SocialChatHooks: app.ChatManager not found");
                return;
            }

            _stampIdToMessage = td.GetMethod("StampIdToMessage(System.UInt32)");
            _messageToStampId = td.GetMethod("MessageToStampId(System.String)");
            _fixedPhraseIdToMessage = td.GetMethod("FixedPhraseIdToMessage(System.UInt32, System.UInt32)");
            _messageToFixedPhraseId = td.GetMethod("MessageToFixedPhraseId(System.String)");

            // addLog never fired in testing — the chat/social path is unclear, so
            // instrument every candidate that carries a ChatInfo and log which one
            // actually fires (the next log pins it). All route to OnChatInfo.
            int hooked = 0;

            // addLog(ChatInfo): args[1] = chatInfo
            HookChatInfo(td, "addLog(app.network.rpc.MessagingSessionRpc.ChatInfo)", "addLog", 1, ref hooked);
            // Chat(ChatInfo): args[1] = chatInfo (processes a received chat)
            HookChatInfo(td, "Chat(app.network.rpc.MessagingSessionRpc.ChatInfo)", "Chat", 1, ref hooked);
            // SetBalloonChat(uint shortId, ChatInfo): args[2] = chatInfo — the
            // speech balloon over a nearby avatar (own + nearby players' socials)
            HookChatInfo(td, "SetBalloonChat(System.UInt32, app.network.rpc.MessagingSessionRpc.ChatInfo)", "SetBalloonChat", 2, ref hooked);

            // SendMessage(MessageType, FormatType, GroupType, string message, string targetId):
            // the OWN send path — the message text/id is a direct string arg (args[4]),
            // so this catches "you used a phrase/stamp" even when ChatInfo reads empty.
            var send = td.GetMethod("SendMessage(app.network.api.Enum.MessageType, app.network.api.Enum.FormatType, app.network.api.Enum.GroupType, System.String, System.String)");
            if (send != null)
            {
                send.AddHook(false).AddPre(args =>
                {
                    try { OnSendMessage((int)(long)args[2], ManagedObject.ToManagedObject(args[4])?.ToString()); }
                    catch (Exception ex) { API.LogError($"[SF6Access] SocialChat SendMessage hook error: {ex.Message}"); }
                    return PreHookResult.Continue;
                });
                hooked++;
            }
            else API.LogInfo("[SF6Access] SocialChat: SendMessage method not found");

            API.LogInfo($"[SF6Access] SocialChatHooks initialized ({hooked} chat hooks installed)");
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] SocialChatHooks init error: {ex.Message}");
        }
    }

    /// <summary>Install a hook that pulls a ChatInfo from a fixed arg index.</summary>
    private static void HookChatInfo(TypeDefinition td, string signature, string label, int argIndex, ref int hooked)
    {
        var m = td.GetMethod(signature);
        if (m == null)
        {
            API.LogInfo($"[SF6Access] SocialChat: {label} method not found");
            return;
        }
        m.AddHook(false).AddPre(args =>
        {
            try { OnChatInfo(label, ManagedObject.ToManagedObject(args[argIndex])); }
            catch (Exception ex) { API.LogError($"[SF6Access] SocialChat {label} hook error: {ex.Message}"); }
            return PreHookResult.Continue;
        });
        hooked++;
    }

    private static void OnChatInfo(string source, ManagedObject chatInfo)
    {
        if (chatInfo == null) return;

        // Field reads came back empty in testing — read via the property getters
        // (RPC message types don't expose plain backing fields), field as fallback.
        int format = CallInt(chatInfo, "get_FormatType", FlowHelper.ReadByteField(chatInfo, "FormatType"));
        string rawMessage = CallStr(chatInfo, "get_Message") ?? FlowHelper.ReadStringField(chatInfo, "Message");
        string speaker = ResolveSpeaker(chatInfo);

        // Diagnostic: confirm which path fires when a phrase/chat is used and whether
        // the getters read the content (field reads came back empty before).
        API.LogInfo($"[SF6Access] Social via {source}: format={format}, speaker='{speaker}', raw='{rawMessage}'");

        string uniqueId = CallStr(chatInfo, "get_UniqueId") ?? FlowHelper.ReadStringField(chatInfo, "UniqueId");
        if (!string.IsNullOrEmpty(uniqueId) && !MarkSeen(uniqueId)) return;

        string text = ResolveText(format, rawMessage);
        if (string.IsNullOrWhiteSpace(text)) return;

        string announcement = string.IsNullOrEmpty(speaker) ? text : $"{speaker}: {text}";
        lock (_lock) _pending.Add(announcement);
    }

    /// <summary>The own send path: announce the social text directly from SendMessage.</summary>
    private static void OnSendMessage(int format, string message)
    {
        API.LogInfo($"[SF6Access] Social SendMessage: format={format}, raw='{message}'");
        string text = ResolveText(format, message);
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (_lock) _pending.Add(text);
    }

    private static int CallInt(ManagedObject obj, string method, int fallback)
    {
        var r = FlowHelper.Call(obj, method);
        if (r == null) return fallback;
        try { return Convert.ToInt32(r); } catch { return fallback; }
    }

    private static string CallStr(ManagedObject obj, string method)
    {
        var r = FlowHelper.Call(obj, method) as string;
        return string.IsNullOrEmpty(r) ? null : r;
    }

    /// <summary>Resolve a chat entry to readable text by its FormatType.</summary>
    private static string ResolveText(int format, string rawMessage)
    {
        try
        {
            switch (format)
            {
                case 2: // Stamp: message encodes a stamp id
                    return ResolveStamp(rawMessage) ?? rawMessage;
                case 3: // Template: a fixed phrase
                    return ResolveFixedPhrase(rawMessage) ?? rawMessage;
                default: // Normal text / MessageId / None: the message text itself
                    return FlowHelper.CleanTags(rawMessage);
            }
        }
        catch { return FlowHelper.CleanTags(rawMessage); }
    }

    private static string ResolveStamp(string message)
    {
        if (_messageToStampId == null || _stampIdToMessage == null || string.IsNullOrEmpty(message)) return null;
        try
        {
            uint id = Convert.ToUInt32(_messageToStampId.InvokeBoxed(typeof(uint), null, new object[] { message }));
            if (id == 0) return null;
            string name = _stampIdToMessage.InvokeBoxed(typeof(string), null, new object[] { id }) as string;
            return string.IsNullOrWhiteSpace(name) ? null : FlowHelper.CleanTags(name);
        }
        catch { return null; }
    }

    /// <summary>
    /// Resolve a fixed-phrase template. MessageToFixedPhraseId returns the phrase
    /// id (and option id); FixedPhraseIdToMessage localizes it. The id pair comes
    /// back boxed — read it defensively, falling back to the raw message.
    /// </summary>
    private static string ResolveFixedPhrase(string message)
    {
        if (_messageToFixedPhraseId == null || _fixedPhraseIdToMessage == null || string.IsNullOrEmpty(message))
            return null;
        try
        {
            var idObj = _messageToFixedPhraseId.InvokeBoxed(typeof(object), null, new object[] { message });
            if (!TryReadPhraseIds(idObj, out uint phraseId, out uint optionId) || phraseId == 0) return null;

            string name = _fixedPhraseIdToMessage.InvokeBoxed(
                typeof(string), null, new object[] { phraseId, optionId }) as string;
            return string.IsNullOrWhiteSpace(name) ? null : FlowHelper.CleanTags(name);
        }
        catch { return null; }
    }

    /// <summary>Read the (phraseId, optionId) pair from the boxed id object.</summary>
    private static bool TryReadPhraseIds(object idObj, out uint phraseId, out uint optionId)
    {
        phraseId = 0;
        optionId = 0;
        if (idObj is ManagedObject mo)
        {
            phraseId = (uint)FlowHelper.ReadIntField(mo, "Item1", 0);
            if (phraseId == 0) phraseId = (uint)FlowHelper.ReadIntField(mo, "FixedPhraseId", 0);
            optionId = (uint)FlowHelper.ReadIntField(mo, "Item2", 0);
            if (optionId == 0) optionId = (uint)FlowHelper.ReadIntField(mo, "OptionId", 0);
            return phraseId != 0;
        }
        try { phraseId = Convert.ToUInt32(idObj); return phraseId != 0; }
        catch { return false; }
    }

    /// <summary>Speaker display name from the ChatInfo source fields.</summary>
    private static string ResolveSpeaker(ManagedObject chatInfo)
    {
        string name = CallStr(chatInfo, "get_SourcePlatformOnlineId")
            ?? FlowHelper.ReadStringField(chatInfo, "SourcePlatformOnlineId");
        if (string.IsNullOrWhiteSpace(name))
            name = CallStr(chatInfo, "get_SourceFighterId")
                ?? FlowHelper.ReadStringField(chatInfo, "SourceFighterId");
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    /// <summary>Record a UniqueId; false when it was already seen this session window.</summary>
    private static bool MarkSeen(string uniqueId)
    {
        lock (_lock)
        {
            if (!_seen.Add(uniqueId)) return false;
            _seenOrder.Enqueue(uniqueId);
            if (_seenOrder.Count > SEEN_MAX) _seen.Remove(_seenOrder.Dequeue());
            return true;
        }
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        string[] batch;
        lock (_lock)
        {
            if (_pending.Count == 0) return;
            batch = _pending.ToArray();
            _pending.Clear();
        }

        // Queue (interrupt:false) so socials don't cut off menu navigation.
        foreach (var line in batch)
            ScreenReaderService.Speak(line, interrupt: false);
    }
}
