using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the Rewards screen (app.UIFlowReward) and its four tabs:
/// Battle Pass, Challenge, Kudos and Master Pass. The generic GroupFocus reader
/// only discovers list/group fields directly on the Param, but the actual
/// navigation lives nested inside per-tab sub-parts (Tier grid/list, kudos and
/// challenge lists), so it never read while moving through them. This hook
/// reads the active tab and the focused row/reward of that tab's sub-part.
/// </summary>
public class RewardHooks
{
    private const string PARAM_TYPE = "app.UIFlowReward.UIFlowParam";

    // UIFlowReward.Mode: tab order
    private static readonly string[] TabNames = { "Battle Pass", "Challenge", "Kudos", "Master Pass" };

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 30;
    private const int POLL_READ_INTERVAL = 8;

    private static bool _isActive;
    private static ManagedObject _param;

    private static int _lastTab = -1;
    private static readonly Dictionary<string, string> _lastSpoken = new();

    public static bool IsActive => _isActive;

    // Set by the grid/list navigation hooks: a cursor move clears the dedup so
    // returning to an already-read row re-announces it (text dedup alone went
    // silent on right-then-left back to the same reward — the poll missed the
    // intermediate row, so the row looked unchanged)
    private static volatile bool _navDirty;

    [PluginEntryPoint]
    public static void Initialize()
    {
        HookNavigation();
        API.LogInfo("[SF6Access] RewardHooks initialized");
    }

    /// <summary>Hook the battle pass tier navigation so every cursor move forces a re-read.</summary>
    private static void HookNavigation()
    {
        try
        {
            var tierType = TDB.Get().FindType("app.UIPartsRewardBattlePassTier");
            foreach (var name in new[] { "GridChanged", "ListChanged", "GridScrolled" })
            {
                var m = tierType?.GetMethod(name) ?? tierType?.GetMethod($"{name}()");
                if (m == null) continue;
                m.AddHook(false).AddPost((ref ulong retval) => _navDirty = true);
            }
        }
        catch (System.Exception ex)
        {
            API.LogWarning($"[SF6Access] RewardHooks nav hook failed: {ex.Message}");
        }
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

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var current = FlowHelper.TrackFlowParam(PARAM_TYPE, _param, out bool changed);
            if (current == null) { Reset(); return; }
            if (changed) TryActivate(); // menu recreated — re-bind caches
        }

        // A navigation event re-arms the content reads (drop only the content
        // keys, keep the tab key so the tab isn't re-announced on every move)
        if (_navDirty)
        {
            _navDirty = false;
            _lastSpoken.Remove("bp");
            _lastSpoken.Remove("challenge");
            _lastSpoken.Remove("kudos");
            _lastSpoken.Remove("mp");
        }

