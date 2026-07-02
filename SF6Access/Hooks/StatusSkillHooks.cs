using System.Collections.Generic;
using System.Runtime.InteropServices;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the Status menu Skills tab (app.UIStatusMenu_Skill) — a 2D
/// skill TREE the generic group reader can't navigate well. The focused node's
/// info is exposed as plain text in the StatusMenu_Skill GUI (e_text_title /
/// e_text_detail / e_text_cost / e_text_category), so read that on focus change,
/// plus the current tree number (1/5, switched with L/R) from CurrentTreeNo.
/// Migrated to ScreenAdapter (ReadInterval 1 so the G key is polled every frame;
/// the tree itself is read every few frames).
/// </summary>
public sealed class StatusSkillHooks : SingleParamScreenAdapter
{
    // eMenuState.ShowConfirm — the "Unlock this skill?" dialog opened with confirm.
    private const int STATE_SHOW_CONFIRM = 4;
    // eMenuState.ResetConfirm — the "Reset Skills" dialog opened with R.
    private const int STATE_RESET_CONFIRM = 8;
    // How often (frames) to re-read the focused tree node.
    private const int TREE_POLL_EVERY = 5;

    protected override string ParamType => "app.UIStatusMenu_Skill.Param";

    public StatusSkillHooks()
    {
        // Re-verify the param often (fast close detection) and poll every frame so
        // the G shortcut's key edge is never missed.
        SearchInterval = 5;
        ReadInterval = 1;
    }

    private int _frame;
    private string _lastNode;
    private int _lastTree = int.MinValue;
    private int _lastMenuState = int.MinValue;
    private int _lastConfirmBtn = int.MinValue;
    private bool _resetGaugeSpoken;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")]
    private static extern System.IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(System.IntPtr hWnd, out uint processId);
    private const int VK_G = 0x47;
    private bool _lastGState;

