using System;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the move list / command list (app.UICommandListWindow).
/// The detail window tracks the focused skill (CurrentSkillId); each skill's
/// FighterSkillUIData exposes localized Guids for name, command inputs
/// (classic and modern) and description. Migrated to ScreenAdapter.
/// </summary>
public sealed class CommandListHooks : SingleParamScreenAdapter
{
    protected override string ParamType => "app.UICommandListWindow.CommandListParam";

    public CommandListHooks()
    {
        SearchInterval = 60;
        ReadInterval = 5;
    }

    private ManagedObject _detailWindow;
    private ManagedObject _categoryTabList;

    private uint _lastSkillId;
    private int _lastCategoryIdx = -1;
    // Currently displayed control type: -1 unknown, 0 Classic, 1 Modern.
    // The command list has an input-type tab; switching it changes which command
    // notation each skill shows (Classic vs Modern), so re-announce on change.
    private int _lastInputType = -1;

    protected override void OnBind()
    {
        _detailWindow = FlowHelper.GetObjectField(Param, "mDetailWindow");
        _categoryTabList = FlowHelper.GetObjectField(Param, "mCategoryTabList");
        _lastSkillId = 0;
        _lastCategoryIdx = -1;
        _lastInputType = -1;

        API.LogInfo($"[SF6Access] Command list active (detail={_detailWindow != null}, " +
            $"categoryTab={_categoryTabList != null})");

        PollSkill();
    }

    protected override void OnExit()
    {
        API.LogInfo("[SF6Access] Command list ended");
        _detailWindow = null;
        _categoryTabList = null;
        _lastSkillId = 0;
        _lastCategoryIdx = -1;
        _lastInputType = -1;
    }

    protected override void Poll()
    {
        PollCategory();
        PollInputType();
        PollSkill();
    }

    /// <summary>Current control type displayed by the list: 1 Modern (casual), 0 Classic.</summary>
    private int GetInputType()
    {
        try
        {
            var raw = FlowHelper.Call(Param, "get_IsCasual");
            if (raw is bool casual) return casual ? 1 : 0;
        }
        catch { }
        return -1;
    }

    /// <summary>
    /// Re-announce the focused move when the input-type tab is switched
    /// (Classic ↔ Modern): the command notation changes even though the skill
    /// does not, so the previously announced command would be stale.
    /// </summary>
    private void PollInputType()
    {
        int type = GetInputType();
        if (type < 0 || type == _lastInputType) return;

        bool first = _lastInputType == -1;
        _lastInputType = type;
        if (first) return;

        AnnounceSkill();
    }

    private void PollCategory()
    {
        if (_categoryTabList == null) return;

        int idx = FlowHelper.CallInt(_categoryTabList, "get_SelectedIndex");
        if (idx < 0 || idx == _lastCategoryIdx) return;

        bool first = _lastCategoryIdx == -1;
        _lastCategoryIdx = idx;
        if (first) return;

        string name = ResolveCategoryName(idx);
        if (string.IsNullOrEmpty(name)) return;

        API.LogInfo($"[SF6Access] Command list category [{idx}]: {name}");
        ScreenReaderService.Speak(name);
    }

    private string ResolveCategoryName(int listIndex)
    {
        try
        {
            // CategoryMessageList entries hold the localized category name Guid
            var msgList = FlowHelper.GetObjectField(Param, "CategoryMessageList");
            var entry = FlowHelper.GetListItem(msgList, listIndex);
            if (entry == null) return null;

            foreach (var field in new[] { "Message", "_Message", "Name", "_Name" })
            {
                string resolved = FlowHelper.ResolveGuidField(entry, field);
                if (!string.IsNullOrEmpty(resolved)) return resolved;
            }
        }
        catch { }
        return null;
    }

    private void PollSkill()
    {
        if (_detailWindow == null) return;

        uint skillId = 0;
        try
        {
            var raw = FlowHelper.Call(_detailWindow, "get_CurrentSkillId");
            if (raw != null) skillId = Convert.ToUInt32(raw);
        }
        catch { }
        if (skillId == 0)
            skillId = (uint)FlowHelper.ReadIntField(_detailWindow, "CurrentSkillId", 0);

        if (skillId == 0 || skillId == _lastSkillId) return;

        bool first = _lastSkillId == 0;
        _lastSkillId = skillId;
        if (first) return;

        AnnounceSkill();
    }

    private void AnnounceSkill()
    {
        var skill = FlowHelper.GetObjectField(_detailWindow, "CurrentSkill")
                 ?? FlowHelper.Call(_detailWindow, "get_CurrentSkill") as ManagedObject;
        if (skill == null) return;

        string name = FlowHelper.ResolveGuidField(skill, "NameMessageId");
        string command = ResolveCommandText(skill);
        string description = FlowHelper.ResolveGuidField(skill, "DescriptionMessageId");

        if (string.IsNullOrEmpty(name))
            name = FlowHelper.ReadStringField(skill, "DisplayName");

        var parts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(name)) parts.Add(name);
        if (!string.IsNullOrEmpty(command)) parts.Add(command);
        if (parts.Count == 0) return;