        if (_pollCounter % POLL_READ_INTERVAL == 0)
        {
            PollTab();
            PollContent();
        }
    }

    private static void TryActivate()
    {
        var param = FlowHelper.FindFlowParam(PARAM_TYPE);
        if (param == null) return;

        // Don't cache child parts here: the param appears in _Handles a few
        // frames before its Header/BattlePass/... fields are populated, so a
        // one-shot bind catches nulls. Re-read them from the param each poll.
        _param = param;
        _lastTab = -1;
        _lastSpoken.Clear();
        _isActive = true;

        API.LogInfo("[SF6Access] Rewards menu active");
    }

    /// <summary>Announce the selected tab (Battle Pass / Challenge / Kudos / Master Pass).</summary>
    private static void PollTab()
    {
        var header = FlowHelper.GetObjectField(_param, "Header");
        int tab = FlowHelper.CallInt(header, "GetSelectedTab", -1);
        if (tab < 0 || tab == _lastTab) return;
        bool first = _lastTab < 0;
        _lastTab = tab;

        // Switching tab must re-announce content even if its text is unchanged
        _lastSpoken.Clear();

        // Prefer the localized header label; fall back to the fixed tab name
        string name = FlowHelper.ReadSelectedItemText(header);
        if (string.IsNullOrEmpty(name) && tab < TabNames.Length) name = TabNames[tab];
        if (string.IsNullOrEmpty(name)) return;

        API.LogInfo($"[SF6Access] Rewards tab: {name}");
        ScreenReaderService.Speak(name, interrupt: !first);
    }

    private static void PollContent()
    {
        switch (_lastTab)
        {
            case 0: PollBattlePass(); break;
            case 1: PollListTab(FlowHelper.GetObjectField(_param, "Challenge"), "ScrollList", "challenge"); break;
            case 2: PollListTab(FlowHelper.GetObjectField(_param, "Kudos"), "ScrollList", "kudos"); break;
            case 3: PollMasterPass(); break;
        }
    }

    /// <summary>Read the focused reward / button in the Battle Pass tier grid.</summary>
    private static void PollBattlePass()
    {
        var battlePass = FlowHelper.GetObjectField(_param, "BattlePass");
        if (battlePass == null) return;

        // SelectedItemType: 0 = GridItem (reward), 1 = Premium Pass button, 2 = Tier Boost button
        int selType = FlowHelper.CallInt(battlePass, "GetSelectedItem", 0);
        if (selType == 1) { Speak("bp", "Premium Pass button"); return; }
        if (selType == 2) { Speak("bp", "Tier Boost button"); return; }

        var reward = FlowHelper.Call(battlePass, "GetSelectedReward") as ManagedObject;
        if (reward != null)
        {
            int category = FlowHelper.ReadIntField(reward, "ItemCategory");
            int itemId = FlowHelper.ReadIntField(reward, "ItemId");
            int num = FlowHelper.ReadIntField(reward, "Num", 1);
            bool received = FlowHelper.ReadBoolField(reward, "Received");
            // BattlePassRewardType: 1 = Free, 2 = Premium (needs the premium pass)
            int rewardType = FlowHelper.ReadIntField(reward, "RewardType");

            string name = itemId >= 0 ? FlowHelper.ResolveItemName(category, (uint)itemId) : null;
            if (!string.IsNullOrEmpty(name))
            {
                if (num > 1) name = $"{name} x{num}";
                // Free vs premium (needs the premium pass), then claimed status —
                // announce "Not claimed" too so an unclaimed reward isn't ambiguous
                if (rewardType == 2) name = $"{name}. Premium";
                else if (rewardType == 1) name = $"{name}. Free";
                name = received ? $"{name}. Claimed" : $"{name}. Not claimed";
                Speak("bp", name);
                return;
            }
        }

        // No structured reward (or name unresolved) — read the focused grid tile text
        var tier = FlowHelper.GetObjectField(battlePass, "Tier");
        var grid = FlowHelper.GetObjectField(tier, "ScrollGrid");
        string text = FlowHelper.ReadSelectedItemText(grid);
        if (!string.IsNullOrEmpty(text)) Speak("bp", text);
    }

    /// <summary>Read the focused row of a list-driven tab (Challenge / Kudos).</summary>
    private static void PollListTab(ManagedObject part, string listField, string key)
    {
        if (part == null) return;
        var list = FlowHelper.GetObjectField(part, listField);
        string text = FlowHelper.ReadSelectedItemText(list);
        if (!string.IsNullOrEmpty(text)) Speak(key, text);
    }

    /// <summary>Master Pass uses two grids (all rewards / per-character).</summary>
    private static void PollMasterPass()
    {
        var masterPass = FlowHelper.GetObjectField(_param, "MasterPass");
        if (masterPass == null) return;

        // FocusItem: 0 = AllGrid, 1 = CharacterGrid
        int focusGrid = FlowHelper.CallInt(masterPass, "GetFocusGrid", 0);
        string field = focusGrid == 1 ? "UICharacterScrollGrid" : "UIAllScrollGrid";
        var grid = FlowHelper.GetObjectField(masterPass, field);
        string text = FlowHelper.ReadSelectedItemText(grid);
        if (!string.IsNullOrEmpty(text)) Speak("mp", text);
    }

    private static void Speak(string key, string text)
    {
        if (_lastSpoken.TryGetValue(key, out var last) && last == text) return;
        _lastSpoken[key] = text;
        API.LogInfo($"[SF6Access] Reward [{key}]: {text}");
        ScreenReaderService.Speak(text);
    }

    private static void Reset()
    {
        API.LogInfo("[SF6Access] Rewards menu ended");
        _isActive = false;
        _param = null;
        _lastTab = -1;
        _lastSpoken.Clear();
    }
}
