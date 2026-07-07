# World Tour MODE accessibility plan (draft — 2026-07-07)

> Goal: make World Tour's **field gameplay** usable with a screen reader — exploration, navigation,
> NPCs, quests, encounters — not just its menus (menus are largely done: shops, device item app,
> status menu, WTM pause, avatar create/result). This doc is the durable output of a full sweep of
> the decompiled game code (`sf6 code/`, namespaces `app.worldtour.*`, `UIFlowWT*`, `UI5xxxx`).
> Everything here comes from decompiled interface stubs — **always verify against a runtime dump
> (F8/F9) before building on a type**.

## Status: sweep COMPLETE (6 parallel sub-sweeps, 2026-07-07). Roadmap in section H.

## A. UI flow inventory (sweep complete)

Number families: **UI50xxx** = smartphone/device apps, **UI59800** = photo, **UI61xxx** = avatar
create (done). `UIFlowUI105xx`/`UIFlowUI111xx` are NOT World Tour. Field HUD elements (area-name
splash, button prompts) are `UIParts*` widgets hosted inside flows, not standalone flows —
`UIPartsSituationName` (area splash) appears in `UIFlowWTMyRoom` and `UIFlowWTDeviceMap`;
`MakeInputGuide()` (button prompts) on `UIFlowWTDeviceMap:521`.

### Smartphone / device apps (flow Param types)
| Type | Screen | Key members |
|---|---|---|
| `app.UIFlowUI50000` | Phone home (app grid) | `AppExtraDataSetList`, `_dispDesctopAppIndexList`, `GetInfoFromMainFocus()` |
| `app.UIFlowUI50201` | Item app — **DONE** | `NowDispItemDatas: IList<WTItemParam>`, nested `CallUsedItemDialog`/`CallNotUseDialog` (+`NotUseWarningMessage` Guid) |
| `app.UIFlowUI50600` | Missions/quest-log app | `MissionInfoList: IList<WTMissionDeviceInfo>`, `CurrentMissionCategoryList`, `GetFocusCategoryId()`, `WaitDetailInfo` |
| `app.UIFlowUI50613/4/5` | Map mission pins | `MissionDeviceInfo: WTMissionDeviceInfo` |
| `app.UIFlowWTDeviceMap` | Map app | `mTextCityName`/`mTextAreaName` (via.gui.Text), `TravelPoint: IList<FastTravelPointUserDataRecord>`, `CityList` |
| `app.UIFlowWTDeviceCustom` | Wallpaper app | `_unlockDataList/_dataList`, `FocusGridItemInfo(int)` |
| `app.UIFlowWTDeviceEmoteShortCut` | Emote/shortcut equip | `_equipEmoteIdList`, `_equipMasterActionIdList`, `GuideDescriptionMessage` |
| `app.worldtour.UIFlowWTDeviceIM` | IM app (threads) | `SubjectList: IList<IMSubjectDataRecord>`, `IMList` |
| `app.worldtour.UIFlowIMContentScreen` | IM conversation | `IMDataList: IList<WTIMData>`, `ChoiceMessageIDs: UInt32_Array1D`, `MediaCommandList` |
| `app.worldtour.UIFlowIMPasscodeScreen`/`Dialog` | IM passcode | input state |
| `app.worldtour.UIFlowWTDevicePhotoMode` + `UIFlowUI59800` | Photo mode | pose/facial lists, `SelfieOn/Off` Guids |
| `app.worldtour.UIFlowWTID_Card` | Avatar ID card | Show/Photograph states |
| `app.UIFlowWTMyRoom` | My Room (time of day) | `mSituation: UIPartsSituationName`, `_nowCityTimeTypeList`, `SetMessageFromTime(uint,Text)` |
| `app.UIFlowWTMameDetailMenu` | Arcade cabinet detail | `CallOperation`, `CallScreenshotExpand` |
| `app.worldtour.UIFlowSpTalkNovelMain` | Visual-novel dialog | `TextItems: IList<UIPartsNovelItem>`, `NextTypeMessage`, `NextTypeChoiceMessageArray` |
| `app.worldtour.UIFlowSpTalkNovelMenu` | Chapter select + log | `ClearedChapterList`, `GetListFocusIndex()` |
| `app.worldtour.UIFlowItemDetailDocumentWindow` | Item document reader | `_documentList: IList<Guid>`, `SetDocumentMessage()` |
| `app.worldtour.UIFlowItemDetailPictureWindow` | Item picture viewer | — |
| `app.UIFlowWorldTourContinue` | Continue/load screen | `Main.CityInfo` (`CountryName`/`CitySectionName` Guids), `Main.MissionInfo.TitleGuid`, `StatusInfo.Level/GetHour()/GetMinute()`; nested `DeleteAlertDialog` (`SelectIndex`) |

