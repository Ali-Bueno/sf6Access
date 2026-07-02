using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Reads the staff-roll / credits screen (shown after finishing a character's
/// Arcade story). app.UIStaffroll.Param holds LineDataList (each LineData has
/// Str0-Str3 text rows) plus the indices of the lines currently on screen.
/// Announces each credit line once, in order, as it scrolls into view.
/// Migrated to ScreenAdapter.
/// </summary>
public sealed class StaffRollHooks : SingleParamScreenAdapter
{
    protected override string ParamType => "app.UIStaffroll.Param";

    public StaffRollHooks()
    {
        SearchInterval = 30;
        ReadInterval = 12;
    }

    private const int MAX_LINES_PER_POLL = 3;
    private int _lastAnnouncedIndex = -1;

    protected override void OnBind()
    {
        _lastAnnouncedIndex = -1;
        API.LogInfo("[SF6Access] Staff roll started");
    }

    protected override void OnExit()
    {
        _lastAnnouncedIndex = -1;
        API.LogInfo("[SF6Access] Staff roll ended");
    }

    protected override void Poll()
    {
        try
        {
            var list = FlowHelper.GetObjectField(Param, "LineDataList");
            int count = FlowHelper.GetListCount(list);
            if (count == 0) return;

            // Frontier of lines that have scrolled into view (newest at bottom)
            int frontier = FlowHelper.ReadIntField(Param, "BottomDataIndex", -1);
            if (frontier < 0) frontier = FlowHelper.ReadIntField(Param, "TopDataIndex", -1);
            if (frontier < 0) return;
            if (frontier >= count) frontier = count - 1;

            int announced = 0;
            while (_lastAnnouncedIndex < frontier && announced < MAX_LINES_PER_POLL)
            {
                _lastAnnouncedIndex++;
                var line = FlowHelper.GetListItem(list, _lastAnnouncedIndex);
                string text = JoinLine(line);
                if (string.IsNullOrEmpty(text)) continue;

                API.LogInfo($"[SF6Access] Credits [{_lastAnnouncedIndex}]: {text}");
                ScreenReaderService.Speak(text, interrupt: false);
                announced++;
            }
        }
        catch { }
    }

    /// <summary>Join the non-empty Str0-Str3 rows of one credit line.</summary>
    private static string JoinLine(ManagedObject line)
    {
        if (line == null) return null;
        var parts = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            string s = FlowHelper.ReadStringField(line, $"Str{i}");
            if (string.IsNullOrWhiteSpace(s)) continue;
            s = FlowHelper.CleanTags(s).Trim();
            if (!string.IsNullOrEmpty(s) && !parts.Contains(s)) parts.Add(s);
        }
        return parts.Count > 0 ? string.Join(". ", parts) : null;
    }
}
