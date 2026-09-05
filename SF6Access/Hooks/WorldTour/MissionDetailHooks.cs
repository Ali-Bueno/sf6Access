using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// The phone's mission DETAIL popup (<c>app.UIFlowUI50613.Param</c>) — the panel
/// that opens on a mission from the list, showing its objective, description and
/// rewards.
///
/// <para>It carries a single field, <c>MissionDeviceInfo</c>, and everything on
/// screen comes off that one object. It is also SHORT-LIVED: in the capture it
/// opened and closed inside a second, and the auto-dump caught its param already
/// dead. So this adapter polls fast and announces on bind rather than waiting to
/// see a selection change — by the time a change could be observed, the screen
/// may be gone.</para>
/// </summary>
public sealed class MissionDetailHooks : SingleParamScreenAdapter
{
    protected override string ParamType => "app.UIFlowUI50613.Param";

    public MissionDetailHooks()
    {
        // Fast, because the popup can be gone within a second.
        SearchInterval = 15;
        ReadInterval = 5;
    }

    private string _lastSpoken;

    protected override void OnBind()
    {
        _lastSpoken = null;
        Announce();
    }

    protected override void OnExit() => _lastSpoken = null;

    protected override void Poll() => Announce();

    private void Announce()
    {
        var info = FlowHelper.GetObjectField(Param, "MissionDeviceInfo")
                   ?? FlowHelper.Call(Param, "get_MissionDeviceInfo") as ManagedObject;
        if (info == null) return;

        var parts = new List<string>();
        Add(parts, FlowHelper.Call(info, "GetChapterNo") as string);
        Add(parts, FlowHelper.Call(info, "GetTitleMessage") as string);
        Add(parts, FlowHelper.Call(info, "GetDetailMessage") as string);

        if (parts.Count == 0) return;

        string spoken = string.Join(". ", parts);
        if (spoken == _lastSpoken) return;
        _lastSpoken = spoken;
        API.LogInfo($"[SF6Access] Mission detail: {spoken}");
        Speak(spoken);
    }

    private static void Add(List<string> parts, string raw)
    {
        string text = FlowHelper.CleanTags(raw)?.Trim();
        if (!string.IsNullOrEmpty(text)) parts.Add(text);
    }
}
