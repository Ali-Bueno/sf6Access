using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Dedicated reader for the World Tour master-fight pause menu submenus that
/// the generic focus reader handled badly (the main tabs still go through
/// GroupFocusHooks):
/// - Escape / give-up (single-option menu): the generic reader stays silent
///   because focus never moves — announce the question + option on entry.
/// - Item tab: the grid cells only carry owned-count numbers, so read the
///   selected item's name + description from the WTMBattlePauseItem GUI instead.
/// - Perks tab: the rows carry a bare "0" counter before the name, and the
///   tooltip is a WLTAG-composed text — read name + resolved tooltip.
/// - Battle Info tab: a static (non-navigable) panel — announce the enemy and
///   the Drop Lock objective rows once on entry.
/// - Special / Super / Other Moves: the rows carry an "SA {0}" template and the
///   damage/category/description live in the detail window — read the selected
///   row's name/command from its own control and everything else from the
///   param's detail widget (a screen-wide GUI scan mixed other rows' values in).
///
/// All these param types are excluded from GroupFocusHooks, and the generic
/// FocusChanged fallback is suppressed while this reader is active (it spoke
/// the raw "SA {0}" row templates over our announcements).
/// </summary>
public sealed class WTMPauseHooks : ScreenAdapter
{
    private const string SPECIAL = "app.UIFlowWTMPauseMenu.SpecialMoves.Param";
    private const string SUPER = "app.UIFlowWTMPauseMenu.SuperArts.Param";
    private const string OTHER = "app.UIFlowWTMPauseMenu.OtherMoves.Param";
    private const string ITEM = "app.UIFlowWTMPauseMenu.Item.Param";
    private const string ESCAPE = "app.UIFlowWTMPauseMenu.Escape.Param";
    private const string PERK = "app.UIFlowWTMPauseMenu.PerkList.Param";
    private const string BATTLE_INFO = "app.UIFlowWTMPauseMenu.BattleInfo.Param";
    private static readonly string[] Types = { SPECIAL, SUPER, OTHER, ITEM, ESCAPE, PERK, BATTLE_INFO };

    /// <summary>Types this reader owns — excluded from the generic GroupFocus reader.</summary>
    public static readonly string[] OwnedParamTypes = Types;

    public override string[] OwnedTypes => Types;

    private static WTMPauseHooks _self;

    /// <summary>True while a WTM pause submenu is being read — MainMenuHooks
    /// skips the queued focus fallback then (it spoke the "SA {0}" row templates).</summary>
    public static bool IsInWTMPause => _self != null && _self.Active;

    public WTMPauseHooks()
    {
        _self = this;
        SearchInterval = 30;
        ReadInterval = 5;
    }

    private ManagedObject _special, _super, _other, _item, _escape, _perk, _battleInfo;
    private string _lastActive;
    private int _lastMoveIdx = int.MinValue;
    private int _lastTabIdx = int.MinValue;
    private int _lastItemIdx = int.MinValue;
    private int _lastPerkIdx = int.MinValue;
    private bool _escapeAnnounced;
    private bool _battleInfoAnnounced;

    protected override bool Locate()
    {
        var f = FlowHelper.FindFlowParams(Types);
        f.TryGetValue(SPECIAL, out _special);
        f.TryGetValue(SUPER, out _super);
        f.TryGetValue(OTHER, out _other);
        f.TryGetValue(ITEM, out _item);
        f.TryGetValue(ESCAPE, out _escape);
        f.TryGetValue(PERK, out _perk);
        f.TryGetValue(BATTLE_INFO, out _battleInfo);
        return _special != null || _super != null || _other != null || _item != null ||
               _escape != null || _perk != null || _battleInfo != null;
    }

    protected override void OnActivate() => ResetState();

    protected override void OnDeactivate()
    {
        _special = _super = _other = _item = _escape = _perk = _battleInfo = null;
        ResetState();
    }

