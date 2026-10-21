# STATUS — Street Fighter 6 (SF6Access)

> Per-mod status ledger / dashboard. Open this first when resuming the mod so progress isn't re-derived from the code each session. Keep it short — a dashboard, not docs. Update the **Next step** line and the section table whenever you finish a chunk. Derive every value from the game's real data — no guessed offsets.

**Last updated:** 2026-10-01

**Next step: user tests Places by name and Guidance / Listen across the city. Do not launch the game.**
The user chose both named selection and recognizing distant places while exploring. Known fixed places
now have an alphabetical view and an optional citywide look mode: one heard name, category sound and
R3 target; selecting exits the survey and starts normal guidance. Default local exploration is retained.
Facilities continue learning after the first discovery; cached places are scoped by city/situation.
Release built/deployed, 0 warnings/errors; 520 navigation checks pass. Native listening test pending.
Coverage is known locations, not every unvisited city facility; phone-map capture remains unverified.
See [controls, coverage, design and test checklist](docs/wt-city-listening.md).

Previous R3 universal cancellation and native rest/day-night category are also awaiting a live test.
Check the room-associated travel point versus the actual door and My Room's newly watched `TimeList`.
The unchanged audio passed 283 checks; the player confirms its object sounds are easier to distinguish.

**Remaining:** listen at the player's speech rate; verify local walking previews, fast-travel contact,
R3 recovery after loss, walking/corners, live NPC contact/loss,
fight return and Boluo's partial roof route. Phone-map empty/day-night freshness and native action
prompt coverage remain unresolved. No gameplay completion or runtime usability claim is made.

**Earlier prototype history (superseded behavior is retained below as evidence):**

**Next step (2026-09-27, latest): test navigating by ear without R3** (`docs/wt-soundscape.md` §6
pass 2 and §8). Tester: "quisiera navegar con algo que no dependiese del R3… sonificar más el mundo".
Built, 135 pure checks, NOT verified in game: `WorldSonarHooks` (was `AccessSonarHooks`) gives earcons
to ladders, doors, shops (`sounds/shop.mp3`) and places (`sounds/place.mp3`, fast travel, saved spots,
pins) — the thing the camera points at (±15°, to 100 m) plays alone and speeds up as you approach
(flashlight), otherwise the 3 nearest within 25 m take turns quietly; Shift+N toggles. Every homing
beacon now plays a fifth higher when dead ahead (`HomingCue` on-course), so the right stick replaces
R3 for aiming. Also still untested: the ladder/door pass and the look sweep below.

**Next step (2026-09-27): test navigating without a menu** (`docs/wt-free-navigation.md`, §6).
Built, 113 pure checks, adversarially reviewed (8 fixes), NOT verified in game: turning the camera while standing names places ahead
(`FieldPlaceSweepHooks`), a tap of R3 right after makes that place the destination; walking past a place
within 12 m names it once (`FieldPassingHooks`). Polish from the test: zone banners no longer bounce at
an edge (`ZoneAnnounceGate`), R3 keeps its camera calibration across menus and fights. Proposed, needs
the tester's decision: continuous guidance (the camera follows the route while walking, replacing the
R3-every-second habit seen in the log).

**Test 2026-09-27 (orientation pass): WORKS, needs polish** (tester: "está bien, hay que pulirlo").
Confirmed in game: section id through `GuiManager.AccessData` (`Section id 410 read through manager`),
the banner hook (`UICityHud_SectionNotice.RequestSectionNotice`), 110 of 110 section names, banners
spoken ("Faith Avenue", "Distrito central - Zona central", "South Street - Zona de Beat Square"), the
picker and the input mask (`Action=3`), choosing shops/people/mission. Registry: 14 NPCs, 1 facility,
0 masters. Seen: banners flapping at a boundary (Seaside Street ↔ Servicios de seguridad Buckler, 6 in
14 s); the phone map was not opened (no snapshot yet); R3 tapped about once a second while walking —
it has become the steering control.

**Test 2026-09-26, eighth run: PASSED.** The tester reached Boluo: partial plan #4 → blocked #2 → 10 m
roof, then got up to him. The new `RequestShowFromPlayer`/`RequestShowFromOM` hooks installed (2) but
never fired all session, ladders included: the prompt HUD is still unread (`IsShow` false, requests
silent) — the way game action prompts are raised is still unknown.

**Test 2026-09-26, seventh run (fixed, not re-verified):** both ladders climbed (blocked leg worked,
the way opened); stopped on the 10 m roof 16 m from Boluo, whose spot is a separate island 3 m up.
At the stand-in the beacon now aims at the destination; game action prompts (Climb, GetOver…) are
announced from their show requests. Next test: from the stand-in, walk to the wall under Boluo and
listen for "Disponible: Trepar"; report `the game requested action prompt` lines.

