using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Options → User Settings → Change Flag (app.UICFNSelectHomeDialog.FlowParam).
/// The continent tabs (_CategoryTabSimpleList) already read through the generic
/// focus reader; the flag grid (_NationalFlagScrollGrid) is pictures only, so the
/// focused cell's index is looked up in CurrentHomeList (List&lt;AppDefine.CfnHome&gt;,
/// the home ids of the open tab) and named through HomeCountryResolver.
/// Registered in ScreenRegistry.
/// </summary>
public sealed class HomeSelectHooks : SingleParamScreenAdapter
{
    protected override string ParamType => "app.UICFNSelectHomeDialog.FlowParam";

    private ulong _lastList;
    private int _lastIndex = -2;

    public HomeSelectHooks()
    {
        SearchInterval = 20;
        ReadInterval = 5;
    }

    protected override void OnBind()
    {
        _lastList = 0;
        _lastIndex = -2;
    }

    protected override void Poll()
    {
        var list = FlowHelper.GetObjectField(Param, "CurrentHomeList");
        int idx = FlowHelper.CallInt(FlowHelper.GetObjectField(Param, "_NationalFlagScrollGrid"), "get_SelectedIndex");
        if (list == null || idx < 0) return;

        ulong listAddr = FlowHelper.AddressOf(list);
        bool newList = listAddr != _lastList;
        if (!newList && idx == _lastIndex) return;

        string name = HomeCountryResolver.Resolve(FlowHelper.ReadIntListItem(list, idx));
        if (string.IsNullOrWhiteSpace(name)) return;

        if (newList) LogList(list);
        _lastList = listAddr;
        _lastIndex = idx;

        API.LogInfo($"[SF6Access] Home select [{idx}]: {name}");
        // A new list means the continent tab changed: queue after the tab name.
        ScreenReaderService.Speak(name.Trim(), interrupt: !newList);
    }

    /// <summary>The home ids a tab offers, so ids missing from the name table show up in the log.</summary>
    private static void LogList(ManagedObject list)
    {
        var ids = new List<int>();
        int n = FlowHelper.GetListCount(list);
        for (int i = 0; i < n; i++) ids.Add(FlowHelper.ReadIntListItem(list, i));
        API.LogInfo($"[SF6Access] Home select list ({n}): {string.Join(",", ids)}");
    }
}