    private void ResetState()
    {
        _lastActive = null;
        _lastMoveIdx = int.MinValue;
        _lastTabIdx = int.MinValue;
        _lastItemIdx = int.MinValue;
        _lastPerkIdx = int.MinValue;
        _escapeAnnounced = false;
        _battleInfoAnnounced = false;
    }

    protected override void OnPoll()
    {
        // Only one submenu is active at a time; reset the per-submenu cursors when
        // it changes so the new submenu reads its focused row afresh.
        string active =
            _escape != null ? ESCAPE :
            _item != null ? ITEM :
            _perk != null ? PERK :
            _special != null ? SPECIAL :
            _super != null ? SUPER :
            _other != null ? OTHER :
            _battleInfo != null ? BATTLE_INFO : null;
        if (active != _lastActive)
        {
            _lastActive = active;
            _lastMoveIdx = _lastTabIdx = _lastItemIdx = _lastPerkIdx = int.MinValue;
            _escapeAnnounced = false;
            _battleInfoAnnounced = false;
        }

        if (_escape != null) { PollEscape(); return; }
        if (_item != null) { PollItem(); return; }
        if (_perk != null) { PollPerks(); return; }
        if (_special != null) { PollMoves(_special, "ActionSkillList", "ActionSetTypeList", "ActionSkillDetail"); return; }
        if (_super != null) { PollMoves(_super, "ActionSkillList", null, "ActionSkillDetail"); return; }
        if (_other != null) { PollMoves(_other, "mSkillList", "mCategoryTabList", "mSkillDetailWindow"); return; }
        if (_battleInfo != null) { PollBattleInfo(); }
    }

    /// <summary>Give-up confirm: announce the question + the single option once.</summary>
    private void PollEscape()
    {
        if (_escapeAnnounced) return;

        var parts = new List<string>();
        foreach (var t in GuiTextReader.ReadTextsByOwner("WTMBattlePauseEscape"))
        {
            if (string.IsNullOrWhiteSpace(t.Text)) continue;
            string s = t.Text.Replace('\n', ' ').Trim();
            // Title/question first (two e_text_title_tutorial: "Giving Up" + the
            // question), then the confirm option (e_text_0).
            if ((t.Name == "e_text_title_tutorial" || t.Name == "e_text_0") && !parts.Contains(s))
                parts.Add(s);
        }
        if (parts.Count == 0) return;

        _escapeAnnounced = true;
        string msg = string.Join(". ", parts);
        API.LogInfo($"[SF6Access] WTM escape: {msg}");
        ScreenReaderService.Speak(msg, interrupt: true);
    }

    /// <summary>Item tab: announce the selected item's name + description on move.</summary>
    private void PollItem()
    {
        var grid = FlowHelper.GetObjectField(_item, "_lineupGrid");
        int idx = FlowHelper.CallInt(grid, "get_SelectedIndex");
        if (idx < 0 || idx == _lastItemIdx) return;
        _lastItemIdx = idx;

        string name = null, detail = null;
        foreach (var t in GuiTextReader.ReadTextsByOwner("WTMBattlePauseItem"))
        {
            if (string.IsNullOrWhiteSpace(t.Text)) continue;
            string s = t.Text.Replace('\n', ' ').Trim();
            if (t.Name == "e_text_name" && name == null) name = s;          // item title (selected)
            else if (t.Name == "e_text_detail" && detail == null) detail = s; // description
        }
        if (string.IsNullOrEmpty(name)) return;

        string msg = string.IsNullOrEmpty(detail) ? name : $"{name}. {detail}";
        API.LogInfo($"[SF6Access] WTM item [{idx}]: {msg}");
        ScreenReaderService.Speak(msg);
    }

