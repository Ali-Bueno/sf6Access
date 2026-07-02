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

        API.LogInfo("[SF6Access] ScreenRegistry: adapters registered");
    }
}
