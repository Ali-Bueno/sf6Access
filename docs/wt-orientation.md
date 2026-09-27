# World Tour — orientation and choosing where to go (2026-09-27)

Tester request (2026-09-26): *"necesitamos encontrar una mejor forma de localizarme por el mundo abierto
y poder ir a cualquier sitio… aún falta anunciar zonas al pasar por ellas… decidir hacia qué punto quiero
ir de cualquier sitio del mapa"*. The radar, beacon, route guidance and ladder routes exist; what was
missing is (1) knowing which part of town you are in, hands-free, and (2) choosing a destination
without the phone map's free cursor. Status of every piece: **PENDING IN-GAME VERIFICATION**.

## 1. What the user's other mods taught (research digest)

| Idea | Seen in | Taken here |
|---|---|---|
| Category + nearest-first target picker built from the game's live markers (R3/V, L1/R1 categories, up/down items, A lock, B cancel) | Dragon Ball Z Kakarot (`nav_tracker.lua`), Diablo II (`docs/entity-scan.md`) | yes — the destination picker |
| Route-aware beacon, spoken reachability verdict | Diablo II (`docs/beacon.md`), Kakarot | already existed (`BeaconDestination`, `LevelRoute`) |
| Zone name on entry from the game's own tracker; first entry instant, only boundary flicker debounced | Among Us (`RoomWatcher`), Diablo II (`docs/rooms.md`) | yes — `ZoneHooks` + `RegionTracker` |
| Persist discovered places per map so a new session lists them at once | Diablo II (`docs/exploration.md`) | yes — `MapIconSnapshot`, `SavedPlaces` |
| On-demand "where am I" macro readout, separate from turn-by-turn | Diablo II (`docs/terrain-overview.md`) | yes — Z (`WhereAmIReadout`) |
| Ambient cues must never interrupt/requeue into dialogue; one-shot events may | Kakarot | zone lines queue, never interrupt; muted during dialogue |
| Remember selection by name, not index, when a list is rebuilt | Kakarot (`screen_map.lua`) | the picker remembers the last category |

Kakarot never solved automatic zone-entry announcement in free roam; Among Us marks room entry as its most
fragile item. Both argue for taking the zone from the game's own tracker, which is what §2 does.

## 2. Zones announced while walking — `Hooks/WorldTour/ZoneHooks.cs`

`ZoneHooks` is the **only** zone speaker. Sources, best first:

1. **The game's section banner** (`Hooks/WorldTour/SectionNoticeHooks.cs`). The banner component
   `app.UICityHud_SectionNotice` is not reachable through `via.Scene.findComponents` (its scene poll never
   logged "found" in any session), so the requests are **hooked** instead:
   `app.worldtour.CitySectionManager.RequestSectionNotice(CitySectionInfo)`,
   `app.UICityHud_SectionNotice.RequestSectionNotice(CitySectionInfo, bool)` and `.SetupNotice(RequestInfo)`
   (`RequestInfo_SectionNotice.SectionInfo`). The section id comes from `CitySectionInfo.SectionId`; the
   banner's own text (`mTextSection`, e.g. "Hong Hu Lu Chinatown - Plaza") is read on the next frames,
   ignoring the previous banner's text, for up to a second; the section-table name is the fallback.
2. **The section id, polled** (`ZoneNameService`). **Root cause of the silence until now:**
   `app.worldtour.CitySectionManager` is **not a managed singleton** — it is a plain object owned by
   `app.GuiManager.AccessData` (`GUIAccessDataManager.SectionManager`, `GuiManager.cs:4653`,
   `GUIAccessDataManager.cs:74`), so `API.GetManagedSingleton` returned null and every reading was 0.
   `WorldTourStateService.GetSectionManager()` now goes through `GuiManager.AccessData`; the static
   `app.Kpi.WT.GetCurrentSectionId()` (`Kpi.cs:397`) is the fallback. A section crossing waits
   `BANNER_GRACE_MS` for the banner's richer wording before speaking the table name.
3. **Landmark region** — when no section has ever been named in this city: the area around the nearest
   fast-travel point ("zona de Beat Square"), from `RegionTracker`: the first reading is immediate; a
   change needs the new landmark to be nearer by 20 % of the distance to the current one (never less
   than `ARRIVED_M`) for two refreshes (1 s). This is what the 2026-09-05 "nearest landmark" chatter
   lacked. Suppressed for the rest of the visit once the game's banners have been heard.
4. **The city**, as the last resort.

Dedupe: sections by id, regions/city by name; a newer zone replaces one still waiting, except the banner
for the same section. Zone lines are queued (never interrupt), muted in menus, fights, dialogue, the pad
tutorial and the destination picker. `FieldAwarenessHooks`' arrival count ("N personas cerca", fired on a
section change, at most every 20 s) is now queued too, so it cannot cut the zone name off.

## 3. "Where am I" — Z (`Services/WorldTour/WhereAmIReadout.cs`)

"En Hong Hu Lu Chinatown - Plaza. Beat Square, a las 3, a 40 metros. Mirando al norte. Destino:
objetivo, a las 11, a 120 metros." — zone, nearest fast-travel landmark with camera-relative clock
direction and distance, facing, and where the current guidance target lies (straight line). A height
difference beyond `ARRIVED_M` is added ("12 metros más arriba").

## 4. Choosing a destination from anywhere — the picker (`Hooks/WorldTour/DestinationPickerHooks.cs`)

**Hold R3 / hold V** (long-press threshold = the user's Windows keyboard repeat delay,
`ReadoutShortcut.SystemHoldDelayMs`, 500 ms by default). A **tap** of R3/V keeps its old meaning — turn
the camera down the next leg of the route — but now fires on release. While open, the field's input is
masked with the game's own one-frame lock (`FieldInputMask`, confirmed working 2026-09-09).

