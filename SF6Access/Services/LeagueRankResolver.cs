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
    private static Method _isMasterRateRank;
    private static bool _cached;

    // LeagueRankWithLevel 37 is Legend (top 500 by MR at the end of a Master League
    // phase). Its record sorts above Ultimate Master (sortNumber 13) but has no name
    // message: the word is drawn into the rank icon. Probed in game 2026-10-12.
    private const int LegendRank = 37;

    private static void Cache()
    {
        if (_cached) return;
        _cached = true;
        var hgui = TDB.Get().FindType("app.helper.hGUI");
        _getUserData = hgui?.GetMethod("GetLeagueRankWithLevelUserData(app.AppDefine.LeagueRankWithLevel)");
        _isMasterRateRank = hgui?.GetMethod("IsMasterRateRank(app.LeagueRankWithLevelUserDataRecord)");
        if (_getUserData == null)
            API.LogWarning("[SF6Access] hGUI.GetLeagueRankWithLevelUserData not found");
    }

    /// <summary>
    /// Full rank of a league-point record (app.network.MsgLeaguePoint or
    /// CommonMatchingLeaguePoint): master_league (High / Grand / Ultimate Master,
    /// Legend) overrides league_rank when set — exactly what
    /// hGUI.GetLeagueRankWithLevel(league_rank, master_league) returns.
    /// </summary>
    public static int RankOf(ManagedObject leaguePoint)
    {
        int master = FlowHelper.ReadIntField(leaguePoint, "master_league", 0);
        return master > 0 ? master : FlowHelper.ReadIntField(leaguePoint, "league_rank", 0);
    }

    /// <summary>
    /// Rank name for a league-point record: "Diamond 3", "Grand Master", or
    /// "Legend, ranked 123" (world MR ranking) for Legend players.
    /// </summary>
    public static string Describe(ManagedObject leaguePoint)
    {
        int rank = RankOf(leaguePoint);
        string name = Resolve(rank, tierOnly: false);
        if (rank != LegendRank) return name;
        int position = FlowHelper.ReadIntField(leaguePoint, "master_rating_ranking", 0);
        return position > 0 ? LocalizedText.LegendRanked(name, position) : name;
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

    /// <summary>True when the record is a Master-tier rank (rated in MR). Uses the
    /// game's own test: the record's isMasterLeague field is false for plain Master.</summary>
    public static bool IsMaster(ManagedObject record)
    {
        Cache();
        if (_isMasterRateRank == null || record == null) return false;
        try { return _isMasterRateRank.InvokeBoxed(typeof(bool), null, new object[] { record }) is true; }
        catch { return false; }
    }

    /// <summary>
    /// Localized rank name from a record: the game's full label ("Diamond 3",
    /// "New Challenger", "Master"), or just the tier name when tierOnly is set.
    /// Null when no message can be resolved (Invalid / unranked).
    /// </summary>
    public static string Format(ManagedObject record, bool tierOnly)
    {
        if (record == null) return null;
        if (FlowHelper.ReadIntField(record, "id", 0) == LegendRank) return LocalizedText.Legend();

        // record.messageId.GUID = the full label. Building "tier + rankLevel" instead
        // said "New Challenger 1", since that rank also has rankLevel 1.
        if (!tierOnly)
        {
            string full = FlowHelper.ResolveGuidField(FlowHelper.GetObjectField(record, "messageId"), "GUID");
            if (!string.IsNullOrEmpty(full)) return full;
        }

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