### Battle-related WT overlays
| Type | Screen | Key members |
|---|---|---|
| `app.worldtour.UIFlowWTTeamBattleVersus` | Team-battle versus splash | `RedTeamMemberInfo`/`BlueTeamMemberInfo` nameplate arrays |
| `app.worldtour.UIFlowWTTeamBattleMatchResult` / `FinallResult` | Team-battle results | `RedWinCount/BlueWinCount` Texts |
| `app.worldtour.UIFlowWTTournamentEntrance` | Tournament entry/rewards | `RewardListSort(IList<RewardItemInfo>)`, `GetTabIndex()` |
| `app.worldtour.UIFlowWTTournament` (+Header/Result/WinAnimation) | Tournament HUD/results | `mButtonText/mStageText`, `ENpcInfoState`, `ShowRank` |
| `app.worldtour.UIFlowMG00Announce` / `UIFlowMG00CurrentCommand` | Minigame banner + command prompts | `VisibleData` arrays |
| `app.worldtour.mg.UIFlowMG03*` / `MG06*` / `MG07*` | Minigames (score/ball/combo) | `UIPartsTextureNumber` scores (needs the SetTextureNums capture trick from BonusResultHooks), `ScoreManager` |

(Encounter-start / EXP-bar / drops have **no dedicated flow** — they render through the avatar
result screen + UIParts widgets; see section E.)

## B. Exploration / movement / map data (sweep complete)

### Player avatar (position / facing)
- **`app.worldtour.avatar.AvatarBase`** — the character type; world pos+rot from its
  `GameObject.Transform`. `IsMasterPlayer()` identifies the user's avatar; `GetRespawnPos(out ...)`.
- **`app.worldtour.avatar.AvatarManager`** (Behavior singleton) — `AvatarList` (all avatars → filter
  `IsMasterPlayer()`), **`CurrentAccessInfoList: IList<AccessInfo>`** (live interaction candidates),
  nested `CheckedOutParam.AccessedInfo {Pos: vec3, Distance, Angle, ToAvatarAngle: float}`.
- **`app.worldtour.CitySectionManager`** — `CurrentSectionInfo`, `CurrentSectionId: uint`,
  `SectionInfoList` = which sub-area the player stands in.

### Interactables (the "what can I interact with" layer)
- **`app.worldtour.avatar.AvatarAccessTargetBase`** — the component on EVERY talkable/examinable
  object: **`GetDispName(): string`**, `GetContactUIType(): HudDef.ContactUIType`,
  `GetShopCategory()`, `IsMissionTarget()/IsRelationToMaster()`, `ContactPanelDispState`; nested
  `AccessCheckShape` carries `UiGuideId: InputAssign.GuideId` (the prompt: Talk/Examine).
  Concrete subtypes: `WTNpcAccessTarget`, `WTOmAccessTargetSimple`, `WTZoneAccessTargetSimple`,
  `WTOtherPlayerAccessTarget`.
- **`app.worldtour.avatar.IAccessTargetSearcher.AccessTargetInfo`** — one entry per NEARBY
  interactable: `Target: AvatarAccessTargetBase`, `NearPos: vec3`, `Distance: float`,
  `Angle: float`, `BasePriority`; `IAccessTargetSearcher.GetCurrentTarget()`.
  **→ `AvatarManager.CurrentAccessInfoList` + this = the live nearby-targets radar.**
- `app.worldtour.CityPointHolder` → `udCityPointList` (`CityPoints`, `FastTravelPoints`), base
  `CityPointDataInfoBase {mPointId: int, Position: vec3, Rotation}` = static interaction-point map.
- `app.worldtour.WTContactSystem` (singleton) — drives the "Talk" contact flows.

### Map / minimap markers
- **`app.UIMapWindowBase`** — nested **`IconInfo {TitleText: string, GlobalPos: vec3, UiPos, Type:
  ICON_TYPE, GroupType, IsFastTravelPoint, Priority, ZoneInfo{Radius}}`**; `ICON_TYPE` enum:
  SHOP_APPAREL, SHOP_GENERAL, JOB, MISSION_MAIN, FAST_TRAVEL, MASTER, HOME, MERCHANT, TOURNAMENT,
  MASTER_TALK…; `PinInfo {CityId, PinTypeId, Position}`; `PlayerTrans: Transform`,
  `MapDispType {CITY, SECTION}`.
- **`app.UIDeviceMapWindow`** — `mIconInfoList: IDictionary<ICON_GROUP_TYPE, IList<IconInfo>>`,
  `PinInfoList` — **the single best source for "every marker + label + world pos"**.
- `app.UIMiniMapWindow` (HUD minimap), `app.UICityHud_CityMiniMap` (`DispSectionId`),
  `app.CityHudManager` (owns all `UICityHud_*` widgets).

### Fast travel / area names
- **`app.UIFlowWorldMap`** — nested `FastTravelPoint {SceneCityId, Id, Name: string (LOCALIZED),
  TimeType}`, `CountryInfo {CountryName: string}`; fields `CityName/CountryName/DetailName: string`;
  static `GetFastTravelPoints(parentCityId, situationId)`. Best travel-destination list.
