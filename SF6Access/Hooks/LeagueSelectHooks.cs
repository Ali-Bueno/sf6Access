using System;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the V-Rival league/rank selector and its inner detail list:
/// - app.UICFNSelectLeague.FlowParam (press R): a _ScrollGrid of league TIERS
///   (Rookie, Iron ... Master); _LeagueItemList is List&lt;CfnLeagueInfo&gt;.
/// - app.UICFNSelectLeagueDetail.FlowParam (entering a tier): a _ScrollList of
///   levels; _LeagueList is List&lt;app.AppDefine.LeagueRankWithLevel&gt;.
/// Both render the rank as an icon, so the focused cell's only text is the
/// "Unspecified" placeholder (read wrongly by the generic reader). The real name
/// is resolved from the league data via app.helper.hGUI.GetLeagueRankWithLevelUserData,
/// whose record carries the tier-name message Guid and the division level.
///
/// ScreenAdapter (multi-Param): the detail list opens on top of the tier grid and
/// is preferred while present. Registered in ScreenRegistry.
/// </summary>
public sealed class LeagueSelectHooks : ScreenAdapter
{
    private const string LEAGUE_TYPE = "app.UICFNSelectLeague.FlowParam";
    private const string DETAIL_TYPE = "app.UICFNSelectLeagueDetail.FlowParam";
    private static readonly string[] Types = { DETAIL_TYPE, LEAGUE_TYPE };

    public override string[] OwnedTypes => Types;

    public LeagueSelectHooks()
    {
        SearchInterval = 20;
        ReadInterval = 5;
    }

    private ManagedObject _param;
    private bool _isDetail;
    private int _lastIndex = -2;
    private string _lastName;

    protected override bool Locate()
    {
        // The detail list opens on top of the grid — prefer it when present.
        var found = FlowHelper.FindFlowParams(Types);
        ManagedObject param = found.TryGetValue(DETAIL_TYPE, out var d) ? d : null;
        bool isDetail = param != null;
        if (param == null) found.TryGetValue(LEAGUE_TYPE, out param);

        if (param == null)
        {
            _param = null;
            return false;
        }

        // Re-bind on instance change or grid↔detail switch (stale-param pattern).
        if (FlowHelper.AddressOf(param) != FlowHelper.AddressOf(_param) || isDetail != _isDetail)
        {
            _param = param;
            _isDetail = isDetail;
            _lastIndex = -2;
            _lastName = null;
        }
        return true;
    }

    protected override void OnDeactivate()
    {
        _param = null;
        _lastIndex = -2;
        _lastName = null;
    }

    protected override void OnPoll()
    {
        try
        {
            var widget = FlowHelper.GetObjectField(_param, _isDetail ? "_ScrollList" : "_ScrollGrid");
            int idx = FlowHelper.CallInt(widget, "get_SelectedIndex");
            if (idx < 0) return;

            string name = _isDetail ? ResolveDetailName(idx) : ResolveTierName(idx);
            if (string.IsNullOrEmpty(name)) return;

            if (idx == _lastIndex && name == _lastName) return;
            _lastIndex = idx;
            _lastName = name;

            API.LogInfo($"[SF6Access] League select [{(_isDetail ? "detail" : "tier")},{idx}]: {name}");
            Speak(name);
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] League select error: {ex.Message}");
        }
    }

    /// <summary>Tier name for the focused grid cell (CfnLeagueInfo → its first level).</summary>
    private string ResolveTierName(int idx)
    {
        var list = FlowHelper.GetObjectField(_param, "_LeagueItemList");
        var info = FlowHelper.GetListItem(list, idx);
        if (info == null) return null;

        // CfnLeagueInfo.LeagueList is List<LeagueRankWithLevel>; its first entry
        // resolves the tier name (the grid picks a whole tier, so drop the level).
        var levels = FlowHelper.GetObjectField(info, "LeagueList");
        int enumVal = ReadEnumListItem(levels, 0);
        if (enumVal <= 0) return null;

        return LeagueRankResolver.Resolve(enumVal, tierOnly: true);
    }

    /// <summary>Tier + level for the focused detail-list level.</summary>
    private string ResolveDetailName(int idx)
    {
        var list = FlowHelper.GetObjectField(_param, "_LeagueList");
        int enumVal = ReadEnumListItem(list, idx);
        if (enumVal <= 0) return null;

        return LeagueRankResolver.Resolve(enumVal, tierOnly: false);
    }

    /// <summary>Read an enum element from a List&lt;enum&gt; as int (value type,
    /// so unbox through InvokeBoxed(int) rather than a managed-object cast).</summary>
    private static int ReadEnumListItem(ManagedObject list, int idx)
    {
        if (list == null || idx < 0) return -1;
        try
        {
            var td = list.GetTypeDefinition();
            var getItem = td?.GetMethod("get_Item(System.Int32)");
            if (getItem == null) return -1;
            var raw = getItem.InvokeBoxed(typeof(int), list, new object[] { idx });
            return raw != null ? Convert.ToInt32(raw) : -1;
        }
        catch { return -1; }
    }
}
