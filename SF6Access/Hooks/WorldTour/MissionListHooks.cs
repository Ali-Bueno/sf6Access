using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// The phone's Missions app — the mission list (<c>app.UIFlowUI50600.Param</c>).
///
/// <para>Note the namespace: this screen is <c>app.UIFlowUI50600</c>, not
/// <c>app.worldtour</c>, even though everything it shows is World Tour data.</para>
///
/// <para><b>Read the data, not the widgets.</b> The two scroll lists on this
/// screen (<c>PartsScrollListMissionEntry</c>, <c>PartsScrollListMissionInfo</c>)
/// are <c>UIPartsScrollList</c>, which has no <c>_Children</c> to walk — the same
/// trap documented for other scroll lists in this codebase. But it does not
/// matter here, because the highlighted mission is available as data:
/// <c>CurrentSelectMissionInfo</c> is a <c>WTMissionDeviceInfo</c> that answers
/// everything worth saying. Reading the GUI text instead would also have meant
/// de-duplicating it, since the same chapter/progress/name triple is rendered
/// twice — once in the row, once in the preview pane.</para>
///
/// <para><b>Methods, not properties.</b> <c>WTMissionDeviceInfo</c> exposes
/// <c>GetTitleMessage()</c> / <c>GetChapterNo()</c> / <c>GetProgressRate()</c> as
/// calls. There is no <c>get_TitleMessage</c> to read.</para>
/// </summary>
public sealed class MissionListHooks : SingleParamScreenAdapter
{
    protected override string ParamType => "app.UIFlowUI50600.Param";

    // The category tabs, from the screen's own TabCategoryType enum.
    private static readonly string[] TabNames = { "All", "Main", "Master", "Collection" };

    private int _lastTab = int.MinValue;
    private string _lastMission;

    protected override void OnBind()
    {
        _lastTab = int.MinValue;
        _lastMission = null;
        API.LogInfo("[SF6Access] Mission list active");
    }

    protected override void Poll()
    {
        AnnounceTab();
        AnnounceMission();
    }

    private void AnnounceTab()
    {
        int tab = FlowHelper.ReadIntField(Param, "CurrentTabCategory", int.MinValue);
        if (tab == int.MinValue || tab == _lastTab) return;

        bool first = _lastTab == int.MinValue;
        _lastTab = tab;
        // The mission announcement below re-fires on the new tab anyway.
        _lastMission = null;
        if (first) return;   // the opening tab is implied by opening the app

        if (tab >= 0 && tab < TabNames.Length) Speak(TabNames[tab]);
    }

    private void AnnounceMission()
    {
        // An interface getter, not a field: this one dispatches correctly.
        var info = FlowHelper.Call(Param, "get_CurrentSelectMissionInfo") as ManagedObject;
        if (info == null) return;

        string title = FlowHelper.CleanTags(FlowHelper.Call(info, "GetTitleMessage") as string)?.Trim();
        if (string.IsNullOrEmpty(title)) return;

        string chapter = FlowHelper.CleanTags(FlowHelper.Call(info, "GetChapterNo") as string)?.Trim();
        string status = Status(info);

        // Chapter first, the way the screen lays it out, then the name, then how
        // far along it is — so the name lands in the middle where it is heard
        // even if the reader is interrupted at either end.
        string spoken = string.IsNullOrEmpty(chapter) ? title : $"{chapter}, {title}";
        if (!string.IsNullOrEmpty(status)) spoken = $"{spoken}, {status}";

        if (spoken == _lastMission) return;
        _lastMission = spoken;
        Speak(spoken);
    }

    /// <summary>Cleared / locked / how far along, preferring the game's own
    /// progress figure over our own words for it.</summary>
    private static string Status(ManagedObject info)
    {
        if (FlowHelper.Call(info, "IsCleared") is bool cleared && cleared)
            return LocalizedText.MissionCleared();
        if (FlowHelper.Call(info, "IsAccepted") is bool accepted && !accepted)
            return LocalizedText.MissionNotAccepted();

        string rate = FlowHelper.CleanTags(FlowHelper.Call(info, "GetProgressRate") as string)?.Trim();
        return string.IsNullOrEmpty(rate) ? "" : rate;
    }
}