- `app.worldtour.FastTravelPointUserDataRecord` — `CityID`, `IsMyRoom`, `PointNameID.GUID` (name).
- **Area names**: `CitySectionDataUserDataRecord {id, SectionNameID.GUID, EnemyLevel_Day/Night}` —
  join with `CitySectionManager.CurrentSectionId`; city names in `CityMessageUserDataRecord`
  (`CityName/CityUIName/CityFlavor` GUIDs).
- **`app.UICityHud_SectionNotice`** — the "entered new area" HUD banner (hook point), nested
  `RequestInfo_SectionNotice {SectionInfo: CitySectionInfo}`.
## C. NPCs & dialogue (sweep complete)

All types below derive `via.Behavior/Component` → reachable via `API.GetManagedSingleton("app.worldtour.<Type>")`.
Prefer the listed backing FIELDS over property getters (IL2CPP getter rule).

### NPC entities (the live world population)
- **`app.worldtour.WTNpcContext`** — the live per-NPC object. Fields: `NpcID: uint`, `NpcName: string`,
  `BasePos: vec3`, `BaseDir: vec3`, `NpcLv: uint`, `isFighting/isInBattle: bool`, `CrowdType`, `Age`,
  `PopedZoneID: uint`, `LedgerData: WTNPCManager.NPCData`. Methods `IsEnemy()`, `IsContactBattle()`.
  **Primary target for a "who's around me" reader** (name + position + level as plain fields).
- **`app.worldtour.WTNPCManager`** (singleton) — `npcDatas: IDictionary<uint,NPCData>`,
  `challengeNpcList: ISet<uint>`, `fightingNpc: ISet<int>`, `isBattle: bool`,
  `InstanceMgr: NpcInstanceManager`; `GetAllNpcController(): IList<WTNpcContext>`,
  `IsChallengeNpc(uint)`.
- **`app.worldtour.NpcInstanceManager`** — `ManageInfoList: IList<NpcSetRequestInfo>` (each entry has
  `Context: WTNpcContext`) = the most reliable FIELD-level live-NPC enumeration.
- **`WTNPCManager.NPCData`** (nested) — per-id ledger: `IsEnemy: bool`, `IsChallengeNpc: bool`,
  `Gender`, `SpeciesType`, `ThumbnailName: string`.
- `app.worldtour.WTNpcData` — authoring blob; `DefaultMessageDatas: NPCMessageUserData_Array1D`
  (greeting lines, `id → GUID`), `EnableEntryBattle: bool`.

### Field dialogue — SpTalk (the WT conversation system; NOT arcade DemoSubtitles)
- **`app.worldtour.SpTalkSystem`** (singleton) — `mState: EState`
  (None/Activating/Idle/Preparing/Ready/Playing/Finalize), `mSpTalkCtrl: SpTalkCtrl`,
  `GetTalkNpcList(): IList<WTNpcContext>`, `IsPlaying()`, `IsNovel()`,
  **`ExecOnDecideChoice(int)`** (choice-decide hook point).
- **`app.worldtour.SpTalkCtrl`** — the dialogue state machine: `mCutId/mPageId: int`,
  `mSubtitlesProgress: SubtitlesProgress`, `GetPageData(int): SpTalkPageData`,
  `ChangePageId(int)` / `SetEndSubtitles()` = the **advance hooks**.
- **`SpTalkCtrl.SubtitlesProgress`** (nested) — `mSubtitlesData: SpTalkSubtitlesData`,
  `mChoiceState: EChoiceState` (None/Select/Decide), **`mSelectChoiceIndex: int`** (live highlighted
  dialogue option), `mMsgWindow: UIWidgetCtrl_MessageWindow`.
- **`app.worldtour.SpTalkSubtitlesData`** — THE line record: `mTextNameGuid: Guid` (speaker),
  `mTextDialogueGuid: Guid` (line), `mListChoice: IList<SpTalkChoiceData>` (each choice:
  `mTextGuid: Guid`, `mNextCutId: int`).
- Existing SpTalkHooks (Battle Hub NPC dialogue, UNTESTED) may partially overlap — verify which
  system it hooks before adding a second reader.

### Street-fight challenge prompt (targeting an NPC)
- **`app.UIPartsContactPanelNPC`** — on-target panel: `mTextLv: via.gui.Text` (level),
  `mCtrlName: Control` (name), style icon, `mTexCantBattleIcon`, `mDropItemDataTbl:
  WTBattleRewardItemData`, `LevelDiff: int` + `LEVEL_LABEL_STATE{DANGER_1..3}`,
  `UpdateAccessTarget(AvatarAccessTargetBase)` (hookable: fires when the targeted NPC changes).
- `app.UIPartsContactDetailItem` — drop-reward rows (`mTextItemNum`, `mTextItemCondition`).
- Opponent data: `app.worldtour.WTNpcBattleSetData` (`IsBoss`, `MinionDataList`) →
  `WTNpcBattleFighterSetData` (`Level: uint`, `FighterId: int`, `FighterClass`).

