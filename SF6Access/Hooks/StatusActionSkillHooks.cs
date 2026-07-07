using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the Status menu Special Moves and Super Arts tabs (both are
/// app.UIStatusMenu_ActionSkillEquipBase): a "Move Set" slot list on the left and
/// a "Moves Learned" list on the right, with a detail panel. The generic group
/// reader announced these inconsistently (and leaked the unfilled "SA {0}" label),
/// so they are read here from the typed widgets instead.
///
/// Avatar Training uses a parallel widget family (UIAvatarTrainingDummyStatusMenu_*)
/// with the SAME field/method names and eMenuState values, so the same logic reads
/// it once its param types are watched too.
///
/// ScreenAdapter (multi-Param, priority order). SearchInterval = ReadInterval = 5:
/// equipping a move can recreate the Param mid-screen, so the live instance is
/// re-found every read tick (stale instance read nothing / close detection must
/// stay fast). Registered in ScreenRegistry.
/// </summary>
public sealed class StatusActionSkillHooks : ScreenAdapter
{
    // All four tabs share the ActionSkillEquipBase shape (MenuState +
    // mSkillPanelList_Set/_Select + mSkillDetail), so the same field/method names
    // work for every param. The avatar-training dummy menu is a separate type
    // family, hence its own entries.
    private static readonly string[] ParamTypes =
    {
        "app.UIStatusMenu_SpecialMoves.Param",
        "app.UIStatusMenu_SuperArts.Param",
        "app.UIAvatarTrainingDummyStatusMenu_SpecialMoves.Param",
        "app.UIAvatarTrainingDummyStatusMenu_SuperArts.Param",
    };

    public override string[] OwnedTypes => ParamTypes;

    // eMenuState: 0=SET_LIST (Move Set slots), 1=CHOICE_LIST (Moves Learned),
    // 2=ATTENTION (confirm popup), 4=CHARGESKILL_ATTENTION.
    private const int STATE_SET = 0;
    private const int STATE_CHOICE = 1;
    private const int STATE_ATTENTION = 2;
    private const int STATE_CHARGE_ATTENTION = 4;

    public StatusActionSkillHooks()
    {
        SearchInterval = 5;
        ReadInterval = 5;
    }

    private ManagedObject _param;
    private int _lastState = -2;
    private int _lastIndex = -2;
    private string _lastText;
    private bool _attentionOpen;
    private string _lastAttentionButton;
    private int _loggedState = -99;
    private int _lastSetType = int.MinValue;  // WTActionSkillSetType tab (Grounded/Air/Super Arts)
    private int _lastCountNow = int.MinValue;  // equipped-slot counter (now / max)
    private int _lastCountMax = int.MinValue;
    private bool _lastFull;

    protected override bool Locate()
    {
        var found = FlowHelper.FindFlowParams(ParamTypes);
        ManagedObject current = null;
        foreach (var type in ParamTypes)
        {
            if (found.TryGetValue(type, out current) && current != null) break;
        }
        if (current == null)
        {
            _param = null;
            return false;
        }

        // Re-bind when the game recreated the Param (equip flow does this).
        if (_param == null || FlowHelper.AddressOf(current) != FlowHelper.AddressOf(_param))
        {
            _param = current;
            ResetState();
        }
        return true;
    }

    protected override void OnActivate()
    {
        API.LogInfo("[SF6Access] Status action-skill tab active");
        PollState(); // the original announced the initial row immediately
    }

    protected override void OnDeactivate()
    {
        API.LogInfo("[SF6Access] Status action-skill tab ended");
        _param = null;
        ResetState();
    }

    private void ResetState()
    {
        _lastState = -2;
        _lastIndex = -2;
        _lastText = null;
        _attentionOpen = false;
        _lastAttentionButton = null;
        _lastSetType = int.MinValue;
        _lastCountNow = int.MinValue;
        _lastCountMax = int.MinValue;
        _lastFull = false;
    }

    protected override void OnPoll() => PollState();

    private void PollState()
    {
        if (_param == null) return;

        // The confirm popup ("A special move is already assigned... Switch moves?")
        // is an overlay that may not change MenuState, so detect it by its GUI
        // being present rather than by state.
        if (PollAttention()) return;

        int state = FlowHelper.ReadIntField(_param, "MenuState", STATE_SET);
        if (state != _loggedState)
        {
            _loggedState = state;
            API.LogInfo($"[SF6Access] Action skill MenuState = {state}");
        }

        // Only the two navigable lists are read here; sort / icon-legend popups skipped
        if (state != STATE_SET && state != STATE_CHOICE)
        {
            _lastState = state;
            return;
        }

        var list = FlowHelper.GetObjectField(_param,
            state == STATE_CHOICE ? "mSkillPanelList_Select" : "mSkillPanelList_Set");
        int idx = FlowHelper.CallInt(list, "get_SelectedIndex");

        // The set-type tab (Grounded / Air / Super Arts) switches with Tab. It
        // changes neither MenuState nor the row index, so the dedup below swallows
        // it and the switch reads as silence — detect it on its own.
        int setType = FlowHelper.ReadIntField(_param, "SetType", -1);
        bool setTypeChanged = setType != _lastSetType && _lastSetType != int.MinValue;
        _lastSetType = setType;

        // Equipped-slot counter (now / max). There is NO point/cost budget for
        // equipping: each category (Ground/Air/Super Arts) just has a slot cap
        // that scales with the avatar's stats, shown as a "now / max" count and a
        // "full" flag. Announce it on entry and whenever it changes (after an
        // equip/unequip) so the player tracks the budget. See docs/sf6-screens.md.
        bool haveCount = ReadEquipCount(out int countNow, out int countMax, out bool full);
        bool countBaseline = _lastCountNow == int.MinValue;
        bool countChanged = haveCount && !countBaseline &&
            (countNow != _lastCountNow || countMax != _lastCountMax || full != _lastFull);
        if (haveCount) { _lastCountNow = countNow; _lastCountMax = countMax; _lastFull = full; }

        bool first = _lastState == -2;
        bool sectionChanged = state != _lastState && !first;
        if (idx == _lastIndex && state == _lastState && !first && !setTypeChanged && !countChanged) return;
        _lastState = state;
        _lastIndex = idx;

        var parts = new List<string>();
        if (setTypeChanged)
        {
            string setName = ReadSetTypeName(setType);
            if (!string.IsNullOrEmpty(setName)) parts.Add(setName);
        }
        if (first || sectionChanged) parts.Add(SectionName(state));
        if (haveCount && (first || sectionChanged || countChanged))
        {
            parts.Add(LocalizedText.EquipSlotCount(countNow, countMax));
            if (full) parts.Add(LocalizedText.SlotsFull());
        }

        // Name + command from the focused row; description from the detail panel
        var item = FlowHelper.Call(list, "get_SelectedItem") as ManagedObject;
        var rowTexts = item == null ? null : GuiTextReader.ReadControlTexts(item);
        string name = FindText(rowTexts, "e_text_name");
        string command = FindText(rowTexts, "e_text_command");
        string spokenCommand = string.IsNullOrWhiteSpace(command) ? null : FlowHelper.SpeakableIcons(command).Trim();

        // e_text_command is empty in two cases: on Modern (Casual) controls the
        // input lives in a separate "casual command" text with a different element
        // name, and on assigned slots the classic command renders as a hidden image.
        // Fall back to the panel's typed command fields so the slot's input/trigger
        // (neutral/forward/back special) is still spoken in the player's own scheme.
        if (string.IsNullOrWhiteSpace(spokenCommand))
            spokenCommand = ReadPanelCommand(item);

        if (string.IsNullOrWhiteSpace(name))
        {
            // Empty Move Set slot: announce its trigger input (e.g. "down plus
            // special move") so the player knows which slot they're on, not just
            // "empty". When the trigger can't be read, fall back to the slot
            // position — without something distinct per slot, a run of identical
            // "Empty" announcements is dropped by the reader's duplicate filter
            // (the 2nd/3rd consecutive empty went silent).
            if (!string.IsNullOrWhiteSpace(spokenCommand)) parts.Add(spokenCommand);
            else parts.Add($"{SlotWord()} {idx + 1}");
            parts.Add(EmptyWord());
        }
        else
        {
            parts.Add(name);
            if (!string.IsNullOrWhiteSpace(spokenCommand)) parts.Add(spokenCommand);

            // In the Moves Learned list, say whether the move is locked (can't be
            // equipped) so the player doesn't have to try each one. The panel's
            // visual state is exposed as a "Lock..." PlayState on its control.
            if (state == STATE_CHOICE && IsFocusedLocked(item))
                parts.Add(LockedWord());

            var detail = FlowHelper.GetObjectField(_param, "mSkillDetail");
            string desc = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(detail, "mTextDescription"));
            if (!string.IsNullOrWhiteSpace(desc)) parts.Add(desc.Trim());
        }

        string text = string.Join(". ", parts);
        // No text dedup here: the index/state gate above already prevents re-reading
        // the same item, and deduping on text instead swallowed legitimate moves to
        // a DIFFERENT slot that happens to share text — a run of identical empty
        // slots stopped announcing "Empty" after the first (and previously caused
        // "silent after confirming", since the post-dialog item matched _lastText).
        _lastText = text;

        API.LogInfo($"[SF6Access] Action skill [{state}/{idx}]: {text}");
        Speak(text, interrupt: !first);
    }

    /// <summary>
    /// The slot's input read from the panel's typed command texts, used when the GUI
    /// element walk found nothing. mTextCasualCommand holds the Modern (Casual) input;
    /// mTextCommand the classic one. The game only fills the text for the active
    /// scheme, so the first non-empty one is the right one to speak.
    /// </summary>
    private static string ReadPanelCommand(ManagedObject panel)
    {
        if (panel == null) return null;
        foreach (var field in new[] { "mTextCasualCommand", "mTextCommand" })
        {
            string raw = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(panel, field));
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string spoken = FlowHelper.SpeakableIcons(raw).Trim();
            if (!string.IsNullOrWhiteSpace(spoken)) return spoken;
        }
        return null;
    }

    /// <summary>The first text of a named element in a focused row's texts (raw kept for command tags).</summary>
    private static string FindText(List<GuiTextReader.GuiText> texts, string elementName)
    {
        if (texts == null) return null;
        foreach (var t in texts)
        {
            if (t.Name != elementName) continue;
            string v = elementName == "e_text_command" ? t.Raw : t.Text;
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        }
        return null;
    }

    /// <summary>
    /// True when the focused skill panel is locked. The panel renders its state
    /// (Default/Empty/Lock) as a "Lock..." animation PlayState on its control, so
    /// detect it there — GetFocusPanel(eMenuState).State did not resolve at runtime.
    /// </summary>
    private static bool IsFocusedLocked(ManagedObject item)
    {
        if (item == null) return false;
        var states = new List<string>();
        GuiTextReader.ReadPlayStates(item, states);
        foreach (var s in states)
            if (!string.IsNullOrEmpty(s) && s.Contains("Lock", System.StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// Read the confirm popup ("A special move is already assigned... Switch
    /// moves?") detected by its GUI being present (it may not change MenuState).
    /// Announces the question on open and the focused Yes/No button as it changes.
    /// Returns true while the popup is open so the lists aren't read underneath.
    /// </summary>
    private bool PollAttention()
    {
        string head = null, notice = null;
        ManagedObject view = null;
        foreach (var (owner, v) in GuiTextReader.FindGuiViews("Attention"))
        {
            foreach (var t in GuiTextReader.ReadViewTexts(v, owner))
            {
                if (t.Name == "e_text_head" && !string.IsNullOrWhiteSpace(t.Text)) head = t.Text.Trim();
                else if (t.Name == "e_text_notice" && !string.IsNullOrWhiteSpace(t.Text)) notice = t.Text.Trim();
            }
            if (notice != null || head != null) { view = v; break; }
        }

        // Require a FOCUSED Yes/No button too: the attention GUI can linger enabled
        // after the choice is made, but its button selection clears — without this
        // the lists underneath stayed muted ("silent after confirming").
        int btnIdx = view == null ? -1 : GuiTextReader.FindSelectedItemIndex(view, "SELECT");
        bool open = (notice != null || head != null) && btnIdx >= 0;

        if (!open)
        {
            if (_attentionOpen) API.LogInfo("[SF6Access] Action skill confirm closed");
            _attentionOpen = false;
            _lastAttentionButton = null;
            return false;
        }

        bool firstOpen = !_attentionOpen;
        _attentionOpen = true;
        _lastIndex = -2;       // force the list to re-announce after the popup closes
        _lastState = -2;

        string button = btnIdx == 0 ? YesWord() : NoWord();

        if (firstOpen)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(head)) parts.Add(head);
            if (!string.IsNullOrWhiteSpace(notice)) parts.Add(notice);
            if (!string.IsNullOrWhiteSpace(button)) parts.Add(button);
            _lastAttentionButton = button;

            string text = string.Join(". ", parts);
            API.LogInfo($"[SF6Access] Action skill confirm: {text}");
            Speak(text, interrupt: true);
        }
        else if (!string.IsNullOrWhiteSpace(button) && button != _lastAttentionButton)
        {
            _lastAttentionButton = button;
            API.LogInfo($"[SF6Access] Action skill confirm button: {button}");
            Speak(button, interrupt: true);
        }
        return true;
    }

    private static string YesWord() => LocalizedText.Yes();

    private static string NoWord() => LocalizedText.No();

    private static string LockedWord() => LocalizedText.LockedM();

    /// <summary>
    /// Equipped-slot counter for the current category. Special Moves (and the
    /// avatar-training variant) expose it as on-screen "now / max" GUI texts
    /// (mTextCountNow / mTextCountMax); Super Arts has no such texts and exposes
    /// GetCurrentEquipSlotNum() + EquipSlotMax instead. The max is a per-category
    /// avatar stat (Ground/Air/SA SkillEquipSlot), not a spendable point budget.
    /// Returns false when no counter can be read.
    /// </summary>
    private bool ReadEquipCount(out int now, out int max, out bool full)
    {
        full = false;
        now = ReadCountText("mTextCountNow");
        max = ReadCountText("mTextCountMax");
        if (now < 0 || max < 0)
        {
            now = FlowHelper.CallInt(_param, "GetCurrentEquipSlotNum");
            max = FlowHelper.CallInt(_param, "get_EquipSlotMax");
        }
        if (now < 0 || max < 0) return false;
        // FullEquiped exists only on the Special Moves params; fall back to the
        // count comparison for Super Arts.
        full = FlowHelper.ReadBoolField(_param, "FullEquiped") || now >= max;
        return true;
    }

    /// <summary>Parse an on-screen count text (mTextCountNow/Max) to an int, or -1.</summary>
    private int ReadCountText(string field)
    {
        string t = FlowHelper.ReadGuiText(FlowHelper.GetObjectField(_param, field));
        if (string.IsNullOrWhiteSpace(t)) return -1;
        return int.TryParse(t.Trim(), out int v) ? v : -1;
    }

    /// <summary>
    /// Name of the current move set-type tab (Grounded / Air / Super Arts), switched
    /// with Tab. Prefer the game's own localized tab label; fall back to the
    /// WTStyleDefine.WTActionSkillSetType enum (Ground=1, Air=2, SuperArts=3).
    /// </summary>
    private string ReadSetTypeName(int setType)
    {
        var tab = FlowHelper.GetObjectField(_param, "mSetTypeTab");
        var item = FlowHelper.Call(tab, "get_SelectedItem") as ManagedObject;
        if (item != null)
        {
            foreach (var t in GuiTextReader.ReadControlTexts(item, visibleOnly: true))
                if (!string.IsNullOrWhiteSpace(t.Text)) return t.Text.Trim();
        }
        return LocalizedText.SetType(setType);
    }

    private static string SectionName(int state)
        => state == STATE_CHOICE ? LocalizedText.MovesLearned() : LocalizedText.MoveSet();

    private static string EmptyWord() => LocalizedText.Empty();

    private static string SlotWord() => LocalizedText.Slot();
}
