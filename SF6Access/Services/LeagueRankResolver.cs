using REFrameworkNET;

namespace SF6Access.Services;

/// <summary>
/// Resolves an app.AppDefine.LeagueRankWithLevel value (1..42) to its localized
/// rank name via the game's own league data — no hardcoded tier/division tables.
/// app.helper.hGUI.GetLeagueRankWithLevelUserData returns a record whose
/// leagueRank.messageId.GUID is the tier-name message and whose rankLevel is the
/// division. Shared by the league selector and the VS-screen rank readout.
/// </summary>
public static class LeagueRankResolver
{
    private static Method _getUserData;
    private static bool _cached;

    private static void Cache()
    {
        if (_cached) return;
        _cached = true;
        _getUserData = TDB.Get().FindType("app.helper.hGUI")
            ?.GetMethod("GetLeagueRankWithLevelUserData(app.AppDefine.LeagueRankWithLevel)");
        if (_getUserData == null)
            API.LogWarning("[SF6Access] hGUI.GetLeagueRankWithLevelUserData not found");
    }

    /// <summary>The league-rank data record for a LeagueRankWithLevel value, or null.</summary>
    public static ManagedObject GetRecord(int leagueRankWithLevel)
    {
        Cache();
        if (_getUserData == null || leagueRankWithLevel <= 0) return null;
        try
        {
            return _getUserData.InvokeBoxed(
                typeof(object), null, new object[] { leagueRankWithLevel }) as ManagedObject;
        }
        catch { return null; }
    }

    /// <summary>True when the record is a Master-tier rank.</summary>
    public static bool IsMaster(ManagedObject record) =>
        FlowHelper.ReadBoolField(record, "isMasterLeague");

    /// <summary>
    /// Localized rank name from a record: "Diamond 3" (tier + division), or just
    /// the tier name when tierOnly is set or the rank is Master. Null when the
    /// tier message can't be resolved (Invalid / unranked).
    /// </summary>
    public static string Format(ManagedObject record, bool tierOnly)
    {
        if (record == null) return null;

        // record.leagueRank.messageId.GUID = the tier-name message ("Diamond").
        var leagueRank = FlowHelper.GetObjectField(record, "leagueRank");
        var messageId = FlowHelper.GetObjectField(leagueRank, "messageId");
        string tier = FlowHelper.ResolveGuidField(messageId, "GUID");

        // Fallback: the record's own message.
        if (string.IsNullOrEmpty(tier))
            tier = FlowHelper.ResolveGuidField(FlowHelper.GetObjectField(record, "messageId"), "GUID");
        if (string.IsNullOrEmpty(tier)) return null;

        if (tierOnly || IsMaster(record)) return tier;

        int level = FlowHelper.ReadIntField(record, "rankLevel", 0);
        return level >= 1 ? $"{tier} {level}" : tier;
    }

    /// <summary>Resolve a LeagueRankWithLevel value straight to its name.</summary>
    public static string Resolve(int leagueRankWithLevel, bool tierOnly) =>
        Format(GetRecord(leagueRankWithLevel), tierOnly);
}