### Masters in the field
- **`app.worldtour.WTMasterManager`** (singleton) — `MasterDataMap: IDictionary<uint,udWTMasterUserData>`,
  `GetMasterIdFromStyleId(uint)`.
- **`app.worldtour.udWTMasterUserData`** — `GetName()/GetUIName()/GetStyleName()/GetDescription()`
  return **strings** (no Guid resolve) → crash-free master-name alternative to the forbidden
  `TryGetMasterProfileUserData`.
- **`app.worldtour.WTPlayerDataMaster`** — `MasterParamList: IList<WTMasterParam>`; each
  **`WTMasterParam`**: `MasterId`, `Favorite: uint` (bond points), `AssistLevel: uint`,
  `IsEncount/IsEntry/IsExpert: bool` — all plain fields (bond/level readout).
- Gift/bond UI: `app.UIMasterPresentList` (gift list), `app.UIPartsBondGauge`,
  `UIPartsMasterInfoPanel/DetailWindow/ListItem`; map icons `UIPartsWorldMapMasterIcon(+List)`.
  `MasterProfileUserDataRecord.MasterHighCommonPresentList` = the liked-gift item ids.

### Emotes / balloons
Field NPCs have NO floating text balloons (talk goes through SpTalk; crowd "gaya" `WTGayaSetter`
has no text). Emote reactions are motion-id based (`app.worldtour.npc/npcai.EmoteReaction`) —
spoken feedback would need an id→label map of our own.
## D. Quests / missions / progression (sweep complete)

### Active quests + objective text (top priority)
- **`app.worldtour.WTMissionSystem`** (singleton) — THE quest manager:
  `GetAcceptedMissionInfoListExcludeClear(): IList<WTMissionProcessInfo>`,
  `FindProgressMissionId(): uint` (the tracked quest),
  `GetTutorialGuideMessage(uint mid, uint phase, out string title, out string detail)`,
  `GetMissionTargetInfo/NpcTargetInfo/OmTargetInfo/ZoneTargetInfo → MissionTargetInfo`.
- **`WTMissionSystem.PhaseUIData`** (nested) — `PhaseIndex`, `TargetData: IList<TargetInfo>`;
  **`TargetInfo {IsCleared: bool, Message: string, NowCount, TotalCount: uint, GetMessage()}`** =
  the objective lines with progress counters ("Defeat 3/5 ...") — the screen-reader payload.
- **`app.UICityHud_MissionProgress`** — the on-screen quest tracker HUD: nested `ProgressInfo
  {MissionId, MissionType, MissionName: string, ChapterNo, SerialNo, PhaseData: PhaseUIData,
  PlayClear}`, widget fields `mTextMissionTitle/mTextMissionNumber: via.gui.Text`,
  `mTargetList: UIPartsScrollList`, `DiffType {MissionId, PhaseUpdate, TargetClear, UpdateCount}`.
- **`app.UICityHud_MissionGuide`** — the objective waypoint arrow: `ProgressMisionInfo
  {MissionTargetInfo, TargetObject: GameObject, TargetType {NPC,OM,ZONE}, UIPos: vec3}`,
  `IsOutSideScreen`, `showArrow` — **the guide-target position for direction/distance callouts**.
- Static definitions: `WTMissionDefine.WTMissionUserData` (`TryGetTitleMsgGuid/TryGetDetailMsgGuid`),
  `WTMissionPhaseUserData` (per-phase target message ids), `MissionMessageUserDataRecord {id, GUID}`.
- NOTE: `CharacterAsset.Mission.*` is a DIFFERENT system (battle trials stage setup), not WT quests.

### Progression notifications (announce-as-they-fire)
All follow the `UICityHud_NoticeRequest` pattern with `HudDef.NoticeType {Start, Clear, Update}`:
- `UICityHud_MissionNoticeBase` (+ `MainMissionNotice`/`SubMissionNotice`) — quest accepted/cleared;
  nested `RequestInfo_Mission {MissionType, NoticeType, MissionID, MissionTitle: string}`,
  texts `mTextMissionName_Accepted/_Clear`, delegate `MissionNoticeEvent(uint, NoticeType)`.
- `UICityHud_ChapterNotice` — chapter banner (`RequestInfo_Chapter`, `mTextTitle`).
- `UICityHud_LevelUpNotice` — `RequestInfo_LevelUp {NewLevel: int}`, `mTextLevelInfo`.
- `UICityHud_ItemGet` — item toast (`RequestInfo_GetItem {Data: udWTItemUserData, Num, IsDropLock}`).
- Also: `StyleUpNotice`, `MileNotice`, `FavoritePointRise` (bond), `SectionNotice` (area),
  `ContentsOpenNotice`, `InformationNotice`. `app.CityHudManager` owns all of them.

### Player progression (authoritative save data)
- **`app.worldtour.WTPlayerDataUtilty`** (static) — `GetPlayerData(uint shortId): WTPlayerData`,
  plus direct `GetStatusData/GetWalletData/GetMissionData/GetMasterData(...)`.
