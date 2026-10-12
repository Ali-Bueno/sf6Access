using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Announces the league rank-up / promotion screen (app.UIFlowRankUp.Param) shown
/// after ranked matches when your league rank changes (Bronze, Silver, Gold ...).
/// The screen text lives under CtrlRankUp; the on-screen rank itself is an icon,
/// so the arrived rank is read as data from the result screen's ResultController
/// (your NextLeague) and named through LeagueRankResolver.
/// Migrated to ScreenAdapter.
/// </summary>
public sealed class RankUpHooks : SingleParamScreenAdapter
{
    protected override string ParamType => "app.UIFlowRankUp.Param";

    public RankUpHooks()
    {
        SearchInterval = 15;
        ReadInterval = 15;
    }

    private bool _announced;
    private int _retries;

    protected override void OnBind()
    {
        _announced = false;
        _retries = 10;
    }

    protected override void Poll()
    {
        if (_announced) return;

        string text = BuildAnnouncement(Param);
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
    /// CtrlRankUp plus the arrived rank from your post-match league data (skipped when
    /// it is already part of the banner text). Null until something is readable.
    /// </summary>
    private static string BuildAnnouncement(ManagedObject param)
    {
        var parts = new List<string>();

        string screen = GuiTextReader.ReadControlTextJoined(
            FlowHelper.GetObjectField(param, "CtrlRankUp"));
        if (!string.IsNullOrWhiteSpace(screen)) parts.Add(screen.Trim());

        string rank = ResolveArrivedRank();
        if (!string.IsNullOrEmpty(rank) &&
            (screen == null || screen.IndexOf(rank, System.StringComparison.OrdinalIgnoreCase) < 0))
            parts.Add(rank);

        return parts.Count > 0 ? string.Join(". ", parts) : null;
    }

    /// <summary>
    /// Localized name of the rank the player arrived at ("Gold 1", "High Master").
    /// NOT from the param's _ListData: that list is the rank ladder, and taking its
    /// last record announced "Ultimate Master" for every rank-up.
    /// </summary>
    private static string ResolveArrivedRank()
    {
        var next = SelfNextLeague();
        return next == null ? null : LeagueRankResolver.Describe(next);
    }

    private static Method _findComponents;
    private static TypeDefinition _resultControllerType;

    /// <summary>
    /// The local player's league after the match (CommonMatchingLeaguePoint), from
    /// the result screen's app.ResultController: SelfData.Fighter.NextLeague. The
    /// controller owns the rank-up flow, so it is alive while that screen shows.
    /// </summary>
    internal static ManagedObject SelfNextLeague()
    {
        try
        {
            _findComponents ??= TDB.Get().FindType("via.Scene")?.GetMethod("findComponents(System.Type)");
            _resultControllerType ??= TDB.Get().FindType("app.ResultController");
            var runtime = _resultControllerType?.GetRuntimeType();
            var scene = (API.GetNativeSingleton("via.SceneManager") as IObject)?.Call("get_CurrentScene") as IObject;
            if (_findComponents == null || runtime == null || scene == null) return null;

            var all = _findComponents.InvokeBoxed(typeof(object), scene, new object[] { runtime }) as ManagedObject;
            if (FlowHelper.GetListCount(all) == 0) return null;

            var self = FlowHelper.Call(FlowHelper.GetListItem(all, 0), "get_SelfData") as ManagedObject;
            return FlowHelper.GetObjectField(FlowHelper.GetObjectField(self, "Fighter"), "NextLeague");
        }
        catch { return null; }
    }
}
