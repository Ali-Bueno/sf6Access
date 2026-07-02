using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the World Tour post-fight avatar result screen
/// (app.UIFlowAvatarResult.Param): the EXP gained and level-up shown on entry,
/// plus the reward lists (level-up gains / skills / items) as you navigate them.
///
/// The EXP/level numbers live only in the screen's via.gui.Text widgets (there
/// are no int fields on the Param) and they animate in, so the summary is read
/// once it settles (two consecutive equal reads) or after a timeout. The three
/// reward lists are UIPartsScrollList (_Level_ScrollList / _Skill_ScrollList /
/// _Item_ScrollList) read by the shared group poller.
/// </summary>
public sealed class AvatarResultHooks : SingleParamScreenAdapter
{
    private const string RESULT_GUI = "AvatarResult";

    protected override string ParamType => "app.UIFlowAvatarResult.Param";

    public AvatarResultHooks()
    {
        SearchInterval = 30;
        ReadInterval = 5;
    }

    private bool _announced;
    private string _lastSummary;
    private int _retries;

    private readonly GroupFocusPoller _rewards = new(
        "AvatarResult", announceFirst: false,
        new GroupFocusPoller.Source(null, "_Level_ScrollList", isList: true),
        new GroupFocusPoller.Source(null, "_Skill_ScrollList", isList: true),
        new GroupFocusPoller.Source(null, "_Item_ScrollList", isList: true));

    protected override void OnBind()
    {
        _announced = false;
        _lastSummary = null;
        _retries = 40;   // the EXP/level numbers animate in over ~1s
        _rewards.Reset();
        API.LogInfo("[SF6Access] Avatar result active");
    }

    protected override void OnExit()
    {
        _announced = false;
        _lastSummary = null;
        _rewards.Reset();
    }

    protected override void Poll()
    {
        if (!_announced) AnnounceSummary();
        _rewards.Poll(Param);
    }

    /// <summary>Announce "EXP. {current}. {gained}. Level Up. {new level}" once the
    /// animated numbers settle (or after a timeout), from the screen's GUI texts.</summary>
    private void AnnounceSummary()
    {
        string summary = BuildSummary();
        _retries--;

        bool stable = !string.IsNullOrEmpty(summary) && summary == _lastSummary;
        _lastSummary = summary;

        if (stable || (_retries <= 0 && !string.IsNullOrEmpty(summary)))
        {
            _announced = true;
            API.LogInfo($"[SF6Access] Avatar result: {summary}");
            ScreenReaderService.Speak(summary, interrupt: true);
            return;
        }
        if (_retries <= 0) _announced = true;   // never got readable text — stop trying
    }

    /// <summary>
    /// The result title plus the EXP/level texts (e_txt_title=EXP, the current and
    /// gained values, e_text_title=Level Up, the new level), joined in on-screen
    /// order. Empty until the screen's texts populate.
    /// </summary>
    private string BuildSummary()
    {
        var parts = new List<string>();

        string title = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(Param, "_Window_TitleText"));
        if (!string.IsNullOrWhiteSpace(title)) parts.Add(title.Trim());

        foreach (var t in GuiTextReader.ReadTextsByOwner(RESULT_GUI))
        {
            if (string.IsNullOrWhiteSpace(t.Text) || t.Name == null) continue;
            // Keep the EXP/level titles and their number values, in tree order.
            if (!t.Name.Contains("title") && !t.Name.Contains("value")) continue;
            string s = t.Text.Trim();
            if (!parts.Contains(s)) parts.Add(s);
        }

        return parts.Count > 0 ? string.Join(". ", parts) : null;
    }
}