    /// <summary>True only when the game window is the foreground app — so the G
    /// shortcut never fires while the user is typing in another window.</summary>
    private static bool IsGameForeground()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            return pid == (uint)System.Environment.ProcessId;
        }
        catch { return false; }
    }

    protected override void OnBind()
    {
        _lastNode = null;
        _lastTree = int.MinValue;
        _lastMenuState = int.MinValue;
        _lastConfirmBtn = int.MinValue;
        _resetGaugeSpoken = false;
        API.LogInfo("[SF6Access] Skill tree active");
        PollTree();
    }

    protected override void OnExit()
    {
        API.LogInfo("[SF6Access] Skill tree ended");
        _lastNode = null;
        _lastTree = int.MinValue;
        _lastMenuState = int.MinValue;
        _lastConfirmBtn = int.MinValue;
    }

    protected override void Poll()
    {
        _frame++;

        // G announces the available skill points on demand (game-focused only).
        bool gDown = (GetAsyncKeyState(VK_G) & 0x8000) != 0;
        if (gDown && !_lastGState && IsGameForeground()) AnnouncePoints();
        _lastGState = gDown;

        if (_frame % TREE_POLL_EVERY == 0) PollTree();
    }

    private void PollTree()
    {
        if (Param == null) return;

        // The "Unlock this skill?" confirm dialog (F on a node) is a sub-state of
        // this param with its own GUI — read it instead of the tree underneath.
        // Track entry INTO a sub-state by MenuState change (a shared "open" flag
        // got stuck between the F and R dialogs and muted the reset announcement).
        int menuState = FlowHelper.ReadIntField(Param, "MenuState", -1);
        bool justOpened = menuState != _lastMenuState;
        if (justOpened)
        {
            _lastMenuState = menuState;
            _lastConfirmBtn = int.MinValue;
            _lastNode = null;   // re-announce the focused node after a dialog closes
        }

        if (menuState == STATE_SHOW_CONFIRM) { PollConfirm(justOpened); return; }
        if (menuState == STATE_RESET_CONFIRM) { PollResetConfirm(justOpened); return; }

        // Tree tab (1/5) — switched with L/R. Announce the new tree on change.
        int tree = FlowHelper.ReadIntField(Param, "CurrentTreeNo", int.MinValue);
        bool firstTree = _lastTree == int.MinValue;
        if (tree != int.MinValue && tree != _lastTree)
        {
            _lastTree = tree;
            if (!firstTree)
            {
                // Moving trees re-lays the nodes out; re-read the focused node next.
                _lastNode = null;
                string treeMsg = $"{TreeWord()} {tree + 1}";
                API.LogInfo($"[SF6Access] Skill tree: {treeMsg}");
                ScreenReaderService.Speak(treeMsg);
            }
        }

        // Focused node — its title/detail/cost/category render in the skill GUI.
        string title = null, detail = null, category = null, cost = null;
        foreach (var (owner, view) in GuiTextReader.FindGuiViews("StatusMenu_Skill"))
        {
            foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
            {
                string text = t.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;
                switch (t.Name)
                {
                    case "e_text_title": title ??= text; break;
                    case "e_text_detail": detail ??= text; break;
                    case "e_text_category": category ??= text; break;
                    case "e_text_cost": cost ??= text; break;  // first = the focused node's
                }
            }
            if (title != null) break;
        }

        if (string.IsNullOrWhiteSpace(title)) return;

        // Key off the title+cost so navigating to a different node re-announces even
        // if two nodes share a name; the reader's duplicate filter still collapses
        // a genuine double-fire of the identical string.
        string key = $"{title}|{cost}|{category}";
        if (key == _lastNode) return;
        _lastNode = key;

        var parts = new List<string> { title };
        string state = ReadNodeState();
        if (!string.IsNullOrEmpty(state)) parts.Add(state);
        if (!string.IsNullOrEmpty(category)) parts.Add(category);
        if (!string.IsNullOrEmpty(cost)) parts.Add($"{CostWord()} {cost}");
        if (!string.IsNullOrEmpty(detail)) parts.Add(detail);

        string msg = string.Join(". ", parts);
        API.LogInfo($"[SF6Access] Skill node: {msg}");
        ScreenReaderService.Speak(msg);
    }

    /// <summary>
    /// "Acquired" / "Locked" / "Available" for the focused tree node, from the
    /// focused panel's TreeSkillState (the authoritative data, not the visual).
    /// </summary>
    private string ReadNodeState()
    {
        var panel = FlowHelper.Call(Param, "GetFocusPanel") as ManagedObject;
        if (panel == null) return null;

        // SkillTreeDef.TreeSkillState: CanOpen=0, Win=1, Locked=2, ReadyLocked=3, Lose=4
        int state = FlowHelper.ReadIntField(panel, "State", int.MinValue);
        API.LogInfo($"[SF6Access] Skill node State={state}");
        return state switch
        {
            1 => AcquiredWord(),       // Win — already unlocked
            2 or 3 => LockedWord(),    // Locked / ReadyLocked
            0 => AvailableWord(),      // CanOpen — can be unlocked
            4 => UnavailableWord(),    // Lose — branch not taken, can't be unlocked
            _ => null,
        };
    }

    /// <summary>
    /// Announce the available skill points (mTreeCount RemainingValue: now/max). The
    /// points are earned by leveling up in World Tour and spent to unlock skills.
    /// </summary>
    private void AnnouncePoints()
    {
        if (Param == null) return;
        // DispCost is the player's available skill-point counter (22 with points,
        // 0 once spent). mTreeCount is a separate tree/phase count (read "2/5") —
        // not the points, so it must NOT be used here.
        int points = FlowHelper.ReadIntField(Param, "DispCost", int.MinValue);
        int money = ReadMoney();
        API.LogInfo($"[SF6Access] Skill points: DispCost={points} money={money}");

        var parts = new List<string>();
        if (points != int.MinValue) parts.Add($"{PointsWord()}: {points}");
        if (money != int.MinValue) parts.Add($"{MoneyWord()}: {money}");
        if (parts.Count == 0) return;
        ScreenReaderService.Speak(string.Join(". ", parts), interrupt: true);
    }

    /// <summary>
    /// The avatar's money (Zenny) from WTPlayerManager.LocalPlayerData.Wallet.Money.
    /// Wallet/Money are getter properties (no backing field), so the field read
    /// fails — fall back to the get_ accessors.
    /// </summary>
    private static int ReadMoney()
    {
        var mgr = API.GetManagedSingleton("app.worldtour.WTPlayerManager") as ManagedObject;
        var playerData = FlowHelper.GetObjectField(mgr, "LocalPlayerData")
            ?? FlowHelper.Call(mgr, "get_LocalPlayerData") as ManagedObject;
        var wallet = FlowHelper.GetObjectField(playerData, "Wallet")
            ?? FlowHelper.Call(playerData, "get_Wallet") as ManagedObject;
        if (wallet == null) { API.LogInfo("[SF6Access] Wallet not found"); return int.MinValue; }

        int money = FlowHelper.ReadIntField(wallet, "Money", int.MinValue);
        if (money == int.MinValue) money = FlowHelper.CallInt(wallet, "get_Money", int.MinValue);
        return money;
    }

    /// <summary>
    /// The "Unlock this skill?" confirm dialog (StatusMenu_SkillOpenConfirm):
    /// announce the skill + cost + question on open, then the focused Yes/No button.
    /// </summary>
    private void PollConfirm(bool justOpened)
    {
        string name = null, detail = null, cost = null;
        ManagedObject view = null;
        foreach (var (owner, v) in GuiTextReader.FindGuiViews("SkillOpenConfirm"))
        {
            foreach (var t in GuiTextReader.ReadViewTexts(v, owner))
            {
                string text = t.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;
                switch (t.Name)
                {
                    case "e_text_skill_name": name ??= text; break;
                    case "e_text_skill_detail": detail ??= text; break;
                    case "e_text_cost": cost ??= text; break;
                }
            }
            view = v;
            if (name != null) break;
        }

        int btn = view == null ? -1 : GuiTextReader.FindSelectedItemIndex(view, "SELECT");

        if (justOpened)
        {
            _lastConfirmBtn = btn;
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(name)) parts.Add(name);
            if (!string.IsNullOrEmpty(cost)) parts.Add($"{CostWord()} {cost}");
            parts.Add(UnlockQuestion());
            if (btn >= 0) parts.Add(btn == 0 ? YesWord() : NoWord());

            string msg = string.Join(". ", parts);
            API.LogInfo($"[SF6Access] Skill confirm: {msg}");
            ScreenReaderService.Speak(msg, interrupt: true);
        }
        else if (btn >= 0 && btn != _lastConfirmBtn)
        {
            _lastConfirmBtn = btn;
            ScreenReaderService.Speak(btn == 0 ? YesWord() : NoWord(), interrupt: true);
        }
    }

    /// <summary>
    /// The "Reset Skills" confirm dialog (StatusMenu_SkillResetConfirm, R): announce
    /// the title on open and the focused button (Yes / No / View Skills). The title
    /// and body render as images, so the heading is spoken from a localized string.
    /// </summary>
    private void PollResetConfirm(bool justOpened)
    {
        ManagedObject view = null;
        string gaugeNow = null, gaugeMax = null;
        foreach (var (owner, v) in GuiTextReader.FindGuiViews("SkillResetConfirm"))
        {
            // The dialog shows the reset RESOURCE as a "now / max" gauge (e.g.
            // 808 / 10000) — NOT the avatar's Zenny. Take the larger value as "now"
            // (a small "x1" quantity also renders as e_text_value).
            foreach (var t in GuiTextReader.ReadViewTexts(v, owner))
            {
                string text = t.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (t.Name == "e_text_total") gaugeMax ??= text;
                else if (t.Name == "e_text_value" &&
                         int.TryParse(text, out int n) && n > 1) gaugeNow = text;
            }
            view = v;
            break;
        }

        int btn = view == null ? -1 : GuiTextReader.FindSelectedItemIndex(view, "SELECT");

        bool gaugeReady = gaugeNow != null && gaugeMax != null;

        if (justOpened)
        {
            _lastConfirmBtn = btn;
            _resetGaugeSpoken = gaugeReady;
            var parts = new List<string> { ResetTitle() };
            // The game blocks reset on a tree where nothing's been spent / the cost
            // can't be paid — tell the player instead of leaving "Yes" silently dead.
            bool canReset = FlowHelper.ReadByteField(Param, "CanResetAtCurrentTree", 0) != 0;
            if (!canReset) parts.Add(CantResetWord());
            // The reset resource gauge (what's actually required to reset).
            if (gaugeReady) parts.Add($"{ResetResourceWord()}: {gaugeNow} / {gaugeMax}");
            if (btn >= 0) parts.Add(ResetButton(btn));
            string msg = string.Join(". ", parts);
            API.LogInfo($"[SF6Access] Skill reset (canReset={canReset}): {msg}");
            ScreenReaderService.Speak(msg, interrupt: true);
        }
        else if (btn >= 0 && btn != _lastConfirmBtn)
        {
            // The button index resolves a few frames AFTER the dialog opens (starts
            // at -1). That first appearance must NOT interrupt the title/gauge still
            // being read — queue it. Only real Yes/No navigation interrupts.
            bool firstResolve = _lastConfirmBtn < 0;
            _lastConfirmBtn = btn;
            ScreenReaderService.Speak(ResetButton(btn), interrupt: !firstResolve);
        }
        else if (!_resetGaugeSpoken && gaugeReady)
        {
            // The gauge numbers populate a few frames after the dialog opens — speak
            // them as soon as they're there so the first open isn't missing them.
            _resetGaugeSpoken = true;
            ScreenReaderService.Speak($"{ResetResourceWord()}: {gaugeNow} / {gaugeMax}", interrupt: false);
        }
    }

    private static string ResetTitle()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "Restablecer habilidades. ¿Restablecer tus habilidades?",
            FlowHelper.UiLang.Pt => "Redefinir habilidades. Redefinir suas habilidades?",
            _ => "Reset Skills. Reset your skills?",
        };

    private static string ResetButton(int idx)
        => idx switch
        {
            0 => YesWord(),
            1 => NoWord(),
            _ => FlowHelper.GetDisplayLang() switch
            {
                FlowHelper.UiLang.Es => "Ver habilidades",
                FlowHelper.UiLang.Pt => "Ver habilidades",
                _ => "View Skills",
            },
        };

    private static string ResetResourceWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "Recurso de reinicio",
            FlowHelper.UiLang.Pt => "Recurso de redefinição",
            _ => "Reset resource",
        };

    private static string AvailableWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "disponible",
            FlowHelper.UiLang.Pt => "disponível",
            _ => "available",
        };

    private static string UnavailableWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "no disponible",
            FlowHelper.UiLang.Pt => "indisponível",
            _ => "unavailable",
        };

    private static string PointsWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "Puntos de habilidad",
            FlowHelper.UiLang.Pt => "Pontos de habilidade",
            _ => "Skill points",
        };

    private static string MoneyWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "Monedas",
            FlowHelper.UiLang.Pt => "Moedas",
            _ => "Coins",
        };

    private static string CantResetWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "No se puede restablecer en este árbol",
            FlowHelper.UiLang.Pt => "Não é possível redefinir nesta árvore",
            _ => "Can't reset on this tree",
        };

    private static string TreeWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "Árbol",
            FlowHelper.UiLang.Pt => "Árvore",
            _ => "Tree",
        };

    private static string CostWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "Coste",
            FlowHelper.UiLang.Pt => "Custo",
            _ => "Cost",
        };

    private static string LockedWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "bloqueada",
            FlowHelper.UiLang.Pt => "bloqueada",
            _ => "locked",
        };

    private static string AcquiredWord()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "adquirida",
            FlowHelper.UiLang.Pt => "adquirida",
            _ => "acquired",
        };

    private static string UnlockQuestion()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "¿Desbloquear esta habilidad?",
            FlowHelper.UiLang.Pt => "Desbloquear esta habilidade?",
            _ => "Unlock this skill?",
        };

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
}