- **`WTPlayerDataStatus`** — `Level`, `Exp`, `SkillPoint`, `VitalNow/Max`, punch/kick/throw/special
  power, `NextLevelUpParam` — the level/XP truth.
- **`WTPlayerDataWallet`** — `Money`, `Mile`, `GetWalletValue(WTWalletID)` — authoritative money
  (prefer over HUD scrape when refactoring the Start/G money readout).
- **`WTPlayerDataMission`** — `mChapterNo`, `mProgressMainMissionId`,
  `mDicMissionProcessData: IDictionary<uint, ProcessData {mMissionId, mPhaseIndex, ...}>`.
- `WTPlayerDataGeneral` — `mCurrentSectionId`, `mPlayTime`, unlocked cities + fast-travel flags.

### Tournament / team battle / minigames
- Tournament: `WTCityContentCtrl003` state machine (`eState {Activate, LoadTournament, Battle,
  UpdateTournament, ...}`), `app.worldtour.tournament.OpponentInfo {NpcId, NpcLv, NpcStyleId}`,
  `RewardInfo`; UI flows in section A.
- Team battle: `WTCityContentCtrl004`, `app.worldtour.teambattle.TeamData {Fighters:
  IList<FighterData {mNpcName: string, mNpcLv, IsPlayerAvatar}>}`.
- Minigames: **`app.worldtour.mg.WTMiniGameManager`** (singleton) — `MiniGameType/Level/Variation`,
  `CurrentPlayInfo: MiniGamePlayInfo {IsClear, PlayTime, Score, IsHighScore}`,
  `IsStartMiniGame/IsPlayMiniGame/IsEndMiniGame`, `GetSaveDataHighScore()`;
  persistent scores in `WTPlayerMiniGame.GetHighScore(type, variation, level)`.
  7 minigame slots (`WTDefine.MinigameType 1..7`, levels 1-6); UI flows MG00/03/06/07 in section A
  (scores are `UIPartsTextureNumber` → need the SetTextureNums capture pattern).

### Ledger
`app.worldtour.ledger` is NOT a quest journal — it's the NPC master registry
(`WTNpcLedgerDataContainer`: names, thumbnails, `udChallengeNPCList`, battle AI).
## E. Field battles / encounters (sweep complete)

### Encounter start/end
- **`app.worldtour.WTEngageBattleFlowMap`** — `onBattleStart(bFlowBase)` / `onBattleEnd(bFlowBase)` =
  the cleanest **hook points for "entering/leaving a WT battle"**.
- `app.worldtour.WTContactSystem` (singleton) — the encounter script interpreter (`State: uint`,
  FlowPhase with `waitSetupBattle/transitionBattle/waitBattleStart/waitBattleEnd`,
  `_EnemyObjects: IList<GameObject>`).
- `app.worldtour.WTBattleManager` — battle lifecycle: `SetupEngageBattle(BattleType, ...)`,
  `StartBattle()/EndBattle()`, `GetBattleFighterParam(teamId, memberId)`; nested
  `BattleStatusParam.FinishBattle(is_win, eLoseType, ...)` (win/lose + lose-type).
- Cheap state polls: `WTIsBattle` / `WTIsBattleFinish` (`evaluate(...): bool`).
- Enemy nameplate: **`app.worldtour.DemoNamePlate`** — `TextName/TextAlias: via.gui.Text`,
  `NameMessage/AliasMessage: Guid`.
- `WTDefine.BattleType {Street, Serious, Tutorial, Online}`;
  `WTDefine.BattleIntrusionDir {Front, Back, Space}` ("approaching from behind").

### In-battle WT data
- **`app.worldtour.WTFighterStatusData`** — live HP + stats for ANY WT fighter:
  `Level`, **`VitalNow/VitalMax: uint`**, `FocusGauge*`, `SAGauge*`, punch/kick/throw/special/defense.
- Enemy definition: `WTNpcBattleData {FighterId, Level, StyleId, IntrusionDir,
  StatusData: WTFighterStatusData, RewardExp, RewardItem}`; multi-opponent composition in
  `WTNpcBattleDataManager {IsBoss, MinionDataList, EngageFighterLimit, FrontLimit}`.
- Buffs/debuffs: **`app.worldtour.WTFighterBuff`** — `GetEnableFighterBuffList()` /
  `InBattleEnableBuffList` = active effects on a fighter; names via `WTBuffManager` +
  **`BuffNameMessageUserData`/`BuffUniqueDescriptionMessageUserData`** (localized).
  `WTBuffDefine.WTBattleBuffEffect {VitalTimeDamage=14 (poison/DoT), VitalTimeRecovery,
  AttackUp/Down, DefenseUp/Down, ...}`, `BurnOutDebuff`.
