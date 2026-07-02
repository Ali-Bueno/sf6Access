using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Dedicated reader for the parts of the World Tour master-fight pause menu that
/// the generic focus reader handled badly (the tabs, Perk list and Battle Info
/// still go through GroupFocusHooks):
/// - Escape / give-up (single-option menu): the generic reader stays silent
///   because focus never moves — announce the question + option on entry.
/// - Item tab: the grid cells only carry owned-count numbers, so read the
///   selected item's name + description from the WTMBattlePauseItem GUI instead.
/// - Special / Super / Other Moves: the list item's own control holds a "SA {0}"
///   template, so read the move name + command from it (skipping the template),
///   plus the description (e_text_comment) from the screen GUI.
///
/// The move-list / item / escape param types are excluded from GroupFocusHooks so
/// they don't double-read.
/// </summary>
public sealed class WTMPauseHooks : ScreenAdapter
{
    private const string SPECIAL = "app.UIFlowWTMPauseMenu.SpecialMoves.Param";
    private const string SUPER = "app.UIFlowWTMPauseMenu.SuperArts.Param";
    private const string OTHER = "app.UIFlowWTMPauseMenu.OtherMoves.Param";
    private const string ITEM = "app.UIFlowWTMPauseMenu.Item.Param";
    private const string ESCAPE = "app.UIFlowWTMPauseMenu.Escape.Param";
    private static readonly string[] Types = { SPECIAL, SUPER, OTHER, ITEM, ESCAPE };

    /// <summary>Types this reader owns — excluded from the generic GroupFocus reader.</summary>
    public static readonly string[] OwnedParamTypes = Types;

    public override string[] OwnedTypes => Types;

    public WTMPauseHooks()
    {
        SearchInterval = 30;
        ReadInterval = 5;
    }

    private ManagedObject _special, _super, _other, _item, _escape;
    private string _lastActive;
    private int _lastMoveIdx = int.MinValue;
    private int _lastTabIdx = int.MinValue;
    private int _lastItemIdx = int.MinValue;
    private bool _escapeAnnounced;

    protected override bool Locate()
    {
        var f = FlowHelper.FindFlowParams(Types);
        f.TryGetValue(SPECIAL, out _special);
        f.TryGetValue(SUPER, out _super);
        f.TryGetValue(OTHER, out _other);
        f.TryGetValue(ITEM, out _item);
        f.TryGetValue(ESCAPE, out _escape);
        return _special != null || _super != null || _other != null || _item != null || _escape != null;
    }

    protected override void OnActivate() => ResetState();

    protected override void OnDeactivate()
    {
        _special = _super = _other = _item = _escape = null;
        ResetState();
    }

    private void ResetState()
    {
        _lastActive = null;
        _lastMoveIdx = int.MinValue;
        _lastTabIdx = int.MinValue;
        _lastItemIdx = int.MinValue;
        _escapeAnnounced = false;
    }

    protected override void OnPoll()
    {
        // Only one submenu is active at a time; reset the per-submenu cursors when
        // it changes so the new submenu reads its focused row afresh.
        string active =
            _escape != null ? ESCAPE :
            _item != null ? ITEM :
            _special != null ? SPECIAL :
            _super != null ? SUPER :
            _other != null ? OTHER : null;
        if (active != _lastActive)
        {
            _lastActive = active;
            _lastMoveIdx = _lastTabIdx = _lastItemIdx = int.MinValue;
            _escapeAnnounced = false;
        }

        if (_escape != null) { PollEscape(); return; }
        if (_item != null) { PollItem(); return; }
        if (_special != null) { PollMoves(_special, "ActionSkillList", "ActionSetTypeList", "WTMBattlePauseSpecialMoves"); return; }
        if (_super != null) { PollMoves(_super, "ActionSkillList", null, "WTMBattlePauseSuperArts"); return; }
        if (_other != null) { PollMoves(_other, "mSkillList", "mCategoryTabList", "WTMBattlePauseOtherMoves"); }
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

    /// <summary>
    /// Move list (Special / Super / Other): announce the set-type/category tab on
    /// change, and the focused move's name + command + description otherwise.
    /// </summary>
    private void PollMoves(ManagedObject param, string listField, string tabField, string guiOwner)
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

        // Move name + command from the selected item's own control, skipping the
        // "SA {0}" template element the list item carries.
        string name = null, command = null;
        var item = FlowHelper.Call(list, "get_SelectedItem") as ManagedObject;
        if (item != null)
        {
            foreach (var t in GuiTextReader.ReadControlTexts(item))
            {
                if (t.Name == "e_text_name" && name == null &&
                    !string.IsNullOrWhiteSpace(t.Text) && !t.Text.Contains("{"))
                    name = t.Text.Trim();
                else if (t.Name == "e_text_command" && command == null && !string.IsNullOrWhiteSpace(t.Raw))
                    command = FlowHelper.SpeakableIcons(t.Raw)?.Trim();
            }
        }

        // Description / damage / category from the detail shown in the screen GUI.
        string comment = null, value = null, category = null;
        foreach (var t in GuiTextReader.ReadTextsByOwner(guiOwner))
        {
            if (string.IsNullOrWhiteSpace(t.Text)) continue;
            string s = t.Text.Replace('\n', ' ').Trim();
            if (t.Name == "e_text_name" && name == null && !s.Contains("{")) name = s;
            else if (t.Name == "e_text_comment" && comment == null) comment = s;
            else if (t.Name == "e_text_value" && value == null) value = s;
            else if (t.Name == "e_text_category" && category == null) category = s;
        }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(name)) parts.Add(name);
        if (!string.IsNullOrEmpty(command)) parts.Add(command);
        if (!string.IsNullOrEmpty(category)) parts.Add(category);
        if (!string.IsNullOrEmpty(value)) parts.Add(value);
        if (!string.IsNullOrEmpty(comment)) parts.Add(comment);
        if (parts.Count == 0) return;

        string msg = string.Join(". ", parts);
        API.LogInfo($"[SF6Access] WTM move [{idx}]: {msg}");
        ScreenReaderService.Speak(msg);
    }
}