    /// <summary>Perks tab: announce the selected perk's name + its tooltip
    /// (a WLTAG-composed detail text, resolved through the game's word list).</summary>
    private void PollPerks()
    {
        var list = FlowHelper.GetObjectField(_perk, "_scrollList");
        int idx = FlowHelper.CallInt(list, "get_SelectedIndex");
        if (idx < 0 || idx == _lastPerkIdx) return;
        _lastPerkIdx = idx;

        // Only the row's name — its e_txt_num is a bare counter that read as
        // "0. High Voltage" through the generic reader.
        string name = null;
        var item = FlowHelper.Call(list, "get_SelectedItem") as ManagedObject;
        if (item != null)
        {
            foreach (var t in GuiTextReader.ReadControlTexts(item))
            {
                if (t.Name == "e_text_name" && !string.IsNullOrWhiteSpace(t.Text))
                {
                    name = t.Text.Trim();
                    break;
                }
            }
        }
        if (string.IsNullOrEmpty(name)) return;

        string detail = null;
        foreach (var t in GuiTextReader.ReadTextsByOwner("WTMBattlePausePerkList"))
        {
            if (t.Name == "e_text_detail" && !string.IsNullOrWhiteSpace(t.Raw))
            {
                detail = FlowHelper.ResolveWLTags(t.Raw)?.Replace('\n', ' ').Trim();
                break;
            }
        }

        string msg = string.IsNullOrEmpty(detail) ? name : $"{name}. {detail}";
        API.LogInfo($"[SF6Access] WTM perk [{idx}]: {msg}");
        ScreenReaderService.Speak(msg);
    }

    /// <summary>Battle Info tab: a static panel with no focus — announce the enemy
    /// (name + level) and each Drop Lock objective row once, when the texts appear.</summary>
    private void PollBattleInfo()
    {
        if (_battleInfoAnnounced) return;

        var parts = new List<string>();

        string enemyName = null, enemyLevel = null;
        foreach (var t in GuiTextReader.ReadTextsByOwner("WTMBattlePauseBattleInfo"))
        {
            if (t.Name == "e_text_name" && enemyName == null && !string.IsNullOrWhiteSpace(t.Raw))
                enemyName = FlowHelper.ResolveWLTags(t.Raw);   // master name is a WLTAG
            else if (t.Name == "e_text_num" && enemyLevel == null && !string.IsNullOrWhiteSpace(t.Text))
                enemyLevel = t.Text.Trim();
        }
        if (!string.IsNullOrWhiteSpace(enemyName))
            parts.Add(string.IsNullOrEmpty(enemyLevel) ? enemyName : $"{enemyName} {enemyLevel}");

        // Per-row reads keep each objective's progress/target/reward together —
        // the flat GUI scan interleaves them across rows.
        var list = FlowHelper.GetObjectField(_battleInfo, "_seriousItemInfoList");
        var children = FlowHelper.GetObjectField(list, "_Children");
        int count = FlowHelper.GetListCount(children);
        var seen = new HashSet<string>();
        for (int i = 0; i < count; i++)
        {
            var child = FlowHelper.GetListItem(children, i);
            var control = FlowHelper.GetObjectField(child, "Control")
                ?? FlowHelper.Call(child, "get_Control") as ManagedObject;
            if (control == null) continue;

            string objective = null, reward = null, total = null, value = null;
            foreach (var t in GuiTextReader.ReadControlTexts(control, visibleOnly: false))
            {
                if (string.IsNullOrWhiteSpace(t.Text)) continue;
                string s = t.Text.Replace('\n', ' ').Trim();
                switch (t.Name)
                {
                    case "e_text_droplock": objective ??= s; break;
                    case "e_text_head": reward ??= s; break;
                    case "e_text_total": total ??= s; break;
                    // A row carries two value texts; the progress count is the
                    // last one in tree order — keep overwriting.
                    case "e_text_value": value = s; break;
                }
            }
            if (objective == null) continue;

            string row = objective;
            if (value != null && total != null) row += $". {value} / {total}";
            if (reward != null) row += $". {reward}";
            if (seen.Add(row)) parts.Add(row);
        }

        if (parts.Count == 0) return;   // texts not populated yet — retry next tick

        _battleInfoAnnounced = true;
        string msg = string.Join(". ", parts);
        API.LogInfo($"[SF6Access] WTM battle info: {msg}");
        ScreenReaderService.Speak(msg, interrupt: false);   // queued after the tab name
    }