**Test 2026-09-26, sixth run (fixed, not re-verified):** on the first roof the tester found nothing to
break and the beacon offered a 133 m street route. New "blocked legs": the second ladder, 10.5 m away
and level but not connected on the navmesh (the sign), is now the next leg, pointed at directly, with
"no hay paso libre… acción de maestro"; `Level route: blocked walk …` logs the objects in between (the
sign's type). Absurd stand-in routes after a climb are rejected.

**Test 2026-09-26, fifth run (fixed, not re-verified):** correct ladder (#5), but "ladders done" fired
while still on the ladder, and the approach searched from the street node below (131 m street route,
back down, into walls). The hand-over now waits until the navmesh confirms the player is on the
ladder's roof. Next test: climb #5, step onto the roof, expect "Las escaleras no suben más…" and the
beacon pointing up/toward the boss (~23 m), not back down.

**Test 2026-09-26, fourth run (fixed, not re-verified):** ladder climbing works ("Ya puedes agarrar
la escalera" + action button). The partial plan had picked a ladder on another building 85 m from the
boss, and the retry then led back down; partial ends are now bounded to 32 m from the target, and the
mission route no longer restarts when the game swaps the NPC for its map icon. Next test: from the
street, expect ladder #5 (-244, 0→5 m), then "Las escaleras no suben más…", break the sign, and the
beacon pointing to the objective. Details: `docs/sf6-screens.md` § multi-level routes, "Fourth run".

**Test 2026-09-26, third run (not re-verified):** the partial plan led to ladder #3's foot, but the
player could not climb it and no game prompt was read. New: lowest-rung height and access-target
match per ladder (`Ladder contact:` lines, "Ya puedes agarrar la escalera", "salta hacia ella"),
first-call log of the action-prompt HUD, and a much faster mission beacon (far gap 0.5 s, keeps
pinging during speech). Details: `docs/sf6-screens.md` § multi-level routes, "Third run".

**Test 2026-09-26, second run (fixed the same day, not re-verified):** the earlier crash was NOT the
emote call: the Windows event log shows an `AccessViolationException` in `FieldRayContacts.Read` — an
unpinned REFramework `ValueType` read racing the GC (`docs/sf6-architecture.md`, "Read a REFramework
`ValueType` only while it is pinned"). Fixed with `PinnedValue` on the hot paths. The mission is
Chapter 2-2 "Moratones a los Caracartones" (boss Boluo, (-229.3, 12.2, -104.3), confirmed by the map
icon). Guides: first ladder, a sign blocks the roof path (break it with a master action), second
ladder. No loaded ladder end connects to Boluo's spot on the navmesh, so the planner now returns a
PARTIAL plan (climb as near the target's height as ladders allow, then approach from there). The map
cursor call no longer runs while the map closes (11 game exceptions). **Test:** from the street, expect
"Ninguna escalera llega al destino, pero … te acercan", the ladders, then "Las escaleras no suben
más…"; break the sign when the breakable beacon points at it. Report `Level route:` lines.

**Test 2026-09-26 (fixed the same day, not re-verified):** hard crash first blamed on `AvatarEmoteHooks`
calling an `out string` method (10 faults) — call removed, but see the second run above. Ladder search
ran but no loaded ladder reaches mission 11011's roof NPC; the fallback said "objetivo … a 5 metros"
for the point BELOW it — now "pie de objetivo" plus the height. Radar laterals no longer go silent
while turning (tester request). New log lines to read: `Mission guide: HUD unused …` (all target
holders by name/position), `ladders readable; unread: …`. Details: `docs/sf6-screens.md` §
"multi-level routes through ladders", "First run".

**Next step (2026-09-22): test multi-level routes through ladders.** Tester reports a mix of
"objective on another floor" and "getting lost on the way"; the 2026-09-09 log shows ground routes
working and a roof objective (`PathNotFound`) leaving the player under it. New `LevelRoute` plans
walk → ladder → walk with the game's navmesh and ladder gimmicks (`Om011000`) and speaks each leg.
Built and logic-reviewed, 49 pure checks pass, **nothing verified in game**. Test: take a roof/upper-floor objective
from the street; expect "N meters above you. Searching for a ladder", then "Route found…", the
beacon leading to the ladder, "Ladder here. Climb up", and "Ladder done…" at the top. Report the
`Level route:` log lines. Details: `docs/sf6-screens.md` § "multi-level routes through ladders".
The 2026-09-14 prototype below is also still untested.

**Previous step (2026-09-14, implemented prototype):** test the automatic navigation/access
increment in World Tour. Radar starts ON; short route bends are retained and announced;
route loss/nearby approach are explicit; matching native contact data replaces proximity
as mission interaction confirmation. New `FieldExplorationHooks` discovers nearby
ladder/door families and reads active contextual action guides without new hotkeys or
automatic movement. `AccessPointReader` now binds enabled `AccessCheckShape` instances
and honors the bool return. Existing audio assets and dependencies retained.

**Validation:** `dotnet build` succeeded, 0 warnings/errors; post-build deployed to the
configured REFramework managed folder. Dependency-free `tests/Navigation.Tests` passed
27 checks (close bends, bearing wrap, turn handedness, invalid positions, per-access
announcement memory, expiry and independent front debounce). Native bindings, audio
timing and usable access positions remain **PENDING IN-GAME VERIFICATION**. Nothing is
marked complete. Test: enter exploration without Shift+B; approach/pass/return to a
ladder; compare closed/open door; approach an NPC around a close corner; check upper-floor
objective fallback; confirm silence in menus/dialogue/combat. Read `Exploration`,
`Access point` and `Mission navigation` log lines. No diagnostic keys are required.

**Navigation review (2026-09-14):** user reaffirmed existing navigation, no additional
keys and no auto-walk. Code review and prioritized proposal added to
`docs/wt-radar-improvements.md` ("Review against current working code"): radar still
starts disabled; close turns can be skipped; automatic mission guidance ignores
`NoRoute`; proximity does not confirm interaction. Recommended next implementation is
hands-free route reliability and final approach before experimental navmesh exits.
No navigation code changed; no feature marked complete or verified in game.

**Game-code follow-up (2026-09-14):** declaration review found ladder endpoints
(`Om011000`), door restrictions/transitions (`Om016000`/`Om017000`), native contextual
action guides, and portal-edge data on our existing path report. `AccessPointReader`
looks for `GetNearPos` on the target, but the declaration belongs to nested
`AccessCheckShape`; binding/return handling needs correction and runtime verification.
Details in `docs/sf6-screens.md` under "Follow-up: independent exploration radar";
hands-free free-exploration proposal in `docs/wt-radar-improvements.md`. Next: automatic
contact/action diagnostics and a ladder/door probe alongside radar reliability fixes.
Analysis/documentation only; no code changes, build or new in-game verification.

**Earlier plan (2026-09-12):** **keep the radar, improve it** (user decision; a full redesign and any auto-walk / teleport were rejected). Plan in `docs/wt-radar-improvements.md`: Level 0 debt (one filter, B reuses the sample, capsule cast, mirrored sides), Level 1 every beacon follows the route + on-course ping, Level 2 real exits from navmesh link edges (`findMapHandle` bound by shape, bounded `NodeQueryInfo`, `queryLinkBoundaryEdge`) feeding the exit cue and "junction / narrow passage", Level 3 route-aware beams, Level 4 speech arbiter. Decide its open questions, then run experiments 1–2. Native leads from the stub sweep: `docs/sf6-screens.md` § "native navigation infrastructure leads".

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
| World Tour — audio exploration and optional guidance | wip | First listening test exposed over-suppression; follow-up built, 300 checks pass, listening pending. `docs/wt-soundscape.md`. |
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
| World Tour — navigation radar (spatial APIs) | wip | Ray API confirmed in game 2026-09-04 (`CastRayAll` + `TerrainRayFilter`). Feature SHIPPED but NOT yet verified in game: `WorldTour/FieldNavRadarHooks` (B = one-shot readout, Shift+B = continuous reactive mode) on `Services/WorldTour/FieldNavRadarService`. Front cues and both keys CONFIRMED working in game 2026-09-04; the sides never fired, root-caused by measurement (every sideways ray the game publishes reaches <=0.60 m) and fixed by casting our own segment via the free-form `CastRayAll(ref vec3, ref vec3, ...)` at the reach of `FRONT_LONG` -- still nine casts per sample. A front class change while still blocked now speaks too. **Pending test:** do the sides now fire at ~2 m in a corridor and on the correct side (not mirrored), does the free-form segment cast stay crash-free over a long walk, does B conflict with a World Tour keyboard action. **False positives fixed 2026-09-05 (UNVERIFIED):** the front verdict no longer comes from the ray height ladder -- it is read from the game itself (`Services/WorldTour/FieldNavVerdictService.cs`: `CollisionInfo.IsWallContact()`/`WallMovableRate` vs `AvatarConstMoveParams.DashMove_StopWallRate`, `CharacterController.Wall` as fallback, `AvatarCollisionCache.GetGoupStepInfo` for "the game will step over this", wall-ride route DISABLED 2026-09-06 after it crashed the game -- constructing the generic interface `IList<ContactedWallInfo>` produced a bogus managed wrapper whose finalizer raised AccessViolationException on the GC thread with a clean log; `NewInstance` now refuses interface/abstract/generic types); the rays now feed distance and description only. **Pending test (2026-09-05):** read the one-time log line `[SF6Access] NavRadar verdict routes: volatile=.., charaCtrl=.., goupStep=.., wallRide=.., stopWallRate=..` and report it -- it says which routes bound; then check that a waist-high wall that really stops the avatar is announced as blocked (was "open"), that kerbs/low fences the avatar climbs are NOT announced as walls, that walking along a wall no longer chatters open/closed, and that a stationary player pressed against a wall still reads as blocked (the `WallMovableRate` risk). **Continuous-mode open/closed cues replaced 2026-09-07:** the armed exit chime (`_exitArmed`/`ExitCheck`, `LongRangeClear`, the 2 m side feelers) is retired — see the PENDING IN-GAME VERIFICATION 2026-09-07 entry below for the new three-beam design and its checklist. **Beam anti-chatter, same day, second pass:** `NavBeams.Sample` now takes a `moving` flag (`FieldNavRadarHooks.Moved()`, avatar XZ displacement ≥ 0.05 m per sample) — while moving the FRONT beam never cues (tracks silently so a stop-and-turn scans from where it is) and any change on any beam must hold `MOVING_HOLD_SAMPLES` = 6 samples (1 s, vs. 2 standing) before cueing; session log showed open/close pairs half a second apart, 20 cues in one 14 s burst, all false positives. See `docs/sf6-screens.md` § World Tour -- spatial navigation APIs (§9 Front verdict / continuous-mode openings). **2026-09-08 rebuild for precision (tester: could never tell exactly where an exit was or where passage was really blocked):** new sensor `Services/WorldTour/FieldRayCaster.cs` casts with the avatar's OWN `CharacterController.FilterInfo` instead of the camera's `TerrainRayFilter`; a new always-on 4-tone bed (`NavToneBed.cs` + `NavProximityBed.cs`, NAudio, ported from the DRG/Megabonk wall-sonification synth) replaces silence-vs-event with silence-vs-continuous-tone; a NavMesh-based exit finder (`NavMeshOpenings.cs`) answers "where is the exit" with real gap widths (a ray-fan exit detector was tried and deleted — proven blind to distant narrow gaps and false-positive-prone at grazing angles by a standalone simulation). **`NavBeams.cs` (§9b, the four-beam open/closed design) is DELETED**, retiring `FieldNavRadarService.Beams()`/`BEAM_REACH_M`; the older ray-ladder + `FieldNavVerdictService` spoken front-verdict (§9) is unchanged. All new, compiles 0 errors, NOTHING verified in game — see "Built but not yet verified in game" below for the full checklist. **2026-09-08, second pass — the continuous mode IS now the RE7 radar:** the wall-sonification tone bed was a wrong turn and is gone; Shift+B runs `FieldRadarService` (event radar, three beams x three capsule heights, open/closed state machine, time-to-contact pitch, monotonic cadence) — see the dedicated entry below. **2026-09-08, later session — that RE7-style line-memory model retired in turn:** measured at 109 cues/90 s in Metro City (a city facade changes slope about once a second, which is a real event a line-break detector cannot filter, and a line break itself means "sees past the boundary", not "passage" — this is what produced the exit/blocked contradiction). `Services/WorldTour/FieldRadarLines.cs` DELETED, replaced by a body-clearance PASSABILITY model (`Services/WorldTour/FieldRadarClearance.cs`): per-beam STATE (`Unknown`/`Passable`/`Blocked`) from the waist body-width cast, not a remembered line, speaking only on a debounced state change; the spoken verdict and the cue channel now share one measured "blocked" boundary (`FieldNavSideRays.PublishedReachM`). `MESH_WAYS_ENABLED` still `false`. **PENDING IN-GAME VERIFICATION** — baseline to beat is the 36-cues/30 s measurement above; see the dedicated entry in "Built but not yet verified in game" and `docs/sf6-screens.md` § 9d |
| World Tour — phone map | wip | Built 2026-09-07 from the F8 capture `sf6access_autodump_154237.txt` (lines 770-965): `Hooks/WorldTour/DeviceMapHooks.cs` + `Services/WorldTour/DeviceMapText.cs` on `app.UIFlowWTDeviceMap.MapParam`. Speaks the location + guide message on entry, then flow-state changes ("Map" / "Fast travel list"), the icon under the free cursor ("Beat Square, shop"), the highlighted fast-travel point ("name, 2 of 7") and the marker info panel. Icon kinds come from `ICON_TYPE` member NAMES mapped to nine family words (`wt.map.icon_*` in `lang/en.txt` + `es.txt`). **PENDING IN-GAME VERIFICATION** — checklist below. The world map (`app.UIFlowWorldMap`) is still untouched |
| World Tour — map-selection aiming + route guidance | wip | Built 2026-09-09, **NOTHING VERIFIED IN GAME**. `Hooks/WorldTour/FieldTrackerHooks.cs` on `Services/WorldTour/{MapIcon, MapIconCatalog, TrackerSelection, TrackerButtons, FieldInputMask, BeaconDestination}.cs`. R3/V opens a modal list of everything the HUD minimap is showing (`app.UIMiniMapWindow.mIconInfoList`, real `IconInfo.GlobalPos` world positions); d-pad up/down or the arrows cycle targets nearest-first, L1/R1 or PageUp/PageDown cycle the game's own `ICON_GROUP_TYPE` categories. The chosen icon takes over the mission beacon; the game removing its icon is what triggers auto-retracking to the next of the same category. **Shift+V turns on `FieldNavPath`** (inert since 2026-09-08) so the beacon aims at the route's first corner. See `docs/sf6-screens.md` § World Tour minimap tracker + route guidance |
| World Tour — multi-level routes (ladders) | wip | Built 2026-09-22, NOT verified in game. `LevelRoute` + `LevelRoutePlanner` + `LadderCatalog`: walk → ladder → walk when the objective's navmesh island is disconnected. `docs/sf6-screens.md` § multi-level routes |
| World Tour — orientation (zones, where am I, destination picker) | wip | Confirmed working in game 2026-09-27 (needs polish). `ZoneHooks`, `SectionNoticeHooks`, `DestinationPickerHooks`; `docs/wt-orientation.md` |
| World Tour — free navigation (look sweep for places, passing by) | wip | Built 2026-09-27, NOT verified in game. `FieldPlaceSweepHooks`, `FieldPassingHooks`; `docs/wt-free-navigation.md` |
| World Tour — soundscape (world sonar, on-course beacons) | wip | Built 2026-09-27, NOT verified in game. `WorldSonarHooks`, `HomingCue`; `docs/wt-soundscape.md` |
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

## Open bugs / pending in-game verification
(Single durable list — machine-local session memory is NOT used; this file and `docs/` are the only
records that survive a machine change.)

**Open bugs**
- WLTAG word type 1005 (perk numeric values) resolves to garbage in the WTM pause perk tooltip —
  see `docs/sf6-screens.md` § WLTAG resolution.
- Training reversal: SPECIAL moves without an `e_txt_0` strength element (L/M/H/OD) announce
  nothing on left/right (sound plays); root cause not found.
- Shop Enhance/Dye side panes still unread.

**Built but not yet verified in game**
- **Battle notices read aloud, 2026-10-16** (`BattleNoticeHooks`). VERIFIED in training: the game's
  text is read once per banner (STUN, PUNISH COUNTER, COUNTER HIT after enabling them in
  `plugins\managed\SF6Access.notices.txt`, applied without a restart). Still unverified: online
  rollback not repeating a banner; what the LOCK notice is.
- **Training Attack Data + frame data aligned with the sighted readout, 2026-10-16.** Detection was
  verified with a live probe (`docs/sf6-screens.md` § Training live display state); the built readouts
  still need an in-game pass. Expected: a single hit says "700. High" EVERY time (identical repeats
  included); a combo says "1230. 4 hits. scaling 60%"; the dummy hitting you is prefixed "Opponent";
  frame data is ONE line per exchange — "You: Startup 6F / Total 22F / Advantage 1F", or
  "Opponent: …" when the dummy attacked — a whiff drops the "Advantage --"; walking/dashing is silent.
  Unverified: the attack-type Guid (`attackInfo`) resolving to text; a dummy attack (recording/CPU)
  producing the "Opponent" variants.
- **`ZoneNameService` now ENUMERATES the section table instead of looking it up by key, 2026-09-10.**
  `CitySectionDataUserDataDict` is keyed by `ManageId`, which is not guaranteed to be the section id.
  The old route asked `get_Item(sectionId)` and then cross-checked the record's own `id`; that check was
  right to exist (handing back somebody else's district is worse than silence) but its consequence was
  that whenever the two ids differ the route went mute. Every record is now read and filed under its own
  `id`, so what the dictionary is keyed by stops mattering. New `FlowHelper.GetTableDict(string)` exposes
  the dictionary for enumeration.

  **Bounded by the table's own count.** There is still no safe way to walk an IL2CPP dictionary's
  entries in this codebase — no helper exists and the enumerator is a struct — so keys are probed upward
  with `ContainsKey` (by-value, returns bool) and the scan stops as soon as `get_Count` records have
  been found. `KEY_PROBE_SLACK` = 8x the record count is only a spin guard; stopping early is logged,
  not hidden. Built once per city, retried on the existing `EMPTY_RETRY_MS` beat while the table is
  still loading.

  **HONEST CAVEAT, stated before it was built:** the failure actually measured in game (2026-09-04) was
  `CurrentSectionId=0, SectionInfoList count=0` — **no section at all**, so the table lookup was never
  reached and this enumeration may fix nothing that was being suffered. It is robustness, not the
  transformation it looked like. The sub-area announcements now come from
  `Hooks/WorldTour/SectionNoticeHooks.cs` (the game's own banner), which sidesteps this table entirely.

  **In-game verification checklist:**
  - [ ] `Zone: CitySectionDataUserDataDict enumerated - N named sections from M of T records` — report
        N, M and T. N=0 means the table is unreadable; M<T means the key probe stopped early and the
        slack needs raising.
  - [ ] Does the Z key still name the district correctly, and does it now name districts it used to be
        silent about?
- **Shop entry/exit announcements, 2026-09-10 (tester: "si entré con el personaje en la tienda, que el
  lector diga tienda tal. si salí, saliendo de tienda tal").** `Hooks/WorldTour/ShopEntryHooks.cs`.

  **The game announces it; nothing is inferred.** `app.worldtour.om.Om008000` calls
  `CallShopInSoundTrigger()` / `CallShopOutSoundTrigger()` (`Om008000.cs:241/:244`) at exactly the
  moments IT considers the player to have gone in and come out, so the announcement can never disagree
  with the game's own idea of what happened. There is **no `IsInside`/`CurrentShop` member anywhere** —
  searched — so hooking these is not the cheap route, it is the only honest one.

  Dynamic hooks (`AddHook(false).AddPre`), `args[1]` as `this`, the shape `BattleInfoHooks` established.
  The name comes from the instance's access target via `GetDispName()` — the same call
  `AvatarFieldReader` has always used to name NPCs and objects, already localized, cleaned through
  `FlowHelper.CleanTags`. **The hook resolves the NAME and lets the engine object go**; only a string
  crosses to the next frame, and the speaking happens on the ordinary tick, never inside the hook.
  Queued, not interrupting, and silent during dialogue.

  **Known limit:** `Om008000` is documented as the BODY SHOP OM (`docs/sf6-screens.md` § 7). Whether
  other shop kinds are other OM numbers with their own triggers is **not established** — if some shops
  stay silent, that is why, and the fix is to hook their families too.

  **In-game verification checklist:**
  - [ ] `ShopEntryHooks initialized (enter=True, leave=True)` — both must be True.
  - [ ] Walk into a shop: does it say "Entrando en <name>", with the same name the map shows?
  - [ ] Walk out: "Saliendo de <name>"?
  - [ ] Do any shops stay silent? Name them — that tells us which OM families are missing.
- **Sub-area announcements, 2026-09-10 (tester asked: "algo que nos pueda leer subzonas... salón,
  restaurante, calle tal").** *Superseded 2026-09-27: the banner component was never found in any
  session; its requests are hooked instead and the section id route is fixed — `docs/wt-orientation.md`.* `Hooks/WorldTour/SectionNoticeHooks.cs` speaks
  `app.UICityHud_SectionNotice.mTextSection` — the game's OWN on-screen banner, already localized and
  already worded ("Hong Hu Lu Chinatown - Calle principal"), announced once per change and queued rather
  than interrupting. The game decides when a crossing counts, so nothing is inferred. Full survey of what
  the game does and does not model in `docs/sf6-screens.md` § naming WHERE THE PLAYER IS.

  **Street names and intersections DO NOT EXIST in SF6** — searched the whole tree; the only `Street` is
  a battle type. Recorded so it is never hunted again. "Cruce entre X e Y" could only ever be our own
  synthesis.

  **Two leads NOT yet built, both high value:** interiors are nameable via
  `GUIAccessDataManager.GetWTCityOMSetterData_OM(OMType.Shop)` → `WTCityOMSetter.GetDispName()`, with the
  game's own enter/exit in `Om008000.CallShopInSoundTrigger/CallShopOutSoundTrigger`; and
  `CitySectionDataUserDataDict` is ENUMERABLE, which likely fixes `ZoneNameService`'s keyed lookup +
  `ManageId` cross-check (`ZoneNameService.cs:130`) that can reject good records — a plausible cause of
  the `CurrentSectionId=0` reading measured 2026-09-04.

  **In-game verification checklist:**
  - [ ] `Section notice: app.UICityHud_SectionNotice found in the scene` — if absent, the component is
        not reachable and this whole route is dead.
  - [ ] Walking between named parts of a district: does it speak them, once each, at the right moment?
  - [ ] Is the wording the same as the banner on screen?
- **Getting the player to an objective on a disconnected navmesh island, 2026-09-09.** Two independent
  mechanisms, both aimed at the tester's blocker ("con el radar no encuentro forma de llegar hasta ahí").

  **1. Route to the ACCESS POINT, not the object's origin.** `ProgressMisionInfo.AccessTarget`
  (`UICityHud_MissionGuide.cs:195`) → `AvatarAccessTargetBase.GetNearPos(ref vec3, Transform, ref vec3)`
  (`AvatarAccessTargetBase.cs:467`) is the game's own "where must the player stand to interact".
  `Services/WorldTour/AccessPointReader.cs`; `MissionTargetService.FindFromGuide` prefers it. The mod had
  been routing to object origins all along — close enough for a street NPC, meaningless for one on an
  upper floor. Refused if further than `5 x ARRIVED_M` from its own target (a misread), and any failure
  falls back to the transform, so it cannot regress.

  **2. `Services/WorldTour/ApproachFinder.cs` — when the objective genuinely cannot be walked to, guide
  to the nearest place that can.** Candidates in order of increasing distance, so the first that routes
  is the closest: the objective projected straight DOWN to the player's height (the street beneath it —
  the answer nearly every time), then two rings of `FieldHeadingService.SECTORS` = 8 points at
  `2 x ARRIVED_M` and `4 x ARRIVED_M`. All radii and counts come from vocabulary this mod already uses.

  **The call pattern is unchanged, and that is the point.** Exactly ONE `queryPathSync` per resolve in
  every branch. When the objective is known unroutable the beacon SPENDS that query on a probe instead
  of re-asking the same failing question once a second — so the search is free and never runs two
  queries in a frame. **That is the rule the 15:07 crash bought**: the earlier attempt re-ran the query
  into the SAME `PathQueryReport` inside one call. Each probe here is an ordinary `TryReach` with its
  own fresh report and its own rate limit.

  **The player is told the truth about it.** A stand-in is not the objective: R3 says "Hacia el pie de
  X" rather than "Hacia X", and ARRIVING says "Al pie de X. Está N metros por encima de ti" instead of
  "At the objective" — which, at the foot of a building the objective sits on top of, would be the
  mod's worst kind of lie.

  **In-game verification checklist:**
  - [ ] `Approach: probe N of 17 reached (x, y, z) - guiding there instead of the objective` — report N
        and the coordinates. Probe 1 is the ground directly below; if that is the one, the whole idea
        works and the rest of the ring never runs.
  - [ ] `Approach: no reachable stand-in within 16 m ... after 17 probes` means even the wide ring
        failed, and the objective's surroundings are not walkable at all.
  - [ ] `Access point: bound GetNearPos on ...` / `access point is N m from the target`.
  - [ ] Does the beacon now actually walk you to the foot of the upper-floor objective, with the
        distance counting down?
  - [ ] Does guidance to ordinary street objectives still behave exactly as before? Both mechanisms are
        no-ops when the objective routes normally, so any change there is a regression.
- **The mission beacon now routes to the ACCESS POINT, not the target's own origin (2026-09-09).**
  This is the answer to "me pide llegar a un objetivo pero según tú no hay camino", and the diagnosis
  that found it is the reason two prepared fixes were thrown away unbuilt.

  **What the log proved.** 69 identical verdicts: `PathNotFound (startNode=<varies>, destNode=10421)`.
  The destination **resolved to a real navmesh node** — it is NOT off-mesh — and simply had no walkable
  link to the player's node. The objective sits on a **disconnected island of navmesh**: an upper floor
  or roof reached through a door, lift or scripted transition the navigation graph does not model.
  **This killed both candidate fixes before they were written**: snapping the point to the mesh (it was
  already on it) and widening `EndNodeSearchRange` (the node was found first try). Neither could ever
  have worked, and both would have cost a test session and mutated a borrowed NPC's component.

  **"Route to the entrance" is not possible either, and that is now recorded.** World Tour does not
  model doors or transitions: `WTAreaManager` areas are streaming cells keyed by name hash with no
  geometry (`WTAreaManager.cs:135-160,1573`), `CitySectionManager` is districts only, there is no
  `*Door*`/`*Elevator*`/`*Stairs*`/`*Indoor*` type anywhere under `app.worldtour.*`, and no OM carries
  an entrance classification. `isNearActiveArea(vec3, ref IList<uint>)` (`:1630`) is a by-ref REFERENCE
  parameter — forbidden. A mission target record carries no area id, no section id and no approach
  position (`WTMissionDefine.cs:659`, `WTMissionSystem.cs`).

  **What the game DOES publish: `ProgressMisionInfo.AccessTarget`** (`UICityHud_MissionGuide.cs:195`) →
  `AvatarAccessTargetBase.GetNearPos(ref vec3 outPos, Transform tr, ref vec3 searcherPos)`
  (`AvatarAccessTargetBase.cs:467`) — "the nearest access position to this searcher", i.e. **where the
  player must stand to interact**. `Services/WorldTour/AccessPointReader.cs` reads it and
  `MissionTargetService.FindFromGuide` now prefers it over `TargetObject.Transform.Position`.

  **This is a better destination in every case, not a patch for the island one** — the mod has been
  routing to object origins all along, which merely happened to be close enough for street NPCs. The
  two by-ref parameters are VALUE types, handled with the `FieldOutBuffer` contract this mod has always
  used for `castRayAll(ref vec3, ref vec3, ...)`; nothing by-ref is a reference type and nothing is
  constructed per call.

  **It cannot regress anything:** an access point further than `5 x ARRIVED_M` from its own target is
  treated as a misread and refused, and any failure falls straight back to the transform — the exact
  behaviour that shipped before.

  **Also fixed:** the no-route log line printed the `DestNodeNotFound` explanation regardless of the
  actual verdict, so it asserted "the destination is off the navmesh" underneath a `PathNotFound` that
  meant the opposite. The explanation now follows the verdict.

  **Breakable beacon, cost corrected:** in game it found `250 intact of 250 scanned across 3 of 5
  families` and read the state correctly (`Om001000.State.Destroyed = 1 (byte)`). But 250 props at ~7
  engine calls each every 1.5 s is over a thousand calls a second — the order of magnitude that has
  crashed this mod. `RESCAN_MS` raised 1500 -> 4000.

  **In-game verification checklist:**
  - [ ] `Access point: bound GetNearPos on ...` then `access point is N m from the target` — report both.
        "not found" means the objective publishes no access target and nothing changed.
  - [ ] Does the beacon now route to the upper-floor objective (a real `Exist=True` with a distance that
        counts down), instead of `PathNotFound`?
  - [ ] Does guidance to ordinary street objectives still behave exactly as before?
- **"No route" is now DIAGNOSED instead of assumed, 2026-09-09.** The tester's intuition — "creo que el
  objetivo sí es alcanzable, solo hay que mirar un poco mejor" — has a mechanism behind it, and the
  engine already answers the question. `PathQueryReport.FailReport` (`PathQueryReport.cs:24`) carries
  `isFailed(FailAttribute)` (`FailReport.cs:157`) over
  `{DestPosNotSpecified, StartNodeNotFound, DestNodeNotFound, PathNotFound, PathfindInterrupt}`
  (`:114`), plus `StartNodeID`/`DestNodeID` (`:138/:144`).

  **`DestNodeNotFound` = the destination point is not ON the navmesh** (a roof, a walkway, inside
  geometry) — our problem to solve. **`PathNotFound` = it is on the mesh and genuinely cannot be
  reached** — the game's answer, to be respected. The mod was treating both as "fall back to a straight
  line". `FieldNavPath.Diagnose` now logs which, once per distinct verdict. **Every call is a by-value
  read on a report already owned: zero new risk**, which is the point after two crashes today.

  **What was NOT done, deliberately.** There is **no** "snap a point to the navmesh" API for a
  `NavigationSurface`: `queryClosestNode` lives only on `MapHandleBase` (`:379/:382`) and the only
  producers are `AIMap.findMapHandle` (FORBIDDEN, crashed the game) and the statics
  `NavigationManager.findMapHandle(string)` / `findMapHandleByFlag(uint)` (`:228/:234`) — shape-safe,
  but the RE could not tell from the stubs whether they share the crashing native path, so it is not
  being tried on a guess. `PathQueryInfo` (`PathQueryInfo.cs:11-35`) has only `FilterTarget`,
  `FilterGroup`, `InterruptDist`, `ShowFailedWarning` — **no "allow nearest" flag**. None of the six
  `queryPathSync` overloads (`NavigationSurface.cs:556-571`) has a by-ref reference type, so all are
  shape-legal if ever needed.

  **The candidate fix, held until the diagnosis comes back:** widen the engine's own end-node search
  with `NavigationSurface.setEndUnderSearchLength(float)` (`:574`),
  `setUnderSearchSettingType(...)` (`:580`), `Navigation.EndNodeSearchRange` (`:1107`),
  `NoEndNodeSearch` (`:1099`) and especially `TryPossibleNearNavigation` (`:1115`). Float/enum setters
  on a live component — shape-safe, and inside types already held. Two caveats that must be honoured:
  it mutates a BORROWED NPC's component (restore the previous value), and whether `queryPathSync`
  honours these is not determinable from the stubs.

  **In-game verification checklist:**
  - [ ] `NavPath no route because: <reason> (startNode=..., destNode=...)` — report the reason verbatim.
        `DestNodeNotFound` means the objective is reachable and we are asking the wrong question;
        `PathNotFound` means it really is not walkable and the mod should say so and stop trying.

- **Breakable beacon, precedence and cadence fixed the same day.** The first version yielded to
  `MissionBeaconHooks.Homing` — which is true whenever an objective is active, i.e. nearly always — so
  the cue was silent almost all the time ("no me indica mucho"). That yield is removed: the two are
  different tasks with different sounds, and the objective is usually far while the crate is at arm's
  reach. Only the NPC beacon still stands down. The cadence gradient now spans `RANGE_M * 0.6` instead
  of the full 25 m, so the speed-up is spent on the stretch where the player is actually closing in.
- **Breakable-object beacon, 2026-09-09 (tester: "para avanzar necesito romper unos carteles").** A
  looping homing pulse on the nearest INTACT breakable street prop, using the tester's own
  `sounds/breakable object.mp3`. `Services/WorldTour/BreakableCatalog.cs` +
  `Hooks/WorldTour/BreakableBeaconHooks.cs`.

  **The type is `app.worldtour.om.Om001000 : WTCityOM : Behavior`** (`Om001000.cs:10`) — the World Tour
  smashable, carrying `Vital` (`:150`), `EventDestroy` (`:254`) and the state below. Asking
  `via.Scene.findComponents` for that CONCRETE type returns only breakables, so no name filter is needed
  (unlike the tutorial panels). Four sibling destructible families are scanned alongside it:
  `Om039000` (`State : byte`), `Om021000` / `Om038000` (`IsDestroyEnd` bool), `Om028000` (`Vital` float).

  **"Already broken" is `Om001000.CurrentState`, type `State : byte { Default, Destroyed }`**
  (`Om001000.cs:115-119,158`) — **ONE BYTE**. An int read would drag in the neighbouring bytes; the
  `Destroyed` value is resolved from the TDB BY NAME at byte width via
  `FieldProbeService.ReadEnum(..., byteWidth: true)`, never written as a literal. `IsDefaultState`
  (`:238`) is deliberately unused — a getter-only property, which does not dispatch on concrete types.

  **A family whose state cannot be read is DROPPED, never assumed intact.** A cue over rubble is worse
  than no cue: it would send the player to smash something that is not there, repeatedly, with no way to
  tell the mod is wrong. Each family logs its verdict once.

  **Deliberately NOT following the RE advice to cache component handles between frames.** That is the
  wrapper-lifetime crash this mod has already paid for twice. The scene is re-scanned every
  `RESCAN_MS` = 1500 ms and each prop is copied into a plain struct immediately; props do not move, so a
  cached position stays true and a broken one simply vanishes on the next scan.

  **Precedence is now mission > breakable > passer-by.** `FieldBeaconHooks` gained one line yielding to
  `BreakableBeaconHooks.Homing`, mirroring how it already yields to the mission beacon. Three cues, three
  distinct sounds.

  **Known gap:** the crate-vs-ball-vs-sign distinction is NOT determinable from the stubs — `Om001000`
  covers them all and its sub-variant is not exposed. The cue is therefore the same for every kind, and
  nothing is spoken. `WTOmAccessTarget.GetDispName()` (`WTOmAccessTarget.cs:450`) is the route if a
  spoken name is ever wanted.

  **In-game verification checklist (nothing below has run):**
  - [ ] `Breakables: N intact of M scanned across K of 5 families` — report N, M and K. K=0 means no
        family bound and nothing will ever pulse.
  - [ ] `Breakables: app.worldtour.om.Om001000.State.Destroyed = 1 (byte)` — if it says "not found",
        the main family is being skipped and that is the bug to chase.
  - [ ] Does a pulse lead to a breakable sign/crate, and does it STOP when you smash it (within ~1.5 s)?
  - [ ] Does it stay silent while the mission beacon is pulsing, and does the NPC beacon stand down
        while a breakable is in range?
- **CRASH ON THE WORLD TOUR LOADING SCREEN, 2026-09-09 15:07 — caused by the mod, and REVERTED.**
  The log stops dead with no shutdown sequence, one line after a change made minutes earlier:
  ```
  step 4: calling queryPathSync ... to (-229.26, 12.18, -104.32)
  step 4: no route to (...); asking again at the player's own height 0.00   <- the new retry
  step 5: reading PathQueryReport.Exist / PathInfo
  step 5: calling PathInfo.createTraceLine()
  step 6: reading calcDistance / PathPointCount / getPathPointInfo(i)
  <end of file>
  ```
  **Cause: re-running `queryPathSync` into the SAME `PathQueryReport`.** Until that change a failed
  query (`Exist=false`) produced `points=0` and was harmless — earlier logs show exactly that, repeatedly,
  with no crash. The second query evidently leaves the report's `PathInfo` in a state that
  `createTraceLine` and the point reads cannot survive. **If the "objective is on a roof" case is worth
  solving, it needs a FRESH report per query and it must be proven before it ships.** The retry is gone
  and the reason is recorded in `FieldNavPath.TryReach` so it is not re-attempted casually.

  **`FieldNavFunnel.cs` was ALSO removed — but note it was wrongly blamed first.** The initial diagnosis
  (hundreds of `getPortalEdgeInfo` wrappers per second flooding the GC thread) was plausible and WRONG:
  the crash log contains only 8 `NavPath step` lines in total, no flood of any kind. The funnel is gone
  because it was an unproven theoretical improvement on a path that already worked, shipped without a
  failure to justify it — not because it was shown guilty. The algorithm and the reasoning are in
  `docs/sf6-screens.md` § 3 if it is ever wanted, but it must not return without a measured need.

  **What survives:** the camera aim (proven: a real 151 m -> 67 m guided walk), the map-selection
  capture, and `BeaconDestination.NoRoute` + the spoken "no hay ruta a pie" — which costs no extra
  engine call, only reporting what the first query already answered. That is the whole point: the player
  is told the difference between a way and a bearing.

  **Standing lesson for this mod, third time it has bitten:** a change that adds an engine call inside an
  existing sequence must be justified by a failure the tester is actually suffering, and must be proven
  before the next one is stacked on top of it.

  **In-game verification checklist (nothing below has run):**
  - [ ] Does World Tour load at all now? That is the only question that matters this round.
  - [ ] Does R3 still guide the way it did in the 151 m walk?
  - [ ] On the rooftop objective, does it now say "no hay ruta a pie" rather than crashing or marching
        into a wall?
- **FOURTH IN-GAME PASS, 2026-09-09 14:50 — the camera aim WORKS, and the wall-bumping was a different
  bug entirely.**

  **Camera aiming is FIXED and proven.** `Camera aim calibrated: yaw is CurrentRotation.y in radians`
  and then a real guided walk: `Aimed at 'objetivo' (151 m ... 139 ... 120 ... 98 ... 79 ... 67 m, via
  the route's next bend)` — the distance counts steadily down, which is a player being led along a
  route. The fix was `Field.GetDataRaw(objAddr, false)` for the live field address; see
  `docs/sf6-architecture.md` § gotchas, "To WRITE a struct field on a live object".

  **The walls: the objective was TWELVE METRES UP and there is no route to it.**
  `step 4: ... to (-229.26, 12.18, -104.32)` → `Exist=False; points=0`, then
  `Aimed at 'objetivo' (22 m, straight line)`. A rooftop / upper floor / walkway. The navmesh answered
  correctly; the MOD was at fault — it treated "no route" as "fall back to the straight line" and
  confidently pointed the player at the foundations of the building. **Silently.** Two fixes:
  1. `FieldNavPath.TryReach` now RETRIES the query with the destination projected to the player's own
     height when the first answer is `Exist=false` and the target is more than one navmesh AGENT HEIGHT
     above them. The route to the foot of the building is genuinely useful and is where the way up is.
  2. `BeaconDestination.NoRoute` is a new state, and R3 now SAYS "No hay ruta a pie hasta X" instead of
     pretending. A bearing is still given, because a direction beats nothing — but the player is told it
     is a direction and not a way.

  **Also added: a proper funnel** (`Services/WorldTour/FieldNavFunnel.cs`). `PortalPos` is a point on an
  EDGE, so a straight line to it cuts corners by luck of the geometry; the simple-stupid-funnel string
  pull returns a corner reachable in a straight line BY CONSTRUCTION. The old straight-run aim remains
  as the fallback when the portal edges cannot be read. **Not the cause of the reported bug** (the
  successful walk above used the old aim), so it is an improvement on principle, not a fix — watch for a
  regression on routes that previously worked.

  **Note:** `createTraceLine()` ran again and again changed nothing measurable. Still unexplained.

  **In-game verification checklist (nothing below has run):**
  - [ ] An objective on a ROOFTOP or upper floor: does R3 now say "no hay ruta a pie" instead of walking
        you into a wall? And does the retry at ground level find a route to the building's foot?
  - [ ] Do routes that already worked (the 151 m walk) still work with the funnel in place? A regression
        here means the funnel's left/right derivation is wrong.
  - [ ] `NavPath step 4: no route to (...); asking again at the player's own height` — does it appear,
        and does the second query then succeed?
- **Map-selection aiming replaces the minimap radar, 2026-09-09 (third pass).** User decision after two
  test sessions: *"quitemos lo del radar y hagamos que R3 me apunte el personaje hacia la dirección del
  punto que esté seleccionado en el mapa, sea misión, marcador propio, npc o lo que sea."*

  **DELETED:** `Services/WorldTour/{MapIcon, MapIconCatalog, TrackerSelection, TrackerButtons,
  FieldInputMask}.cs` and `Hooks/WorldTour/FieldTrackerHooks.cs` — the whole modal radar. Reason: its
  icon source could never be reached. Neither `app.UIMiniMapWindow` NOR its owner
  `app.UICityHud_CityMiniMap` turns up in `via.Scene.findComponents`, confirmed in game twice
  (`Map icon catalogue: the live app.UIMiniMapWindow was not found in the current scene`), even though
  `app.UICityHud_MissionGuide` is found that way and always has been. **Unexplained, and still open** —
  it is also why the compass has been running on a guessed +Z axis since 2026-09-05
  (`Heading: north from fallback axis`).

  **NEW:** `Services/WorldTour/MapSelectionMemory.cs` (the phone map's cursor selection, kept as plain
  floats so it outlives the map closing), `Services/WorldTour/CameraAimService.cs`, and
  `Hooks/WorldTour/MapAimHooks.cs` (**R3 / V** = face the selection; **Shift+V** = route guidance
  on/off). The capture is a single line in `DeviceMapHooks.AnnounceCursorIcon` — every kind of thing on
  the map arrives through that one branch, including the player's own pins, since the game builds a
  pin's icon with `MakePinIconInfo` and so its `IconInfo.GlobalPos` is the pin's own position.
  `BeaconDestination` now resolves the map selection instead of a tracker target, so the beacon guides
  to it for the whole walk.

  **Aiming turns the CAMERA, not the avatar** (`WTFollowCameraController.CurrentRotation`, a `via.vec3`
  euler field, `WTFollowCameraController.cs:20`): World Tour movement is camera-relative, so turning the
  avatar would leave "forward" pointing the old way — and the avatar's own rotation is rewritten every
  frame by `AvatarFieldParam_Common.ApplyDefaultRot` anyway. Held for 4x `CameraHoldService.HOLD_MS`
  because the follow layer interpolates. **Which euler component is the yaw, and whether it is radians
  or degrees, is NOT in the stubs and is NOT guessed**: both are derived at runtime by comparing every
  component, both units and both signs against the camera's demonstrable bearing from
  `GetCameraForward()`, and the write is read back to prove it landed. If nothing matches or the write
  is a copy, the service reports unavailable and the key SPEAKS the clock direction instead — a press
  never produces silence, and a wrong guess never spins a blind player's camera somewhere arbitrary.

  **PATHFINDER: CONFIRMED WORKING IN GAME 2026-09-09, and the bug was mine.** The borrowed-NPC agent
  resolved (`hasValidMap() on agent 1 of 19`) and `queryPathSync` returned real routes — `Exist=True`
  with 65 / 12 / 2 points, and a correct `Exist=False` for an objective 12 m up a building. What made it
  useless: **the path points are dense SAMPLES, not corners** — 65 points for a 19 m route, one every
  30 cm — and the aim took the first point past the agent radius, i.e. about a metre ahead, at a
  position dominated by the trace's initial wiggle. In the logged case the destination was 19 m to −X
  and the chosen point was 1 m to +X, so the beacon panned the player at their next footstep, sometimes
  behind them. That is the reported "it leads me into a wall". `FirstCorner` is replaced by `AimPoint`,
  which walks outward extending a straight RUN and aims at its far end — "same bearing" = within half a
  clock hour (finer than can be spoken), "far enough" = `MissionBeaconHooks.ARRIVED_M`. It stops as soon
  as a straight run is long enough, so it reads about a dozen points instead of 65.
  `FieldNavPath.Enabled` now defaults to **true** (kill switch still on Shift+V).

  **SECOND IN-GAME PASS, 2026-09-09 — two verdicts.**
  1. **Camera aiming NEVER WORKED:** `Camera aim unavailable: no euler component matched the camera's
     actual bearing`, and `Could not aim ...; spoke the bearing instead` **17 times**. So R3 only ever
     spoke a direction. The approach was wrong in principle — comparing a component against the camera's
     ABSOLUTE bearing requires the two to share a zero and a winding direction, which nothing promised.
     `CameraAimService` is rewritten to calibrate DIFFERENTIALLY (which component changes in proportion
     to the heading changing, over two independent turns) and to apply the aim as a DELTA, which needs
     no absolute mapping and is recomputed every frame so the turn converges. Needs a second or two of
     the player turning the camera before it can aim; until then it speaks, as before.
  2. **The path points were being misread, and THIS was "it leads me into a wall".** `PortalPos` is a
     PORTAL CROSSING — the boundary between two navmesh polygons, positioned somewhere along an EDGE —
     not a waypoint; the count is one per polygon, not a 30 cm sample spacing (the note previously
     recorded here). Raw, two completely different destinations produced nearly the same bearing because
     both were the same few edges around the player's own polygon. The missing step is
     `PathInfo.createTraceLine()` (`PathInfo.cs:307`, zero params, void), the string-pulling pass that
     turns the corridor into a route; it now runs before any point is read, and points flagged
     `IsSkiped` are skipped. Full detail in `docs/sf6-screens.md` § 3.

  **Confirmed WORKING in that pass:** the map-selection capture (`Map selection: 'Beat Square, viaje
  rápido' at (-36.0, 0.1, -110.0)` — shops, NPCs and fast-travel points all captured with real world
  coordinates), R3 binding (`MapAimHooks: R3 = 2000`), and the map pick reaching the pathfinder (a
  228 m query to the chosen shop).

  **THIRD IN-GAME PASS, 2026-09-09 14:05 — the differential calibration WORKS, the write did not.**
  `Camera aim calibrated: yaw is CurrentRotation.y in radians (a -26.9 deg turn moved it by -0.469)` —
  and -26.9 x pi/180 = -0.4695, so the measurement is exact. `Aimed at 'objetivo' (127 m, via the
  route's next bend) using CurrentRotation.y in radians` then fired **nine times** with the read-back
  check passing every time. **And the camera never moved.**

  **Root cause: the write went to a COPY, and the verification was reading that same copy.**
  `FieldProbeService.Member` tries the untyped GETTER first, and a getter returning a struct returns a
  copy of it; writing to the copy succeeds and reading it straight back returns exactly what was
  written. The check proved nothing. `CameraAimService` now resolves the `CurrentRotation` FIELD
  directly (walking up to `WTFollowCameraController`, where it is declared) and reads it with the
  CONTROLLER as the container, which is what yields an address inside the live object; the write is then
  verified against a **freshly obtained** view rather than the wrapper it was written through.
  **A read-back through the same wrapper is not a verification** — worth remembering mod-wide.

  **`createTraceLine()` ran and changed NOTHING measurable.** `NavPath step 5: calling
  PathInfo.createTraceLine()` appears, and the resulting aim `(-222.43, 0.17, -118.20)` with `points=65`
  is byte-identical to the value the same route produced before string-pulling existed. Either the
  optimisation is already applied by `LineOptimizeTiming`, or its result is published somewhere other
  than `PortalPos`. **Unresolved.**

  **Open question, not yet answerable:** the route reports **127 m walked to an objective 19 m away in a
  straight line** (a 6.7x detour), and that objective's `y` is 3.09 — three metres up. That may be
  entirely correct (a target on a walkway genuinely needs a long way round) or may mean the route is
  wrong. It cannot be told apart until the camera actually turns and the route is walked.

  **In-game verification checklist (nothing below has run):**
  - [ ] **Does the camera visibly turn now?** That is the whole test. The log will say
        `Aimed at ...` either way — only the player can say whether the view moved.
  - [ ] If it still does not: look for `writing CurrentRotation.y does not reach the live controller`,
        which is the new honest failure and means the field write itself is refused.
  - [ ] Once it turns: walk the route and report whether the 127 m detour was real.
  - [ ] `Camera aim calibrated: yaw is CurrentRotation.<x/y/z> in <unit>` — the single line that says
        whether R3 can turn at all. It should appear within seconds of moving the camera.
  - [ ] `NavPath step 5: calling PathInfo.createTraceLine()` — and then does the step-7 aim finally lie
        in the direction of the destination rather than sideways?
  - [ ] Open the phone map, put the cursor on something, close it. Log: `Map selection: '<name>' at
        (x, y, z)`. Does the position look like a real world coordinate?
  - [ ] Press R3 (and V). Either `Camera aim: yaw is CurrentRotation.<c> in <unit>` followed by a real
        turn, or `Camera aim unavailable: ...` and a spoken clock direction. **Report which**, and the
        exact line — that line is the answer to the one thing the decompiled code could not tell us.
  - [ ] After aiming, does holding the stick forward actually walk toward the thing?
  - [ ] Does the mission beacon now pulse toward the map selection instead of the story objective, and
        say "En <name>" (not "objetivo") on arrival?
  - [ ] With route guidance on (default), does the beacon's direction now change sensibly as you walk —
        rather than pointing a metre ahead? Log `NavPath step 7: ... aim (x,y,z) vs avatar (x,y,z)`:
        the aim should now be metres away, not centimetres.
  - [ ] Shift+V still toggles route guidance and says so.
- **Minimap tracker + route guidance, 2026-09-09 — ALL NEW, NOTHING HAS RUN IN GAME.** User request:
  "a radar that tracks things on the player's minimap, R3 or V, d-pad up/down to cycle, R1/L1 to change
  category, and use the pathfinder so the beacon takes you there — and keep tracking the same category
  automatically once the player does whatever removes that thing from the radar."

  **What it reads.** `app.UIMiniMapWindow.mIconInfoList : IDictionary<eUpdateTargetType,
  IconInfo_Array1D>` (`UIMiniMapWindow.cs:260`) bounded by `mInfoCount` (`:268`) — the arrays are
  REUSED BUFFERS, so entries past the count are stale. Each `IconInfo` gives `TitleText` (already
  localized), `Type`, `GroupType`, `UniqueIndex` (**int**) and `GlobalPos` (the real world position).
  The live window comes from `FieldHeadingService.MiniMapWindow()` — now the mod's single search for
  it, shared with the compass. Full table in `docs/sf6-screens.md` § World Tour minimap tracker.
  **First IL2CPP dictionary read in this codebase**: `ContainsKey` + `get_Item`, never `TryGetValue`
  (its out parameter is a reference type — the documented crash family); keys taken from the enum, not
  by enumerating the dictionary. Icon structs are read with `FlowHelper.Call(array, "Get", i)` and
  `FieldProbeService.Member`, NOT `GetListItem` (which casts to `ManagedObject` and would return null
  for every struct element), and copied into a plain `MapIcon` immediately — no engine wrapper is kept
  across frames.

  **Auto-retracking is free, and that is the design.** No per-category "done" flag was needed or found
  (none exists on `IconInfo` or `CityPointDataInfoBase`): the GAME removes an icon from the minimap
  when the player consumes it, so the tracker simply notices its pinned `UniqueIndex` stopped appearing
  and moves to the nearest remaining icon of the same category. Both cursors are pinned by identity,
  never by list index, because the list is re-sorted by distance every poll — and that identity is the
  COMPOSITE `MapIcon.Key` (`GroupType/Type/UniqueIndex/TitleText`), **not `UniqueIndex` alone**: only
  `MakePinIconInfo` takes a unique index, so it may be 0 for every shop and mission, which would make a
  whole category read as a single target (cursor stuck, auto-retracking dead). Worth confirming in game
  what `UniqueIndex` really holds for a non-pin icon.

  **Input is taken with the game's own lock, not a hook**:
  `app.InputManager.RequestMaskLowerAndEqualLayers(InputLayerPriority.Action)` (`InputManager.cs:1524`),
  the same mechanism the phone UI uses. **`RequestMaskLowerLayers` was the first attempt and is wrong**
  — the enum is `UI=0 ... Action=3, Other=4` and "lower" means lower PRECEDENCE, so it would have
  masked only `Other`, left the avatar walking, and logged success anyway. It lasts
  ONE FRAME and is re-requested every frame, so it fails safe — if the mod stops ticking the player's
  controls come back next frame and they can never be locked out. The press that OPENS the list still
  reaches the game (`ACT_RSB` etc. are real field slots); every press after it is the tracker's.
  Button bits are resolved from `via.hid.GamePadButton` BY MEMBER NAME (`RStickPush`, `LUp`, `LDown`,
  `LTrigTop`, `RTrigTop`), so no hex is written into mod code.

  **The category cycle starts on "mission objective"** (a documented -2 sentinel, the one literal in
  the feature — `ICON_GROUP_TYPE` is non-negative), which tracks nothing. That is deliberate: the
  tracker must never take the mission beacon over on its own, and cycling back to it is how the player
  hands the beacon back. Every session and every city load starts there.

  **`FieldNavPath` now has a caller and a live switch.** `PATHFINDER_ENABLED` became
  `FieldNavPath.Enabled` (default OFF, toggled by **Shift+V**), so a session that starts misbehaving is
  rescued without a rebuild. `BeaconDestination` keeps the AIM point (the route's first corner) apart
  from the DESTINATION (distance, ping cadence, arrival) — measuring arrival against a corner would
  announce "you are here" at every turn. `FirstCorner` no longer trusts path point 0: it walks the
  points and takes the first one farther from the avatar than the navmesh agent's own `AgentRad`, which
  settles the open "is point 0 the start or the first turn" question without having to answer it.

  **Cost while idle is zero by construction.** The catalogue is only read when the list is open or the
  player has cycled off the default category, so a normal World Tour session never walks the scene for
  the minimap component — this mod has been taken down twice by per-frame wrapper churn finalizing on
  the GC thread, and the tracker must not be a third.

  **Reviewed adversarially before shipping, five real defects found and fixed** (the input mask above;
  the `UniqueIndex` pin above; `TrackerButtons.Reset` zeroing its edge state, which made a still-held
  R3 read as a second press and slam the list shut on the frame after it opened; arrival announcing the
  MISSION objective on reaching a tracked shop; and the idle scene walk).

  **FIRST IN-GAME PASS, 2026-09-09 — no crash, and two root causes found.** Shift+V did NOT crash the
  game: `queryPathSync` is safe to reach for. But the beacon did not change, and the log said why.
  1. **`NavigationSurface` is an AGENT, not the navmesh, and the player has none.** `NaviSurface` null
     and `getComponent(NavigationSurface)` null, once a second for two minutes, so `TryReach` returned
     "no answer" and the beacon fell back to the straight line silently. Fixed by BORROWING a navigating
     NPC's agent from the scene (first with `hasValidMap()` true, never cached). Details in
     `docs/sf6-screens.md` § 3.
  2. **The minimap window is not a scene component**, so the icon catalogue was empty all session
     (`Nada en todo` on every category) — and the same bug had been quietly feeding the compass a
     GUESSED north since 2026-09-05 (`Heading: north from fallback axis`). It is owned by
     `app.UICityHud_CityMiniMap.MiniMapWindow` (`UICityHud_CityMiniMap.cs:7,27`); `UICityHud_*` IS
     findable in the scene, which is why `MissionGuideReader` always worked. Details in
     `docs/sf6-screens.md` § 2b.

  **Confirmed WORKING in that pass:** the modal itself, the pad buttons (`R3=2000, up=1, down=2,
  L1=100, R1=400`), the input mask (`bound app.InputManager.RequestMaskLowerAndEqualLayers(...Action=3)`),
  the category cycle and every spoken line (`Siguiendo objetivo de mision`, `Radar cerrado`, `Nada en
  todo`). Only the icon SOURCE was empty.

  **Also fixed the same day:** `MapIconCatalog` had a failure path that returned without logging
  anything (which is what made cause 2 hard to see) — every exit now says why; and `FieldNavPath.Fail`
  now stops the numbered trace, because a route that fails every second wrote four log lines a second,
  ~500 in that session, the log-flood pathology this mod has been bitten by twice. The scene-component
  lookup was down-duplicated from four copies into `FieldProbeService.SceneComponents`.

  **In-game verification checklist (the first four are now ANSWERED; the rest still unrun):**
  - [x] ~~`Tracker buttons: ...`~~ CONFIRMED 2026-09-09: all five resolved.
  - [x] ~~`Field input mask: bound ...`~~ CONFIRMED 2026-09-09.
  - [x] ~~R3 opens the list and says "mission objective"~~ CONFIRMED 2026-09-09.
  - [x] ~~Shift+V does not crash~~ CONFIRMED 2026-09-09.
  - [ ] The one-time log line `[SF6Access] Map icon catalogue: N icons across M buckets` — report N and
        M. **Was ABSENT in the 2026-09-09 pass**, which is what exposed the minimap bug; it should now
        appear. `the live app.UIMiniMapWindow was not found in the current scene` means the HUD-owner
        route did not work either.
  - [ ] `[SF6Access] Heading: minimap window via app.UICityHud_CityMiniMap.MiniMapWindow` followed by
        `Heading: north from minimap = (x, z)` — the compass finally on the game's own north rather than
        the fallback axis. This is a fix to a PRE-EXISTING bug, worth reporting separately.
  - [ ] `[SF6Access] NavPath step 1/2` then `NavPath route: ... on ...` — does a borrowed NPC agent
        resolve, and what agent radius/height and map name does it report?
  - [ ] `[SF6Access] Tracker buttons: R3=2000, up=1, down=2, L1=100, R1=400` — a 0 anywhere means that
        member did not resolve and only the keyboard mirror works for it.
  - [ ] `[SF6Access] Field input mask: bound app.InputManager.RequestMaskLowerAndEqualLayers(...)` — if it says
        "not found", browsing will ALSO reach the game (a warning is logged when the list opens).
  - [ ] R3 (and V) opens the list and it says "mission objective" — NOT a shop. If it names an icon on
        the first press, the default category is wrong and the beacon has been hijacked.
  - [ ] R1 cycles to "everything" and names the nearest icon with a clock hour and metres; L1 goes back.
        Do the category words match what the phone map calls the same icons?
  - [ ] With the list open: does the avatar stay still, and does the d-pad NOT open the shortcut wheel?
  - [ ] Close with R3: it should say "Following <icon>" and the mission beacon should now pulse toward
        that icon instead of the story objective.
  - [ ] Walk into/complete the tracked thing: does it say "Next: <the following one>" by itself, and
        does the beacon move on? This is the whole feature.
  - [ ] Cycle back to "mission objective" — the beacon must return to the story objective.
  - [ ] **Shift+V (route guidance) is the risky press.** Read the log for `NavPath step 1..9` lines in
        order; the LAST one present names the call that did not return. Step 6 is the first call that
        has never run in game. If SF6 dies here, that is the pathfinder and Shift+V should stay off.
  - [ ] With route guidance on and a destination round a corner: does the beacon pan toward the corner
        while the spoken distance still counts down the WALKED metres, and does it not announce arrival
        at the corner?
- **World Tour navigation radar — API mapped from decompiled code, in progress.** Porting the RE7 mod's
  navigation-radar concept to World Tour. The physics raycast, SF6 collision filter (`app.CollisionSystem`),
  authoritative ray-height, avatar-capsule, ground/wall, NavMesh and transit-point APIs are all mapped
  and marked `CONFIRMED (decompiled)` / `PENDING RUNTIME VERIFICATION` in
  `docs/sf6-screens.md` § World Tour — spatial navigation APIs; none of it has run in game yet. An F10
  diagnostic probe (`SF6Access/Hooks/WorldTour/FieldProbeHooks.cs`) to exercise these APIs live is being
  written by another agent in parallel — do not create or edit that file here.
  **NAudio is already integrated** (`Services/AudioService.cs`, stereo-panned playback of mod-owned
  sound files) with the RE7 radar's cues copied into `SF6Access/sounds/` (`door.mp3`, `exit.mp3`,
  `impassable.mp3`, `interactable.mp3`) ready to be wired to whichever navigation source (raycast vs.
  NavMesh) wins the in-game verification pass.
- **WT reactive radar — RE7 replication, built 2026-09-08, `PENDING IN-GAME VERIFICATION`.** Shift+B's
  continuous mode now runs `Services/WorldTour/FieldRadarService.cs`, a replication of the RE7 mod's
  `RadarService` (`Re7Access/src/RadarService.cs`): an EVENT radar, silent until the
  shape of the street changes. Three camera-relative beams (left/front/right) x three capsule heights =
  nine casts per evaluation through the existing `FieldRayCaster` (extended with
  `TryCastBodyStack`/`FieldRayMetrics.BodyHeights`, both derived from the live `CharacterController`
  capsule); per-beam open/closed state machine with enter != exit dead bands
  (`FieldRadarBeamState.cs`); cues in `FieldRadarCues.cs` (`exit.mp3` / `impassable.mp3` / rising stair
  motif / one-note time-to-contact pitch), flat stereo pan `sin(angle) * 0.9`; exit dedup by 2 m virtual
  cell + 8-way world sector + 4 s; travel speed for the TIME-to-contact buckets from
  `AvatarBase.GetVelocity()` with an announced position-delta fallback (`FieldRadarTravel.cs`). Every
  ported constant with its source line is in `Services/WorldTour/FieldRadarTuning.cs`. Cadence is now the
  MONOTONIC clock, never a frame count: 33 ms walking / 16 ms turning / 100 ms when the sensor is
  unavailable, plus a 100 ms post-cue gate that does NOT stop sensing; the spoken verdict
  (`FieldNavVerdictService` via `NavReading.Block`) is untouched and now samples every 100 ms.
  **Pending test:** read the one-time log line `[SF6Access] Field radar armed: ...` and report it (it
  names the capsule heights, the cadence and whether the speed came from `GetVelocity` or the position
  delta); then walk a street and check that a side opening fires ONE `exit.mp3` on the correct side, that
  passing the same doorway twice from the same spot does not repeat while the NEXT doorway does, that
  walking into a wall raises the pitch note in three steps, that turning on the spot is silent, and that
  the frame rate holds (the beams cast at an unbounded 1000 m and SF6's `castRayAll` cannot near-sort, so
  a long ray can return many contacts). Doors are deliberately NOT cued: RE7's door identity comes from
  scanning `app.DoorPush`/`app.FloorDoor`, which has no confirmed SF6 equivalent.
- WT continuous tracking (M key, `FieldTrackingHooks`) — new 2026-07-20.
- **WT continuous tracking, cadence reworked 2026-09-05 (tester: too chatty).** Sticky-target logic
  extracted into reusable `Services/WorldTour/StickyTarget.cs` (also used by `FieldAimHooks`). Terse
  updates while walking to the same target now fire only on a clock-hour change or a halved/doubled
  distance band (`FieldTrackingHooks.CrossedBand`, floor reused from `MissionBeaconHooks.ARRIVED_M`
  rather than a new literal) instead of speaking on almost every ~2 s poll. Pending in-game
  verification.
- **WT continuous tracking, cadence sped up again 2026-09-06 (tester: "verbalise more often, like the
  beacon"), `PENDING IN-GAME VERIFICATION`.** Poll rate raised from `MIN_GAP_TICKS = 120` to
  `POLL_TICKS = 30` (0.5 s at 60 fps). On top of the existing hour-change / distance-band events, a
  distance-paced REPEAT now speaks the terse update (`LocalizedText.ClockShort`) even when nothing
  changed, on a period that shrinks with distance — `REPEAT_NEAR_MS` = 3000 ms at
  `MissionBeaconHooks.ARRIVED_M` (4 m) down to `REPEAT_FAR_MS` = 8000 ms at `FieldBeaconHooks.HOME_RANGE_M`
  (25 m), linear in between (`RepeatPeriod`) — the same ramp shape the homing pulse already uses.
  Repeats may say the same sentence twice in a row (that is their job); hour/band events still dedupe
  against the last spoken text. Technical recipe in `docs/sf6-screens.md` § Continuous tracking
  (hands-free guidance).
- **WT continuous tracking, notable-only + slower repeat 2026-09-07 (session log: crowd census,
  `PENDING IN-GAME VERIFICATION`).** World Tour crowd NPCs turn out to be NAMED and interactable too
  ("Kenneth, persona", "Susana, persona"), so the tracker was reading out a census as pedestrians passed
  at arm's length. It now follows `StickyTarget.NearestNamed` — nearest person for whom
  `AvatarNameCache.IsNotable` is true (a master, another player, or an NPC-context name; the crowd's
  plain `HudDef.ContactUIType.NPC` is excluded) — and stays silent when nobody notable is in range; the
  homing pulse still follows the raw nearest person, crowd included, so there is always a sound to walk
  toward. `AvatarNameCache` (new, `Services/WorldTour/AvatarNameCache.cs`) caches name+kind per avatar
  address (30 s forget) so the filter costs a dictionary lookup, not a component walk, per person per
  poll. Repeat cadence slowed to match (`REPEAT_NEAR_MS` 3000→5000, `REPEAT_FAR_MS` 8000→10000); clock-hour
  changes are no longer spoken inside `CLOSE_M` = 2×`MissionBeaconHooks.ARRIVED_M` = 8 m (the pulse's pan
  already covers it, and the log showed hour churn "a las 4, a las 5, a las 8" from passers-by at 1–3 m).
  Technical recipe in `docs/sf6-screens.md` § Continuous tracking / § Notable vs. crowd.
- **WT zone announcement, landmark chatter fixed 2026-09-05 (tester: too chatty in dense areas).**
  `ZoneHooks` automatic (hands-free) announcement now fires only for a real `Section` or the `City`
  fallback; the nearest-landmark route (`ZoneSource.NearestPoint`) is spoken only on the on-demand
  **Z** key, since it used to change on every landmark switch. Pending in-game verification.
- **WT navigation improvements 2026-09-05 — camera hold, hands-free compass, aim-at-nearest, compass
  sweep, reworked exit cue.** New code, compiles, **PENDING IN-GAME VERIFICATION** for all of it.
  Technical recipe in `docs/sf6-screens.md` § World Tour compass, aim and sweep (2026-09-05).
  - **Camera look-input hold** (`Services/WorldTour/CameraHoldService.cs`) — pre-hooks on
    `app.worldtour.WTPlayerCameraController.camera_input_proc` / `camera_input_proc_with_mouse` return
    `PreHookResult.Skip` for `HOLD_MS` = 50 ms (user preference) after any direction announcement,
    freezing pad-stick and mouse look input for a beat so the player can let go of the stick while the
    word just spoken is still true.
  - **Hands-free compass** (`Services/WorldTour/FieldHeadingService.cs` +
    `Hooks/WorldTour/FieldHeadingHooks.cs`) — speaks one of 8 compass points ("north", "southwest", ...)
    whenever the camera's facing crosses into a new sector, with 7.5° hysteresis (a sixth of the 45°
    sector) so resting near a boundary does not flap between two names. North is derived at runtime
    from the minimap's `ConvertTo_UIPos` world→UI projection (only when `MapRotateFlag` is false — a
    rotating minimap gives no compass); falls back to the world +Z axis until the minimap binds, and
    which source is active is always logged. **2026-09-06:** reads gained the same 10 Hz gate
    (`POLL_TICKS = 6`) as `FieldAimHooks`, removing per-frame wrapper churn and log spam ("Member not
    found: CameraPosition/LookAtPosition", "get_Count").
  - **Aim-at-nearest-person, reworked into a look-sweep 2026-09-06 (user request: find a specific NPC
    just by looking, no keys/menus, no spam), `PENDING IN-GAME VERIFICATION`** (`Hooks/WorldTour/FieldAimHooks.cs`)
    — no longer only "aim at the nearest": every NAMED person (`AvatarFieldReader.DescribeAvatar !=
    null`) within `FieldBeaconHooks.HOME_RANGE_M` (25 m, now `internal` so the sweep can share it) that
    ENTERS the ±15° aim cone (`ENTER_DEG` = half a clock hour, leaves at `LEAVE_DEG` = a full hour) is
    spoken as `LocalizedText.AtMeters(name, meters)` WITHOUT interrupt, one sentence per 10 Hz poll,
    nearest first. Unnamed/filler avatars are never spoken by the sweep. The shared nearest person
    (`StickyTarget.NearestPerson`) additionally gets the rising tone + camera hold plus `AimAhead`
    ("{name}, straight ahead") WITH interrupt on every alignment. **2026-09-07: the per-person cooldown
    removed.** `SCAN_REPEAT_MS` (10 s) and the once-per-person `Seen.NamedAsTarget` flag are gone —
    looking away past `LEAVE_DEG` and back now re-announces the same person every time (user rule
    2026-09-07: "found them, overshot, coming back" must answer; the cooldown swallowed exactly that
    case). The only anti-spam left is the `ENTER_DEG`/`LEAVE_DEG` hysteresis. State is now a
    `Dictionary<ulong, bool>` (in-cone flag only) keyed by avatar address, pruned each poll against the
    current avatar list, and reset on leaving the field.
  - **Look-sweep, turn-gated 2026-09-07, `PENDING IN-GAME VERIFICATION`.** Two more changes on the same
    file: (1) the sweep speaks EVERY named person, crowd included (a notable-only filter was tried the
    same day and rejected by the user: passers-by can be fought or give items, so hiding them loses
    interactions — only the tracker's voice is notable-only); (2) output now fires only while the camera is turning ≥ `SCAN_TURN_DEG_PER_S`
    (half a clock hour per second, measured between 10 Hz polls) — walking down a street pushes people
    into the cone on their own, which read as running commentary. Cone bookkeeping (`InCone`) still
    updates every poll regardless of `turning`, so a look-back after walking straight past someone finds
    them correctly. A Hero's Call precedent: auto-scan silent while walking straight, spoken on a turn.
  - **Shared target now also drives homing** — `Services/WorldTour/StickyTarget.NearestPerson` is
    followed by `FieldTrackingHooks`, `FieldAimHooks` **and** the NPC homing pulse
    (`Hooks/WorldTour/FieldBeaconHooks.cs`, 2026-09-06 rework — see the NPC audio beacons entry above),
    so tracker speech, aim tone and homing pulse can never point at three different people.
  - **Shift+Z compass sweep** (`Hooks/WorldTour/FieldSweepHooks.cs`) — on demand, 8 horizontal 12 m
    segments cast from the game's own `FRONT_LONG` ray origin via the free-form `CastRayAll` overload
    (`FieldNavRadarService.Sweep`, `FieldNavSideRays.TryCastDirection`), answered as "Open: north,
    east. Walls: south 3 meters, west 6 meters".
  - **Z now appends facing** — the on-demand zone readout (`ZoneHooks`) appends "facing <point>" from
    `FieldHeadingHooks.CurrentFacing()` on manual reads only; the automatic zone announcement stays
    bare.
  - **Radar exit cue and every beam design (armed exit chime 2026-09-05/06, three-beam and then
    four-beam THIRD-pass open/closed 2026-09-07) — RETIRED 2026-09-08, `Services/WorldTour/NavBeams.cs`
    DELETED** (and with it `FieldNavRadarService.Beams()`/`BEAM_REACH_M`; `LocalizedText.NavExit` is now
    unused). All of them were event-only — silent until something CHANGED — and every anti-chatter tuning
    pass made them fire less, leaving them saying very little about where the player actually was.
    Replaced by an always-on continuous tone bed; see the 2026-09-08 entry below for the new design and
    its checklist. The ray-ladder + `FieldNavVerdictService` front-VERDICT system these beams sat next to
    (§9 above, spoken "wall"/"blocked"/"wall you can run along" on B/Shift+B) is UNCHANGED.
  - **Look-sweep re-entry, cooldown removed — see the updated Aim-at-nearest-person bullet above.**

- **Navigation radar rebuilt for precision, 2026-09-08 — `PENDING IN-GAME VERIFICATION`, all new code.**
  Tester report: "no logro nunca saber con precisión dónde hay una salida, o dónde realmente no puedo
  pasar" (I can never tell precisely where there's an exit, or where I really can't pass). Five root
  causes found and fixed:
  1. The sweep cast with `eFilterInfo.TerrainRayFilter` — measured in game as `layer=3:TerrainRay
     mask=0x8 [TCStopCamera]` — which is what stops the CAMERA, not the avatar.
  2. The whole radar was event-only (spoke/chimed only on a CHANGE); every 2026-09-07 redesign made it
     fire less, leaving it saying very little. The shared reference
     `D:\code\modding projects\reference\audio-navigation\README.md` ("Which to use", "3D first-person
     explorer" row) says a 3D explorer needs a CONTINUOUS sonification backbone with events as the
     supplement — the mod only had the supplement.
  3. Fixed-metre thresholds (`NavBeams.OPEN_M` = 12 m) vs. the game's own ray reach (~2 m) — two
     contradictory notions of "open".
  4. The side rays were the game's own `SIDE_R`/`SIDE_L` (real reach 0.40-0.60 m) stretched 6×,
     amplifying grazing-angle noise.
  5. Contacts were never classified against the avatar's own slope limit or step height, so ground/ramp
     and kerb (auto-stepped) were not told apart from a real wall.

  **New sensor — the avatar's OWN collision filter.** `Services/WorldTour/FieldRayCaster.cs` +
  `FieldRayContacts.cs` + `FieldRayMetrics.cs` + `RayHit.cs`. Binds
  `app.CollisionSystem.castRayAll(vec3, vec3, via.physics.CastRayResult, via.physics.FilterInfo, bool)`
  by SHAPE (5 parameters, param 0 a value type, param 3 a REFERENCE type — what tells it apart from its
  twin taking the `eFilterInfo` enum), and casts with the PLAYER'S OWN
  `CharacterController.FilterInfo` (`WTPlayerManager.GetAvatarPlayer()` → `Components` — a single object,
  not a collection — → `CharacterController.FilterInfo`), not the camera's `TerrainRayFilter`. Origin at
  the capsule's real mid-height (`Height` × the live size ratio, clamped inside a `Radius` of each cap).
  Contacts classified against the avatar's own `SlopeLimit` (ground/ceiling) and against the game's own
  `FOOT_FRONT` ray height (step), with `AvatarConstSystemParams.RAY_STEP_UP` as a fallback; architectural
  vs. clutter comes from the collision LAYER NAME (`via.physics.System.getLayerName`) since SF6 publishes
  no `isFixedObject`. Logs one route line: `[SF6Access] FieldRayCaster route: ...` (overload, filter
  layer/group/mask, waist height, slope cut, step height, whether layer names bound) — no silent
  fallback. `TryCastMany` resolves the avatar/filter/thresholds once and fires every direction from the
  same origin and instant.

  **New continuous tone bed.** `Services/WorldTour/NavToneBed.cs` (NAudio, 4 channels, 70 % triangle +
  30 % sine per-sample-smoothed, ported from the DRG/Megabonk wall-sonification synth,
  `D:\code\unity and such\drg access\...\WallNavigationAudio.cs`) + `AudioService.AddPersistentInput`.
  Front 500 Hz / Back 180 Hz / sides 300 Hz (pitch tells front/back apart), sides hard-panned ±1,
  front/back centred, base volume 0.12. Channels are created once and NEVER stopped — only their volume
  moves. `Services/WorldTour/NavProximityBed.cs` drives it: 4 rays (camera front/back/left/right) per
  sample via `FieldRayCaster.TryCastMany`, reach `FieldNavRadarService.SWEEP_REACH_M` (12 m, existing
  constant), quadratic falloff (`1 - norm²`), `minRange` = the avatar's real capsule radius
  (`FieldRayCaster.CapsuleRadius()`). Open = silence. A `Step` contact (kerb) is NOT sonified; an
  `Unknown` contact IS (a missed wall costs more than an extra hum, for a navigation aid).

  **New NavMesh exit finder.** `Services/WorldTour/NavMeshOpenings.cs`. Route:
  `app.global.WTCommon.CityResource` → `WTCityResources.CityAIMap` → `AIMap.findMapHandle()` →
  `queryClosestNode(vec3)` (picked by shape; the zero-argument `queryNode()` is NEVER called — city-wide,
  stalls the game) → `queryLinkToNodes()`; the edge shared with each linked polygon is found from the
  vertices the two hold in common (`getGlobalVertexCount`/`getGlobalVertex`), and that edge's length is
  the real width of the gap; `Passable` = width ≥ the avatar's capsule diameter and the neighbour is not
  a `Wall` node. Re-queried only once the player has moved half their own capsule width. Logs one line,
  `[SF6Access] NavMesh openings: queryClosestNode(vec3) ok via ...findMapHandle(); node (x,y,z) vs player
  (x,y,z); N vertices, M links, K shared edges, capsule D m` — and calls out a node position that reads
  exactly `(0,0,0)` as the known value-type marshalling bug (`docs/sf6-screens.md`, "Value-type reads: a
  known trap"), not a real node at the origin, since the widths come from `getGlobalVertex` regardless.

  **CRASHED THE GAME on its first press (2026-09-08) — now DISABLED and rewritten; only the user
  re-enables it.** B in World Tour killed SF6 with a clean log (last mod line: the radar's own
  `NavRadar verdict routes:` at 10:35:21.422, written by the sample that runs immediately before it; no
  `NavMesh openings` line ever appeared). No .NET exception was logged, so it was a native fault or a
  hang, which no `try/catch` can see. `FieldNavRadarHooks.MESH_WAYS_ENABLED` stays `false`.
  Post-mortem in `docs/sf6-screens.md` § 6 (what was cleared with evidence — the by-value `vec3`
  argument shape, the handle route, the overload binding, the capsule ratio read, the return-type trap —
  and what stays suspect: `queryClosestNode(vec3)` itself, then the wrappers around
  `queryLinkToNodes()`/`getNode(i)`). **Root cause identified 2026-09-08, later session (`docs/
  sf6-screens.md` § 6, "crash root cause identified"):** `NavMeshNodes.cs:121` resolves the map handle
  with `FlowHelper.Call(aiMap, "findMapHandle")` — a **by-name, zero-argument** bind, while
  `via.navigation.AIMap` declares three `findMapHandle` overloads; a by-name call is arity-blind and can
  return any of them. The same session mapped a safely-bindable synchronous pathfinder
  (`NavigationSurface.queryPathSync`, same argument-shape class as `castRayAll`) and the avatar's own
  `NaviSurface` route as candidates for a future rebuild — nothing from that mapping is implemented yet.
  The engine calls moved to `Services/WorldTour/NavMeshNodes.cs`,
  one per method, each logging BEFORE it runs; `NavMeshOpenings.cs` keeps the cache and the geometry,
  reuses `FieldRayCaster.CapsuleRadius()` instead of its own duplicate capsule reader, and no longer
  re-queries a navmesh that failed (it used to run a full query every sample whenever the capsule was
  unreadable — which is the current in-game state, `charaCtrl=missing`).
  **When the user asks for the next test, the log must be read for these lines, in order** — the LAST
  one present names the call that did not return:
  1. `[SF6Access] NavMesh step 1: resolving the map handle (...findMapHandle())`
  2. `[SF6Access] NavMesh step 2: handle <type> ok; calling queryClosestNode(vec3) at (x, y, z) ...`
  3. `[SF6Access] NavMesh step 3: calling getGlobalVertexCount() on the player's node` (+ a second
     step-3 line `... reports N vertices; calling getGlobalVertex(uint) on it`)
  4. `[SF6Access] NavMesh step 4: reading the avatar's collision capsule (FieldRayCaster.CapsuleRadius)`
  5. `[SF6Access] NavMesh step 5: player node read; calling NodeInfo.queryLinkToNodes() and getNodeCount()`
  6. `[SF6Access] NavMesh step 6: calling getNode(i) of N linked nodes` (+ that neighbour's two step-6
     vertex lines), repeated per neighbour
  7. `[SF6Access] NavMesh openings: queryClosestNode(vec3) ok via ...` — the success summary; it also
     switches the trace off, so a working navmesh logs one line per city, not six per press.
  A `[SF6Access] NavMesh openings unavailable: <reason>` warning instead means the route stopped
  cleanly, not that it crashed.

  **Wiring.** `Hooks/WorldTour/FieldNavRadarHooks.cs`: Shift+B (continuous mode) now drives the tone bed
  every sample instead of the old beams; B (one-shot readout) additionally speaks the NavMesh exits
  ("opening at 10 o'clock, 1.8 meters wide, 3.0 meters away" / "gap at 10 o'clock, only 0.6 meters wide"
  when nothing passable is in range). The bed is silenced during dialogue and on leaving the field;
  `NavToneBed.Shutdown()` runs in `Plugin.Unload` before `AudioService.Shutdown()`. New lang keys
  `wt.nav_opening`, `wt.nav_gap_narrow`, `wt.nav_no_openings` in `lang/en.txt`/`lang/es.txt`. **The
  ray-ladder + `FieldNavVerdictService` front-verdict system (§9, spoken "wall"/"blocked"/"wall you can
  run along") is UNCHANGED** — it still casts with `TerrainRayFilter` and still drives B/Shift+B's spoken
  obstacle class; only the continuous SOUND and the exit list are new.

  **Negative finding, recorded so it is not retried: a ray-fan exit detector does not work.**
  `Services/WorldTour/NavOpenings.cs` (a ring of rays around the avatar) was written and then DELETED. A
  standalone 2D simulation proved two unfixable failures: (a) with 24 rays (15° spacing) a 1.6 m gap at
  6 m subtends about 5°, and no ray crosses it — "straight street", "street with a doorway" and "street
  with a narrow slot" produced IDENTICAL ray profiles; seeing a gap that size at 20 m would need roughly
  78 rays; (b) a wall met at a grazing angle produces a jump between neighbouring rays as large as a real
  gap, so it cascades false positives (a straight street reported 8 "exits"). Conclusion: a ray fan
  answers "what is near me on each side", never "where is the exit" — only the NavMesh answers that.

  **In-game verification checklist (2026-09-08, nothing below has run in game yet):**
  - [ ] `[SF6Access] FieldRayCaster route: ...` appears once and names the player's own
        `CharacterController.FilterInfo` (layer/group/mask), not `TerrainRayFilter`.
  - [ ] `[SF6Access] NavMesh openings: ...` appears once per city/session and shows the SUCCESS shape:
        vertices ≥ 3, links ≥ 1, shared edges = links, capsule diameter around 0.6-1.0 m. If `node.Pos`
        prints exactly `(0,0,0)`, that is the known value-type marshalling bug, not a real node at the
        world origin — read the widths (from `getGlobalVertex`) as the trustworthy part regardless.
  - [ ] Turn on Shift+B and walk up to a wall: that side's tone rises steadily as you approach and drops
        as you back away; a genuinely open direction is SILENT; the four tones (front/back/sides) are
        told apart by pitch (front high, back low, sides mid) with the sides panned left/right.
  - [ ] Walk over a kerb/low step the avatar auto-climbs: its tone must NOT sound (`Step` contacts are
        deliberately not sonified).
  - [ ] Press B near a real doorway/street opening: the spoken width and distance roughly match what is
        actually there, and a too-narrow gap is reported as a gap, not an opening.
  - [ ] No log flood: `FieldRayCaster`/`NavMeshOpenings` log at most a handful of lines per session, not
        per sample.

- **Navigation radar, line-memory model retired and replaced with body-clearance passability,
  2026-09-08 (later session) — `PENDING IN-GAME VERIFICATION`.** Supersedes the tone-bed checklist
  immediately above (already retired per the Section status table entry) and the "WT reactive radar —
  RE7 replication" entry further up this list (`FieldRadarService.cs`'s line-memory model, the direct
  port of A Hero's Call's `ReactiveRadar`/RE7's `RadarService`).
  **Measured baseline that triggered the retirement:** a logged Metro City session fired **109 cues in
  90 seconds at 109 DIFFERENT contact points** — 36 cues per 30 s, almost none coalescing, no
  alternation between fixed points. Not a malfunction: a city facade genuinely changes slope about once
  a second (shopfronts, columns, awnings, kerbs, benches), which the line-memory event model has no
  defence against, and a line break ("this beam now sees past where the boundary was") is a depth
  discontinuity, not a passage — a 40 cm shopfront recess fires one exactly like a real doorway, which is
  what made the mod say "exit to your right" and then, on walking right, "blocked".
  **`Services/WorldTour/FieldRadarLines.cs` DELETED.** Replaced by
  `Services/WorldTour/FieldRadarClearance.cs`: each beam now holds a STATE (`Unknown`/`Passable`/
  `Blocked`) from the body-width waist cast (3 parallel rays at the capsule centre ±radius, most
  obstructed wins), not an event from a remembered line; only a beam that CHANGES state speaks, gated by
  a `StateConfirmMs` (4 sensing intervals) debounce, a silent first seed per beam, and silence on the
  lateral beams while the camera is mid-turn (the front beam keeps talking). Duplicate cues are now
  structurally impossible rather than filtered, so the old per-beam coalescer in
  `Services/WorldTour/FieldRadarCues.cs` and its tuning constants (`PosNoiseFloorM`, `MinStepM`,
  `LineSameCos`, `DepthDiscontinuityM`, `SameCueCoalesceMs`) are gone.
  **Also fixed — the exit/blocked contradiction's root cause:** the spoken readout's "blocked" boundary
  (`FieldNavSideRays`' segment, length = the game's own `FRONT_LONG`, 2.00 m at runtime) and the radar's
  cue boundary now come from the same measured number (`FieldNavSideRays.PublishedReachM`, with a
  `BlockedEnterRadii` = 4 body radii = 2.0 m fallback before the first measurement); "passable" is
  `PassableEnterRadii` = 12 body radii = 6.0 m, a wide dead band that costs nothing because clearance
  measured along a beam is bimodal (an alley reports its own depth in the tens of metres, an ordinary
  street's open side reports half its width, 1-3 m). The 12-radii multiplier is a DESIGN choice
  (calibrated against RE7's measured 3-body-radii door-leaf span), not a game-published number — logged
  on arming so a session can correct it. Full recipe: `docs/sf6-screens.md` § 9d.
  `MESH_WAYS_ENABLED` in `Hooks/WorldTour/FieldNavRadarHooks.cs` remains `false`.
  **In-game verification checklist:**
  - [ ] Walk a normal city street with Shift+B on: cues drop from the measured baseline (36/30 s) to a
        small handful per 30 s, each on a real change (a doorway, a corner, entering/leaving an alley) —
        not once-a-second facade noise.
  - [ ] No beam ever cues "passable"/exit toward a direction the spoken verdict (B / the game's own
        collision) simultaneously calls blocked, and vice versa — the contradiction that started this
        rework.
  - [ ] Standing still against a plain wall stays silent once the verdict has settled (no repeat cues
        while nothing changed).
  - [ ] Turning the camera in an enclosed yard: the lateral beams stay silent mid-turn, the front beam
        still speaks a state change if the front direction crosses one.
  - [ ] Arming Shift+B, teleporting, or entering a new area produces no cues from the silent first seed
        (three beams do not all announce at once on entry).

- **Body-clearance passability radar — GC-thread crash root-caused after three failed fix attempts,
  second log-flood pass, beam refinements, 2026-09-09 — confirmed working in play.** Continues the entry
  immediately above.

  **The crash.** The game died ~90 s into a World Tour session on 2026-09-09, about 15 s after the
  navigation radar armed. `re2_framework_log.txt` stopped dead mid-stream with no shutdown sequence, no
  `RIP:` line and no REFramework `VMContext` handler output — the same "clean log" signature as the
  2026-09-08 death. Root cause confirmed this time from the Windows Application event log (.NET Runtime,
  event 1026, 10:13:55):
  ```
  Application: StreetFighter6.exe
  Exception: System.AccessViolationException
     at REFrameworkNET.ManagedObject.Internal_Finalize()
     at REFrameworkNET.ManagedObject.Finalize()
     at System.GC.RunFinalizers()
  ```
  Correlated `Application Error` 1000 at 10:13:59 (`0xc0000005`, faulting module reported as `unknown`)
  and a 138 MB dump at `C:\Users\ali-b\AppData\Local\CrashDumps\StreetFighter6.exe.34496.dmp`. The fault
  was on the GC/finalizer thread, which is why REFramework's own handler never wrote anything.

  **Real mechanism (took three attempts across the same day to find).** `FieldProbeService.NewInstance`
  → `TypeDefinition.CreateInstance(0)` → the game's own `System.Activator.CreateInstance`, invoked on the
  calling thread's VM context. Per RE Engine's own object model — documented by Capcom in *"Achieve Rapid
  Iteration: RE ENGINE Design"* and quoted in REFrameworkNET's own `ManagedObject.hpp` — a **local
  object** "can only be referenced by the spawned thread", is "registered in the local table for each
  thread", carries a **negative reference count** that is an index into that table, and **"all objects
  created from C# will be local objects."** Nothing AddRefs it (`AddRefIfGlobalized` bails because the
  object is never globalized), so nothing keeps it alive and the engine's frame GC is free to reclaim it
  out from under a live C# reference. Every lifetime tried for a `NewInstance` container was therefore
  unsafe; only the rate of failure changed:
  - **One per cast, 450/s (first fix attempt — shipped, then failed in play):** the container died fast,
    and when REFramework's own finalizer later dereferenced the reclaimed engine object it did so from
    the GC thread — this is the crash quoted above. Killed the game ~15 s after the radar armed.
  - **One per process, reused but never globalized (second attempt):** the engine reclaimed it between
    frames anyway, so `castRayAll` wrote into freed memory (`0xc0000005`, caught by `VMContext`) and the
    following `result.Call("clear")` brought the process down immediately on arming (Shift+B). Managed
    stack: `UnifiedObject.Call` ← `FieldRayCaster.CastColumn` ← `FieldRadarSense.Measure` ←
    `FieldRadarService.Update` ← `FieldNavRadarHooks.Tick`.
  - **One per sweep, 30/s (third attempt):** same use-after-free, at 1/15th the rate. Never shipped as
    final.

  **The fix that shipped.** New `FieldProbeService.SharedInstance(TypeDefinition)`: one instance per
  type, created once and **`Globalize()`d**, held in a static dictionary for the life of the process.
  `Globalize` AddRefs, which takes the reference count positive and — per RE Engine's model — promotes a
  per-thread local object to one any thread may hold; the engine stops reclaiming it. REFrameworkNET's
  own documentation for `Globalize` prescribes exactly this case: it "should only need to be called" when
  "you are manually creating an instance of a managed object." Because the wrapper is never collected,
  its finalizer never runs, so the GC-thread dereference that produced the crash above cannot happen at
  all — the fix removes the crash surface rather than narrowing it. Callers still call the engine's own
  `clear` before each use, since the container is now shared. Runtime guard: if `GetReferenceCount()` is
  not positive after globalizing, the container is refused and logged rather than used.

  Switched to `SharedInstance`: `Services/WorldTour/FieldRayCaster.cs` (the radar's cast loop, was
  450/s) and `Services/WorldTour/FieldNavRadarService.cs` (two sites, the older nav sample path at
  ~10/s — **very likely the actual cause of the 2026-09-08 crash**, which predates the new radar
  entirely). Left on `NewInstance` deliberately: `Services/WorldTour/FieldNavProbe.cs:108` and
  `Services/WorldTour/FieldRayProbe.cs:122`, both reached only from the F9/F10 diagnostic dump in
  `Hooks/WorldTour/FieldProbeHooks.cs` (one object per key press, not a loop), and
  `Services/WorldTour/FieldNavPath.cs:152`, inert behind `PATHFINDER_ENABLED = false`.

  **The comment that caused the wrong first fix.** `FieldNavRadarService` carried, for months, a comment
  claiming the result was "never globalized (rooting an object the engine writes through is how a silent
  failure becomes a delayed crash)", and `FieldProbeService.NewInstance` said the same. That had the
  danger exactly backwards, and it is what sent this investigation toward "stop reusing the container"
  instead of "globalize it" for a full day. Both comments are corrected in the source.

  **A second, independent lesson from the same day.** `AccessViolationException` is a corrupted-state
  exception, and **.NET Core does not let `catch (Exception)` catch it**. `FieldRayCaster.CastColumn` has
  a try/catch wrapping the entire method; it produced no log line and no recovery for any of the three
  crashes above — the process simply died. Any "this is guarded by a try/catch" reasoning about engine
  calls in this codebase is false comfort; the only guard that works is not making the bad call.

  **Confirmed working.** The tester reported the radar working in play on 2026-09-09 ("parece que
  funciona bien") after the `SharedInstance`/`Globalize` fix, following the earlier measurement of 7 cues
  in one session against the retired line-memory model's baseline of 36 cues per 30 seconds.

  **Second source of finalizable garbage, fixed in the same pass.** In that same 89 s session, **6,966
  of the log's 9,398 lines (74%) were failed member lookups**, retried every frame. Worst offenders:
  `CameraPosition` and `LookAtPosition` at 756 failed reads each (4,536 log lines together), then
  `DisplayFunc` (378), `get_Components` (309), `get_Count` (260), `get_Collision` /
  `get_ValidWallContactInfo` / `get_CollisionCache` / `get_DoesGoupStepCheck` (136 each),
  `LocalPlayerObject` (88). Root cause, and a general rule: the reads were never failing.
  `FlowHelper.GetObjectField` calls `IObject.GetField(name)`, which finds no field, logs
  `Member not found: X`, then invokes the property getter — which SUCCEEDS and returns a `via.vec3`
  boxed as a `REFrameworkNET.ValueType`. `GetObjectField` then casts the result `as ManagedObject`; a
  boxed struct is not a `ManagedObject`, so the cast yields null and a perfectly good vector is discarded
  as finalizable garbage. A fallback then invoked the same getter a second time and kept the value. Proof
  in the log: `Method not found: get_CameraPosition` occurs **0** times against 756 of
  `Member not found: CameraPosition` — the getter was found and invoked every time. So the rule (also
  recorded in `docs/sf6-architecture.md`): a struct-typed member read through `GetObjectField` always
  looks missing, however well the read went, and a member lookup miss is not free. **Fix:** new
  `Services/MemberAccess.cs` resolves field / getter / backing-field once per concrete type FullName +
  member name, caching the negative result too and logging a miss once. `FlowHelper.Call`,
  `FlowHelper.GetObjectField`, the new `FlowHelper.ReadMember` and `FieldProbeService.Member` all route
  through it. `FieldDirectionService`'s broken `_positionPairProbed`/`_positionPairMissing` latch and its
  `HasMember` helper were deleted. **Verified against REFrameworkNET's own C++ source before shipping**,
  because `FlowHelper.Call` is used by all ~65 hook files: `ManagedObject` does not override `Call`, so
  `IObject.Call` reaches `t->FindMethod(name)`, and `TypeDefinition.GetMethod(name)` IS `FindMethod(name)`
  (`TypeDefinition.hpp:270`) — the same C++ function on the same instance, so no divergence is possible.
  Separately, the native `RETypeDefinition::get_method` (`RETypeDefinition.cpp:493`) DOES walk the parent
  chain, so inherited members were always reachable in one lookup and the hand-rolled `ParentType` loops
  in this codebase were redundant, not load-bearing.

  **Radar refinements made the same day, on top of the body-clearance model above:**
  - The BLOCKED verdict keeps its confirm window; PASSABLE now fires on the first sample with no delay.
    The asymmetry is physical: a transient object can only ever make clearance SHORTER, so an outward
    jump is either the geometry ending or the blocker leaving, and both are true when they happen.
  - The beam facing away from travel is no longer muted. That mute came from the retired line-memory
    model, where a receding wall produced phantom openings — an event-detector artefact a state machine
    cannot have. It was breaking a requirement: run past an opening, hear it close, back up without
    turning, and if the FRONT beam held the opening it was the muted one and never re-announced.
  - A verdict formed while a beam is muted (laterals during a camera turn) is now HELD and spoken when
    the beam is audible again, rather than discarded — walking past an alley and turning back swept it
    into a lateral mid-turn, so the player reached its mouth having been told nothing. A per-beam record
    of what was last spoken stops that becoming chatter.
  - The silent seed now happens on the first EVALUATION rather than the first definite verdict, so a
    beam that starts in the dead band no longer spends its silence on the first real opening it meets.

  **Good news from the same session.** The passability radar fired **7 cues in the whole session** (4
  PASSABLE, 3 BLOCKED) against the measured baseline of 36 cues per 30 seconds from the retired
  line-memory model above. The tester reported it "worked better". Cue log lines now carry both the
  clearance in metres and the avatar's speed, so any confirm-delay cost can be measured rather than
  argued.

  **Unchanged:** `MESH_WAYS_ENABLED` stays `false` (`Hooks/WorldTour/FieldNavRadarHooks.cs:275`), and
  `Services/WorldTour/FieldNavPath.cs`'s NavMesh pathfinder is still inert — `PATHFINDER_ENABLED = false`
  at line 58, no callers, intended for the mission beacon rather than the radar.

  **Pending in-game verification:**
  - [ ] The game survives more than a few minutes of walking with the radar on.
  - [ ] The log's failed-lookup volume collapses (baseline: 6,966 lines in 89 s).
  - [ ] No reader regressions anywhere in the mod — the `FlowHelper.Call` / `GetObjectField` change is
        mod-wide, not radar-only.

  **Pending in-game verification checklist (2026-09-07, second pass — replaces the earlier list, which
  the 11:55–12:00 session already answered: crowd census and beam chatter were both real bugs, now
  fixed above):**
  - [ ] Walking a crowded street: no tracker sentences about pedestrians, but the homing pulse still
        sounds toward the nearest one.
  - [ ] A master nearby: "Chun-Li, maestra a las 2, a 12 metros", repeating every 5–10 s
        (`REPEAT_NEAR_MS`/`REPEAT_FAR_MS`), with no clock-hour churn once inside `CLOSE_M` = 8 m.
  - [ ] Turning in place: the sweep names masters/players only, once per cone entry, and re-reads on a
        deliberate look-back; walking straight ahead stays silent (no commentary on pedestrians walking
        into the cone).
  - Radar, THIRD pass 2026-09-07 (`NavBeams.cs`, four-beam open/closed design) — **superseded, do not
        test this list item.** The file was deleted 2026-09-08; see that entry below for the radar's
        current checklist.
  - [ ] Log floods gone: NO `Method not found: get_Distance` and NO `Member not found: CameraPosition`
        lines (73,591 and 7,653 in the 15:40 session — the getter miss returned an EMPTY wrapper, not
        null, so the earlier read-failure guard never tripped; both now probed once on the type).
  - [ ] Mission beacon loop: `sounds/mission beacon.mp3` trimmed from 2.01 s to 0.78 s (1.3 s of
        trailing silence removed) and `HomingCue.GAP_FAR_MS` 3000 → 2500: the far ping now repeats
        every ~3.3 s instead of ~5 s, the near one every ~1 s instead of ~2.3 s. Also affects the NPC
        pulse's far gap (shared constant). Confirm it is quicker but still under the spoken lines.
  - [ ] Log lines present and sane: `Camera forward: CameraPosition/LookAtPosition unavailable, using
        CameraVec` (once per session, not per poll). (`Nav beam ...` lines no longer apply —
        `NavBeams.cs` was deleted 2026-09-08.)
  - [ ] Mission guide: accept a main and a sub mission, select the SUB one in the phone, and confirm
        the beacon plus the logged `source=hud` id follow it, then switch back to the main mission and
        confirm the beacon follows that instead (`Services/WorldTour/MissionGuideReader.cs`).
  - [ ] Naming hits / sub-zones (research, `docs/sf6-screens.md` § 7b): no key needed — walk around
        normally with the navigation radar on (Shift+B), bump into a lamp post, a shop counter, a shop
        door, a fence and a plain wall, then report the distinct `[SF6Access] Contact: ...` lines from
        `re2_framework_log.txt` (`Services/WorldTour/ContactCatalog.cs` logs one automatically, on the
        game's own blocked verdict, whenever the nearest hit's identity changes). First dump (15:41, a
        plain wall): `obj='wtc0400_24' layer=1(Terrain) folder='wtc0400_24' comps=[via.Transform,
        via.render.Mesh, via.physics.Colliders, via.dynamics.RigidBodyMeshSet,
        app.sound.SoundObsOclTargetApp, app.WTEnvController]`, `mat=?` —
        CONFIRMED `via.physics.ContactPoint` has no `Material`; the surface material has to come from the
        game's wall contacts (§ 5 `WallInfo.Material`) instead. Still needed: lamp post, counter, door. "Farola/poste/mostrador/puerta" as words are NOT in the
        game's data; layer + surface material + OM component is the most it can give.
  - [ ] "Saliendo de X" (`wt.leaving`): walk up to a shop counter until it is announced, walk away:
        one "Saliendo de X" line, none when a nearer interactable replaces it instead.
- **NPC audio beacon — probe stage.** `dev-source/SoundProbe.cs` (a REFramework source plugin, hot
  reloads without restarting; install + keys in `docs/hot-reload-workflow.md`). In World Tour: **F2**
  scan nearest NPC → **F3 / Shift+F3 / Ctrl+F3** step through its trigger ids → **F4** re-fire.
  Confirmed 2026-08-03: the emitter is `app.sound.SoundDynamicContainerApp` and it exposes **543
  trigger ids**, and firing them works. **3D CONFIRMED in game** — the sound stays glued to the NPC
  when the camera rotates, so the beacon is built on the game's own audio (real panning, distance
  attenuation, occlusion, and it obeys the player's volume sliders).
  The NPCs turn out to have **greeting/bark voice lines**, which the user proposes using as the
  locator instead of a synthetic ping — diegetic, and it makes the city feel alive. **F5** filters to
  voices only via the `LanguageEventId_*` discriminator: **237 of the 543** are real voices (see
  `docs/sf6-architecture.md` § Game audio — the "not set" sentinel is `uint.MaxValue`, not 0).
  The triggers come **grouped by soundbank with readable names** (`docs/sf6-architecture.md` § Game
  audio has the full bank map): voices, `foot_steps_es`, `wcs_mvmt_cotton_01_es` (cloth), and the ones
  to avoid — `dmg_*` / `down_human_es` (pain, falls) and `ui_*` (2D).
  F5 walks All → Voices → Effects → **each bank on its own** (Shift+F5 back). **Some triggers LOOP**
  (one forced a game restart), so the probe now has graduated stops — Shift+F4 last, Ctrl+F4 all mine,
  Ctrl+Shift+F4 `stopAll()` (which also kills the music permanently).
  Spy mode was REMOVED — polling cannot work (the request lists drain within the frame; it caught
  ~1/s, always the player's own steps, even while an NPC audibly walked past). Reviving that idea
  needs a hook on `SoundManager.postRequestInfo(RequestInfo)` **from the compiled DLL**, not a source
  plugin. Measurements in `docs/sf6-architecture.md`.
  **Sound chosen (by ear, in game 2026-08-03):** bank **`foot_steps_es`**, whose **first ~5 entries
  are the small noises an NPC makes while standing still** (the rest are walking steps; the tail is
  cloth rustle). That bank measured 53 triggers on two different NPCs and is not actor-specific, so
  selecting by bank name + position is portable. Ids on the scanned NPC were 202667136, 931425362,
  4153751688, 507986086, 2688901618 — recorded for reference only; **the code selects by bank +
  position, never by id.**
  **Beacon prototype on F1 of the probe** (source plugin, hot-reloads). Design settled in game:
  - **Two layers, because one cadence cannot serve both jobs.** A single 3–8 s interval spread over 5
    NPCs meant any ONE of them sounded every 20–40 s — useless for actually walking towards someone.
    Now: a **homing** pulse from the NEAREST NPC on a *regular* beat that tightens with distance
    (1.2 s up close → 5 s at 25 m, like a Geiger counter — regular on purpose, a steady pulse is much
    easier to walk towards), plus a sparse **ambient** ping from someone else every 5–12 s, randomised
    because a mechanical rhythm would grate there.
  - **Voices when the NPC has them, noises when it doesn't**, so filler NPCs stay locatable. Voice
    banks are matched by the game's `IsLanguage` flag (their names carry the actor id, so a name match
    would not carry across NPCs); the noise bank by name (`foot_steps_es` IS shared). Tutorial voice
    banks excluded. Voice roughly 1 ping in 4 while homing, 1 in 3 ambient — voices are long.
    The tester picked out short interjections ("hey!", "hum", "ha?") around voice positions 8–13, so
    the slice is the first 16.
  - **Louder-than-the-mix: tried and abandoned.** `RequestInfo.AttenuationScalingFactor` was made to
    work end to end (verified applied by reading the value back — see `docs/sf6-architecture.md` for
    the two traps) and at 2.0 it made **no audible difference** in A/B. Not shipped. Do NOT reach for
    a global RTPC / bus volume (moves all game audio) or `SoundTriggerInfo.TriggerRange` (shared game
    data). If loudness matters again, the honest lever left is picking louder source sounds.

**NPC audio beacons — now IN THE COMPILED MOD** (`Hooks/WorldTour/FieldBeaconHooks.cs` +
`Services/WorldTour/NpcBeaconService.cs`), **always on, no key** since 2026-08-14. Moved out of the probe because the
prototype had no way to see the mod's gates and its pings were **colliding with World Tour dialogue
and tutorial lines, making them drop** — a beacon voice competes with the game's own dialogue voice.
Gates: held while `SpTalkNovelHooks.DialogueActive`, `PadGuideHooks.Active`, while anything is in
interaction range (where prompts/tutorials appear and `FieldAwarenessHooks` owns the moment), and for
1.2 s after the reader speaks. Watch for tutorial text that is NOT WT novel dialogue — there is no
generic "tutorial active" signal in the mod, so that case may still slip through.

**HOMING layer reworked 2026-09-06 — `PENDING IN-GAME VERIFICATION`, off the game's emitter.** The
nearest person (shared `StickyTarget.NearestPerson`, also followed by `FieldTrackingHooks` and
`FieldAimHooks` so the three readers can never disagree) is now pulsed through the mod's own mixer
with `Services/WorldTour/HomingCue.cs` — the same class the mission beacon uses — instead of firing
through the NPC's own Wwise emitter. Reason: the emitter route (shipped 2026-08-14) was attenuated by
the engine to inaudible past a few metres, and its 2.2-7 s beat was far too slow to steer by while
turning. The new pulse plays `npc beacon.mp3`, panned by camera bearing, dropped one octave when the
target is behind, its repeat gap interpolated 250 ms..3 s between `MissionBeaconHooks.ARRIVED_M`
(4 m, tightest) and the beacon's own `HOME_RANGE_M` (25 m, loosest); silent beyond 25 m (rechecked
every 2 s) and silent while `MissionBeaconHooks.Homing` is true (the mission beacon already has an
objective fix — two homing pulses would be two directions at once). Same gates as above.

The **AMBIENT layer is unchanged**: it picks a nearby NPC at random and fires one of **its own**
triggers at a randomized interval (7-15 s) — a voice line if the NPC has one, a footstep/cloth SE
otherwise so filler NPCs stay locatable too; never the homing target, same gates as above.

  **Pending in-game verification (NPC homing pulse):**
  - [ ] Pans correctly toward the target (left/right not mirrored).
  - [ ] Drops one octave when the target is behind.
  - [ ] Stops beyond ~25 m (`HOME_RANGE_M`).
  - [ ] Yields while a mission beacon is homing (`MissionBeaconHooks.Homing`).
  - [ ] Cadence feels steerable while turning (not just a distance readout).
- Avatar creator full in-game pass after the 2026-07-07 rebuild (spoken colors, preset grids,
  localized categories; known gaps listed in `docs/sf6-screens.md` § Post-rebuild findings).
- VS-screen LP/MR readout online; combo-trial clear status; combo-gate removal.
- Non-Spanish languages of the localized readouts (es is the tested one; en spot-checked).

**Fixed, needs confirming in game**
- Log flood: `FlowHelper.GetTrainingDisplaySetting()` probed `TrainingManager.DisplayFunc` every frame
  in every mode (the singleton is alive outside training too), and each failed probe made REFramework
  log 3 lines — ~180 lines/second into `re2_framework_log.txt`. Now resolved once against the type
  definition. On next launch the log should carry one `TrainingManager.DisplayFunc present: …` line
  and no more spam. **Latent issue this exposed, NOT fixed:** if that field really is absent at
  runtime, `TrainingAttackDataHooks`' reads of `DisplayFunc._gData.PlayerDatas` never worked either —
  worth checking during a training-mode pass.

**SHIPPED IN THE MOD 2026-08-14, needs an in-game test — sequential guide to the tutorial's step-on
panels.** Built and deployed as `Hooks/WorldTour/PadGuideHooks.cs` + `Services/WorldTour/
FieldPadService.cs`; **requires a game restart** (compiled plugin, not the hot-reloaded probe).

- **No toggle key — it arms itself** (user rule 2026-08-14). The panels *are* the tutorial, and a
  player who must know a shortcut exists in order to be told where to walk has already been left
  behind. Presence of panels in the scene is the only switch: guide on when they are there, off and
  forgotten when they are gone (which also makes the tutorial replayable).
- **Cost of losing the toggle:** the scene-by-type enumeration would otherwise run every frame, which
  the project rules forbid. It is throttled instead — 1 s while there is nothing to guide to, 0.25 s
  while guiding (fast enough to catch the instant a panel is stepped on). Cue and speech cadences
  count real elapsed ticks, so they do not drift when the scan rate changes underneath them.
- Sounds the nearest un-walked panel through the panel's **own** emitter — real 3D, the game's own
  sound, cadence tightening inside 4 m (0.75 s vs 2 s).
- **Speaks direction and distance** on every change of target, and re-announces every 5 s while
  walking to the same one, reusing the calibrated camera-relative clock
  (`FieldDirectionService.ClockHour`) and the existing `wt.at_clock_meters` / `wt.clock_short`
  phrasing. This is not decoration: Wwise attenuates each sound over its own authored distance, so a
  far enough panel is **inaudible** — the tester found the last one "ultra lejos, no sonaba". Sound
  alone cannot distinguish "none left" from "30 m behind you"; speech has no range limit.
- Clears a panel by **horizontal** distance (`CLEAR_FLAT_M = 0.6 m`, measured — see below), silences
  it with `stopTriggered` on its own emitter, and announces completion once, then switches itself off.
- Gates copied from `FieldBeaconHooks`: silent during World Tour dialogue, during interaction prompts
  (where tutorial text lives), and for 1.2 s after the screen reader speaks.
- New lang keys `wt.pad_guide_on/off`, `wt.pads_done`, `wt.pad` in en/es. **Missing in the other 11
  languages**, like the rest of `wt.*`.
- **To verify in game:** does the access-range gate (`GetAccessInfoCount > 0`) silence the guide while
  *approaching* a panel? If panels register as access targets, that gate must be relaxed for this
  hook — it was written for the ambient beacons, where going quiet near a target is correct.
- The probe's F1 guide still exists for research and is a **separate** toggle; do not run both.

**Research trail (kept — this is how the panels were identified)**

What the user asked for: in the early World Tour tutorial Luke projects marker pads ("panels" in the
English dialogue) that the player must walk over one by one. Wanted: a UI-ish cue on the NEAREST
un-stepped pad, which stops and moves to the next once that one is cleared, until all are done.
Note this is a **sequential guide**, a different mechanic from the ambient beacon: one target at a
time, and it ends.

**Where it stands — the pads ARE now identified** (see the table below); what is still missing is the
signal that one has been stepped on. Two early attempts failed and are recorded so they are not
retried:
1. Guessing by name from the decompiled code. `app.worldtour.ScenarioZoneGroup`,
   `app.worldtour.MissionZoneTarget` and `app.worldtour.WTZoneAccessTargetSimple` all returned
   **0 instances** in `via.Scene.findComponents` while the pads were visibly on the ground. (The
   `app.worldtour.WTMissionZoneSystem` singleton *does* exist, but its zones are evidently not these.)
2. Chasing the word "panel" from the dialogue — every `*Panel*` type in the game is UI
   (`UIParts*`, `HudSetting_*`), nothing world-space. It is flavour text, not a type name.

**The "what is near me" sweep worked — strong candidate found (2026-08-14, needs confirming).**
The probe's F1 was rewritten to ask the opposite question (not "where is type X" but "what is near
me"): enumerate the scene by `via.Transform`, keep what is within 12 m, sort nearest-first, log each
GameObject's name plus its `app.*` components. Seven sweeps were captured in game.

Two sweeps taken while walking the tutorial (07:46:24 and 07:46:31) turned up, at 0.8 m and 2.7 m:

```
'vi_020000_08'  [app.worldtour.WTEnvDissolveController,
                 app.worldtour.om.GimmickVisualController,
                 app.sound.SoundContainerApp]
'vi_020000_07'  [same three]
```

Sequentially numbered objects sharing one base id, present only in those two sweeps, absent before
and after. `app.worldtour.om.GimmickVisualController` was **already documented** in
`docs/worldtour-accessibility-plan.md` as the interactive-object controller carrying
`onContact(CollisionInfo)` and `SelfState` — a contact event plus a state is exactly the shape a
step-on pad needs. Each one also carries its own `SoundContainerApp`, so a positioned cue needs no
vec3 math at all.

**Caveat that hid this until now:** the raw sweep logs only the nearest 30, and 434-503 objects sit
within 12 m. Only the pad underfoot was ever going to appear; the rest of the set was cut off.

**The typed scan CONFIRMED the set (2026-08-14).** Scanning for the type directly (rather than
sweeping by `via.Transform` and hitting the 30-cap) found **16 `GimmickVisualController` in the
scene**, in three clearly distinct families:

| Family | Members seen | Components | Reading |
|---|---|---|---|
| `vi_020000`, `_06`…`_10` | 6 | `WTEnvDissolveController` + `GimmickVisualController` + `SoundContainerApp` | **the pads** — one numbered set, all at walkable distances (3.8-12.8 m), each with its own emitter |
| `vi_031200/01/02/10/11` | 5+ | the above **plus** `WorkRate`, `ForceChainController`, `EPVExpertWTAvatar3DActionStatusEffect` | something else (physics-driven / avatar-action props) — do NOT confuse with the pads |
| `vi_017005`, `vi_017009` | 2 | `GimmickVisualController` alone | bare scenery gimmicks, no audio |

So the type alone is **not** a sufficient filter — three families share it. The pads are the
`vi_020000` family, and the three-component signature above tells them apart.

**Full member list of `app.worldtour.om.GimmickVisualController`** (Ctrl+F1 dump, two instances):

```
field app.worldtour.om.GimmickVisualSettingData        SettingData
field app.worldtour.om.GimmickVisualController.State   SelfState = 0
field System.Collections.Generic.List`1<System.String>  SuccessFsmCondition
methods: OnStart, lateUpdate, SpawnPhysics, EventRecieveUpdate,
         GetRecievableEvents, onContact, onOverlapping, onSeparate, .ctor
```

**Still unknown: what marks a pad as stepped on.** `SelfState` read **0 on all 16, in all 25 scans
taken** — but no scan was taken with a pad actually being stepped on (the tester cannot find the pads
to stand on them, which is the whole point of the feature). So 0-everywhere is not evidence that
`SelfState` is inert; it is evidence that the interesting moment was never sampled. The three
candidates for the signal are `SelfState`, `SuccessFsmCondition`, and the pad simply leaving the
scene (`onSeparate` / dissolve).

**The pads have their own sounds, and positioned playback exists (2026-08-14).** `Ctrl+F2` on a pad
returned:

- 9 components, the emitter being `app.sound.SoundContainerApp`;
- **one bank, `om020000_es`, with 3 trigger ids** — named after the object family (`vi_020000`), i.e.
  the pads' *own* sounds: `1448412547`, `1581634986`, `493868606` (0 voices, 3 effects);
- and the full `soundlib.SoundContainer.trigger` overload set, which **does** include a positioned
  form taking a `via.GameObject positionGameObj` (recorded in `docs/sf6-architecture.md` § Game
  audio). So a cue can be either the pad's own sound through its own emitter, or any known-good sound
  placed at the pad — both without constructing a `via.vec3`.

**Why that test read as "nothing happened": a probe bug, now fixed.** `F4`/`Fire()` resolved the
emitter through `NearestRig()`, which *always* returned the nearest **NPC**. Ids scanned off a pad
are unknown to an NPC's banks, so `exists()` rejected them and nothing played — the log shows
`trigger[0] id=1448412547 unknown to this NPC` three times. `NearestRig()` now re-resolves against
whichever kind of object the ids came from (a flag, re-resolved per press — never a cached
`ManagedObject`), preferring a pad over any other gimmick family.

**Next step (tooling deployed, untested) — break the chicken-and-egg.** The tester cannot test a pad
finder without first finding a pad, so the probe now finds them *for* them, and **F1 alone does
everything**: it collects the pad's own sounds automatically (no separate key), then guides.

- **F1** toggles the **pad guide**. It sounds the nearest pad *through the pad's own emitter*, so the
  cue arrives in 3D from the pad itself, and the cadence tightens inside 4 m (~0.75 s vs ~2 s) so
  "getting warmer" is audible without listening to a spoken number. It also speaks
  "*tail number*, *N* metres" when that changes. Targeting is restricted to the `vi_020000` family —
  the other two families sit 8-18 m away and would drag the player off the tutorial.
- **F3** still steps through the pad's 3 sounds *while the guide runs*, so they can be compared live
  and the best one chosen without restarting anything.
- The guide's scan doubles as the **watch**: it logs every change in the gimmick set — appeared /
  disappeared / `SelfState` changed — keyed by NAME, never by index, since the list is
  distance-sorted and reorders on every step. Walking onto a pad with F1 on will therefore *record*
  whichever of the three candidate signals actually moves. **This is the one remaining unknown.**
- **Ctrl+F2** (still there) re-scans the nearest pad's container by hand; **Ctrl+F1** dumps the
  nearest gimmick, now including `SettingData`'s fields and every `SuccessFsmCondition` string;
  **Shift+F1** keeps the raw nearby sweep.

**Known probe limitation:** the watch snapshot is keyed by GameObject name, and three of the 16
gimmicks share names in pairs (`vi_031201/02/10` appear twice), so those collapse to one entry. The
pads all have unique names, so this does not affect them.

**CONFIRMED WORKING in game 2026-08-14: the guide finds the pads.** The tester walked to a pad by
ear. The chosen cue is the **second** of `om020000_es`'s three sounds (`id=1581634986`), now the
probe's default (`PREFERRED_SOUND_INDEX`), with F3 still overriding it live.

**Still open, and now instrumented properly: which signal marks a pad as stepped on.** Across a full
guided walk, `SelfState` never moved on any pad and none went `GONE` — but only that one field was
being watched, so the negative result proves nothing about the other members. Rather than guess a
third field, the watch now **snapshots every readable value on the pad** — the GameObject's
`DrawSelf`/`UpdateSelf`/`Valid`, and for each of its 9 components the enable flags plus every
non-static field holding a value or string — and logs only the ones that differ between scans.
Object-valued fields are skipped (their `ToString` is constant, so they can never differ). Watching
is restricted to the `vi_020000` pads so the unchanging props do not bury the signal.

It also now logs `approach: '<pad>' at <d>m` on every scan within 3 m. Without that, "nothing
changed" is unreadable — it cannot distinguish *the game gives no signal* from *the pad was never
actually walked onto*. Next run's log answers both at once. Sampling drops to ~0.25 s inside that
range (stepping on a pad is an instant and can fall between two 0.75 s samples); the **cue** keeps
its own slower countdown so it does not machine-gun exactly where the player needs to hear the
game's own confirmation.

**SETTLED 2026-08-14 — the pad object records nothing, so the clear is inferred from position.**
With the all-values watch running, the tester stepped on two pads (`vi_020000_10`, `vi_020000_09`,
3D distance bottoming out at 0.54 m and 0.41 m). Result: **all 46 watched values per pad identical
before and after, on both, and no pad left the scene.** That is a conclusive negative — the
GameObject's flags and every field of all 9 components were sampled 4×/second throughout. Whatever
marks a pad as used lives in the mission system, not on the pad.

So the guide clears a pad by **horizontal** distance (`PAD_CLEAR_FLAT_M = 0.6 m`). Horizontal, not
3D: a pad's origin sits a fixed height off the player's, which is exactly why the 3D distance
bottomed out at 0.41 m and never approached zero on a pad that was definitely stood on. The 0.6 m is
measured from those two step-ons (≈0 m and ≈0.35 m on the ground plane), not guessed, and the
`approach:` log line now prints both distances so it can be re-calibrated from real numbers. Cleared
pads are silenced with `stopTriggered` on their own emitter, skipped as targets thereafter, and when
all six are done the guide announces it once and falls quiet. Toggling F1 resets the cleared set, so
a wrong call costs two key presses rather than a restart.

**Two panel bugs found and fixed 2026-08-14** (reported as "el mod no identifica bien cuáles pisé"):

1. **Order of operations.** `ClearUnderfoot` ran *after* the speech gates, so stepping on a panel that
   registers as an interaction target made the hook return before it could mark the panel walked —
   and it then sounded forever. Marking a panel is bookkeeping, not speech: it now runs before every
   gate that only governs output.
2. **Over-eager forgetting.** Any single unreadable frame called `Reset()`, which wiped the whole
   walked-panel set. `ReadPads` also returns empty when the player position momentarily cannot be
   read, so this fired routinely. Split into `Pause()` (stand down, remember) and `Reset()` (forget),
   with forgetting requiring `FORGET_AFTER_EMPTY_SCANS = 3` consecutive empty scans.

**THE BIG ONE, found 2026-08-14 from the panel log: every field distance in the mod could be
measured from the WRONG PLAYER.** `AvatarFieldReader.ReadPlayerPos` (and `ReadOthers` before it)
took the first avatar whose type name contains `AvatarPlayer` — but **every human-controlled avatar
is an `AvatarPlayer`**, so with other online players nearby it could lock onto somebody else. The
symptom that exposed it: panel `vi_020000_09` logged distances of 0.8 m, then 11.8 m, then 3.7 m on a
target that cannot move, and was never marked walked (5 of 6 cleared, the guide cueing it forever).

Fixed by asking the game which avatar is us: **`app.worldtour.WTPlayerManager.LocalPlayerObject`**
(a `GameObject`) → `get_Transform` → `get_Position`, with the old scan kept as a fallback and a
one-shot log line saying which route was taken. `ReadOthers` now shares that same origin, so the
radar, the tracker, the beacons and the panel guide all measure from one place. This very likely
also explains distance oddities in the older readers that were never chased down.

**Front/back cue (requested 2026-08-14).** A source dead ahead and one dead behind arrive almost
identically in 3D audio. The request was a downward pitch shift for "behind"; **Wwise exposes no
pitch call** — `via.simplewwise.SendRequest.setRtpcValue(ulong gameObjId, uint rtpcId, float value,
bool isGlobal, float durationMs, bool bypassInterpolation)` moves a game parameter, and pitch only
follows if the game's own Wwise project wired that parameter to pitch *for that sound*. The probe dumped the RTPC table
(`via.simplewwise.BankInfoManager.getRtpcNameTblCount()` / `getRtpcNameTbl(i)` / `getRtpcIdTbl(i)`)
in game 2026-08-14 and the answer is **0 RTPCs declared by the loaded banks**. With no game
parameter to move, `setRtpcValue` has nothing to act on: **a pitch shift is not available by this
route.** (Caveat: 0 may also mean the table is only populated under conditions we did not meet —
but nothing else points at pitch, so this is not worth another round.)

So "behind" uses the panel's **other** free sound (index 0; index 1 is the chosen cue, index 2 is
reserved for the game's own step-on confirmation). Confirmed working in the log —
`Panel cue '…' 3,1m behind played` alternating with `ahead`. `Silence` stops both cue ids, since a
panel could have been sounding as either.

**Reported by the tester 2026-08-14: stepping on a pad makes the game play that pad's THIRD sound**
(`id=493868606`), possibly with others. Two consequences:
1. The guide must not use sound 3 as its cue — it would be mistaken for the game's own "cleared"
   confirmation. The default is sound 2 (`id=1581634986`), so there is no clash.
2. There is a real, game-authored event at the moment of clearing. If the all-values snapshot comes
   up empty, the fallback is to observe that event directly: a dynamic hook
   (`method.AddHook(false)`, per the project's IL2CPP rule) on `soundlib.SoundContainer.trigger`,
   filtered to the three `om020000_es` ids, identifying the pad from the container's GameObject.
   Not attempted yet — that hook fires for every sound in the game, so it is the second choice, and
   it is unknown which of the 7 `trigger` overloads the game actually uses here.

**Requested, deferred to the port (2026-08-14):** the pad cue must obey the same silence rules as the
NPC beacons — nothing during dialogue, tutorial prompts, or before the player has control. Those
signals (`SpTalkNovelHooks.DialogueActive`, `ScreenReaderService.LastInterruptTick`,
`AvatarFieldReader.GetAccessInfoCount`) live in the mod, not in the self-contained probe, so this
lands when the guide moves out of the probe into `Hooks/WorldTour/` — see `FieldBeaconHooks` for the
exact gate set to copy.

**Small refactor TODO**
- Route `MainMenuHooks`' `IsInX` OR-chain suppression through `UiDispatcher.AnyAdapterActive`.

## Built 2026-08-14, ALL NEED AN IN-GAME PASS

**World Tour phone — four new adapters** (`Hooks/WorldTour/`), registered in `ScreenRegistry`.
Technical reference in `docs/sf6-screens.md` § World Tour phone.
- `DeviceIMHooks` — Messages: contact list + thread list.
- `IMContentHooks` — reading a thread. Body text comes from the **GUI** (`IMContentScreen`), because
  the param's `WTIMData` records are asset-backed with no plain string.
- `MissionListHooks` — mission list, read from `CurrentSelectMissionInfo` data rather than the
  `_Children`-less scroll lists (which would also have needed de-duplicating, since the GUI draws
  each mission twice).
- `MissionDetailHooks` — the detail popup. Polls fast and announces on bind: it lived under a second
  in the capture.

**Mission objective beacon** (`Services/WorldTour/MissionTargetService.cs` +
`Hooks/WorldTour/MissionBeaconHooks.cs`). Resolves the tracked mission's target and beacons it.
**Speech is the primary channel here**, unlike the panel guide:
a mission target is usually far, and Wwise attenuation makes a distant sound simply inaudible — so
it announces clock direction + distance every 8-15 s (by range), says "at the objective" once inside
4 m and then hands over to the game's own prompt. The sound plays on the objective's own emitter when
it has one (mission targets are usually NPCs, which do), and it is speech-only when it does not.
Stands down for the panel guide.

**Target resolution now tries the game's own HUD marker first (2026-09-07, new
`Services/WorldTour/MissionGuideReader.cs`, `PENDING IN-GAME VERIFICATION`).**
`app.UICityHud_MissionGuide` — the same component the on-screen arrow reads — is found live via
`via.Scene.findComponents` (the `FieldHeadingService`/minimap pattern), and its `missionTarget`
(`curProgressMissionId`, `TargetObject`, `TargetType` = `eTargetType` NPC/OM/ZONE) is read first
because it is the mission the PLAYER SELECTED (primary or secondary), which is what a sighted player
sees the arrow pointing at — the previous `WTMissionSystem.FindProgressMissionId()` route only knows
"the story's mission in progress" and can disagree with the player's own pick in the phone.
`MissionTargetService.Find()` falls back to the mission-system route whenever the HUD marker has no
target or an unreadable position. All member names are decompile-only, unverified at runtime. Log:
`Mission guide: id=<n> type=<NPC|OM|ZONE|?> source=<hud|system> pos=(x, y, z)` once per change of
objective, and the one-time bind warning `Mission guide: cannot bind '<member>' ...` if a game patch
ever renames something. **Verification:** accept a main and a sub mission, select the SUB one in the
phone, confirm the beacon and the `source=hud` id follow it; then switch back to the main mission.

**Crowd fixes** — the tester's "el lector se pone a decir todo" in a busy area:
- The target-change reader now needs the same target across 3 consecutive polls before announcing.
  A crowd makes the nearest interactable flicker every poll, turning a walk into a running
  commentary.
- The tracker keeps its target until somebody else is **2 m closer**
  (`StickyTarget.DEFAULT_SWITCH_MARGIN_M`, extracted 2026-09-05 into `Services/WorldTour/StickyTarget.cs`
  so `FieldAimHooks` can share it), remembered by ADDRESS rather than by caching a `ManagedObject`.
  Without hysteresis it renamed its target every couple of seconds, which is a census, not guidance.
- The on-demand radar names the nearest 8 and then counts the rest ("y 14 más") — never silently
  truncated.

Still open on the phone: the Messages **passcode gate** (`UIFlowIMPasscodeScreen`) is unhandled, and
the mission-detail **reward lines** are in the `ui50613` GUI but not yet announced.

### Dialogue: stop chasing the SOURCE, read the WINDOW (2026-08-14)

`WTContactSystem` was a dead end for this symptom: the hook **attached fine**
(`WTContactTalkHooks initialized (AddMessageLog hooked)` in the log) and **never fired once** — a
shopkeeper (Viz) produced no `Contact talk` line, and no `Novel:` line either. Three rounds were
spent identifying dialogue by the flow that opens it, and each round turned up another source.

The tester's OCR ended it: the on-screen text sat above a **"Transcripción"** prompt, which is the
input guide attached to the **`MessageWindow`** widget — the very widget the novel reader already
knows how to read. The text was on screen in a known place the whole time; nothing was polling it,
because the reader that polls it only wakes for `UIFlowSpTalkNovelMain.Param`.

New `Hooks/WorldTour/MessageWindowHooks.cs` is a `ScreenAdapter` keyed on the **GUI owner** rather
than a flow param (`OwnedTypes` is empty; `Locate()` is "does MessageWindow currently hold a
line"). It stands down while `SpTalkNovelHooks.DialogueActive`, which owns the novel path and its
branch choices. Registered after it, for that reason.

**The lesson worth keeping:** for dialogue, the window is the invariant and the flow is not. Every
new speaker type was another silent case and another test round; keying on the widget ends that
class of bug. `WTContactTalkHooks` is kept — it costs nothing, and if a Contact-driven line ever
does arrive it will be read.

Still unknown: which flow actually opens the shopkeeper's window. It no longer matters for reading
it, but it would matter if that window ever needs choices handled.

### Generic street-NPC dialogue — first attempt, `WTContactSystem` (attached, never fires)

The tester's own observation is what cracked it: **rival/story dialogue read fine, ordinary street
NPCs were silent.** That rules out a broken reader and points at a different system — and there is
one. SF6 has **three** dialogue paths in World Tour:

| System | Type | Read by |
|---|---|---|
| Story / rival visual-novel scenes | `app.worldtour.UIFlowSpTalkNovelMain.Param` + GUI `MessageWindow` | `SpTalkNovelHooks` |
| Staged "Special Talk" (Battle Hub, cutscene-ish) | `app.worldtour.SpTalkCtrl.SubtitlesProgress.ChangePage` | `SpTalkHooks` (gated by the Subtitles option) |
| **Casual "Hablar" chat with any NPC** | **`app.worldtour.WTContactSystem`** | **nothing — this was the gap** |

New `Hooks/WorldTour/WTContactTalkHooks.cs` hooks
`WTContactSystem.AddMessageLog(WTContactDefine.WTContactMessageLog)` (dynamic hook, pre only) and
reads `Name` / `Message` off the log entry. **They are already plain resolved strings**, unlike
`SpTalkSubtitlesData`'s Guids — nothing to look up. `logType` distinguishes `Talk` from `Choice`.
Not gated by the Subtitles option: that setting governs the voiced staged talk, and this system has
no connection to it — the same reasoning that leaves the novel reader ungated. The tester expects
this to cover the Battle Hub too, which matches: the Contact system is shared.

Reading is deferred to the next `LateUpdate` rather than done inside the hook, following
`SpTalkHooks` — the hook runs on whatever thread the game called from.

**Ruled out:** the Subtitles option. It only gates `SpTalkCtrl`, and nothing in the Contact system's
surface touches it. Worth one glance in game purely to eliminate, not as the suspect.

**Confirmed from the dump:** the pre-interaction panel is GUI `CityHud_ContactPanel_NPC`
(`e_text_name`, `e_text_num` = level, `e_text_0_d_35` = "Hablar"). No dump has yet been taken *while*
a generic NPC's line was on screen, so the exact rendering widget is still unconfirmed — but the hook
above does not depend on it, since it takes the text from the data rather than the screen.

### NPC dialogue "not read" — earlier round, NOT reproduced 2026-08-14

The reported dump (`sf6access_autodump_142118.txt`) contains **no NPC dialogue at all**: only a
gesture-item pickup toast and a reward ticker, plus proximity nameplates. The log for that whole
session has zero `UIFlowSpTalkNovelMain` and zero `WT novel dialogue active` — the "Hablar" prompt
appears in the GUI but was never taken. An EARLIER dump the same day
(`sf6access_autodump_135446.txt:690`) does contain a real conversation, and it goes through exactly
the path the mod already hooks:

```
--- GUI: MessageWindow ---
  e_text_conversation = Mmm, vale.\nEs básicamente una lista de cosas que hacer.
  e_text_name = Bosch
```
under `app.worldtour.UIFlowSpTalkNovelMain.Param` — which is `SpTalkNovelHooks.ParamType`, and it IS
registered (`ScreenRegistry.cs`). `SpTalkHooks` (Battle Hub "Special Talk", subtitle-gated) is a
separate, disjoint system and also live. So there is no hookless gap; what is missing is a capture of
the failure.

Two changes made anyway, both of which would have shortened this:
- **`ScreenAdapter.Tick` no longer swallows exceptions silently.** Its bare `catch {}` meant any
  throw inside a screen's `OnBind`/`Poll` left that screen mute with nothing in the log —
  indistinguishable from "the screen was never opened". Now logs once per adapter:
  `<Name> faulted and is now silent: <message>`.
- `SpTalkNovelHooks.SearchInterval` 30 → 10 frames. Half a second to notice the talk box had opened
  is long enough to miss a short first line — the one that names the speaker.

**To reproduce properly:** walk to an NPC, actually press the "Hablar" prompt, and look for
`[SF6Access] WT novel dialogue active` then `[SF6Access] Novel: <speaker>: <line>` in the log. If the
flow appears in the dump with a populated `e_text_conversation` but no `Novel:` line follows, that is
the real bug — and the new fault log should now name it.

### First in-game pass, 2026-08-14 — four fixes

### The dialogue reader muted the whole field (2026-08-14) — found and fixed

Symptom: after a World Tour battle the mod stopped announcing NPCs entirely. The obvious suspect was
a stuck battle gate — **the log cleared it**: `WTBattleManager.IsBattle` went True at 16:26:16 and
False at 16:26:44, and every signal read False afterwards.

The real cause was the new `MessageWindowHooks`. The log shows `Dialogue window open` at 16:25:52
with **no matching `closed`** for the remaining two minutes: the `MessageWindow` widget keeps its
last line after the conversation ends, so `Locate()` stayed true forever. And because it is a
`ScreenAdapter`, `UiDispatcher.AnyAdapterActive` stayed true → `FieldPresenceService.MenuActive()`
true → `CanSpeak` false → **every World Tour reader muted**. The last `Beacon home` is at 16:25:51,
one second before that window "opened", which pins it exactly.

Fixed by requiring the text to still be MOVING: the window counts as open only while its line has
changed within `STALE_MS = 10 s`. Dialogue advances every few seconds, so frozen text is a leftover,
not a conversation. **The general lesson: a GUI-keyed adapter needs a liveness rule, not just a
presence test** — a widget that lingers is indistinguishable from one in use.

### Mission beacon: the panel sound was tried and REMOVED (2026-08-14)

The tester settled it: *"ya no paso más por el tutorial"*. The panel cue id can only be learned by
standing next to a panel, so for anyone who has finished the tutorial — which is most players — it
is never learned at all. And routing it first was actively harmful: `TryUiSound` returned true
whenever the call did not throw, which counted as success and **suppressed the working voice
fallback**, leaving exactly those players in silence. Same trap as "played" meaning "did not throw".
**A path that cannot be verified as audible must not pre-empt one that can.**

Removed: the `UISound.Trigger` branch, `FieldPadService.CueTriggerId` and its remembering. The
mission beacon is now the objective's own voice (random line, matching the audible ambient beacons)
plus the spoken bearing, which was always the primary channel at mission range.

A distinctive non-voice cue is still possible, but it needs a bank loaded everywhere — the probe's
**Shift+F2** container sweep exists to find one. Nothing else is worth trying until that list is in
hand. The `app.UISound.Trigger(uint, via.GameObject, ContainerType)` recipe and
`ContainerType.Resident = 0` are recorded in `docs/sf6-architecture.md` for when it is.

### Superseded: mission beacon panel-sound attempt (2026-08-14)

The tester reaffirmed they want the *panel* cue, not an NPC voice. Implemented via
`app.UISound.Trigger(uint, via.GameObject, app.sound.SoundManager.ContainerType)` — a static call
that places a sound at an object without needing an emitter on it — with
`ContainerType.Resident = 0`, the always-loaded container.

The id is **remembered, not hardcoded**: `FieldPadService.CueTriggerId` records it the first time a
panel is cued, so the value still comes from the game's own data (trigger ids are hashes with no
enum, and this project carries no magic numbers) while surviving the walk out of the tutorial. Until
a panel has been seen in the session there is no id, and the beacon falls back to the objective's own
voice — logged, so silence is never left to interpretation.

**Honest caveat, stated to the tester:** even with the right id, Wwise will only sound it if
`om020000_es` is still loaded outside the tutorial. That is now a question the log answers
(`panel cue id=…` vs `objective voice`) rather than one to settle by ear.

**Why the voice fallback was inaudible — evidence, not theory.** The tester confirmed the mission
beacon is a *separate* system from the dialogue reader, so an NPC voice is a perfectly good cue. It
was already using one and still could not be heard at **5.5 m**, while reporting "sounded". The
difference from the ambient NPC beacons, which ARE audible: those pick their line **at random**,
this one pinned **index 0** for recognisability. Index 0 of a voice bank is evidently a dud entry
rather than a line. `sameEveryTime` dropped for this beacon — matching the path that demonstrably
makes noise beats a consistency nobody can hear.

Also added: `NpcBeaconService` logs the emitter type, bank list and chosen id once per distinct
container. "played" only ever meant "the call did not throw" — it says nothing about audibility, and
that gap cost two rounds on this beacon.

### Log flood, round 2 (2026-08-14) — measured, not guessed

After the `ReadVecComponent` fix the `get_x/y/z` spam is gone from the top offenders, but a fresh
count over ~100 s still showed **2043 "Member not found" + 2357 "Method not found"**. The two
biggest were **mine, both introduced with the always-on readers**:

- `<_tData>k__BackingField` (633×2 lines) — `FieldPresenceService` asked "is this a live Training
  session?" six times a second. Every reader behind that gate is already restricted to the World
  Tour field, so Training could never have been the answer. **Check deleted.** `DisplayFunc` (578×3)
  comes from the same call path and goes with it.
- `ListHolderObj` (55) — `MissionTargetService` asked for the field before the getter. Reversed.

Remaining and NOT yet chased: `get_Count` (1106) from `FlowHelper.GetListCount` — it already picks
`get_Length` for arrays, so these are collections that are neither arrays nor expose `get_Count`
(`SafeList`-style wrappers). Lower rate, and fixing it needs knowing which types they are.

1. **PERFORMANCE / LOG FLOOD, and the most important of these.** `FlowHelper.ReadVecComponent` tried
   `get_x`/`get_y`/`get_z` *before* the value-type path. `via.vec3` is a value type whose components
   are FIELDS and has no such getters, so every single position read logged three
   "Method not found" lines. Harmless when a position was read on a keypress; once the World Tour
   readers became always-on it became hundreds of lines a second in a hot path. Order reversed —
   value type first, getter as fallback. `WTPlayerManager.LocalPlayerObject` had the same shape (a
   property with no backing field, asked field-first) and is now getter-first.
2. **Messages read only one bubble.** A thread shows several at once; the reader took the first
   `e_text_name`/`e_text_message` pair and stopped, and on re-entry re-read the same line. Now it
   walks the GUI in tree order pairing every sender with its message, drops a line identical to the
   one before it (the same bubble is rendered twice), and **speaks only the part that is new** —
   re-reading the whole conversation each time a bubble is appended would be unusable. Logs
   `Message (N shown)` so the next pass says whether the GUI really exposes the whole thread.
3. **Arrival readout was reciting eighteen people.** "18 cerca: Katrin, persona a las 7, a 8
   metros, …" on every arrival. Arriving now says **only the count**; the detail stays one keypress
   away on the on-demand radar, where it was asked for. Also: section id 0 means "unavailable", not
   "section zero", and treating it as real made the id flap and re-fire — now ignored, with a 20 s
   floor between automatic announcements as a backstop.
4. **The mission beacon was working all along — and inaudible.** The log has
   `Mission target found via GetListNpcMissionTargetInfo (mission 11001)` and three
   `Mission beacon … (sounded)` lines at 24.8 m, 22.9 m and 17.8 m. So `WTMissionSystem` resolves
   correctly and the cue fires; the tester simply could not hear it, because it was playing the
   target's *idle noises*, which Wwise authors to carry a few metres. Switched to `allowVoice: true`:
   a spoken line carries across a street, which is the range this beacon actually operates at.

## Next step

### 2026-09-04 — navigation radar / zone / beacon land as a PROTOTYPE (user verdict)

User's own assessment at the end of the session: *"me gusta como prototipo, pero hay que mejorarlo
bastante al radar, el anuncio de zonas, npcs, en fin. esto necesita pulirse y mucho"*. Committed as a
checkpoint, NOT as a finished feature. Do not treat any of it as done.

**Named by the user as needing work:** the navigation radar, the zone announcement, and NPCs.

**Contradiction to fix first — this breaks a standing user rule.** The hands-free rule above
(2026-08-14, "todo activado sin pulsar ninguna tecla") was NOT applied to what shipped today:

| New reader | Current | Should be |
|---|---|---|
| `FieldNavRadarHooks` continuous mode | opt-in toggle **Shift+B** | always on, per the hands-free rule |
| `FieldNavRadarHooks` one-shot readout | key **B** | an on-demand key is fine, but B is provisional and unverified against World Tour's own keyboard bindings |
| `ZoneHooks` | auto-announces on change **and** key **Z** | already follows the rule; Z is provisional. Since 2026-09-05 the automatic side only covers `Section`/`City`, not the landmark route — see the 2026-09-05 zone bullet above |

`B` and `Z` were both picked as free keys without checking them against World Tour's own keyboard
actions -- verify or rebind.

**Still unverified in game at checkpoint time:** the sideways rays firing at ~2 m and on the correct
(non-mirrored) side; the mission beacon's pan and its 0.5 pitch when the objective is behind; whether
zone Route B (real district names) ever resolves or whether it always falls back to the nearest
landmark -- the log line `Zone:` says which.

**Known rough edge, user-facing:** `mission beacon.mp3` runs 2.01 s, so at pitch 0.5 it plays for
4.0 s. That may feel sluggish when the objective is behind. Options if so: trim the sample, or move to
a duration-preserving pitch shift instead of the current rate-shift.

**HANDS-FREE BY DEFAULT (user rule 2026-08-14) — the World Tour readers no longer have toggle keys.**
"Para el mod quiero que venga todo activado sin pulsar ninguna tecla." Applied to all four:

| Reader | Was | Now |
|---|---|---|
| `FieldBeaconHooks` (NPC beacons) | toggle **B** | always on |
| `FieldTrackingHooks` (continuous tracking) | toggle **M** | always on |
| `PadGuideHooks` (tutorial panels) | toggle **P** | arms itself when panels exist |
| `FieldAwarenessHooks` (radar) | on-demand **N**/Start only | **also reads the surroundings on arrival**, on `CurrentSectionId` change; N/Start kept |

**Fallout, fixed 2026-08-14 (`Services/WorldTour/FieldPresenceService.cs`).** In game the four
always-on readers each had their own idea of when to shut up, and it went wrong immediately: the
arrival radar repeated "nada cerca" **in the main menu**, and the distance reader talked across
tutorial subtitles. So the gate now lives in one place:

- **`InField`** = `AvatarManager` resolves **AND** the player has a readable world position.
  `AvatarManager != null` alone was the old test and is what let the main menu speak — the singleton
  survives leaving the field. The player avatar does not, so its position is the honest signal. Still
  true in the opening tutorial, which is the case that ruled out `WTCityManager.IsActivated()`.
- **`Moving`** = the player's position, sampled ~6×/s, moved faster than 0.4 m/s, with a 0.7 s grace
  after stopping and a 5 m/sample teleport reject. **Nothing in the game's managed surface exposes
  avatar speed** (searched: `Velocity`, `MoveSpeed`, motion state — none exist), so it is measured
  rather than read.
- Refresh-on-demand, not a registered callback: every reader calls `Refresh()` first, repeat calls
  inside one sample window are free, and there is no ordering dependency between hooks.

Applied per reader, matching what was actually asked for:

| Reader | Gate |
|---|---|
| Distance reader (`FieldTrackingHooks`) | `CanSpeakWhileMoving` + 1.2 s hold after any interrupting announcement — standing still is silent, and that hold is what protects tutorial text (the WT dialogue flag only covers novel dialogue) |
| NPC beacons | `CanSpeakWhileMoving` **and not** `InFightingGameplay()` |
| Panel guide | `CanSpeak` only — deliberately NOT movement-gated, since the cue is what tells a standing player which way to set off |
| Arrival radar | `CanSpeak`, and **never announces emptiness**: "nothing nearby" is an *answer*, so only the on-demand key ever says it |

**Battles DO leak — the assumption that a fight is its own scene was wrong** (tested 2026-08-14: the
distance reader and the beacons both kept going during a World Tour battle). A WT battle keeps the
walkable field loaded, so `InField` stays true. There is **no confirmed "in a fight" flag** in the
mod or in any obvious game singleton, so `FieldPresenceService.Fighting` now ORs two and **logs
three**, so one real battle in the log settles which is authoritative:

| Signal | Status | Gated on? |
|---|---|---|
| `app.commentator.bCommentatorGlobalInfoHolder.IsBattleNow` | singleton proven reachable (`BattleInfoHooks` already uses it); this member inferred from decompiled decls + its `OnBattleStart`/`OnBattleEnd` pair | **yes** |
| `app.worldtour.WTBattleManager.IsBattle` | member declared, with `StartBattle`/`EndBattle` alongside; singleton reachability inferred from every other `app.worldtour.*Manager` | **yes** |
| live Training (`FlowHelper.GetTrainingDisplaySetting() != null`) | proven | **yes** |
| `bCommentatorGlobalInfoHolder.CurrentBattleDesc != null` | best-proven of all (already read in `BattleInfoHooks`) | **no — logged only** |

**RESOLVED in game 2026-08-14** (261 logged transitions across two World Tour battles):

- **`WTBattleManager.IsBattle` is the authoritative signal** — true for the whole battle, false the
  moment it ends. Now gated on.
- **`CurrentBattleDesc != null` tracks it exactly and does NOT persist afterwards**, which was the
  only reason it was held back. Now gated on too.
- **`IsBattleNow` never fired once.** Kept only as an OR-term for non-World-Tour fights, which these
  readers never reach anyway.
- Curiosity worth remembering: `training` also read true during a World Tour battle, so
  `GetTrainingDisplaySetting()` is not exclusive to Training mode.

**Rejected:** `FlowTrackerHooks.IsFlowActive("Vs")`. `UIFlowVs` is the pre-fight rule-select *menu*,
and `_Handles` only holds modal UI flows — the live fight runs through the separate scene-flow-map
system and never appears there. WT contact battles skip that screen entirely.

Two consequences that had to be designed for, not just switched:

1. **Arbitration.** Four always-on readers share one voice. `PadGuideHooks.Active` is the priority
   signal: while the panel tutorial is running it owns the mic, and the beacons, the tracker and the
   arrival readout all stand down. Everything already stood down for dialogue, interaction prompts,
   and 1.2 s after the reader speaks.
2. **The arrival readout is DEFERRED, not fired on the spot.** Arriving somewhere is exactly when the
   game is most likely to be talking; consuming the section change while muted would lose the
   announcement outright. So arrival sets a pending flag that is spent when speaking is allowed. The
   on-demand key deliberately bypasses every gate — an explicit press is a request, and a request
   must always be answered.

`LocalizedText.BeaconOn/Off` and `TrackingOn/Off` (and their `wt.*` keys) are now unused; kept in
case a toggle is ever wanted again.

World Tour field awareness (WT-1) is COMPLETE and fully calibrated in game 2026-07-20 (radar:
names at any range + camera-relative clock + metric distances). Continuous
tracking (`FieldTrackingHooks`) — event-based "a las 12, a 4 metros" toward the nearest avatar
(reworked 2026-09-05: fires on clock-hour change or a halved/doubled distance band, not a fixed
timer), silent while dialogues/arrival readers speak. Audio beacons on NPCs — the game's
sound system is mapped (`docs/sf6-architecture.md` § Game audio (Wwise)); NPCs carry their own
`SoundContainerApp`, so a positioned ping needs no vec3 math, but Wwise can only fire events already in
SF6's soundbanks, so the user's own mp3s are not playable *that* way.
**RESOLVED — the mp3 route is no longer blocked:** `Services/AudioService.cs` (NAudio, shipped next to
the plugin) mixes mod-owned samples itself, confirmed working in game, so any sound the user supplies
plays. Stereo pan only — no HRTF or occlusion — plus a playback-rate control (`rate:`) that shifts pitch
and speed together. First user: the mission beacon (`Services/WorldTour/HomingCue.cs`), which pans its
sample toward the objective, halves the rate when the objective is behind, and tightens its repeat as
you close in; it replaced the Wwise objective-voice ping, which was a different line every time and
inaudible at 5.5 m. Open decisions: final key bindings (N/M/Start provisional) and the
missing `wt.*` lang keys in the 11 languages beyond en/es. Then: verify the shared avatar/status menus
inside World Tour (not just Avatar Arcade) and complete the in-game pass on the reworked character
creator.

## Known issues / open questions
- Uses **Tolk**, not PRISM (the playbook default is PRISM).
- Avatar creator + World Tour screens were largely built from decompiled code and still need in-game verification passes.
- Runtime concrete vs. decompiled-interface type names — confirm with a dump before trusting a name.
- **Log noise, not ours to fix now (2026-09-07 session, World Tour field readers):** `DisplayFunc` 1859
  misses, `SimpleList_2`/`SimpleList_3` 174 misses, and 4× `c0000005` (access violation, non-fatal to
  the session) in `app.TableDataManager.TryGetEquipEmoteNameMessage` (the equip/emote name reader).
  Pre-existing engine/game noise unrelated to today's tracker/sweep/radar changes; see this session's
  log if revisiting. **Recurred in the 2026-09-08 radar-rebuild session log** (same
  `TryGetEquipEmoteNameMessage` access violations, repeatedly caught by REFramework's own VMContext
  handler — the game survives each one). Still not investigated, still not radar-related.
- **World Tour phone map (`app.UIFlowWTDeviceMap`) — wip, `PENDING IN-GAME VERIFICATION` (2026-09-07).**
  Implemented as `Hooks/WorldTour/DeviceMapHooks.cs` + `Services/WorldTour/DeviceMapText.cs` from the
  capture `sf6access_autodump_154237.txt` (fields documented in `docs/sf6-screens.md` § World Tour
  phone map). Compiles; nothing about it has been heard in game yet. Checklist:
  1. Open the phone map: does it announce the area + city + the guide message ONCE (not on a loop)?
  2. Move the free cursor onto icons: does each one read "name, kind" (shop / mission / master /
     enemy / marker …), once per icon, with no repetition while the cursor sits still?
  3. Open the fast-travel list ("Abrir viaje rápido"): does it say "Fast travel list", then each
     highlighted destination with its "N of M" position as you move?
  4. Place/remove a marker and check the pin info panel is read; then send
     `re2_framework_log.txt` — it carries the one-off line
     `[SF6Access] Map top group focus index = N` that settles the missing `eTopGroupFocus` field.
- World Tour **world map** (`app.UIFlowWorldMap`) accessibility — not started; needs its own F8/F7
  capture to find the live flow param and GUI owner.

**Detailed history:** see CHANGELOG.md / docs/.
