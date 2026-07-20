# SF6Access — Per-Screen Technical Reference

Confirmed type FullNames, fields, enums, and read recipes per screen. See
[`sf6-architecture.md`](sf6-architecture.md) for the shared services and patterns these rely on
(`FlowHelper`, `GuiTextReader`, GroupFocus, stale-param re-entry, IL2CPP gotchas, dump tools).

Runtime uses CONCRETE types; decompiled code shows interfaces. Always verify a name with a dump. Many
menus name items `c_item_N` (not just dialogs) — read the subtree text, use Yes/No only as last resort.

---

## Main menu, tabs, options

### Main menu (`MainMenuHooks`, `FGMenuHooks`)
- `UIAgent.FocusChanged()` — any focus change (gives `SelectItem`). Suppress while a dedicated hook
  owns the screen (option menu, key config, news, status, rewards, …).
- `UIStartMenu.FlowParam.MenuItemSelectionChanged()` — grid item selection.
- Items `item\d+` / `c_item_\d{2,}` = grid items; `c_item_\d` (single digit) = dialog buttons.
- Tab names: `fg`=Fighting Ground, `bh`=Battle Hub, `wt`=World Tour.
- Fighting Ground: `app.menu.UIFlowFGMainMenuList.Param` (starts AFTER `FlowParam` ends). Hooks:
  `MainChanged` (horizontal category, `GetSelectData().Name` is a Guid), `SetSubPos`, `Right`, `Left`.
  Vertical items `c_SubMenu_item0..3`.

### Options (`OptionMenuHooks`, `OptionSubScreenHooks`)
- `UIOptionSettingMenu` + `OptionMenuParam`. `OptionManager` singleton holds all current values.
- Navigation: `UIPartsOptionUnit.SwitchFocus(bool isFocus)` dynamic hook. Fires **2× per move, both
  isFocus=true** (old + new item); order depends on direction. Solution: collect all addresses in
  LateUpdate, announce the one differing from the last announcement. Also verify `get_IsFocus`
  (`UIPartsItem.IsFocus`) at process time to skip a unit that already lost focus (rapid up/down).