    /// <summary>
    /// Move list (Special / Super / Other): announce the set-type/category tab on
    /// change, and the focused move otherwise — name/command from the selected
    /// row's own control, category/damage/description from the param's detail
    /// widget (which always describes the selected move).
    /// </summary>
    private void PollMoves(ManagedObject param, string listField, string tabField, string detailField)
    {
        // Set-type / category tab
        if (tabField != null)
        {
            var tab = FlowHelper.GetObjectField(param, tabField);
            int tabIdx = FlowHelper.CallInt(tab, "get_SelectedIndex");
            if (tabIdx >= 0 && tabIdx != _lastTabIdx)
            {
                bool first = _lastTabIdx == int.MinValue;
                _lastTabIdx = tabIdx;
                _lastMoveIdx = int.MinValue;   // tab switch re-lays the moves
                if (!first)
                {
                    string tabName = FlowHelper.ReadSelectedItemText(tab);
                    if (!string.IsNullOrEmpty(tabName))
                    {
                        API.LogInfo($"[SF6Access] WTM move tab [{tabIdx}]: {tabName}");
                        ScreenReaderService.Speak(tabName);
                    }
                    return;
                }
            }
        }

        var list = FlowHelper.GetObjectField(param, listField);
        int idx = FlowHelper.CallInt(list, "get_SelectedIndex");
        if (idx < 0 || idx == _lastMoveIdx) return;
        _lastMoveIdx = idx;

        // Name + command from the selected row's own control (hidden variants
        // included), skipping the "SA {0}" template element the rows carry.
        string name = null, command = null;
        var item = FlowHelper.Call(list, "get_SelectedItem") as ManagedObject;
        if (item != null)
        {
            foreach (var t in GuiTextReader.ReadControlTexts(item, visibleOnly: false))
            {
                if (string.IsNullOrWhiteSpace(t.Text) || t.Text.Contains('{')) continue;
                if (t.Name == "e_text_name" && name == null)
                    name = t.Text.Trim();
                else if ((t.Name == "e_text_command" || t.Name == "e_text") && command == null &&
                         !string.IsNullOrWhiteSpace(t.Raw))
                    command = FlowHelper.SpeakableIcons(t.Raw)?.Trim();
            }
        }

        // Category / damage / description from the detail widget — reading them
        // off the whole screen GUI picked up other rows' values ("Flash
        // Knuckle. 700" with Tiger Uppercut's damage).
        string category = null, value = null, comment = null;
        var detail = FlowHelper.GetObjectField(param, detailField);
        var detailControl = FlowHelper.GetObjectField(detail, "Control")
            ?? FlowHelper.Call(detail, "get_Control") as ManagedObject;
        foreach (var t in GuiTextReader.ReadControlTexts(detailControl, visibleOnly: false))
        {
            if (string.IsNullOrWhiteSpace(t.Text) || t.Text.Contains('{')) continue;
            string s = t.Text.Replace('\n', ' ').Trim();
            switch (t.Name)
            {
                case "e_text_name": name ??= s; break;
                case "e_text_category": category ??= s; break;
                case "e_text_value": value ??= s; break;
                case "e_text_comment": comment ??= s; break;
            }
        }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(name)) parts.Add(name);
        if (!string.IsNullOrEmpty(command)) parts.Add(command);
        if (!string.IsNullOrEmpty(category)) parts.Add(category);
        // The "Damage" caption next to the value is a texture (no gui.Text in
        // the dumps) — label the bare number ourselves or it reads as noise.
        // Utility moves show a placeholder 0: skip it.
        if (!string.IsNullOrEmpty(value) && value != "0")
            parts.Add($"{DamageWord()} {value}");
        if (!string.IsNullOrEmpty(comment)) parts.Add(comment);
        if (parts.Count == 0) return;

        string msg = string.Join(". ", parts);
        API.LogInfo($"[SF6Access] WTM move [{idx}]: {msg}");
        ScreenReaderService.Speak(msg);
    }

    private static string DamageWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "Daño",
            FlowHelper.UiLang.Pt => "Dano",
            _ => "Damage",
        };
}
