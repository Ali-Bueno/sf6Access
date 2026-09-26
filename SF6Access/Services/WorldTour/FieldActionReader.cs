using System;
using System.Collections.Generic;
using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>Copies active context prompts while their native HUD is live. No retained wrappers.</summary>
internal static class FieldActionReader
{
    internal readonly record struct Prompt(string Key, string Text, ulong Target, long SeenAt);
    private static readonly List<Prompt> Prompts = new();
    private const string GuideEnum = "app.UIWidgetPlayerActionGuide.GuideDef";
    private static readonly List<Prompt> Requested = new();
    private static readonly Dictionary<int, string> GuideNames = new();
    private static readonly HashSet<string> RequestLogged = new();
    private static long _nextCapture;
    private static bool _logged, _hookLogged;
    internal const long SampleMs = 250; // Human-scale prompt updates, four per second.

    internal static void Capture(ManagedObject hud)
    {
        long now = Environment.TickCount64;
        if (now < _nextCapture) return;
        _nextCapture = now + SampleMs;
        Prompts.Clear();
        object shown = MemberAccess.ReadFieldsFirst(hud, "IsShow", typeof(bool));
        if (!_hookLogged)
        {
            // Never confirmed in game: says whether the prompt HUD is reached at all, and what IsShow reads.
            _hookLogged = true;
            API.LogInfo($"[SF6Access] Exploration action HUD hook fired; IsShow={shown ?? "unreadable"}");
        }
        if (shown is not true) return;
        var list = MemberAccess.ReadFieldsFirst(hud, "_widgetList") as ManagedObject;
        int count = FlowHelper.GetListCount(list);
        for (int i = 0; i < count; i++)
        {
            var widget = FlowHelper.GetListItem(list, i);
            if (widget == null || MemberAccess.ReadFieldsFirst(widget, "IsShow", typeof(bool)) is not true
                || MemberAccess.ReadFieldsFirst(widget, "IsManualHide", typeof(bool)) is true) continue;
            int kind = FlowHelper.ReadIntField(widget, "GuideType", -1);
            string action = FlowHelper.ResolveEnumName("app.UIWidgetPlayerActionGuide.GuideDef", kind);
            string fallback = ExplorationText.Action(action);
            if (fallback == null) continue;
            var target = MemberAccess.ReadFieldsFirst(widget, "AccessTarget") as ManagedObject;
            var parts = MemberAccess.ReadFieldsFirst(widget, "InputGuideParts");
            var control = MemberAccess.ReadFieldsFirst(parts, "Control") as ManagedObject;
            string text = GuiTextReader.ReadControlTextJoined(control);
            // Pure button icons cannot name an action; append the semantic fallback then.
            if (string.IsNullOrWhiteSpace(text)) text = fallback;
            string name = FlowHelper.Call(target, "GetDispName") as string;
            if (!string.IsNullOrWhiteSpace(name)) text = FlowHelper.CleanTags(name) + ", " + text;
            ulong id = target?.GetAddress() ?? widget.GetAddress();
            Prompts.Add(new Prompt(id + ":" + action, text, target?.GetAddress() ?? 0, now));
        }
        if (!_logged)
        {
            _logged = true;
            API.LogInfo($"[SF6Access] Exploration action HUD: {count} widgets, {Prompts.Count} active readable prompts");
        }
    }

    /// <summary>The game asked its prompt HUD to show action <paramref name="guide"/> (<c>RequestShowFromPlayer</c> /
    /// <c>RequestShowFromOM</c>). The HUD's own <c>IsShow</c> never read true in game (2026-09-26), so the request
    /// is the reliable moment an action such as Climb becomes available.</summary>
    internal static void NoteRequest(int guide)
    {
        if (!GuideNames.TryGetValue(guide, out string action))
            GuideNames[guide] = action = FlowHelper.ResolveEnumName(GuideEnum, guide) ?? guide.ToString();
        if (RequestLogged.Add(action)) API.LogInfo($"[SF6Access] Exploration: the game requested action prompt {action}");
        string text = ExplorationText.Action(action);
        if (text == null) return;
        string key = "request:" + action;
        Requested.RemoveAll(p => p.Key == key);
        Requested.Add(new Prompt(key, text, 0, Environment.TickCount64));
    }

    internal static IReadOnlyList<Prompt> Current(long now)
    {
        // A destroyed/hidden HUD stops feeding snapshots, and a request not renewed has gone; never speak either.
        if (Prompts.Count > 0 && now - Prompts[0].SeenAt > 2 * SampleMs) Prompts.Clear();
        Requested.RemoveAll(p => now - p.SeenAt > 2 * SampleMs);
        if (Requested.Count == 0) return Prompts;
        var all = new List<Prompt>(Prompts);
        all.AddRange(Requested);
        return all;
    }

    internal static bool AtMissionContact()
    {
        try { return ReadMissionContact(); }
        catch { return false; }
    }

    private static bool ReadMissionContact()
    {
        var guide = MissionGuideReader.Read();
        if (!guide.Ok || guide.AccessTarget == null) return false;
        ulong wanted = guide.AccessTarget.GetAddress();
        var list = AvatarFieldReader.GetAccessInfoList(WorldTourStateService.GetAvatarManager());
        int count = FlowHelper.GetListCount(list);
        for (int i = 0; i < count; i++)
        {
            var info = MemberAccess.ReadFieldsFirst(FlowHelper.GetListItem(list, i), "TargetInfo");
            var target = MemberAccess.ReadFieldsFirst(info, "Target") as ManagedObject;
            if (target?.GetAddress() == wanted) return true;
        }
        return false;
    }

    internal static void Reset() { Prompts.Clear(); Requested.Clear(); _nextCapture = 0; }
}
