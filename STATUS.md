# STATUS — Street Fighter 6 (SF6Access)

> Per-mod status ledger / dashboard. Open this first when resuming the mod so progress isn't re-derived from the code each session. Keep it short — a dashboard, not docs. Update the **Next step** line and the section table whenever you finish a chunk. Derive every value from the game's real data — no guessed offsets.

**Last updated:** 2026-07-20

## Identity
- **Engine / framework:** RE Engine (Capcom), REFramework.NET C# plugin (net10.0), `SF6Access/Plugin.cs` entry, attribute + dynamic hooks.
- **Screen-reader transport:** Tolk / TolkDotNet (`references\tolk\`, `SF6Access/Services/ScreenReaderService.cs`) — NOT PRISM.
- **Build command:** `dotnet build` from `SF6Access/` (post-build auto-deploys).
- **Mod install path:** `Street Fighter 6\reframework\plugins\managed` (`TolkDotNet.dll` → `managed\dependencies`; `lang\*.txt` → `managed\SF6Access.lang`).
- **Run / test:** Launch SF6 with REFramework (`dinput8.dll`) installed and a screen reader (NVDA/JAWS) running; check the RE Engine framework log for backend + errors.

## Section status
`done` = works with the screen reader on; `wip` = started; `todo` = not begun.

| Section / feature | Status | Notes |
|---|---|---|
| Main menu / Fighting Ground / tabs | done | `MainMenuHooks`, `FGMenuHooks`; fg/bh/wt tabs |
| Options menu (+ sub-screens) | done | `OptionMenuHooks`, `OptionSubScreenHooks`; `OptionManager` polling |
| Key config | done | `KeyConfigHooks` |
| Character / Stage / Side / League select | done | `CharacterSelectHooks`, `StageSelectHooks`, `SideSelectHooks`, `LeagueSelectHooks` |
| Training — frame data / attack / reversal | done | `TrainingFrameDataHooks`, `TrainingAttackDataHooks`, `TrainingReversalHooks`, `TrainingMenuHooks` |
| Combat readouts (focus, battle info) | done | `FocusValueHooks`, `GroupFocusHooks`, `BattleInfoHooks`; `ComboTracker` |
| Combo Trial | done | `ComboTrialHooks`, `ComboTrialListHooks` |
| Command list | done | `CommandListHooks` |
| Status / skills / action skills | done | `StatusMenuHooks`, `StatusSkillHooks`, `StatusActionSkillHooks`, `StatusMySetActionSkillHooks` |
| Online / social (rooms, hub, chat, shop) | done | `CustomRoomHooks`, `BattleHubResultHooks`, `SocialChatHooks`, `ChatMenuHooks`, `OnlineShopHooks`/`OnlineShopBuyHooks` |
| Dialogs / flow / tickers / news | done | `DialogHooks`, `DialogFlowHooks`, `FlowTrackerHooks`, `TickerHooks`, `NewsHooks` |
| World Tour — exploration dialogue | wip | v0.5.5: NPC VN dialogue + branch choices now read; subtitle de-dup fixed (`WorldTour/`, `SpTalkNovelHooks`) |
| World Tour — field awareness (radar) | done | Confirmed in-game 2026-07-20: N key names every nearby avatar with camera-relative clock direction + metric distance ("Luke, maestro a las 12, a 5 metros"); fully calibrated (forward + handedness). Key binding N/Start still provisional. See `docs/sf6-screens.md` § World Tour — field awareness |
| Avatar creator (colors, presets, traits) | wip | v0.5.5 rework; ~600 preset descriptions, colors named in 13 langs; needs in-game pass (`AvatarCreate/`, `AvatarStatsReader`) |
| Avatar — Special Moves / Super Arts equip | wip | Slot-usage announced (per-category slots, no cost system); verified in Avatar Arcade |
| World Tour full flow | todo | Next big goal; shared avatar menus not yet verified in WT |

## Derived facts (so we never re-RE them)
| Fact | Value | Source |
|---|---|---|
| Focus-change signal | `UIAgent.FocusChanged()` (gives `SelectItem`); suppress while a dedicated screen hook owns it | `docs/sf6-screens.md` |
| Grid selection | `UIStartMenu.FlowParam.MenuItemSelectionChanged()`; items `item\d+` / `c_item_\d{2,}` (grid), `c_item_\d` single-digit = dialog buttons | `docs/sf6-screens.md` |
| Options types | `UIOptionSettingMenu` + `OptionMenuParam`; `OptionManager` singleton holds live values; `OptionManager.GetOptionValue(typeId)`; nav via `UIPartsOptionUnit.SwitchFocus(bool)` (fires 2× isFocus=true) | `docs/sf6-screens.md` |
| Tabs | `app.Option.TabType` 0 General…6 Graphic; main tabs fg/bh/wt | `docs/sf6-screens.md` |
| Runtime type caveat | Runtime uses CONCRETE types, decompiled shows interfaces — always verify a name with a dump (`ObjectDumper`) | `docs/sf6-screens.md`, `docs/sf6-architecture.md` |
| Shared services | `FlowHelper`, `GuiTextReader`, `GroupFocusPoller`, stale-param re-entry handling | `docs/sf6-architecture.md`, `SF6Access/Services/` |

## Next step
World Tour field awareness (WT-1) is COMPLETE and fully calibrated in game 2026-07-20 (radar on N:
names at any range + camera-relative clock + metric distances). NEW, needs in-game test: continuous
tracking on M (`FieldTrackingHooks`) — periodic "a las 12, a 4 metros" toward the nearest avatar,
silent while dialogues/arrival readers speak. Possible follow-up the user floated: real audio beacons
on NPCs (3D sound) — needs either RE of the game's own sound system (via.sound) or an audio library
decision (new DLL = ask first). Open decisions: final key bindings (N/M/Start provisional) and the
missing `wt.*` lang keys in the 11 languages beyond en/es. Then: verify the shared avatar/status menus
inside World Tour (not just Avatar Arcade) and complete the in-game pass on the reworked character
creator.

## Known issues / open questions
- Uses **Tolk**, not PRISM (the playbook default is PRISM).
- Avatar creator + World Tour screens were largely built from decompiled code and still need in-game verification passes.
- Runtime concrete vs. decompiled-interface type names — confirm with a dump before trusting a name.

**Detailed history:** see CHANGELOG.md / docs/.
