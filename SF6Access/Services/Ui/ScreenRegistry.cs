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

        // New screen readers built on the foundation
        UiDispatcher.Register(new AvatarResultHooks());
        UiDispatcher.Register(new WTMPauseHooks());
        UiDispatcher.Register(new DeviceItemAppHooks());
        UiDispatcher.Register(new ShopHooks());
        UiDispatcher.Register(new OnlineShopHooks());
        UiDispatcher.Register(new ComboTrialListHooks());

        API.LogInfo("[SF6Access] ScreenRegistry: adapters registered");
    }
}