- `get_Setting()` works (→ `OptionSettingUnit`); `get_UnitData()` **always null**.
- Current value: poll `OptionManager.GetOptionValue(typeId)` every frame (catches all mechanisms —
  spin/AddNum/SubNum don't fire for `DecideEventType=3`, which open sub-lists). Label via
  `Setting.GetValueMessage(index)`.
- `OptionSettingUnit` fields: `_DataType` (0=Group,1=Value), `TypeId` (→ `Option.ValueType`, e.g.
  611=DispLanguage), `InputType`, `DecideEventType` (3=opens sub-list,10=inline),
  `TitleMessage`/`DescriptionMessage` (Guid), `ValueMessageList` (List<Guid>).
- `UnitInputType`: 0-3 Button, 4 SpinText, 5 SpinText_OnOff, 6 SpinText_Num, 7 Slider. Value widget by
  InputType: 4/5→`SpinTextList.GetFocusMessage()`; 6→`ItemParts_SpinText._numText`; 7→`SliderValueText`.
  Row widgets are POOLED across screens with stale text — only the InputType-selected widget is live.
- Tabs (`app.Option.TabType`): 0 General,1 Interface,2 Battle,3 Field,4 Audio,5 Language,6 Graphic.
  `TypeId/100 - 1` → tab index. Tab labels via `mTabList._Children[tabIndex].Control`.
- Sub-lists (`OptionMenuParam.ListState`, `eListState`): MainList=0, SubList=1, RadioButtonList=2,
  NonCtrl=3. Dropdowns open on 1 **or** 2 (language uses RadioButtonList). `UIPartsSimpleList`/
  `UIPartsScrollList.InvokeSelectionChanged` fires inside sub-lists (read deferred — index not yet
  updated in the PRE hook). Language tab: DispLanguage=611, VoiceLanguage=610,
  CharacterVoiceLanguage=640, CharacterVoiceLanguageCustom=600.

### Dialogs (`DialogHooks`, `DialogFlowHooks`, `TextInputDialogHooks`)
- `app.UIFlowDialog.MessageBoxParam` — corrupt-save / autosave-caution / generic message boxes.
  `Message` may be `<PLATMSG Arg0="65">` → `FlowHelper.ResolvePlatformTags` BEFORE `CleanTags`.
- `DialogManager.get_IsShowDialog()` is unreliable (misses UIStartMenu exit confirm; false-positives
  on option sub-lists where `GetEnableLineData` is null). Prefer reading buttons via subtree text.

---

## Character / stage / side / battle setup

### Character select (`CharacterSelectHooks`)
- Hook `SelectedFighterCtrl.SetFighterSetting(int no, uint fid, int costume, int color)` (fires EVERY
  frame for both players — track `lastFighterId` per player). `UIPartsFighterSelectSimple.CursorChange`
  does NOT dispatch. Name via `app.IDScriptExtensions.GetFighterNameText(CHARA_ID)` (byte enum).
  Buffer P1+P2 per frame; format `"{name} Player {no+1}"`.

### Stage select (`StageSelectHooks`)
- Offline: `app.menu.UIFlowStageSelect.Param` — name from field `text0` (via.gui.Text) `get_Message`.
  (`UIFlowStageSelectTitle.Param` is just timer/animation.)
- Rival-AI (BH): `app.UIFlowGenericStageSetting.Param` has no text field — name in GUI
  `GenericStageSetting_BH` element `e_text_stage` (`FindGuiViews("StageSetting")`).
- Stage BGM (Q/E): GUI `StageSelect` element `e_text_bgm`. Param has `partsBgmSelect`
  (`UIPartsBgmSelect`) + `IsBgmSelectDisable`. Poll `e_text_bgm`, announce on change.

### Side select (`SideSelectHooks`)
- `app.UIFlowSideSelect.Param` — two instances (`UserIndex` 0=P1,1=P2). CpuIcon arrays are SHARED.
  Primary indicator: `ArrPadIconCtrl[].PlayState` read from BOTH — `POS_1P`=P1 Human, `DEFAULT`=both
  CPU, `POS_2P`=P2 Human. On `DEFAULT`, announce based on previous state. Hook
  `UIPartsSideSelectPadIcon.Left/Right`. "Human"/"CPU" hardcoded (labels are images).

### Versus rule settings (`BattleSettingsHooks`)
- Runtime type is `app.menu.UIFlowVersusRuleMain.Param` (NOT the decompiled `UIFlowMatchingSetting`).
- `SettingType` enum: Commentator=0, MaxRound=1, TimeCount=2, MatchCount=3, Ready=4, NUM=5.
- `tateList` (via.gui.Text[]) = on-screen VALUES (`get_Message`). `mRuleSettingMessData`
  (SpinText_MessageList[]) — each `.Text` Guid=LABEL, `.TextList`=value options. `spinIndex` (int[]),
  `ArrSettingType` (SettingType[]). Focus items `c_setting_00`… routed from MainMenuHooks. Hooks:
  `EventCursorLeft`/`EventCursorRight` on Main.
- Commentator: `UIFlowCommentatorSelect.Param` — `TitleText` + `SelectState` (0 Commentator,1 Caster).

### CPU panel / CPU Level (`FighterSettingHooks`)
- Human panel `app.UIFlowUI10505.Param` (object_name ui10505); CPU panel = SAME type with
  `mCpuFlag=True` (object_name ui10507). Both have `mPlayerIndex=0` — `mCpuFlag` is the ONLY signal;
  read it LIVE each poll (set a few frames after `mIsActive`). Spin order: 0 Costume,1 Color,
  2 Control/CPU-Level, (3 Preset human-only). `FindActiveParam` prefers the FOCUSED candidate
  (`param.Group.get_IsFocus`).
- **CPU Level is NOT a spin.** UI10505 spin 2 is genuinely Control Type. CPU Level lives in the active
  panel's GUI (object_name "ui10507"): `e_text_title`=localized "CPU Level", the FIRST following
  `e_text`=the number. GUI order: title, level, color, costume. (Do NOT use `UIFlowUI10506.Param
  getLevel()` — returns stale 0.)
- Preset names: `app.UIKeyConfig.Utility.GetBattlePresetName(TEAM.ID, EConfigInputType, Int32)`.
- Costumes: `TableDataManager.GetFighterCostumes(fighterId)` → record (`costumeNo`, `messageId.GUID`
  =name). Colors: `GetFighterCostumeColors(costumeId, isDefault)` — record `name` is internal Japanese
  (unusable); use `InventoryManager.GetName(ItemCategory=6, ManageId)` instead.

---

## Training

### Training menu (`TrainingMenuHooks` — confirmed working)
- Singleton `app.training.TrainingManager` (concrete): `get_IsMenuOpening`, `get_PrimaryIndex` /
  `get_SecondaryIndex`, `get_CurrentMenuData` → `TrainingMenuData` (Guids `_MessageID`,
  `_SubMessageID`, `_GuideMessage`, `_GuideMessageID`).
- **`TrainingManager._tData` is NULL on menus** (populated only during a LIVE fight). Old
  `_tData.ReversalSetting` / `.SkillData` paths are dead on menus.

### Reversal move-selection submenu (`TrainingReversalHooks`)
- Child params: `app.training.UIFlowTrainingMenu_Reversal_{Normal,CommandNormal,Special,SA,Recording,
  Common}.Param` — each has only `BtnYEvent` + `_pGroupScroll` (`UIPartsGroupScroll`, a `UIPartsGroup`
  → `_FocusIndex`/`GetFocusChild`). Read focused move: `_pGroupScroll` → `GetFocusChild` → `Control`
  → GUI `e_txt_name`. Only ONE child flow exists at a time.
- EXCEPTION — Super Art tab uses `_pScrollList` (`UIPartsScrollList`, `get_SelectedIndex` /
  `get_SelectedItem`), not `_pGroupScroll` (`PollMoveList` falls back).
- Main param `app.training.UIFlowTrainingMenu_Reversal.Param`: `TabIndex` (plain int), `Type`
  (`ReversalType`), `TitleDatas` (List<TitleData>, localized tab names), `_pScrollList`
  (`UIPartsTrainingTab`), static `GUID_TEXT_*` tab-name Guids.
- Move strength renders in GUI element `e_txt_0` (L/M/H/OD → Light/Medium/Heavy/Overdrive); SA-tab
  moves have none. Move list GUI `ui11261`; tabs GUI `ui11260`.

### Reversal / Recording / Playback SLOTS (`TrainingMenuHooks`)
- Each slot is its own secondary row (`_SlotID`). `app.training.ItemType`: PLAY_SLOT_ITEM=5,
  RECORD_SLOT_ITEM=6, REVERSAL_ITEM=8. Read focused slot from GUI `ui11200` focused child:
  `e_txt_name`="Slot N", `e_txt_center`=move/"Empty", `e_txt_sub`="On"/"Off" (T),
  `e_txt_east`="Delay: 0F" (R), `e_txt_right` (hidden)="Count: 1".
- Slot number: GUI `e_txt_name` is offset — use the row's `_SlotID + 1` (authoritative).

### Character-specific submenu (`TrainingCharacterSpecificHooks`)
- `app.training.UIFlowTrainingMenu_All.Param` (inherits `UIFlowTrainingMenu.Param` → has
  `_SecondaryList` `UIPartsGroupScroll`). Rows: `e_txt_chara` + `e_txt_name` + `e_txt_0` value.
  L/R reads only changed value; up/down reads full row. Pauses `TrainingMenuHooks` while active.

### R "Delay Settings" spin-list submenu (`TrainingSubListHooks`)
- `app.training.UIFlowTrainingMenu_SpinList.Param` — values in its OWN `_MenuList` (`UIPartsGroup`),
  not the parent's `_SecondaryList`. GUI `ui11231` `e_txt_0` = "0F"/"1F".

### Shortcut settings (`ShortcutSettingHooks`)
- `app.UIFlowShortcutSetting.Param` — `_MenuList` (`UIPartsGroupScroll`→`UIPartsGroup._FocusIndex`);
  `ShortcutData` (`app.ShortcutSettingData[]`), per item `ItemMessage` + `GuideMessage` Guids.

### Display toggles gate (`TrainingFrameDataHooks`, `TrainingAttackDataHooks`)
- Gate on the SETTING, not panel presence (panels stay in scene when off). `_tData.DisplaySetting`
  (`TM_DisplaySetting`): `Is_FrameMeter_View`, `Is_DS_AD_View`. Helper
  `FlowHelper.GetTrainingDisplaySetting()` (mgr._tData → DisplayFunc._tData fallback). Panels:
  `ui11253`=Attack Data, `ui11255`=Frame Meter, `ui11254`=input history, `ui11258`=command.
  Read frame data only while `TrainingManager != null` (replay-leak fix).

### Combo tracking (`Services/ComboTracker.cs`)
- Authoritative: `app.cTeam.mComboCount` (a **short** — read with `ReadShortField`; int-read grabs
  `mComboCountOld` bytes). Stays >0 for the whole combo, returns to 0 at true end/drop = on-screen
  "X HITS". `cTeam.mComboDamage` (int) = accumulated damage.
- Reach a cTeam: `cWork.owner_add`(→cPlayer)→`mpTeam`, or `cPlayer.mpTeam`. Training:
  `TrainingManager.DisplayFunc._gData.PlayerDatas[i].shell`→`owner_add`→`mpTeam`. Lives on one side —
  read BOTH, take max. API: `TeamOf`, `NoteTeams`, `CountOf(team,out damage)`, `IsComboActive`, `Clear`.
- Used as a NON-destructive suppressor over the working HUD-hook + quiet-frames + `PlayerLocalData`
  (comboDamage/prevComboCount) path — do not replace that with cTeam-only (broke damage announcing).
- **Do NOT gate the announcement on `count == hudCount`** (removed 2026-07-06): the training data
  and the HUD counter advance at different times on multi-hit supers, and the equality gate silently
  dropped the whole combo readout on any mismatch (tester: "small hits get missed, damage dropped").
  The `PlayerLocalData` values are the panel's own latched result — announce them once the HUD end is
  confirmed AND `mComboCount` cleared AND the numbers were stable for `END_CONFIRM_FRAMES`; log the
  HUD/data difference instead.
- Other latched sources (unused, candidates): `CommentBattleParamRecorder.ComboFinishChecker`
  (`LastComboCount`/`LastComboDamage`, `OnFinishCombo` edge — commentator system, may be inert when
  commentary is off); `cTeam.mComboCountOld` (short, previous-frame count); `FBattleMediator.AtckInfo`
  (`ComboDamage`/`HitCount`) + `FBattleMediator.GetComboDamage(int teamID)`.

---

## Combo trials & tutorials

### Combo trials (`ComboTrialHooks`)
- Recipe panel `app.esports.UI11439.Param` (NEW instance per trial — compare `GetAddress`). Fields
  (behind `k__BackingField`): `TextTitle`, `PartsScrollListRecipe`, `CurrentProgressNo`,
  `IsFailedProgress`. `CurrentProgressNo`: 0 waiting, climbs per cleared step, **-1 after attempt end
  (success AND fail)**, back to 0 on reset. Row PlayStates via `PartsScrollListRecipe.Control`
  `getChildren` → `get_PlayState`: DEFAULT/CLEARED/CURRENT/FAILED (`ItemPlayState` statics).
- Battle-side judge `app.FBattleMission` (nBattle params): `ResetProgress(cPlayer)` = every real
  attempt boundary (PRIMARY re-read trigger, never mid-combo); `TrialOnAttack(cWork, cPlayer)` = per
  landed hit; `TrialOnActionChange(cPlayer)` = too noisy. Dynamic `AddHook(false)`.
- Fail signals: mid-combo drop → rows FAILED + progress drops (polling). Success → all CLEARED,
  progress -1, then `FGTutorialBattleAnnounceUI` flow starts (success only). Zero-progress/wrong-combo
  fail → NO change anywhere (polling blind).
- `getChildren` walks BOTTOM-TO-TOP and `get_Position` doesn't resolve — REVERSE the tree-order list.

### Command list (`CommandListHooks`)
- `app.UICommandListWindow.CommandListParam`: `mDetailWindow` (`CurrentSkillId` uint + `CurrentSkill`
  `app.FighterSkillUIData`), `mCategoryTabList` (`UIPartsScrollList`) + `CategoryMessageList`.
- `FighterSkillUIData` Guids: `NameMessageId`, `DescriptionMessageId`, `NormalCommandMessage`
  (Classic input), `CasualCommandMessage` (Modern), `CasualManualCommandMessage` (Modern manual),
  `SupplementCommandMessage` (fallback). Statics `GetFighterSkillName/Description(skillId)`.
- **Control type:** `get_IsCasual()` (true=Modern, false=Classic; follows the input-type tab, defaults
  to the player's active control type) and `get_DispCasualManualCommand()` pick which command Guid to
  read (Classic→Normal first; Modern→Casual, or CasualManual when manual toggle on). Re-announce the
  focused move when the input-type tab switches (skill id unchanged).
- Command tag format `<CMD _236 ICON>+<CMDBTN LowP>` — drop CMD/CMDBTN/ICON/BTN; `_236`→236;
  LowP/MidP/HighP/LowK/MidK/HighK→LP/MP/HP/LK/MK/HK.

### Tutorial / list-screen control-type toggle (`TutorialControlTypeHooks`, `TutorialHooks`)
- Three list screens, two mechanisms:
  - `app.esports.UI11410.Param` = **Tutorials** — tabs are images. Enum `TabInputType : byte`
    {Classic=0, Modern=1, Dynamic=2}; field `CurrentSelectTabInputType` (read via `ReadByteField`);
    `PartsSimpleListTabInputType`. Hook `EventInputTypeChanged` + `UpdateInputTypeChanged` (post);
    speak localized name via `GetDisplayLang()` (hardcoded En/Es/Pt, tabs image-based). Keys Z/C or L2/R2.
  - `app.esports.UI11413.Param` = **Character Guides**, `app.esports.UI11414.Param` = **Combo Trials
    list** — both expose `EConfigInputType ConfigInputType`, `via.gui.Text TextControlType`,
    `bool UpdateControlType()`. Hook `UpdateControlType` (post), read `TextControlType` (game text).
  - **Trial clear status** (`ComboTrialListHooks`): the check mark is a texture. UI11414.Param's
    `PartsScrollListItem.SelectedIndex` indexes `CurrentItemDataInfoList` (List<ItemDataInfo>) →
    `BattleFGComboTrialData.UniqueID` (uint) → `SystemSaveManager.Data.ComboTrialSaveData.IsClear(id)`
    (method inherited from `PracticeSaveDataBase`; per-trial records are `PracticeSubData`
    {UniqueID, IsClear, IsNew}). Announced deferred ~12 frames so it queues behind the generic row
    read; wording hardcoded En/Es/Pt (no game text for it).
- `app.EConfigInputType` (sbyte): NOT_SPECIFIED=-1, NORMAL=0 (Classic), CASUAL=1 (Modern),
  SUPER_EASY=2 (Dynamic). `Services/ControlTypeNames.Resolve` prefers
  `app.IDScriptExtensions.DispMessage(EConfigInputType)` (localized), hardcoded table as fallback.
- Tutorial instructor overlays: `app.esports.UI11430.Param` (`_Message00-02`, TextTitle/Detail/Command),
  `UI11434.Param` (banner `_Message00`), `FGTutorialBattleAnnounceUI.Param`. Guide flows
  (UI11430/31/34) end+restart their params constantly — a param DISAPPEARING must count toward the
  "forget" timer (found-but-empty check alone stays silent on a full reset).

---

## Battle info / online

### VS screen & round wins (`BattleInfoHooks`)
- `VSInfoOffline.Param`: `mIsRivalAiBattle`, `mIsOnline`, `PlayerData.mOnlineLP`
  (`app.network.MsgLeaguePoint`), `PlayerData.mInputType`, `PlayerData.mIsPlayer`.
- `MsgLeaguePoint` fields: `character_id` (uint), `league_point` (int), `league_rank` (uint),
  `master_league` (uint), `master_rating` (int), `master_rating_ranking` (int).
- **Rank (data-driven):** `league_rank` IS an `app.AppDefine.LeagueRankWithLevel`. Resolve via
  `Services/LeagueRankResolver` (`GetRecord` uses `app.helper.hGUI.GetLeagueRankWithLevelUserData`;
  `Format(record, tierOnly)` → "Diamond 3" from `leagueRank.messageId.GUID` + `rankLevel`). Reject when
  `IsMaster && master_rating <= 0` (unranked sentinel); non-master → "Tier Level {league_point} LP"
  (LP skipped when ≤ 0 — pre-placement is -1); master → "Tier {master_rating} MR" (tester request:
  the point values are announced alongside the rank). `league_rank=39` is a VALID rank
  ("New Challenger 1"), not a sentinel.
- **Control type:** `PlayerData.mInputType` (`app.EConfigInputType` sbyte; NOT_SPECIFIED=-1 reads as
  byte 255 → reject). Gate on `mIsPlayer` (CPU sides default NORMAL). `Services/ControlTypeNames`.

### Opponent connection type (`BattleInfoHooks`)
- `via.network.core.InterfaceType` (= `app.network.api.Enum.InterfaceType`): 0 Unknown, 1 Wireless
  (WiFi), 2 Wired (cable). Per-player: `app.battle.FighterProfileDesc.get_InterfaceType`.
- VS-screen path: `bCommentatorGlobalInfoHolder` singleton → `get_CurrentBattleDesc()` →
  `BattleDesc.getFighter(teamIdx, fighterIdx)` → `FighterDesc.get_Profile()` → InterfaceType. Fallback
  `app.UIFlowUI10501.FlowParam.FighterDescArray[team].get_Profile()`.
- **"Opponent found" confirm screen:** holder path is NULL. Connection set via
  `app.UIWidget_MatchingSelect.SetSignalStrength(Antenna antenna, InterfaceType)` — hook args[2]=antenna,
  args[3]=interface → "Opponent: WiFi/Cable, signal N of 5" (skip `Antenna.Loading=-1`; Antenna0..5 =
  0-5 bars). Fires EVERY FRAME while shown → use as a liveness watchdog (reset a small frame counter
  each call). `EventDecide()` sets accepted. GUI owners `Resident_Cmn_MatchingSelect` /
  `Resident_Cmn_MatchingStandby`. No readable opponent NAME (only `MatchingManager.OpponentPlatformId`).

### Ranked/Casual match menu (`MatchingSettingHooks`, `MatchingFighterSettingHooks`)
- `app.UIFlowMatchingSetting.Param` — value fields `TextBgm, TextCommentator, TextController, TextSide,
  TextBattleHud, TextSkin`; rank `TextLeaguePoint, TextMasterLeaguePoint, TextCirtifiedCount`; `TabList`
  (`UIPartsSimpleList`). Track `Param.Group` and `MatchingSettingMatching.mGroup` `_FocusIndex`. Skip "---".

### League select (`LeagueSelectHooks`)
- `app.UICFNSelectLeague.FlowParam`: `_ScrollGrid` + `_LeagueItemList` (List<CfnLeagueInfo>) — picks a
  TIER. `app.UICFNSelectLeagueDetail.FlowParam`: `_ScrollList` + `_LeagueList`
  (List<LeagueRankWithLevel>) — picks a level (prefer Detail when both active).
- Enums: `app.AppDefine.LeagueRankWithLevel` (1..42, gap at 38), `app.AppDefine.LeagueRank` (1..14).
  Resolver `hGUI.GetLeagueRankWithLevelUserData` → record (`leagueRank.messageId.GUID`=tier name,
  `rankLevel`, `isMasterLeague`).

### Rank-up (`RankUpHooks`)
- `app.UIFlowRankUp.Param`: `CtrlRankUp` (Control banner — walk for text), `ListData`/`_ListData`
  (IList<LeagueRankWithLevelUserDataRecord>), `IsExam`. `LeagueRankWithLevelUserDataRecord`: `messageId`
  (→GUID full "Gold 3"), `leagueRank` (→messageId→GUID bare), `rankLevel`, `isMasterLeague`,
  `arrivalLeaguePoint`/`arrivalRating`. Arrived rank = LAST ListData record.

---

## Battle Hub

### Generic focus reader — watched prefixes (`GroupFocusHooks`)
Confirmed prefixes exposing list widgets: `app.UIFlowAccessOtherPlayerMenu` (mPartsSimpleList),
`app.UIFlowCabinetMenu`, `app.UIFlowRivalAi`, `app.UIFlowAvatarRandomMatch` (MainList),
`app.UIFlowServerSelect`, `app.UIFlowDailyTournament`, `app.esports.UIFlowResultMenu`,
`app.UIFlowChat`, `app.UIFlowFixedPhraseList` (Param=PartsScrollList),
`app.UIFlowStampList` (Param=PartsScrollGrid — icons, may stay silent),
`app.UIFlowBattleHubPlayerList` (`_ScrollList` + `_ListGroup`/`_RootGroup`),
`app.UIFlowTextList.Param` (generic preset-text picker; `PartsList` + Title/Texts).

### Other-player access / profile (`AccessOtherPlayerProfileHooks`)
- Gated on `app.UIFlowAccessOtherPlayerMenu.FlowParam` in `_Handles`. Reads GUI
  `BattleHubContactPanel_OtherPlayer` `e_text_name` + GUI `CFNFighterProfileSimpleTop`
  (`e_text_fid_name`/`e_text_title`/`e_text_lp_num`/`e_text_mr_num`) once on appear.
  `AccessOtherPlayerMenu.mSimpleProfileData` = `HatoClientAPI.Component.PostFighterProfileLightOut`.
  Radial GUI `BattleHub_BattleHubContactPanel_Common` `e_text_0_d_34` = player name / "Access".

### Post-match info (`BattleHubResultHooks`)
- via.gui.Text fields read once per change: `WinMessage.Param.mText`;
  `RivalAISuggestion.Param.mTextSuggestion`+`mCompleteText`;
  `ResultCounter.Param` `mP1WinCount`/`mP2WinCount` + per-player `PlayerObj.TextRatio`.
- **Rank gauge — DATA-DRIVEN (do NOT poll the animating text):** `app.UIFlowRankGauge.Param`:
  `RankInfoAfter`/`RankInfoBefore` (`Component.CommonMatchingLeaguePoint`) → `league_point` (int),
  `league_rank` (uint → LeagueRankResolver), `master_rating` (int); `IsMove` (bool animating),
  `PlayerIndex`, states GaugeWait/RankChange/HideAndEnd, `TextName`. Read `RankInfoAfter` as data.
- Image-based (skipped): `UIFlowResultTitle`, `UIFlowResultTimer`, `UIFlowResultRivalAi`.

### Chat window (`ChatMenuHooks`)
- `app.UIFlowChat.Menu.Param` (GUI `BattleHubChatMenu`): RootGroup/InputGroup/ButtonsGroup (all
  `UIPartsGroup`). Focus enums: RootGroup {Log=0, InputGroup=1, ButtonsGroup=2}; InputGroup
  {TextInput=0, SendButton=1}; ButtonsGroup {FixedPhraseList=0, StampList=1}. Icon buttons carry no game
  text (hardcoded Message/Send/Phrases/Stickers). Chat log: `LogScrollControlParts`
  (`UIPartsChatLogScrollControl`) + `LogParts` (`UIPartsChatLog`) — needs a dedicated log reader.
- `app.UIFlowChat.SubMenu.Param`: `PartsSimpleList` + `Result` enum; labels are image-based (need
  resolving `SubMenu.Result` values).

---

## Social / chat / emotes

### Chat feed (`SocialChatHooks`)
- Hook `app.ChatManager.addLog(app.network.rpc.MessagingSessionRpc.ChatInfo)` (also `Chat(ChatInfo)`,
  `SetBalloonChat(uint shortId, ChatInfo)`). `ChatInfo`: `Message` (string), `FormatType` (byte),
  `MessageType` (byte), `SourceShortId` (uint), `SourceFighterId`/`SourcePlatformOnlineId`, `UniqueId`.
- `app.network.api.Enum.FormatType`: None=0, Normal=1 (literal), Stamp=2, Template=3 (fixed phrase),
  MessageId=4. Static resolvers on `app.ChatManager`: `StampIdToMessage(uint)`,
  `FixedPhraseIdToMessage(uint,uint)`, `GetPersonalInfo(shortId)` (fighter_id/platform_online_id).
  `UIChat.LogParam` has pre-resolved `SpeakerName`+`Message`+`Self`.
- **KNOWN DEAD END:** `ChatInfo` is an RPC object — fields read EMPTY via both getters and field reads
  (own-send `SendMessage` args also empty). Reading chat text from network objects is impractical; the
  pivot is to read the on-screen text balloon over the avatar via `GuiTextReader` (needs a dump of the
  balloon GUI owner/element while a phrase is visible).

### Emotes / poses (`AvatarEmoteHooks`) — PARKED (unnameable)
- `app.worldtour.avatar.AvatarInputController.SetEmote(uint id)` / `SetEmoteHold(uint id)`. Emotes are
  ExActions (`app.worldtour.WTExActionData.IsEmoteActionId`), id space ~600M with no name link. ALL name
  resolvers fail (`hGUI.GetEmoteName`, `TableDataManager.TryGetEquipEmoteNameMessage`,
  `InventoryManager.Emote.GetName`). Don't retry id resolvers. Loadout (not playback) lives in
  `app.worldtour.WTPlayerDataEmote` (Emote/MasterAction/CheerAction) and
  `UIFlowWTDeviceEmoteShortCut.DeviceEmoteShortCutUserData`. In-match wheel
  `app.esports.FighterEmoteCtrl.SetEmoteState(playerIndex, EmoteState)` carries DIRECTION only.

---

## Arcade / story scenes

### Scenes & subtitles (`ArcadeHooks`, `SpTalkHooks`)
- Story cutscenes: `app.UIFlowComicDemo.Param` + `app.UIFlowComicDemoSubtitle.Param` (subtitle fields
  `NameText`/`DialogueText`, via.gui.Text). Final demo subtitle hook
  `UIFlowDemoSubtibles.Param.SetMessage(Guid,Guid)` (post; new guids stored in `OldName`/`OldDialog`).
  WT intro: `app.worldtour.DemoSubtitles.UIFlowDemoSubtibles.Param`. Commentary subtitle types:
  `UIComicDemoSubtitle` (cutscene), `UICommentatorSubTitle`.
- **Dedup a WT/demo line by its `OldDialog` Guid, not the rendered text** (`FlowHelper.ReadGuidKey`).
  The game re-calls `SetMessage` every frame while a line holds, and the two announce paths read
  different text for the SAME line — the poll reads the GUI `_TextDialog` (full) while the SetMessage
  callback resolved the `OldDialog` Guid (occasionally missing a word). Sharing a text-based `_lastDialog`
  made the two variants ping-pong and repeat forever. `AnnounceSubtitles` now keys both paths on the
  Guid (one announce per line) and prefers the GUI text, falling back to the resolved Guid only when
  `_TextDialog` hasn't refreshed (the final BH-intro line).
- **Subtitles option gate:** `app.Option.ValueType.SubTitleDisplay = 450`, read via
  `app.Option.GetOptionValueOnOff(ValueType)`. `FlowHelper.AreSubtitlesEnabled()` (fails OPEN). Gates
  cutscene dialogue; win quotes are NOT gated.
- **BH NPC "Special Talk":** each line = `app.worldtour.SpTalkSubtitlesData` (`mTextNameGuid` +
  `mTextDialogueGuid`). Advance hook `app.worldtour.SpTalkCtrl.SubtitlesProgress.ChangePage(
  SpTalkSubtitlesData)` (AddPre, args[1]=line; resolve on next LateUpdate). SpTalkCtrl/SpTalkSystem are
  Components, not singletons. BH ambient greetings are voice-only random barks (not routed through SpTalk).
- **WT novel dialogue (`SpTalkNovelHooks`, SingleParamScreenAdapter):** the visual-novel text box shown
  during World Tour gameplay = `app.worldtour.UIFlowSpTalkNovelMain.Param`. NOT covered by SpTalkHooks
  or ArcadeHooks — was silent. IMPORTANT: `Param.setMessage(string)` / `setChoice(...)` **never fire**
  for this path (hooking them is a dead end — confirmed via log). The line lives in the on-screen
  **`MessageWindow`** GUI as `e_text_conversation` (dialogue, FULL string even mid-typewriter) +
  `e_text_name` (speaker); read it with `GuiTextReader.ReadTextsByOwner("MessageWindow")` and dedup on
  the conversation text. This is the primary dialogue UI (always shown), so it is **NOT** gated by the
  cutscene Subtitles option. Branch choices: the active novel item (`UIPartsNovelItem` where
  `canSelect()` is true) exposes `getChoiceIndex()` (focused option) and `ChoiceItems`
  (`UIPartsNovelChoiceItem.Text` labels) — read the list on appearance and the focused option as the
  cursor moves. Speaker-name / choice-label element live in `MessageWindow` (`TextItems[].Text` are the
  novel bubbles; the conversation element is what the reader keys on).
- **Final-boss mid-fight dialogue CANNOT be read** — voice-only, no subtitle widget
  (`app.ArcadeBossBattleVoice`). Pre-fight cutscene dialogue IS read.

### Post-game results (`ArcadeResultHooks`, `BonusResultHooks`, `StaffRollHooks`)
- Stage results `app.UIFlowUI11105.Param`: int fields `mRewardScore`(Score), `mTimeScore`(Time),
  `mLifeScore`(Vitality), `mFinishTypeScore`(Finish), `mRoundScore`(Subtotal), `mTotalScore`(Total).
  `mIsActive`/`mIsFadeIn` stay FALSE — trigger on `mTotalScore != 0` with ~60-frame delay.
- Ending cards `app.UIFlowArcadeEndCard.Param` (ui11108): caption in `text` field (GUI `e_txt_detail`).
- Bonus stage (car crush) `app.UI75520.Param`: values are `UIPartsTextureNumber` (no readable value
  field) — hook `UIPartsTextureNumber.SetTextureNums(int)` into an address→value map, match panel
  `PartsTextureNumberTime/Score/Clear/TotalScore` by `GetAddress()`; ~90-frame settle.
- Credits: `UIStaffroll` `LineDataList` (`StaffRollHooks`).

---

## News / mailbox / rewards / items

### News / mailbox (`NewsHooks`, `TickerHooks`)
- `app.UIFlowMailBox.UIFlowParam`: `MailList` (`UIPartsMailList`), `MailText` (`UIPartsMailText`),
  `Header` (`UIPartsMailHeader`), `TickerList`, `MainGroup`. **MainGroup has TWO columns:** index
  0=headline LIST, 1=body pane.
- `UIPartsMailList`: `GetSelectedMail()` → `app.MailData.Mail` (opened); `ScrollList`
  (`get_SelectedIndex`=cursor); `MailList` (IList<Mail>); `MailCount`. **`Mail.Id` is a plain FIELD**
  (`get_Id` fails). Statics `app.MailData.Util.GetTitleText/GetBodyText(MailText)`.
- `UIPartsMailHeader.GetSelectedTab()` → TabItem {Mail=0 (news), Ticker=1 (history)}. Managed by
  `MultiMenuManager.MailManager`. List-cursor read: `_mailList.ScrollList.get_SelectedIndex` → title at
  index (before reading opened mail). Article view state `app.UIFlowMailBox.UIFlowMailText` (hook
  `OnEnter`). Ticker: `app.UIFlowTicker.UIFlowParam` → `UIPartsTicker.Text` (interrupt:false).

### Reward/item dialogs (`ItemListDialogHooks`, `ItemPreviewHooks`)
- `app.UIFlowItemListDialog.FlowParam` (GUI `ItemListDialog`): `Dialog` (`app.UIPartsItemDialog`),
  `IsReceivable` (True→"Accept", False→"Close"), `ItemList`, `ReturnStatus`, `CallerType`.
  `UIPartsItemDialog`: `FocusMode {ItemList=0, Button=1}`, `GetFocusMode()`, `GetSelectedItem()` →
  `app.UIDataItem.Item` (`ItemCategory`, `ItemId`, `Num`), `ButtonText`, `ScrollList`.
- `app.UIFlowItemPreview.DefaultFlowParam` (GUI `ItemPreview`) / `.BattlePassFlowParam` — `Preview`
  (`UIPartsItemPreview`): `TitleText` + `DescriptionText` + `ButtonText01`; `DisplayMode` {Preview=0,
  Receive=1, RecommendPremiumPass=2, RecommendPassAndTierBoost10=3}. Reward dialog must announce
  `interrupt:true` (else queues behind the article body).
- Item names: `app.InventoryManager.GetName(ItemCategory, uint itemId)` (localized). Colors: category=6
  with `ManageId`.

### Rewards / Battle Pass (`RewardHooks`, `RevivalPassWarningHooks`, `OnlineShopBuyHooks`)
- `app.UIFlowReward.UIFlowParam`: `Header` (`UIPartsRewardHeader`), `BattlePass`, `Challenge`, `Kudos`,
  `MasterPass`, `MainGroup`. Re-read children from `_param` every poll (populate a few frames late).
  `Header.GetSelectedTab()` → `UIFlowReward.Mode` (0 BattlePass,1 Challenge,2 Kudos,3 MasterPass).
- BattlePass (`UIPartsRewardBattlePass`): `GetSelectedItem()` (0 GridItem,1 PremiumPassButton,
  2 TierBoostButton), `GetSelectedReward()` → `BattlePassData.BattlePassTierReward` (`ItemCategory`,
  `ItemId`, `Num`, `Received`, `RewardType` {1 Free,2 Premium}, `CanReceive()`). Name via
  `ResolveItemName`. Re-read on `UIPartsRewardBattlePassTier.GridChanged/ListChanged/GridScrolled`.
- Challenge/Kudos: `ScrollList` → `ReadSelectedItemText`. MasterPass: `GetFocusGrid()` (0 AllGrid,
  1 CharacterGrid). Pattern: `FlowHelper.Call(obj, "GetSelected*")` dispatches on concrete instances.
- Reissue-pass warning `app.UIFlowRevivalPassWarningDialog.FlowParam` → `Param.UIDialog`
  (`GetSelectedReward()`, RewardList; item `UIItemNameText`/`UIItemCountText`/`UISoldoutPanel`);
  `GetFocusMode()` (0 ItemList,1 Button). Shop buy `app.UIFlowOnlineShopGoodsBuy.UIFlowParam` (GUI
  `OnlineShopBuyDialog`: `e_productname`, `e_text_count`, `e_text_total`, `e_coin_num_used`;
  `ChoiceList` `UIPartsSimpleList`).
- In-game store top (`OnlineShopHooks`): `app.UIFlowOnlineShop.UIFlowParam` — `CategoryList`/`GoodsList`
  (UIPartsScrollList), and the balance widgets `TicketText` (UIPartsTicketText) / `FighterCoinText`
  (UIPartsFighterCoinText), both `UIPartsMoneyTextBase` with an authoritative **`GetWalletMoney()`**
  method (the captions are icons). G / Start announces "Drive Tickets N. Fighter Coins N".
- **Product price + currency**: the currency is only an icon. The param's `CurrentGoodsInfo`
  (GoodsInfo, updates with the focused product) has `Prices`/`SalePrices` (+`_IsNowSale`) — lists of
  **`System.Tuple<UIFlowOnlineShop.CurrencyType, System.Int32>`** (log-confirmed; a REFERENCE type:
  read the private `m_Item1`/`m_Item2` fields or the `get_Item1/2` getters — there are no `Item1`
  fields); enum: FIGHTER_COIN_PAID=0, FIGHTER_COIN_FREE=1, TICKET=3 (Drive Tickets). The currency
  side is the one in {0,1,3}. Platform-store products (Steam) have EMPTY wallet prices — no
  announcement. Announced deferred ~12 frames behind the generic row read.

---

## Key config

### KeyConfig (`KeyConfigHooks`, `Services/InputNameResolver.cs`) — confirmed working
- `app.UIFlowKeyConfig.Menu.Param`: `GetFocusLeftGroupItemId()` {Mode=0, Preset=1, NegativeEdge=2,
  LowStickSensitivity=3, List=4}; `GetLeftListItemId()` {Edit=0, Initialize=1, Copy=2, TestInput=3}.
  Spin values `TextSpinMode/TextSpinPreset/TextSpinNegativeEdge/TextSpinLowStickSensitivity`.
  Setting rows `PartsListSetting` (`UIPartsScrollList`) + `GetSettingParam(listIndex)` →
  `UIKeyConfig.SettingParam` (`GetName(app.AppDefine.GameMode)`, `GetInputIcon()`, `GetGamePadButton()`).
- `Menu.Param.TargetDevice` (`app.UIKeyConfig.TargetDevice` {GamePad=0, Keyboard=1}) fixed at Start;
  **R key switches device in place** — re-read via `GetSettingParams(TargetDevice)`. Device by concrete
  type name containing "Keyboard".
- Save/discard popup `app.UIFlowKeyConfig.MessageBox.Param` (`In` Title/Message/Yes/No Guids,
  `PartsList`, MessageBoxIndex {Yes=0, No=1, Cancel=2}).
- Input test `app.UIFlowKeyConfig.InputTest.Param`: game widgets are dead — **poll physical device**:
  `API.GetNativeSingleton("via.hid.GamePad")` → `get_MergedDevice` → `get_Button` (mask 0x1FFFF);
  keyboard `API.GetNativeSingleton("via.hid.Keyboard")` → `get_Device` → `isDown((int)key)`.
  `EConfigInputType` button layouts: NORMAL {lp,mp,hp,-,lk,mk,hk}, CASUAL {di,dp,sp,auto,l,m,h},
  SUPER_EASY {di,dp,sa,od,l,m,h,throw} (enum values are bit indices). Right-stick up/down scrolls the
  assignment overview (emulated bits EmuRdown=0x4000000 / EmuRup=0x1000000).
- `InputNameResolver`: `GamePadButtonName(uint)` (`via.hid.GamePadButton`; 64=RLeft=Square/X),
  `KeyboardKeyName(int)`.

---

## First boot

- Corrupt/new save + autosave caution: `app.UIFlowDialog.MessageBoxParam` (DialogHooks).
- Language select: `app.UIFlowFirstBootOptionSetting.Param` (GUI ui01207, RadioButtonList).
- EULA/terms/privacy: `app.UIFlowFirstBootConsentDialog.Param` (`TitleMessage`/`ContentMessage`/
  `OfflineMessage`; `BootMessageHooks` CheckFlow; fires multiple times).
- Title: `app.menu.UIFlowTitle.Param`.
- Capcom ID: `app.UIFlowFighterAccountCreate.Param` (only `mListTopButton`; explanatory text loads
  seconds later with empty `Message`/MessageId — climb `get_Parent` from `mListTopButton._List`, walk
  with `GuiTextReader.ReadControlTexts(resolveMessageIds:true)`).
- Other boot screens: `app.UIFlowWarningMessage.Param` (`BodyMessage`), `app.UIFlowAttention.Param`
  (no text fields — scrape GUI). Chrome GUI owners filtered from generic scans:
  InputGuide/Resident_Cmn/Ticker/OnlineBannerUI/GameGuideWidget/MessageBox.

---

## Custom rooms

- Top: `app.UIFlowCustomRoomTop.Param` → `FunctionList.SelectedIndex` + `GuideMessage()`. MenuType
  {ConditionSearch=1, Create=2, Invite=3}.
- Join / invitations: `app.UIFlowCustomRoomJoin.Param` — `Tab` (`UIPartsSimpleList`: "Rooms with
  Friends" / "Rooms You've Been Invited To"), `Rooms` (`UIPartsScrollList`). Banner GUI texts:
  `e_txt_name` (room master), `e_txt_code` (ShortId), `e_txt_num` (entrants), two `e_text`
  (comment + rule). `UIPartsScrollList.get_SelectedItem` → `via.gui.SelectItem` (read via
  `ReadSelectedItemText`), NOT a typed banner part.
- Create form: `CustomRoomCreateParam` (ModeGroup/RoomGroup/TeamGroup `UIPartsSpin`; MainGroup) —
  generic reading; descend nested `MenuGroup._Children[0]` by `child._FocusIndex`.

---

## World Tour / Avatar — Status menu

### Master / style name resolution (`FlowHelper.ResolveMaster*`)
Master/style names render as **textures** — every text source yields only
`<WLTAG CmdNo="2" Arg0="2" Arg1="N">` where **Arg1 = master id** (1=Luke). `ResolvePlaceholder` does
not resolve these. Recipes:
- **NAME** — `ResolveMasterFighterName(uint masterId)`: `app.worldtour.WTMasterManager` singleton →
  FIELD `MasterDataMap` (Dictionary<uint, udWTMasterUserData>) → `get_Item(masterId)` → `.FighterId`
  (int CHARA_ID) → `app.IDScriptExtensions.GetFighterNameText((byte)FighterId)` → "Ryu".
  `ResolveStyleFighterName(styleId)` = `WTMasterManager.GetMasterIdFromStyleId` → the above.
- **DESCRIPTION / style flavor** — `ResolveMasterMessage(uint masterId, fieldName)`:
  `app.TableDataManager` singleton → FIELD `MasterProfileUserDataDict`
  (Dictionary<uint, RecordHolder<MasterProfileUserDataRecord>>) → `get_Item(masterId)` → unwrap the
  RecordHolder field whose type contains `MasterProfileUserDataRecord` → message field `.GUID` →
  resolve. Use `StyleNameID` (style flavor+desc) or `DescriptionMessageID` (master desc); NEVER
  `MasterUINameID`/`MasterNameID` (texture WLTAG, empty).
- **DO NOT** call `TableDataManager.TryGetMasterProfileUserData(uint, out Record)` — the out-param
  invoke access-violates.

### Status root & tabs (`StatusMenuHooks`)
- `app.UIStatusMenu.StatusMenuParam`: `TabIndex` + `TabList` (`UIPartsSimpleList`). Tabs (`MenuType`):
  Equip, SpecialMoves, SuperArts, Skill, Master. General widget `PlayerStatusSet`
  (`app.UIPartsPlayerStatusSet`). While `StatusMenuHooks.IsInStatusMenu`, suppress MainMenu generic
  focus + GuideTextHooks InputGuide.

### Equip (gear) tab
- `app.UIStatusMenu_Equip.Param`: `<GroupFocus>k__BackingField` (0 Preset,1 Top,2 Item); `mTopList`
  (`UIPartsSimpleList`, slots), `mItemList` (`UIPartsScrollGrid`), `mPresetList` (`UIPartsScrollList`).
  Focused item name in `mEquipItemLabel._nameText` (grid cells may have no text).
- `TopFocusType` order: Style, Head, Body, BodySub, Leg, Shoes, Acc1-3, MySet, EquipType.
- Style grid: parse Arg1 (masterId) from `EquipStyleList[idx].GetUIName()` WLTAG →
  `ResolveMasterFighterName` → "{name}'s Style" + `ResolveMasterMessage(id,"StyleNameID")`. Style slot
  not entered: `_statusParam.PlayerData.Style.StyleEquipId` → `ResolveStyleFighterName`.
- Gear slots: equipped item from FIELD `EquipParamMap` (Dict keyed by `WTEquipItemSlot`) via fixed
  `FocusToEquipSlot` table `{0,1,2,8,3,4,5,6,7}` → `WTEquipSlotParam.ItemParam.GetNameWithLevel()`;
  "Empty" when `EquipItemId <= 0` (IsEquip unreliable).

### Stats panel & reader (`Services/AvatarStatsReader.cs`)
- On-panel labels: `UIStatusMenu_Equip.Param.mEquipStatus` (`UIPartsPlayerEquipStatus`) → `mLabelList`
  (`StatusLabel[]`), each = `StatusType` (`WTPlayerStatusType`) + `mTextValue`. Perks = `mPerkStatus`
  (`UIPartsBuffListWindow`, names in `e_text_name`).
- `WTPlayerStatusType` (`app.worldtour.WTStatusDefine`): 1 VitalMax(Vitality), 6 PunchPower,
  7 KickPower, 8 ThrowPower, 9 SpecialPower(Unique Attack), 10 Defense; 2-5 gauge levels;
  11-15 skill/accessory slots + skill point.
- **Equipped-total (authoritative):** `WTPlayerData.Status.CalcStatus` →
  `WTPlayerStatusArray.GetValue(WTPlayerStatusType)` (`InvokeBoxed`, enum as int). Do NOT read panel
  text (lags / shows grid preview). **Per-item:** `WTItemParam.GetEquipStatus(false).DataList` (each
  Type+Value; skip Value==0); locate item by NAME match in `equipParam.ItemParamList`
  (`GetNameWithLevel`, lenient contains), not by grid index.

### Unique-moves popup (T on Style slot)
- `UniqueMovesWindow` field (`UIStatusMenu_Equip.UniqueMovesListWidget`); GUI owner
  "StatusMenu_UniqueSkill"; no own flow param. Gate `UniqueMovesWindow.get_IsShow`; track
  `mPartsList.get_SelectedIndex`; read `mPartsDetail.mTextMoveName`/`mTextDescription` + focused row
  `e_text_command` (SpeakableIcons). Toggle: `GetFocusSkill().SkillId` →
  `UniqueMovesListWidget.IsEquip(skillId)`.

### Special Moves / Super Arts tabs (`StatusActionSkillHooks`)
- Both `app.UIStatusMenu_ActionSkillEquipBase`; params `UIStatusMenu_SpecialMoves.Param` /
  `UIStatusMenu_SuperArts.Param` (avatar-training variants
  `UIAvatarTrainingDummyStatusMenu_SpecialMoves.Param` / `_SuperArts.Param`, same fields).
- `eMenuState`: SET_LIST=0 (Move Set slots), CHOICE_LIST=1 (Moves Learned), ATTENTION=2, SORT=3,
  CHARGESKILL_ATTENTION=4, ICON_EXPLANATION=5. Active list `mSkillPanelList_Set` /
  `mSkillPanelList_Select`. Focused row: `e_text_name` + `e_text_command` (SpeakableIcons) +
  `mSkillDetail.mTextDescription`; "Empty" when no name.
- Command fallback `ReadPanelCommand` when `e_text_command` empty (Modern / assigned slots): read
  `mTextCasualCommand` then `mTextCommand` (`UIPartsActionSkillPanel`). Slot triggers also in
  `udWTActionSkillUserData.DefaultTrigger` (`WTFighterActionTrigger.ID`).
- Slot budget (NOT a point/cost system): equipping is capped by a per-category **slot count**, not a
  spendable currency. Special Moves (and its avatar-training variant) show it as GUI count texts
  `mTextCountNow` / `mTextCountMax` + a `FullEquiped` bool field; Super Arts has no count texts — use
  `GetCurrentEquipSlotNum()` + `get_EquipSlotMax` instead. The max = avatar stat `GroundSkillEquipSlot`
  (11) / `AirSkillEquipSlot` (12) / `SASkillEquipSlot` (13), scaling with the avatar (`WTStatusDefine`).
  `ReadEquipCount` reads it (texts first, methods as fallback); announced on entry + on change (after
  equip/unequip) as `slots_count`, plus `slots_full` when full. `WTPlayerDataStyle.SkillPoint` is the
  skill-**tree** learning currency, unrelated to equipping; `WTSkillEquipParam.SkillCost` exists but is
  unused by any equip UI.
- Lock: a move is un-equippable because it isn't unlocked/learned (`WTPlayerDataStyle.IsUnlockSkill`),
  or (supers) fails the SA-level / set-type check — it lands in `CanNotEquipSkillList` and the panel
  shows PanelState=Lock. Read the focused item's "Lock" PlayState (`GuiTextReader.ReadPlayStates`, names
  Lock/Lock_Unfocus/LockS); no per-panel reason string is exposed, but the game's own requirement text
  (when shown) comes through `mSkillDetail.mTextDescription`, already read. Equip-confirm popup: GUI owner StatusMenu_SpecialMoveSetAttention, a
  sub-state of the SAME param (no own handle — DialogHooks misses it). Detect by GUI present
  (`FindGuiViews("Attention")` + visible `e_text_notice`/`e_text_head`). Yes/No = SimpleList
  `p_SimpleList_1_h` `c_item_0`/`c_item_1`; focused carries PlayState "SELECT"
  (`FindSelectedItemIndex(view,"SELECT")`). Param recreated on equip — re-find every poll. Both types in
  `GroupFocusHooks.ExcludedTypes`.

### Move Set assignment (`StatusMySetActionSkillHooks`)
- `app.UIStatusMenu_MySetActionSkill.Param` (GUI StatusMenu_MySetActionSkill): left `mPresetList`
  (empty names render "－－－－"); top-right `mSetTypeTab` (`WTStyleDefine.WTActionSkillSetType`
  {Ground=1, Air=2, SuperArts=3}, current on `SetType`, cycles with Tab); right `mSkillPanelList_Modern`
  (`UIPartsScrollGrid`) / `mSkillPanelList_Classic` (`UIPartsScrollList`) directional slots
  (`e_text_command`: `<ICON 5>+sm`=neutral, `<ICON 6>`=fwd, `<ICON 4>`=back, `<ICON 2>`=down).

### Skills tab (skill tree) (`StatusSkillHooks`)
- `app.UIStatusMenu_Skill.Param`: 5 trees (L/R via `CurrentTreeNo`); `mSkillDetail`; `mOpenSkillList`
  (acquired); `DispCost` (point counter). Focused node = PLAIN TEXT in GUI `StatusMenu_Skill`:
  `e_text_title`, `e_text_detail`, `e_text_cost`, `e_text_category` (first = focused).
- `eMenuState`: MoveToSkillTree=0, SkillTree=1, SkillList=2, ChangeTree=3, ShowConfirm=4, GetAnim=5,
  OpenAnim=6, Complete=7, ResetConfirm=8, ResetSkillCheck=9, ResetResult=10, OverStorageNotice=11.
- Node state: `Param.GetFocusPanel()` → `UIPartsSkillTreePanel.State` (`SkillTreeDef.TreeSkillState`:
  CanOpen=0, Win=1 acquired, Locked=2, ReadyLocked=3, Lose=4). Unlock confirm (F): ShowConfirm(4), GUI
  `StatusMenu_SkillOpenConfirm`. Reset (R): ResetConfirm(8), GUI `StatusMenu_SkillResetConfirm`,
  `CanResetAtCurrentTree`. G key (foreground-gated): `DispCost` → points; money via
  `WTPlayerManager.LocalPlayerData` `get_Wallet`/`get_Money`. Detect dialog entry via `eMenuState`
  change. In `GroupFocusHooks.ExcludedTypes`.

### Master tab
- `app.UIStatusMenu_Master.Param`: `mMasterPanelList` (`UIPartsScrollGrid`); names in HIDDEN
  `e_text_name` (rendered as images) — `ReadSelectedItemText` falls back to hidden texts.

---

## World Tour — field awareness (interactable radar + avatar positions)

Runtime-confirmed in the WT opening tutorial (2026-07-19/20). Code:
`Hooks/WorldTour/FieldAwarenessHooks.cs` + `Services/WorldTour/WorldTourStateService.cs`.

### The two-level model (why one list is not enough)
- **`AvatarManager.CurrentAccessInfoList` is ARM'S-LENGTH ONLY.** Confirmed live: it stays `count=0`
  for an entire walk across the tutorial and only reaches `count=1` while the player is practically
  touching the target (logged `dist=1.54`). It answers "what can I interact with *right now*" — it
  can NEVER guide a player toward a distant NPC.
- **Distant guidance must come from world positions**: `AvatarManager.AvatarList` holds every avatar in
  the field, so `|otherPos − playerPos|` gives a real metric distance for hot/cold navigation.
  Confirmed live: the tutorial list is `count=3` — `[0]` `AvatarPlayer` (the player), `[1]`/`[2]`
  `AvatarNpc`.

### Reading the lists
- `CurrentAccessInfoList` entry chain (verified): `AccessInfo.TargetInfo` →
  `AccessTargetInfo { Target, Type, ShapeIndex, vec3 NearPos, float Distance, float Angle,
  BasePriority }`. `Target.GetDispName()` and `Target.GetContactUIType()` both dispatch correctly per
  concrete subtype — don't cache the Method across instances.
- `CurrentFailedMostNearInfoList` **does not exist at runtime** ("Method not found") — it appears only
  in the decompiled source. `CurrentDefaultAccessInfoList` exists but read `0` throughout.
- `AvatarList` is an `AvatarManager.SafeList<AvatarBase>` wrapper whose `get_Count` is not the standard
  accessor: it reports `0`. Read its inner `System.Collections.Generic.List<AvatarBase>` field instead
  (`FindInnerList` scans the wrapper's fields for a `System...List` type).
- **The WT managers are recreated on scene load.** `GetManagedSingleton` works for them, but a pointer
  cached during the WT loading screen goes dead once the field spawns and every later read silently
  returns null/0. `WorldTourStateService.Singleton()` therefore re-fetches on every call and re-binds
  when `GetAddress()` changes — the stale-param rule applies to singletons too, not just flow params.

### Avatar world position
- `AvatarBase` has **no `DrawObj`** (in the decompiled source that name only exists inside the nested
  per-body-part `WTBodyDisp` struct). Use the avatar's own `Component.get_GameObject()` →
  `get_Transform()` → `get_Position()`.
- Read the components with `FlowHelper.ReadVecComponent` — see the `GetDataBoxed`
  `isContainerValueType` gotcha in `docs/sf6-architecture.md`; getting that flag wrong returns x/y = 0
  and z = adjacent garbage for every avatar, which silently collapses all distances to zero.
- Sanity-guard the result: require every component finite, and treat an EXACT `(0,0,0)` as a failed
  read (nothing stands at the world origin) rather than announcing a bogus "0 metres".
- Per-instance fallbacks if the GameObject ever proves to be shared across avatars:
  `AvatarBase.GetPreFrameTransform(ref vec3)` and `AvatarBase.GetAccessCheckPos(out vec3 pos, out vec3
  dir)` — the latter is the very position the game's own contact system uses to compute
  `AccessTargetInfo.Distance`/`Angle`.

### Naming NPCs (incl. distant ones)
`GetDispName` lives on `AvatarAccessTargetBase`, **not** on `AvatarBase`/`AvatarNpc` (a runtime member
dump of `AvatarNpc` confirms it exposes no name/id/CharaId member at all). But the access-target
component (`WTNpcAccessTarget`, `WTNpcAccessTargetSimple`, `WTOmAccessTarget*`, ...) is a Behavior
**attached to the avatar's own GameObject** — so a DISTANT avatar from `AvatarList` IS nameable: walk
`avatar.get_GameObject().get_Components()` (a native array — `FlowHelper.GetListCount/GetListItem`
handle arrays via `get_Length`/`Get`) and call `GetDispName`/`GetContactUIType` on the component whose
type name contains `AccessTarget` (searcher components also match the substring but return no name —
skip empty results). Fallback name source on the same GameObject: `WTNpcContext` (`NpcName` property,
`NpcID`, `AvatarNpc` back-reference; `RandomNameUpdate` populates crowd names). Global registries also
exist if ever needed: `app.GUIAccessDataManager.AccessTargetList`
(`IDictionary<ContactUIType, IList<AvatarAccessTargetBase>>` + `GetAccessTargetData(type)`) and
`AvatarManager.AccessTargetList` (untyped). For "which NPC is the current objective", use
`AvatarBase.GetCurrentAccessTarget()` (returns the full `AccessTargetInfo`, including `NearPos`) or
`__GetCurrentActionTargetObject()`.

### Kind words
`HudDef.ContactUIType`: `None = -1`, `NPC = 0`, `Legendary = 1` (a Master), `OM = 2` (object/gimmick),
`OtherPlayer = 3`. Note Luke reads as `Legendary` in the opening tutorial, so he is announced as
"master", not "person".

### Clock direction (camera-relative)
`Services/WorldTour/FieldDirectionService.cs`. The announced hour ("person at 2 o'clock, 14 meters",
`wt.at_clock_meters`) is **camera-relative** — the stick steers relative to the camera, so 12 must mean
"push up", not "where the avatar model faces".
- **Camera source: `app.CameraManager`** (global managed singleton, resolved with the same
  stale-rebind `Singleton()` pattern as the WT managers). Forward = `LookAtPosition − CameraPosition`
  projected to XZ — two positions, so no quaternion decomposition and no sign ambiguity; `CameraVec`
  is the fallback. It also exposes `CameraRotation` (Quaternion) but that's unneeded. The WT-specific
  `app.worldtour.WTCameraManager` (`StableRotation`/`CurrentActualRotation`, `IsDuringTransition`) and
  `PlayerCameraManager`/`WTPlayerCameraController` exist but are Behaviors with rotation-only state —
  the position-pair on `app.CameraManager` is simpler and mode-agnostic.
- **Avatar facing** (diagnostic only, to compare frames): avatar GameObject → Transform → `get_AxisZ`
  — `AxisZ` is RE Engine's standard forward-axis idiom (used across the game's own code); there is no
  `get_Forward`. `AvatarBase` itself has no facing accessor (`GetAccessCheckPos(out pos, out dir)`'s
  `dir` is the access-check direction, unverified as facing).
- Hour math: `ahead = d·fwd`, `rightward = dz*fx − dx*fz` (= d·(forward × up)), hour =
  `round(atan2(rightward, ahead)/30°)` mapped 1–12; hour 0 is returned when the frame is unreadable
  (fall back to the plain distance phrase).
- **Calibration CONFIRMED in game (2026-07-20):** forward — target dead ahead reads 12
  (`d=(0.01,5.17)`, `camFwd=(0.06,1.00)`); handedness — **RE Engine's world is right-handed Y-up**,
  i.e. on the XZ plane the rightward basis is `forward × up = (−fz, fx)`. Ground truth: with the
  target at 12, the player rotated the camera RIGHT and the announced hour ROSE (1, 2, ...) under the
  opposite sign (`up × forward`), which is mirrored — rotating right must DROP the hour toward 11.
  The hour also updates live as the camera rotates (each key press recomputes).

### Continuous tracking (hands-free guidance)
`Hooks/WorldTour/FieldTrackingHooks.cs` (shared readers extracted to
`Services/WorldTour/AvatarFieldReader.cs`). Toggle key (provisional: keyboard M, no pad button — Start
is taken by the radar) starts periodic guidance toward the NEAREST avatar: full sentence when the
target changes ("Luke, maestro a las 12, a 5 metros"), terse `wt.clock_short` updates while closing in
("a las 12, a 4 metros"), ~2 s cadence (`ANNOUNCE_TICKS`, same 60 fps LateUpdate tick convention as
the radar poll). Silence rules: identical phrase → silent (standing still); holds while
`SpTalkNovelHooks.DialogueActive` (static flag set in its OnBind/OnExit) or while
`CurrentAccessInfoList` is non-empty (arrival is the target-change reader's moment); auto-off when the
field unloads.

### Diagnostics
Log floats with `CultureInfo.InvariantCulture`: under a Spanish locale the decimal comma collides with
the separators and coordinate logs become unreadable (`pos=(0,0,0,0,48013800000,0)`).

---

## World Tour / Avatar — other

### Avatar training options (`UIWorldTourTrainingMenu`)
- `app.training.UIWorldTourTrainingMenu.Param` (GUI ui44145; separate from normal training):
  `_OptionGroup`, `_TabSimpleList`, `_TrainingSimpleList`, `_TrainingScrollGrid`; `_CurrentTagType`
  (`ETagType` {MAINMENU, SETTINGS}); `MenuData` → Option.Items[] → ItemData {ItemType (`EItemType`
  BUTTON/SPIN/TAB_BUTTON), MessageID, MessageIDs (spin Guids), DescriptionID}. Enabled by adding prefix
  `app.training.UIWorldTourTrainingMenu` to `GroupFocusHooks.WatchPrefixes`.

### Avatar battle settings overlay (`UIFlowAvatarMatchingSetting`)
- `app.UIFlowAvatarMatchingSetting.Param`: rows Control Type / Button Preset / Control Settings; values
  render as text `e_text_operationValue`, `e_text_keyPresetValue` (also fields `TextOperation` /
  `TextPreset`). Watch prefix `app.UIFlowAvatarMatchingSetting`. `GetTrackableFields` SKIPS "Tab"-named
  fields for this type (stale `TabList` phantom tabs) — this would hide its real tabs if ever needed.

### Avatar Arcade Top (`AvatarArcadeTopHooks`)
- Course list `MainList` handled by GroupFocus (prefix `app.UIFlowAvatarArcade`). Mode description:
  GUI `InputGuide` `e_text`, on `MainList` index change. G key (foreground-gated): style name+rank via
  `ResolveStyleFighterName(WTPlayerData.Style.StyleEquipId)` + on-screen `e_text_style`; stats from
  `WTPlayerManager.LocalPlayerData` → `AvatarStatsReader.ReadStatsFromPlayerData`.

### Master-fight pause menu (`WTMPauseHooks`)
- `app.UIFlowWTMPauseMenu.*` — Main.Param (tab bar `_menuTab`, stays on GroupFocus) + one child Param
  per tab, all owned by `WTMPauseHooks` (excluded from GroupFocus; `IsInWTMPause` also suppresses the
  MainMenuHooks focus fallback, which spoke the rows' raw `SA {0}` templates):
  - **Escape.Param**: single-option confirm; GUI `WTMBattlePauseEscape` `e_text_title_tutorial` ×2
    (title + question) + `e_text_0` (Confirm). Announce once on entry. CONFIRMED working.
  - **Item.Param**: `_lineupGrid` (ScrollGrid; cells only carry `e_text_total` counts — the selected
    cell's one is the item's owned count, appended as "xN"). Selected item's name/description from GUI
    `WTMBattlePauseItem` `e_text_name`/`e_text_detail`. CONFIRMED working. The use-item confirm popup
    creates **no flow param** (Item.Param stays active): GUI `UIWidget_ItemConfirmWindow` with
    `e_text_detail` (question "Use Energy Drink S?"), `e_text_name` (effect) and `e_text_value`
    (amount); the GUI view disappears entirely when closed — announce once per appearance. Its Yes/No
    buttons only surface through the generic FocusChanged reader (MainMenuHooks), which is otherwise
    suppressed during WTM pause — `IsItemConfirmOpen` lifts that suppression while the popup is up.
  - **PerkList.Param**: `_scrollList`; rows carry `e_txt_num` (bare "0" — skip) + `e_text_name`.
    Tooltip = GUI `WTMBattlePausePerkList` `e_text_detail`, a WLTAG-composed raw → `ResolveWLTags`.
  - **BattleInfo.Param**: `_mainGroup`, `_enemyInfoList` (List<EnemyInfo>), `_streetEnemyList` (null in
    master fights), `_seriousItemInfoList` (ScrollList, NON-navigable) — announce once on entry.
    `UIPartsScrollList` has NO `_Children` field (verified in the log), so the rows can't be walked from
    the param; read the flat GUI owner `WTMBattlePauseBattleInfo` instead: each row renders
    `e_text_droplock` (keep-condition) directly followed by its `e_text_head` (reward) — pair them in
    tree order, dedupe (the widget duplicates rows). The bare `e_text_value`/`e_text_total` counters
    interleave across rows — don't announce them. Enemy: `e_text_num` (Lv) + `e_text_name`
    (master-name WLTAG → `ResolveWLTags`).
  - **SpecialMoves/SuperArts.Param** (`ActionSkillList` + `ActionSetTypeList` tabs + `ActionSkillDetail`)
    and **OtherMoves.Param** (`mSkillList` + `mCategoryTabList` + `mSkillDetailWindow`): read the move
    name/command from the selected row's control (visibleOnly:false — variant rows keep them hidden;
    skip `{`-containing template texts), and category/damage/description from the **detail widget's**
    control (`e_text_category`/`e_text_value`/`e_text_comment`, hidden included). Do NOT scan the whole
    GUI owner: it mixes other rows' `e_text_value` into the announcement ("Flash Knuckle. 700" with
    Tiger Uppercut's damage). The "Damage" caption is a texture → hardcoded label via `GetDisplayLang`;
    value "0" is a placeholder on utility moves — skip.

### Avatar post-fight result (`AvatarResultHooks`)
- `app.UIFlowAvatarResult.Param`: `_Window_TitleText`, `_Level_Text_LevelTitle/LevelValue`,
  `_Level_PlayerExp` (gauge), reward lists `_Level_/_Skill_/_Item_ScrollList`. GUI `AvatarResult`:
  `e_txt_title`=EXP, `e_text_value` (gauge % + gained), `e_text_title`=Level. Summary announced once
  the texts settle (two equal reads) or on timeout. **Do not poll the reward lists before the summary
  is announced**: the focused reward row contains the animating EXP number and the poller read the
  whole count-up ("22", "27" … "100").
- New-move popup `app.UIFlowDialog.SPMoveGetParam` (auto-appears over the result): `TitleMessage`,
  `Message` (full description), `CommandMessage` (`<CMD _236><ICON +><ICON s>…` → `SpeakableIcons`),
  `SupplementCommandMessage` ("(Hold the button…)"); the style tag ("MAI") is GUI-only
  (`SPMoveGet` `e_text_style`). Title+Message alone sounded half-read — announce all five parts.
- Style-obtained popup `app.UIFlowDialog.EnrollingParam`: `TitleMessage`/`Message` are **null**;
  `MasterId` (uint) is set. GUI `Enrolling`: `e_text_body` is the raw body WITHOUT the name
  ("You've obtained 's Battle Style…" — the game splices the name at render time from
  `e_text_style`, a master WLTAG). Resolve the name (MasterId → `ResolveMasterFighterName`, fallback
  `ResolveWLTags(e_text_style raw)`) and splice/prepend it; the dialog announces once, so RETRY
  (don't latch) while the name is still unresolvable.

### WLTAG resolution (`FlowHelper.ResolveWLTags`)
- Render-time composed texts (perk tooltips, master names) read as raw `<WLTAG CmdNo="2" Arg0="X"
  Arg1="Y">`. Resolve via `app.MessageManager.WLTagCmdRegister` (STATIC field) → the `WLCmdWordList`
  entry → `CmdWordList(Arg0=wordType, Arg1=messageId)` returns the localized string. Word type 2 =
  master names (textures, exchange returns empty) → fall back to `ResolveMasterFighterName(Arg1)`.
- **OPEN BUG — word type 1005 (perk numeric values):** `CmdWordList(1005, …)` returns a
  garbage/pointer-looking number instead of the perk's value, e.g. "High Voltage. Active when
  vitality is 705723773660r above" in the WTM pause perk tooltip. Word type 1005 needs its own
  resolver (source of the real value unknown yet).

### Shop (`ShopHooks`) — `app.UIFlowShop.*`
- **WTTopMenu.Param**: `List` (UIPartsSimpleList) — the Buy/Sell/Enhance/Dye menu; stays in the
  handles while an item list is open (item list wins).
- **BuyItemList.ParamGeneral / BuyItemList.ParamApparel / SellItemList.Param** all inherit
  `app.UIFlowShop.ItemListBaseParam` (fields in `sf6 code/.../UIFlowShop.cs`): `_categoryTab`
  (UIPartsScrollList; current category mirrored in the `_categoryText` gui text), `_itemGrid` +
  `_itemGrid_PickUp` (UIPartsScrollGrid — apparel's pick-up section uses the second one),
  `_itemDetail`, `_itemEffectList`, `ProductList` (List<WTShopProduct>).
- **Selected item name/description: try the param's `_itemDetail` widget's control first** (hidden
  texts included — "Toggle Item Detail Display" hides the panel) and the effect pair from
  `_itemEffectList` (`e_text_value` precedes its `e_text_name`). Its control/element layout is
  UNVERIFIED on some lists — trusting it alone MUTED the whole shop, so when it yields no name fall
  back to the flat `ShopItemList` owner scan, SKIPPING the `_playerEquipStatus` compare panel's stat
  labels: those are the `e_text_name` entries directly preceded by an `e_text_current` value
  ("Defense" got announced as the item name in the gear lists). Grid cells carry only numbers:
  `e_text_price` (announce with a localized "Price" label — the zenny caption is an icon),
  `e_text_num` (NOT the owned count: it's 0 even on sellable items — don't announce),
  `e_text_equip_value`. `e_text_stateName` = buy/sell mode line ("Get - Takeout" / "Sell - All"),
  announced on toggle.
- **Hub goods shop** (Battle Hub gear store, reached from the in-game store): same family —
  `app.UIFlowShop.BuyItemList.ParamOnline` (inherits ParamApparel) + `OnlineMain.Param`, with its
  OWN GUI owner **`ShopOnlineItemList`** and an extra grid `_itemGridView`
  (UIPartsOnlineShopViewScrollGrid) — try `_itemGrid` → `_itemGrid_PickUp` → `_itemGridView`.
  Cells carry TWO `e_text_price` (tickets + zenny) and `e_text_shop_value` (stock).
  **Grid polling caveat**: a list param can host SEVERAL live grids (normal / pick-up / hub "Group
  View") and the inactive ones keep a stale `SelectedIndex` — poll all of them and announce for the
  one that CHANGED ("first grid with an index" froze on the hub's normal grid: only the entry item
  ever announced). The hub view mode also has its own `_categoryTabView`/`_categoryTextView` pair.
- **Gear stats**: buy lists render an item-vs-equipped compare block as STRICT triplets in tree
  order — `e_text_value` (gear's value) directly followed by `e_text_current` (equipped) then
  `e_text_name` (LOCALIZED label) → "Defense 5". Adjacency is REQUIRED: loose value/name pairing
  read the player-status panel instead and announced the avatar's totals ("Defense 377") on every
  item. Enhance lists: the focused gear's stats live in the side panes' `UIPartsPlayerEquipStatus`
  (`StrengthTarget.Param._targetInfo` for the target list, `StrengthResult.Param._materialInfo` for
  the material list) — captions are textures, so read `mLabelList` (StatusLabel = `StatusType` +
  `mTextValue`) via `AvatarStatsReader.ReadStatsFromEquipStatusWidget`. Last resort for buy lists:
  `ProductList` → match `_productName` → `_itemParamList[0]` → `ReadStatsOfItem`
  (WTItemParam.GetEquipStatus, non-zero only). Do NOT use `WTShopProduct.TryGetItemParam`
  (out-param — AV risk).
- **Buy/sell confirm popup**: its OWN flow param under prefix `app.UIFlowShop.DialogUI.` —
  `SellDialog.SellParam_Single/_Mul`, `BuyDialog.BuyParam_Single/_Mul/_Online`. `ParamSingle` (decompiled)
  has `Spin` (UIPartsSpin quantity), `_total` (via.gui.Text), `Group` (UIPartsGroup — the
  Spin/Decide/Return rows, focus read via `GroupFocusPoller`); `ParamMul` has `ChoiceList` instead.
  Shared GUI owner `ShopBuyPopup`: `e_text_title` ("Sell"/"Buy"), first `e_text_num` = quantity,
  first `e_text_total` = the already-labeled "Total:  N" (the SECOND e_text_total is the player's
  money — don't read it).
- **StrengthTargetList.Param** (enhance), **StrengthMaterialList.Param** (the material list after
  picking a gear piece; state "Materials - All") and **ColorStainingList.Param** (dye) also inherit
  `ItemListBaseParam` — handled by the same item-list poll (their GUI is the same `ShopItemList`;
  state lines "Enhance - All" / "Color - Gear"). **StrengthTarget.Param** / **StrengthResult.Param**
  (the target/result side panes; GUIs `ShopStrengthTarget`/`ShopStrengthResult` — bare stat values
  with texture labels) are not announced.
- **Screen arbitration**: backed-out shop screens LINGER in `_Handles` (`RestoreFlow`) — a fixed
  priority goes stale (the enhance list kept owning the screen after backing out to the top menu,
  which then read nothing). Use `FlowHelper.FindFlowParamsOrdered` (handle order, index 0 = newest):
  the first watched type wins; also reset the reader cursors when the active param's ADDRESS changes
  (re-entering can rebuild the param on the same index).
- **Zenny readout**: read the money from the on-screen GUI (first `e_text_total` of `ShopBg`, or of
  `ui50201` in the device item app), NOT from `Wallet.get_Money` while a button is being processed:
  when the readout shortcut doubled as a game action (R3), the getter access-violated
  (log-confirmed c0000005) and returned 0. Wallet getter kept only as fallback.
- **ColorStainingDetail.Param** (dye detail window): persists while browsing the gear list — gate on
  its `IsShow` bool (byte read). `_scrollList` (UIPartsScrollList) rows = gear variants + the dyes
  each needs (read via `ReadSelectedItemText`); `_priceText` (via.gui.Text) = the dye cost
  (announced with the localized Price label on open); `_changeRate` = the raw price uint.
- **G / Start currency shortcut** (`ReadoutShortcut`, per-frame poll): announces the current
  Zenny (GUI-first, see above; `CurrencyReader`) anywhere in the shop and in the device item app.
  Pad button = Start/Options (0x8000) — R3/L3, Triangle/Y AND Square/X are all game actions in these
  menus (R3 also triggered the wallet AV above; Square is the gear action).

### Emulator pause, gallery, profile, tips
- Emulator pause: `app.UIFlowEmulatorPauseMenu.Param` (only `outSelectedIndex`);
  `UIFlowEmulatorPauseMenu.Start(int romId)` static. Trial/tutorial pause:
  `app.esports.UIFlowESportsPauseMenu.Param` (group field `PauseMenuList`).
- Gallery: `app.gallery.UIFlowGallery.Top.Param` (`_scrollGrid`), Main.Param, IllustTop.ScrollTabParam;
  titles in `c_text_detail` under Gallery*ScrollTab GUIs.
- Profile: `app.UICFNFightersProfileTop.FlowParam` (banner only); tabs `app.UICFNFightersProfileTab*`.
- Tips: `app.UITipsMenu.Param`. Item tooltips: GUI `InputGuide` `e_text`. Rotating hints
  `GameGuideWidget`.

### Device (in-game smartphone) — `app.UIFlowUI50xxx`
- **UI50000.Param** = device desktop/top (`_partsGridDesctopMainApp` app grid, `_commonInfo`,
  World Tour/Battle Hub info parts). **UI50010.Param** = the phone 3D-mesh boot/render flow
  (no UI to read). **UI50201.Param** = the item app ("View consumable and sellable items"),
  read by `DeviceItemAppHooks`: `PartsSimpleListTabMenu` (category tabs), `PartsScrollGridItem`
  (grid; cells only carry counts), `PartsItemDetail`; selected item name/description in GUI
  `ui50201` (`e_text_name`/`e_text_detail`, same shape as the WTM pause Item tab — shared
  `ItemGridReader`/`ItemConfirmWatcher` in `Services/Ui/ItemUiReaders.cs`).

## World Tour — Avatar creation (character creator)

> Full offline sweep of the decompiled UI61xxx family 2026-07-07 (5 parallel code sweeps);
> implemented in `Hooks/AvatarCreate/` (AvatarCreateHooks + AvatarChildFlowReader +
> AvatarColorWatcher) + `Services/ColorNamer.cs`. **Derived from decompiled code only — every
> member below still needs a runtime-dump pass (F11) before being treated as confirmed.**

### Flow structure
- Main param: `app.worldtour.UIFlowUI61000.Param` (matched by fragment, namespace varies).
  Child flows stack on top of it in `_Handles` (index 0 = newest/topmost); every sub-menu is
  its own `app.worldtour.UIFlowUI61xxx.Param`, and the HLS color picker is
  `app.worldtour.UIFlowWTAvatarCreateColorPopUp.Param`. Skip `IsEnd` handles or a closed
  sub-menu keeps being read.
- Main categories (`MainCategoryType`): TYPE, PRESET, BODY, FACE, BODY_PAINT, FACE_PAINT,
  COLOR, VOICE, RECIPE. **Localized names**: `Param.MainCategoryNameMessageId` (Guid array,
  index = category) → resolve via message system. `CurrentMainCategory` /
  `CurrentMiddleCategory` are plain fields. Middle-category row text is read from
  `PartsScrollListMiddleItem` (paint tabs use `PartsScrollList[Body|Face]PaintMiddleItem`
  + `PartsSimpleList[Body|Face]PaintMiddleItem`); each middle flow also carries
  `UIGroupBase.CategoryMessageId` + `ItemMessageTbl` (Guid array) if the row text fails.
- Child flow map: 61100 body type (MAN/WOMAN), 61101 gender identity, 61200 face preset,
  61201 random (3 buttons), 61202 face blend (two source presets `CurrentPage00/Index00` +
  `01`, ratio `BlendSliderParts`), 61203 body preset + figure TriangleBar; 61300 height
  (`HeightBuffer`/`SittingHeightBuffer`... floats), 61301 randomize, 61303/61304 upper/lower
  proportions (+TriangleBar), 61305 build TriangleBar, 61306 skin color, 61307 body hair
  (per-position `BodyHairPartsList`), 61308 body-hair color; 61400 face shape, 61401 hair,
  61402 eyes, 61403 pupils (two grids R/L), 61404 eyelashes (+up/down `UIPartsSpin` +
  `*TextList`), 61405 eyebrows (two grids), 61406 nose, 61407 mouth, 61408 ears (+color grid),
  61409 beard, 61410 skin age, 61411 expression, 61412 skin definition; paints (grid + scale
  sliders + `UIPartsPositionGrid` + `LocationNumber`) — **screen↔flow pairing log-confirmed
  2026-07-07: 61500 body paint, 61501 face makeup, 61502 body mole, 61503 body scar, 61505 face
  fixed paint** (61506/61507/61508 = face free paint / face scar / face mole, pending); 61700 voice
  (`PartsScrollListVoice`, ids only — reads perfectly, user-confirmed 2026-07-07); 61801
  recipe save/load, 61802 download, 61803 detail,
  61805 upload (player-named strings).

### Reading items
- Preset grids are `app.UIPartsAvatarCreatePresetScrollGrid`: focused cell =
  `PartsWorker` (UIPartsScrollGrid) → `get_SelectedIndex`/`get_ItemMax`; page `CurrentPageNum`;
  committed cell `_CheckedSelectIndex`/`_CheckedPageIndex`; per-part indices on
  `CurrentSelectPresetData` (HairIndex, BrowIndex, EyeR/LIndex...).
  **CONFIRMED in-game 2026-07-07: `PresetDataMessageInfo` is DEBUG info, not a display name**
  ("CharacterCreateEditParam_Man_01 (0, 0)" = asset file + column/row) — preset cells are
  unnamed thumbnails; screenshots confirm the game itself shows only a NUMBER badge per cell
  (`e_text_num`, running ACROSS pages: page 2 shows 7-12). The reader speaks the focused cell's
  on-screen text: the number alone when numeric, a label + position when textual (gender
  identity cells carry a real label — user-confirmed reading), and appends "selected" when the
  focused cell is the applied one (`_CheckedSelectIndex`/`_CheckedPageIndex`).
- **UI-part members on these params are auto-property backing fields** (`<X>k__BackingField`,
  confirmed by dump) — always resolve via the FlowHelper helpers (they try both forms) and
  normalize the name for labels/log tags (`avslider.*` keys use the CLEAN name).
- Color swatch grids (`PartsScrollGridColorPreset`, plain `UIPartsScrollGrid`) are
  **index-only thumbnails** — no name/color on the cell. Palettes live in
  `AvatarCreateData.ColorPresetBody/Default32/BodyAdd00/01` (`ColorPalletPreset`:
  `ColorRGB[]` + `ColorHLS[]`) if per-swatch color is ever needed.
- Sliders: `PartsSliderAry` (values via `getValue()`); 61300 mirrors values into `*Buffer`
  floats (slider names). Individual sliders are their field names (ColorRoughness etc.).
  TriangleBar cursor = `_CurrentPos` (vec2 struct read).

### Colors — the real model (`AvatarColorWatcher`)
- **All applied colors live in `Param.MyEditPresetParam` (`AvatarCreateEditParamData`,
  computed getter) → `.EditParam` (`app.CharaEdit.charaEditParam`, plain field) as raw
  `via.Color` RGBA structs** (uint `rgba`, r = low byte; read via ValueType + Marshal —
  `FlowHelper.ReadColorField`). **CONFIRMED in-game 2026-07-07** for the direct fields:
  `FaceColor` (skin — e.g. #FF3E3F4D = a dark skin tone), `PaintColor`, `HairColor`/`2/3/4`,
  `Chest/Back/Arm/LegHairColor`. The nested owners `eye_r/eye_l` (`iris_col`, `sclera_col`),
  `brows` (`colL/colR`), `lash` (`colorup/colordown`) are **INLINE STRUCTS** (they box to
  ValueType, not ManagedObject) — read via `FlowHelper.ReadColorFieldIn(ownerField.Type,
  vt.GetAddress(), isContainerValueType:true, colorField)` (`AvatarColorWatcher.ReadEntryColor`;
  fix UNTESTED). The model applies grid/slider edits live, so watching these fields gives real
  color feedback with no scale guessing.
- The color popup (`UIFlowWTAvatarCreateColorPopUp.Param`) has sliders `ColorHueSlide`,
  `ColorSaturationSlide`, `ColorLightnessSlide`, `ColorRoughness/Metallic/Emissive/Blend/
  TransparencySlide`, a swatch grid `ColorGridParts`, current color `InitColorData`
  (`app.LightEditData.HLSColor`: ushort `ColorHue`, byte `ColorSaturation`/`ColorLightness`),
  and slot identity `HairColorType` (`AvatarCreateColorSetType`, 30 slots: Hair_00.., Beard,
  BodyHair_00-04, Pupil/EyeBall_00-02, EyeLash_00/01, EyeBrows_00-02, BodyPaint_00-03,
  FacePaint_00-06). Per-slot HLS store: `AvatarCreateEditParamData.UIColorData[]`.
- **The game has NO color-name table** — `Services/ColorNamer.cs` maps RGB→HSL→a small
  localized vocabulary (`color.*` LangFile keys, "rojo oscuro"/"dark red"), documented
  hardcoding (last resort).
- Other useful data: `charaEditParam.gender`/`genderIdentity`/`voiceId`, `BodyHeight`,
  `Facial_*` floats, `SkinAge*`; edit limits per category via
  `AvatarCreateConfigDataBank.GetConfigData(main, middle)` → `AvatarCreateEditParamLimit
  .GetMaxParam/GetMinParam(index)`. Height slider→cm stays a community lookup table
  (on-screen cm is a texture).

### Implementation notes
- `AvatarChildFlowReader` discovers the child param's parts by field TYPE at bind
  (preset grids / swatch grids / sliders+arrays / spins / triangle bars) and hands
  `UIPartsGroup`/scroll/simple lists to a dynamically-built `GroupFocusPoller`. Every bind
  logs the discovered parts (`Avatar child <type>: ...`) — diagnose silent screens from the log.
- F11 = avatar dump (works outside the screen too): worldtour handles, main param key fields,
  charaEditParam colors (hex + spoken name), full child-flow field dump.

### Post-rebuild findings (rounds 3–15, mostly user-confirmed)
- **Preset-grid `SelectedIndex` order is PER-GRID inconsistent** (row-major on some catalogs,
  column-major on others) — no fixed remap works. Number cells from
  `CurrentSelectPresetData.Column/Row` (the cell's explicit visual position) instead.
- **Page-flip debounce is required**: `CurrentPageNum` and `SelectedIndex` update on DIFFERENT
  frames when flipping pages — announce only once the `(page, index)` pair reads stable across two
  consecutive polls, or announcements come out duplicated/mixed.
- Swatch palettes are matched to a grid by `ColorRGB[]` length == the grid's `ItemMax`; some skin
  grids may need composing the Body + BodyAdd palettes.
- `AvatarCreateHooks.IsInAvatarCreator` must stay EXCLUDED from the generic
  `FocusValueHooks`/MainMenuHooks fallback — otherwise stale pooled cell text double-speaks over
  the dedicated reader.
- **Preset description catalog is COMPLETE**: 603 `avdesc.*` LangFile entries (es+en; other
  languages fall back to English). Catalogs are keyed by **body type, not gender**, and shared
  across body types for face/hair/eyes/etc. — only body, ears, expression and premade-avatar
  catalogs are body-type-specific.
- Known gaps for the pending in-game pass: triangle-bar wording, voice list (numbers only, no
  names), color-popup HLS slider labels.
