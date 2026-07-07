using REFrameworkNET;
using REFrameworkNET.Attributes;
using SF6Access.Hooks;

namespace SF6Access.Services.Ui;

/// <summary>
/// Single registration point for the adapters driven by <see cref="UiDispatcher"/>
/// — the reference architecture's central registry. Migrated screen hooks are
/// instantiated and registered here; adding a screen is one line. Legacy hooks
/// that still own their own [Callback] are not listed here and run independently
/// until they are migrated.
/// </summary>
public class ScreenRegistry
{
    [PluginEntryPoint]
    public static void Register()
    {
        UiDispatcher.Register(new MatchingSettingHooks());
        UiDispatcher.Register(new OptionSubScreenHooks());

        // Batch 1 — single-param poll screens
        UiDispatcher.Register(new TickerHooks());
        UiDispatcher.Register(new EmulatorPauseHooks());
        UiDispatcher.Register(new RankUpHooks());
        UiDispatcher.Register(new StaffRollHooks());
        UiDispatcher.Register(new CustomRoomJoinHooks());
        UiDispatcher.Register(new MatchingFighterSettingHooks());

        // Batch 2 — single-param poll screens
        UiDispatcher.Register(new AccessOtherPlayerProfileHooks());
        UiDispatcher.Register(new ChatMenuHooks());
        UiDispatcher.Register(new ItemListDialogHooks());
        UiDispatcher.Register(new CustomRoomHooks());
        UiDispatcher.Register(new ArcadeSettingHooks());
        UiDispatcher.Register(new TrainingCharacterSpecificHooks());

        // Batch 3 — single-param poll screens
        UiDispatcher.Register(new MusicPlayerHooks());
        UiDispatcher.Register(new MusicPlayerEditHooks());
        UiDispatcher.Register(new OnlineShopBuyHooks());
        UiDispatcher.Register(new ShortcutSettingHooks());

        // Batch 4 — remaining single-param poll screens (some with a G shortcut)
        UiDispatcher.Register(new StatusMySetActionSkillHooks());
        UiDispatcher.Register(new CommandListHooks());
        UiDispatcher.Register(new AvatarArcadeTopHooks());
        UiDispatcher.Register(new StatusSkillHooks());

        // Category B batch 1 — multi-param / prefix screens
        UiDispatcher.Register(new ArcadeResultHooks());
        UiDispatcher.Register(new CharDetailSettingHooks());
        UiDispatcher.Register(new LeagueSelectHooks());
        UiDispatcher.Register(new ProfileHooks());

        // Category B batch 2 — auto-shown displays / overlays
        UiDispatcher.Register(new BattleHubResultHooks());
        UiDispatcher.Register(new ItemPreviewHooks());
        UiDispatcher.Register(new DeathMatchSettingHooks());
        UiDispatcher.Register(new BootMessageHooks());
        UiDispatcher.Register(new TutorialHooks());

        // Category B batch 3 — dialogs / training sub-lists / status tabs / stage select
        UiDispatcher.Register(new TrainingSubListHooks());
        UiDispatcher.Register(new DialogFlowHooks());
        UiDispatcher.Register(new StatusActionSkillHooks());
        UiDispatcher.Register(new StageSelectHooks());

        // Category B batch 4 — the big custom-walk screens
        UiDispatcher.Register(new StatusMenuHooks());
        UiDispatcher.Register(new AvatarCreateHooks());
        UiDispatcher.Register(new FighterSettingHooks());

        // Category D batch 1 — poll part migrated, AddHook parts kept in each
        // hook's static [PluginEntryPoint] (TutorialControlTypeHooks is pure
        // method-hook and stays legacy)
        UiDispatcher.Register(new BonusResultHooks());
        UiDispatcher.Register(new ReplayInfoMenuHooks());
        UiDispatcher.Register(new RevivalPassWarningHooks());
        UiDispatcher.Register(new ArcadeHooks());
        UiDispatcher.Register(new SideSelectHooks());
        UiDispatcher.Register(new RewardHooks());

        // Category D batch 2 (FGMenuHooks stays legacy: its callback is an
        // event-driven combine timer, not the poll scaffold)
        UiDispatcher.Register(new NewsHooks());
        UiDispatcher.Register(new BattleSettingsHooks());
        UiDispatcher.Register(new ComboTrialHooks());

        // Category D batch 3 (OptionMenuHooks + BattleInfoHooks stay legacy:
        // event-driven activity / always-on battle monitor, no screen poll)
        UiDispatcher.Register(new TrainingMenuHooks());
        UiDispatcher.Register(new KeyConfigHooks());

        // Final sweep — remaining pure-poll hooks (flow-param or GUI-view based)
        UiDispatcher.Register(new TrainingReversalHooks());
        UiDispatcher.Register(new EventBannerHooks());
        UiDispatcher.Register(new TrainingFrameDataHooks());
        UiDispatcher.Register(new TextInputDialogHooks());

        // New screen readers built on the foundation
        UiDispatcher.Register(new AvatarResultHooks());
        UiDispatcher.Register(new WTMPauseHooks());
        UiDispatcher.Register(new DeviceItemAppHooks());
        UiDispatcher.Register(new ShopHooks());
        UiDispatcher.Register(new OnlineShopHooks());
        UiDispatcher.Register(new ComboTrialListHooks());
        UiDispatcher.Register(new SpTalkNovelHooks());

        API.LogInfo("[SF6Access] ScreenRegistry: adapters registered");
    }
}