| Action | Pad | Keyboard |
|---|---|---|
| next / previous place (nearest first, wraps) | d-pad down / up | ↓ / ↑ |
| next / previous category | d-pad right / left, R1 / L1 | → / ←, PageDown / PageUp |
| choose | Decide (A / Cross) | Enter |
| close without changing | Cancel (B / Circle), R3 | Backspace, V |
| delete a saved spot | X / Square | Delete |

Each entry: "Style Lab Beauty Salon, tienda, a las 2, a 85 metros, 3 de 12". Choosing sets
`MapSelectionMemory`, so the existing beacon, route guidance, ladder routes and R3 aiming all follow it;
"Objetivo de la historia" hands guidance back to the mission; "Guardar este lugar" saves the current
position as "<zone>, lugar N".

- **The chosen destination now survives menus, the phone and street fights.** `MapAimHooks` used to
  clear it whenever `CanSpeak` went false; it is now forgotten only when control comes back in a
  different city (`ForgetOtherCity`).
- After closing, the field stays masked until the closing button is released, so the game never sees
  that press (A would otherwise talk to whoever stands in front). The picker closes itself for
  dialogue, menus, fights and the pad tutorial (which also uses Decide).
- While the list has no shops/masters/missions/jobs, opening it adds once per session: "Para añadir
  tiendas, maestros y misiones, abre una vez el mapa del teléfono".

Categories (`DestinationCategory`, cycled in this order, empty ones skipped): story objective, places
(fast travel), shops and services, masters, missions, jobs, people nearby, map markers, my places.
Sources (`DestinationCatalog`, live first; same name within `2 × ARRIVED_M` in a category = one place):

| Source | Status |
|---|---|
| Mission objective (`MissionTargetService`) | confirmed in game |
| Fast-travel points (`FastTravelCatalog`, extracted from `ZoneNameService`) | confirmed 2026-09-04 (8 points, Metro City) |
| Nearby named avatars (`AvatarFieldReader`, the N key's source) | confirmed 2026-07-20 |
| `GuiManager.AccessData`: `GetAccessTargetData(ContactUIType.Legendary / NPC)` (masters; NPCs with `ShopComponent` = shop clerks) and `GetWTCityOMSetterData_OM(OMType.Shop)` (`WTCityOMSetter.GetDispName()` is a message **Guid**, resolved pinned, facilities cached per city) | **unverified** — log line `Destinations from the game's map registry: …` |
| Phone map snapshot: `MapParam.DeviceMap` → `UIDeviceMapWindow.GetMapIconPanelList(ICON_GROUP_TYPE)` → panel `Info` (`TitleText`, `Type`, `GlobalPos`), every 2 s while the map is open (first capture 2 s after it opens; a group that reads empty keeps what was learned), saved per city to `reframework/data/sf6access_map_places.txt` (missions not saved) | **unverified** (the same `Info` read is confirmed for the cursor) — log `Map places captured - city …` |
| Saved spots, `reframework/data/sf6access_saved_places.txt` | mod-side |

The phone map is the richest source (everything a sighted player sees). If the registry route fails,
opening the phone map once per city is enough to fill the picker, in that session and later ones.

## 5. Other RE facts found for this work (decompiled, `sf6 code/`)

- `GUIAccessDataManager` (`app/GUIAccessDataManager.cs`) also holds `ScenarioZoneList`
  (`ScenarioZoneGroup`, mission zones with `MissionZoneTarget.mRadius`) and `NpcDummyAccessTargetList`.
- `HudDef.ContactUIType`: `NPC=0, Legendary=1, OM=2, OtherPlayer=3` — masters are `Legendary`; shop
  clerks, job givers and mission NPCs are all `NPC`, told apart by `WTNpcAccessTarget` members
  (`ShopComponent`, `IsLegendaryNPC`, `MissionTargetInfo`, `ObjectPos`, `VisibleUI`).
- Player map pins are saved in `WTPlayerDataUI.GetCityPinInfoList(uint cityId)` → `PinInfo.Position`
  (reached from `WTPlayerManager.getPlayerData(...)`.UI) — not used yet; the map snapshot covers pins.
- `udCityPointList.CityPoints` (`PointDataCityInfo`) has positions but no names.

## 6. In-game verification checklist

- [ ] `SectionNoticeHooks initialized (3 of 3 section notice hooks)`; walking between named parts of a
      district logs `Section notice hook fired: …` and `Section notice: '<text>' (section N)` and speaks it once.
- [ ] `Section id N read through manager` (or `Kpi.WT`) appears — the polled route works.
- [ ] `Zone: CitySectionDataUserDataDict enumerated - N named sections from M of T records`.
- [ ] If neither fires: "zona de <landmark>" announcements while walking — too many, too few?
- [ ] Z: zone, landmark with clock direction, facing, destination — all correct?
- [ ] Hold R3 (and V): "Destinos. Lugares, 8. Beat Square, a las …". Tap R3 still aims, only on release.
- [ ] `Destination picker pad: decide=… cancel=… south=… east=…` and `Field input mask bound …`.
- [ ] While the picker is open the avatar does not move; d-pad/arrows browse; L1/R1 switch categories.
- [ ] Choose a shop: "Destino: …"; the beacon and R3 aim now lead there; arrival clears it.
- [ ] `Destinations from the game's map registry: N masters, N NPCs, N facilities` — report the numbers.
- [ ] Open the phone map, wait 2 s: `Map places captured - city 400: TRAVEL=…, FACILITY=…` — then the
      picker lists those categories, also after restarting the game.
- [ ] "Guardar este lugar", walk away, choose it back; Delete/X removes it.
- [ ] Zone lines never interrupt dialogue and are not cut off by the "N personas cerca" count.
