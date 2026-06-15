using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Announces the league rank-up / promotion screen (app.UIFlowRankUp.Param) shown
/// after ranked matches when your league rank changes (Bronze, Silver, Gold ...).
/// The screen text lives under CtrlRankUp; the arrived rank is also held as data
/// in ListData (LeagueRankWithLevelUserDataRecord) — the on-screen rank itself is
/// an icon, so the localized rank name is read from the record's message Guid.
/// </summary>
public class RankUpHooks
{
    private const string PARAM_TYPE = "app.UIFlowRankUp.Param";

    private static int _pollCounter;
    private const int POLL_INTERVAL = 15;

    private static ManagedObject _param;
    private static bool _announced;
    private static int _retries;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] RankUpHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;
        if (_pollCounter % POLL_INTERVAL != 0) return;

        var param = FlowHelper.FindFlowParam(PARAM_TYPE);
        if (param == null)
        {
            _param = null;
            _announced = false;
            return;
        }

        bool fresh = _param == null;
        _param = param;
        if (fresh) { _announced = false; _retries = 10; }
        if (_announced) return;

        string text = BuildAnnouncement(param);
        if (string.IsNullOrEmpty(text))
        {
            // Texts/data load a beat after the screen appears — retry, then stop.
            if (--_retries > 0) return;
            _announced = true;
            return;
        }

        _announced = true;
        API.LogInfo($"[SF6Access] Rank up: {text}");
        ScreenReaderService.Speak(text);
    }

    /// <summary>
    /// "{on-screen text}. {arrived rank}" — the localized banner text walked from
    /// CtrlRankUp plus the rank name resolved from the rank record (skipped when
    /// it is already part of the banner text). Null until something is readable.
    /// </summary>
    private static string BuildAnnouncement(ManagedObject param)
    {
        var parts = new List<string>();

        string screen = GuiTextReader.ReadControlTextJoined(
            FlowHelper.GetObjectField(param, "CtrlRankUp"));
        if (!string.IsNullOrWhiteSpace(screen)) parts.Add(screen.Trim());

        string rank = ResolveArrivedRank(param);
        if (!string.IsNullOrEmpty(rank) &&
            (screen == null || screen.IndexOf(rank, System.StringComparison.OrdinalIgnoreCase) < 0))
            parts.Add(rank);

        return parts.Count > 0 ? string.Join(". ", parts) : null;
    }

    /// <summary>
    /// Localized name of the arrived rank from the last ListData record
    /// (LeagueRankWithLevelUserDataRecord). Tries the record's own message Guid,
    /// then the nested leagueRank message Guid.
    /// </summary>
    private static string ResolveArrivedRank(ManagedObject param)
    {
        try
        {
            var list = FlowHelper.GetObjectField(param, "ListData")
                     ?? FlowHelper.Call(param, "get_ListData") as ManagedObject;
            int count = FlowHelper.GetListCount(list);
            if (count <= 0) return null;

            var record = FlowHelper.GetListItem(list, count - 1); // the rank arrived at
            if (record == null) return null;

            // record.messageId.GUID — usually the full "Gold 3" style label
            string name = ResolveMessageGuid(FlowHelper.GetObjectField(record, "messageId"));
            if (string.IsNullOrEmpty(name))
            {
                // fall back to record.leagueRank.messageId.GUID — the bare rank name
                var leagueRank = FlowHelper.GetObjectField(record, "leagueRank");
                name = ResolveMessageGuid(FlowHelper.GetObjectField(leagueRank, "messageId"));
            }
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch { return null; }
    }

    /// <summary>Resolve the GUID field of a *Message object to its localized string.</summary>
    private static string ResolveMessageGuid(ManagedObject messageObj)
    {
        if (messageObj == null) return null;
        return FlowHelper.ResolveGuidField(messageObj, "GUID");
    }
}
