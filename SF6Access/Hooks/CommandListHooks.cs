using System;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the move list / command list (app.UICommandListWindow).
/// The detail window tracks the focused skill (CurrentSkillId); each skill's
/// FighterSkillUIData exposes localized Guids for name, command inputs
/// (classic and modern) and description.
/// </summary>
public class CommandListHooks
{
    private const string PARAM_TYPE = "app.UICommandListWindow.CommandListParam";

    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    private static ManagedObject _param;
    private static ManagedObject _detailWindow;
    private static ManagedObject _categoryTabList;

    private static uint _lastSkillId;
    private static int _lastCategoryIdx = -1;

    public static bool IsInCommandList => _isActive;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] CommandListHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (!_isActive)
        {
            if (_pollCounter % POLL_SEARCH_INTERVAL != 0) return;
            TryActivate();
            return;
        }

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var current = FlowHelper.TrackFlowParam(PARAM_TYPE, _param, out bool changed);
            if (current == null)
            {
                Reset();
                return;
            }
            if (changed)
                TryActivate(); // menu was recreated — re-bind param and child caches
        }

        if (_pollCounter % POLL_READ_INTERVAL == 0)
        {
            PollCategory();
            PollSkill();
        }
    }

    private static void TryActivate()
    {
        var param = FlowHelper.FindFlowParam(PARAM_TYPE);
        if (param == null) return;

        _param = param;
        _detailWindow = FlowHelper.GetObjectField(param, "mDetailWindow");
        _categoryTabList = FlowHelper.GetObjectField(param, "mCategoryTabList");
        _lastSkillId = 0;
        _lastCategoryIdx = -1;
        _isActive = true;

        API.LogInfo($"[SF6Access] Command list active (detail={_detailWindow != null}, " +
            $"categoryTab={_categoryTabList != null})");

        PollSkill();
    }

    private static void PollCategory()
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

    private static string ResolveCategoryName(int listIndex)
    {
        try
        {
            // CategoryMessageList entries hold the localized category name Guid
            var msgList = FlowHelper.GetObjectField(_param, "CategoryMessageList");
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

    private static void PollSkill()
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

    private static void AnnounceSkill()
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
    /// Resolve the command input text. Icon tags are kept as their inner names
    /// (e.g. "<ICON arrow_236>" becomes "arrow_236") until a proper mapping exists.
    /// </summary>
    private static string ResolveCommandText(ManagedObject skill)
    {
        foreach (var field in new[] { "NormalCommandMessage", "CasualCommandMessage", "SupplementCommandMessage" })
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
                    words.Add(mapped);
                else
                    words.Add(t);
            }
            return " " + string.Join(" ", words) + " ";
        });

        return System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ").Trim();
    }

    private static void Reset()
    {
        API.LogInfo("[SF6Access] Command list ended");
        _isActive = false;
        _param = null;
        _detailWindow = null;
        _categoryTabList = null;
        _lastSkillId = 0;
        _lastCategoryIdx = -1;
    }
}