- Item wheel mid-battle: `WTPlayerDataItem` register set (`RegisterItemParam`, `GetRegisterId`,
  `ItemArray`) = the quick slots; `UseItem/UseItemEffect(itemId, ...)` fire on use;
  names via `WTItemManager.GetItemData(itemId): udWTItemUserData`.
- Drop-lock (rare-drop condition): `WTDropLockController {State: eEvalState, Condition, Reward,
  Rarerity}` + `WTDefine.DropLockConditionType {KO, HIT_PUNCH, COUNTER, FIRST_ATTACK, COMBO, ...}` +
  `DropLockConditionMessageUserData` (localized condition text — "win with a throw").

### Rewards / level-up (data behind the avatar result screen — AvatarResultHooks is the UI)
- **`WTBattleManager.BattleRewardParam`** — `Exp: uint`, `StyleExpList: IList<StyleExp>`,
  `RewardItemList: IList<WTRewardItem {ItemId, Num}>`, `OutfitPoint`,
  `RewardRatioType {Win, Lose_Death, Lose_BattleIn}`.
- **`WTStatusManager.GetPrevLevelUpParam(...)` → `WTPrevLevelUpStatusData.GetUpperStatus(
  WTPlayerStatusType): float`** = per-stat level-up deltas ("+X attack").
- Win/streak totals: `WTBattleFlowMap.ResultData {BattleTotal, ArrBattleWin, StreakNum, DrawNum}`.
- `WTBattleRewardExpData {Exp, StyleExp}`, `udWTBattleNpcRewardTable {NormalList/RareList, RareRatio}`.

### Field hazards / projectiles / destructibles
- `app.worldtour.om.GimmickVisualController` — destructible/interactive object controller
  (`onContact(CollisionInfo)`, `SelfState`); `om.action.Destroy/DestroyPhysics.Execute(...)` =
  "object destroyed" hooks; numbered `Om0xxxxx` catalog (positions via GameObject transform).
- `app.worldtour.shell.WTShellManager/WTShellEmitter/WTShell` — field projectiles the player must
  dodge, with hit conditions (`WTShellConditionHitTerrain/OnDamage/OnGuard`).
- Per-hit data: `WTDamageResult {DamageValueForBattleIn, HitPos: vec3, MultiAttackNum,
  IsSuperAttack}`, `WTAttackResult` (player-dealt).

### WT story-event captions
Field/story events use **SpTalk** (section C) — `SpTalkCtrl.SubtitlesProgress.mSubtitlesData`
(`mTextNameGuid`/`mTextDialogueGuid`) is the WT analog of DemoSubtitles. `SpTalkSystem.IsPlaying()`
detects an active event. (Arcade `DemoSubtitles`/`ComicDemoSubtitle` are already hooked by
ArcadeHooks and also fire for shared cutscenes — verify overlap at runtime before adding a reader.)

## G. Smartphone / inventory / skill tree data layer (sweep complete)

- Device shell: all phone apps derive `app.UIFlowDeviceAppBase` (`BaseAppParam`); walk
  `UIFlowDeviceBase.BaseParam` (`ParentParam/ChildParam`, `GetDeviceTopParam()`) to know the open
  app; app names in `DeviceAppNameMessageUserData`.
- IM (messages): UI in section A; data in `WTPlayerDataIM` (received/read/replied/unread),
  `WTIMDefine.WTIMProvisionUserData` (one message; text via WT script `Provision`),
  `IMChoiceWork.GetChoiceWord(replyNo)` (reply options), `IMSubjectUserDataRecord.SubjectMessage.GUID`
  (thread title). `UIFlowIMContentScreen.IMContentFlowParam` has `ChoiceNum + ChoiceMessageIDs`
  and `eContentInputState {Default, PlayerPost, PlayerStamp, Wait}`.
- Inventory manager (data under the item app): **`WTPlayerDataItem`** —
  `GetItemParamList(storageType, itemType, sortType, ...): IList<WTItemParam>`, `GetItemNum(...)`;
  **`WTItemParam {ItemId, Num, Level, ItemType, GetNameWithLevel()}`**;
  **`udWTItemUserData`** — `GetName()/GetDescription(): string` (direct strings!), `Rarity`,
  `BuyPrice/GetSellPrice(level)`, `DetailTextList: IList<Guid>`.
- Stats/skill tree: `WTPlayerDataStatus` (section D); **`WTPlayerDataStyle`** — `StyleLevel`,
  `SkillPoint(Total/Used)`, `SkillTreeOpenList`, `IsUnlockSkill(skillId)`,
  `GetOpenSkillTreeSkillList(): IList<udWTAbilitySkillUserData>`;
  **`app.SkillTreeDef`** — `TreeSkillState {CanOpen, Win, Locked, ReadyLocked, Lose}`,
  `TreeUnit/OpenSkillData {OpenSkillId, OpenCost}`,
  `SetSkillDescriptionMessage(Text, udWTAbilitySkillUserData)` (description binder),
  `SkillNameMessageUserData` (names). UI parts: `UIPartsSkillTreePanel/Group/TreeSkillDetail`.