        string announcement = string.Join(". ", parts);
        API.LogInfo($"[SF6Access] Move: {announcement}");
        ScreenReaderService.Speak(announcement);

        if (!string.IsNullOrEmpty(description))
            ScreenReaderService.Speak(description, interrupt: false);
    }

    /// <summary>
    /// Resolve the command input text for the control type the list is currently
    /// showing. Each skill carries separate command Guids per control type:
    /// NormalCommandMessage = Classic, CasualCommandMessage = Modern, and
    /// CasualManualCommandMessage = Modern when the manual-command notation is
    /// toggled on. Reading NormalCommandMessage unconditionally showed Classic
    /// inputs even on Modern controls. Icon-tag tokens resolve through the
    /// compact FGC map first (LP/MP...), then the localized command vocabulary
    /// (directions, motions, SM/SA buttons).
    /// </summary>
    private string ResolveCommandText(ManagedObject skill)
    {
        foreach (var field in GetCommandFieldOrder())
        {
            try
            {
                var td = skill.GetTypeDefinition();
                var f = td?.GetField($"<{field}>k__BackingField") ?? td?.GetField(field);
                if (f == null) continue;

                var raw = f.GetDataBoxed(typeof(Guid), skill.GetAddress(), false);
                if (raw is not REFrameworkNET.ValueType vt) continue;

                string resolved = ResolveGuidRaw(vt);
                if (string.IsNullOrWhiteSpace(resolved)) continue;

                API.LogInfo($"[SF6Access] Command raw ({field}): {resolved}");
                return HumanizeCommandTags(resolved);
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Command Guid fields to try, ordered by the control type the list is
    /// currently showing. Modern (casual) prefers the manual notation when that
    /// toggle is on; Classic uses the normal command. Other types are kept as
    /// fallbacks so a move with only one notation still reads something.
    /// </summary>
    private string[] GetCommandFieldOrder()
    {
        bool casual = GetInputType() == 1;
        if (!casual)
            return new[] { "NormalCommandMessage", "CasualCommandMessage", "SupplementCommandMessage" };

        bool manual = false;
        try
        {
            var raw = FlowHelper.Call(Param, "get_DispCasualManualCommand");
            if (raw is bool b) manual = b;
        }
        catch { }

        return manual
            ? new[] { "CasualManualCommandMessage", "CasualCommandMessage", "NormalCommandMessage", "SupplementCommandMessage" }
            : new[] { "CasualCommandMessage", "CasualManualCommandMessage", "NormalCommandMessage", "SupplementCommandMessage" };
    }

    private static string ResolveGuidRaw(REFrameworkNET.ValueType vt)
    {
        // ResolveGuid in FlowHelper strips tags; for commands we need the raw string
        var method = TDB.Get().FindType("via.gui.message")?.GetMethod("get(System.Guid)");
        if (method == null) return null;
        try
        {
            var task = System.Threading.Tasks.Task.Run(() =>
            {
                try { return method.InvokeBoxed(typeof(string), null, new object[] { vt }) as string; }
                catch { return null; }
            });
            if (task.Wait(TimeSpan.FromMilliseconds(200)))
                return task.Result?.Trim();
        }
        catch { }
        return null;
    }

    // Button/token names inside command icon tags → compact FGC notation
    private static readonly System.Collections.Generic.Dictionary<string, string> CommandTokens = new()
    {
        { "LowP", "LP" }, { "MidP", "MP" }, { "HighP", "HP" },
        { "LowK", "LK" }, { "MidK", "MK" }, { "HighK", "HK" },
        { "Punch", "P" }, { "Kick", "K" },
        { "LowPK", "LP LK" }, { "MidPK", "MP MK" }, { "HighPK", "HP HK" },
    };

    private static string HumanizeCommandTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        string result = System.Text.RegularExpressions.Regex.Replace(text, @"<([^>]+)>", match =>
        {
            var words = new System.Collections.Generic.List<string>();
            foreach (var token in match.Groups[1].Value.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
            {
                // Drop tag-type keywords, keep only meaningful content
                if (token is "CMD" or "CMDBTN" or "ICON" or "BTN") continue;

                string t = token.TrimStart('_');
                if (CommandTokens.TryGetValue(t, out var mapped))
                {
                    words.Add(mapped);
                }
                else
                {
                    // Localized command vocabulary (same wording as tutorials/
                    // combo trials): "236" → "quarter circle forward", "2" →
                    // "down", "SM" → "special move" — raw tokens like "2 SM"
                    // were meaningless on Modern controls.
                    string spoken = FlowHelper.SpeakInputToken(t);
                    words.Add(string.IsNullOrEmpty(spoken) ? t : spoken);
                }
            }
            return " " + string.Join(" ", words) + " ";
        });

        return System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ").Trim();
    }
}
