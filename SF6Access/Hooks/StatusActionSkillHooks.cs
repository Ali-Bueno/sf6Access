using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the Status menu Special Moves and Super Arts tabs (both are
/// app.UIStatusMenu_ActionSkillEquipBase): a "Move Set" slot list on the left and
/// a "Moves Learned" list on the right, with a detail panel. The generic group
/// reader announced these inconsistently (and leaked the unfilled "SA {0}" label),
/// so they are read here from the typed widgets instead.
/// </summary>
public class StatusActionSkillHooks
{
    // Both tabs share UIStatusMenu_ActionSkillEquipBase, so the same field/method
    // names work for either param.
    private static readonly string[] ParamTypes =
    {
        "app.UIStatusMenu_SpecialMoves.Param",
        "app.UIStatusMenu_SuperArts.Param",
    };

    // eMenuState: 0=SET_LIST (Move Set slots), 1=CHOICE_LIST (Moves Learned),
    // 2=ATTENTION (confirm popup), 4=CHARGESKILL_ATTENTION.
    private const int STATE_SET = 0;
    private const int STATE_CHOICE = 1;
    private const int STATE_ATTENTION = 2;
    private const int STATE_CHARGE_ATTENTION = 4;

    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    private static ManagedObject _param;
    private static int _lastState = -2;
    private static int _lastIndex = -2;
    private static string _lastText;
    private static bool _attentionOpen;
    private static string _lastAttentionButton;
    private static int _loggedState = -99;

    public static bool IsActive => _isActive;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] StatusActionSkillHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (!_isActive)
        {
            if (_pollCounter % POLL_SEARCH_INTERVAL == 0) TryActivate();
            return;
        }

        if (_pollCounter % POLL_READ_INTERVAL != 0) return;

        // Re-find the live param every read: equipping a move can recreate it, and
        // a stale instance reads nothing ("stopped reading after confirming").
        var current = FindParam();
        if (current == null) { Reset(); return; }
        if (FlowHelper.AddressOf(current) != FlowHelper.AddressOf(_param))
        {
            _param = current;
            _lastState = -2;
            _lastIndex = -2;
            _lastText = null;
            _attentionOpen = false;
            _lastAttentionButton = null;
        }

        PollState();
    }

    private static ManagedObject FindParam()
    {
        foreach (var type in ParamTypes)
        {
            var p = FlowHelper.FindFlowParam(type);
            if (p != null) return p;
        }
        return null;
    }

    private static void TryActivate()
    {
        var p = FindParam();
        if (p == null) return;

        _param = p;
        _lastState = -2;
        _lastIndex = -2;
        _lastText = null;
        _attentionOpen = false;
        _lastAttentionButton = null;
        _isActive = true;
        API.LogInfo("[SF6Access] Status action-skill tab active");
        PollState();
    }

    private static void PollState()
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

        bool first = _lastState == -2;
        bool sectionChanged = state != _lastState && !first;
        if (idx == _lastIndex && state == _lastState && !first) return;
        _lastState = state;
        _lastIndex = idx;

        var parts = new List<string>();
        if (first || sectionChanged) parts.Add(SectionName(state));

        // Name + command from the focused row; description from the detail panel
        var item = FlowHelper.Call(list, "get_SelectedItem") as ManagedObject;
        var rowTexts = item == null ? null : GuiTextReader.ReadControlTexts(item);
        string name = FindText(rowTexts, "e_text_name");
        string command = FindText(rowTexts, "e_text_command");
        string spokenCommand = string.IsNullOrWhiteSpace(command) ? null : FlowHelper.SpeakableIcons(command).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            // Empty Move Set slot: announce its trigger input (e.g. "down plus
            // special move") so the player knows which slot they're on, not just "empty"
            if (!string.IsNullOrWhiteSpace(spokenCommand)) parts.Add(spokenCommand);
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
        // Don't dedupe the first read after (re)entering the list — returning from
        // the confirm dialog lands on the just-equipped move, whose text matches
        // the last announcement and was being swallowed ("silent after confirming").
        if (text == _lastText && !sectionChanged && !first) return;
        _lastText = text;

        API.LogInfo($"[SF6Access] Action skill [{state}/{idx}]: {text}");
        ScreenReaderService.Speak(text, interrupt: !first);
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
    private static bool PollAttention()
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
            ScreenReaderService.Speak(text, interrupt: true);
        }
        else if (!string.IsNullOrWhiteSpace(button) && button != _lastAttentionButton)
        {
            _lastAttentionButton = button;
            API.LogInfo($"[SF6Access] Action skill confirm button: {button}");
            ScreenReaderService.Speak(button, interrupt: true);
        }
        return true;
    }

    private static string YesWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "Sí",
            FlowHelper.UiLang.Pt => "Sim",
            _ => "Yes",
        };

    private static string NoWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "No",
            FlowHelper.UiLang.Pt => "Não",
            _ => "No",
        };

    private static string LockedWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "bloqueado",
            FlowHelper.UiLang.Pt => "bloqueado",
            _ => "locked",
        };

    private static string SectionName(int state)
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => state == STATE_CHOICE ? "Movimientos aprendidos" : "Conjunto de movimientos",
            FlowHelper.UiLang.Pt => state == STATE_CHOICE ? "Movimentos aprendidos" : "Conjunto de movimentos",
            _ => state == STATE_CHOICE ? "Moves Learned" : "Move Set",
        };

    private static string EmptyWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "Vacío",
            FlowHelper.UiLang.Pt => "Vazio",
            _ => "Empty",
        };

    private static void Reset()
    {
        API.LogInfo("[SF6Access] Status action-skill tab ended");
        _isActive = false;
        _param = null;
        _lastState = -2;
        _lastIndex = -2;
        _lastText = null;
        _attentionOpen = false;
        _lastAttentionButton = null;
    }
}