- Wardrobe: `WTPlayerDataEquip` (`GetEquipSlotParam`, `UpdateEquipItem`, `CalcEquipStatus`);
  re-edit appearance reuses the `UIFlowWTAvatarCreate*` screens (already covered).
- Mail app in WT = the same `app.UIFlowMailBox` NewsHooks already reads.

## H. Feature roadmap (what to build, in order)

Reglas: generic-first (probar `WatchPrefixes` del GroupFocus antes de un reader dedicado);
todo tipo/campo de este doc debe **verificarse con un dump F8/F9** antes de construir encima;
texto siempre del juego (Guids → message table); anunciar CAMBIOS, no estados.

### Phase WT-1 — Field awareness core (foundation, highest value)
1. **`WorldTourStateService`** (new service): cache `WTCityManager` (`IsActivated()` gate for every
   WT hook), expose city/section/time/weather. Announce area changes via `UICityHud_SectionNotice`
   (hook its request) or by polling `CitySectionManager.CurrentSectionId` →
   `CitySectionDataUserDataRecord.SectionNameID.GUID`.
2. **Interactable radar** (new hook): poll `AvatarManager.CurrentAccessInfoList`; announce the
   CURRENT access target when it changes (`Target.GetDispName()` + prompt `UiGuideId`); on-demand
   key lists nearby targets sorted by `Distance` with clock direction (camera yaw from
   `WTPlayerCameraController.getCurrentRotation()`, or `WTPlayerManager.GetPlayerToAngle`).
3. **Contact panel reader**: `UIPartsContactPanelNPC` (name, level + `LEVEL_LABEL_STATE` danger,
   talk vs can't-battle icon, drop rewards via `UIPartsContactDetailItem`). Hook
   `UpdateAccessTarget(AvatarAccessTargetBase)` for the change signal.

### Phase WT-2 — Quests
4. **Quest tracker reader**: `UICityHud_MissionProgress.ProgressInfo` (MissionName + PhaseData
   objectives `TargetInfo.GetMessage()` + `NowCount/TotalCount`); announce on `DiffType` changes;
   on-demand key re-reads the tracked quest.
5. **Progression notices**: one adapter covering the `UICityHud_*Notice` family (mission accepted/
   cleared, chapter, level-up, item get, style-up, mile, bond) — hook their `Request*` methods or
   poll their texts; `HudDef.NoticeType {Start, Clear, Update}` picks the phrasing.
6. **Objective direction on demand**: `UICityHud_MissionGuide.ProgressMisionInfo.TargetObject`
   position vs player position + camera yaw → "objetivo a las 2, 30 metros"; repeatable key.
7. **Quest log app** (`UIFlowUI50600`): try generic reader first (it has ScrollLists); dedicated
   adapter only if rows are texture-rendered.

### Phase WT-3 — Dialogue & phone
8. **SpTalk reader** (field conversations + WT story events): poll `SpTalkSystem.IsPlaying()` →
   `SpTalkCtrl.mSubtitlesProgress.mSubtitlesData` (speaker + line Guids); announce page changes
   (hook `ChangePageId`/`SetEndSubtitles` or poll `mPageId`); read choices (`mListChoice` labels)
   and track `mSelectChoiceIndex` for the highlighted option. Check interplay with the existing
   SpTalkHooks (Battle Hub) — likely the same system, extend rather than duplicate.
9. **IM app reader**: thread list (`UIFlowWTDeviceIM` subject list), conversation bubbles + reply
   choices (`UIFlowIMContentScreen`). New-message notification via `WTPlayerDataIM` unread state.

### Phase WT-4 — Encounters & combat
10. **Encounter announcer**: hook `WTEngageBattleFlowMap.onBattleStart/onBattleEnd`; speak enemy
    name (`DemoNamePlate`), level/style (`WTNpcBattleData`), `IsBoss`, minion count, and
    `BattleIntrusionDir` ("te atacan por la espalda").
11. **WT battle status on demand**: HP (`VitalNow/VitalMax`) both sides, active buffs/debuffs
    (`WTFighterBuff` + localized names), drop-lock condition + its success state.
12. **Reward verification**: cross-check AvatarResultHooks against `BattleRewardParam` +
    `WTPrevLevelUpStatusData` (per-stat deltas) so level-ups read the numbers, not the count-up.

### Phase WT-5 — Navigation assist & extras
13. **Obstacle probe**: `WTCameraControllerBase.castSphereTerrain` forward-ray on demand
    ("pared a 3 metros"); potencialmente un modo sonar continuo opcional.
14. **Fast-travel + map**: map app reading (`UIFlowWTDeviceMap` city/area texts + travel list);
    marker enumeration from `UIDeviceMapWindow.mIconInfoList` (label + distance + direction).
15. **Minigames**: per-game announce via `WTMiniGameManager` (`CurrentPlayInfo.Score/IsClear`,
    high score) + MG00 command prompts (`UIFlowMG00CurrentCommand`); scores are texture numbers →
    reuse the `SetTextureNums` capture pattern from BonusResultHooks.
16. **Tournament / team battle**: entrance + bracket + results flows (section A) — generic-first.
17. **Skill tree**: `UIPartsSkillTreePanel` focus + `SkillTreeDef` node state/cost/description.

### Research protocol per feature
1. F8 auto-dump navegando la pantalla/situación una vez; F9 para estados sin flow param.
2. Verificar FullNames/campos del doc contra el dump (los stubs pueden divergir del runtime).
3. Generic reader primero si hay UIParts lists; dedicated adapter (ScreenAdapter) si no.
4. Añadir lo aprendido a `docs/sf6-screens.md` (pantallas) o a este doc (modo campo).
## F. Managers / singletons / navigation backbone (sweep complete)

### Core singletons (fetch by FullName via `API.GetManagedSingleton`)
- **`app.worldtour.WTCityManager`** — THE hub. `mCityId: uint`, `mTimeType: uint`
  (→ `WTDefine.TimeType {Invalid=0, Day=2, Night=4, Midnight=5}`), `mWeatherType`
  (→ `WTDefine.WeatherType {Fine,Cloudy,Foggy,Rainy,Snowy,Storm}`), `mSituationId`,
  `mState {Deactivated,Activated}`, `mDicFastTravelPoint: IDictionary<uint,IList<PointDataFastTravelInfo>>`.
  Methods: `IsActivated()` (**the reliable "we are in World Tour" signal**), `GetCountryId(cityId)`,
  `GetTalkNpcList()`, `GetFastTravelPointList(...)`. Transition types in `mCityJumpType`
  (Normal/WorldMap/FastTravel/SubwayIn/Out/AfterBattle/Tournament...).
- **`app.worldtour.WTPlayerManager`** — `LocalPlayerData: WTPlayerData`, `LocalPlayerObject: GameObject`,
  `GetAvatarPlayer(): AvatarPlayer`, **`GetPlayerToAngle(vec3 currentPos, vec3 forwardDir): float`**
  (ready-made relative-angle helper for "target at N o'clock").
- **`app.worldtour.WTNPCManager`** — see section C.
- **`app.worldtour.WTMissionDataManager`** — quest backbone: `GetAllMissionId()`,
  `GetMissionData(missionId): WTMissionData`, `GetMissionChapterInfo/CityInfo/MasterInfo(...)`,
  `GetNpcTalkContactList(...)`.
- **`app.worldtour.WTAreaManager`** — named sub-areas: `AreaInfo/AreaControl/AreaData` with
  `AreaName: string`, `ActiveAreas` (**"which named sub-area am I standing in"**).
- `WTStatusManager` (level/EXP math: `GetNextLevelUpParam`), `WTMasterManager`, `WTStyleManager`,
  `WTItemManager` (inventory), `WTWalletManager` (currencies, `WTWalletID`), `WTBuffManager`
  (active buffs), `WTBattleManager` (field-battle setup/rewards), `WTRewardManager`,
  `WTMileManager` (challenge points), `WTIMDataManager` (phone messages), `WTCrowdManager`,
  `bWTHitManager` (`CachedLockOnInfo` — combat lock-on target cache).

### Camera (facing basis for relative directions)
- **`app.worldtour.WTPlayerCameraController`** — `Position/LookAt: vec3`,
  **`getCurrentRotation(): Quaternion`** → camera forward for clock-direction math.
- `app.worldtour.WTCameraManager` — active WT camera stack (`getHeighestOrderCamera()`).
- Base `WTCameraControllerBase` — **`castSphereTerrain(ref vec3 start, ref vec3 end, ref float r,
  out CollisionSystem.HitResult): bool`** = terrain/wall probe a mod can CALL to sense surroundings;
  plus `calcLookAt`/`calcRotFromLookAt` geometry helpers.

### Navigation data
- `WTCityRoute` / `udWTNpcRoute` — per-city, per-situation authored waypoint route graphs
  (positions; NPC-oriented but usable as walkable-path hints).
- `WTHeightMapResourceHolder` — terrain height texture (ground-height sampling).
- `WTMissionTutorialGuideData` — `mListGuideInfo: IList<GuideInfo>`
  (`mTitleMessageId/mDetailMessageId/mMissionId/mPhaseId`) — authored guide targets.
- `app.worldtour.om.Om00xxxx` types (many) — interactable field objects with `SettingParam :
  WTCityOMSetParamBase` (shops, gimmicks) — the interactable catalog.

### Practical recipe (from the sweep)
`WTCityManager.IsActivated()` gates everything → `WTPlayerManager.LocalPlayerObject` for player pos →
`WTNPCManager.GetAllNpcController()` + `WTMissionDataManager` for targets →
`WTPlayerCameraController.getCurrentRotation()` as facing basis → `GetPlayerToAngle` or atan2 for
clock direction → `castSphereTerrain` for wall/obstacle probes.
## G. Feature roadmap (to be written from B–F)
