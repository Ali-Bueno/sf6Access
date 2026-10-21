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

**Implementation update 2026-10-01 (pending runtime verification):** nearby NPC selections now copy
the live component address, owning GameObject address, city, name and contact kind into
`DestinationActor`; no wrapper survives a poll. `LiveDestinationReader` re-finds the exact actor in
the current avatar list or `GUIAccessDataManager.GetAccessTargetData`, and reads its current position.
Unreadable/truncated observations are pending, not absence. Confirmed loss/identity mismatch requires
reselection; a name match alone never attaches the selection to another person. Current interaction
uses `CurrentAccessInfoList.TargetInfo.Target`, matched to the exact registry target or owning
GameObject. These combine existing readers; their behavior together still needs an in-game pass.
Automatic speech and scans now follow [the shared feedback policy](wt-soundscape.md); historical
reader behavior described below may have been superseded.

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
`AvatarManager.AccessTargetList` (untyped). For the current interaction target, investigate
`AvatarBase.GetCurrentAccessTarget()` (returns `AccessTargetInfo`, including `NearPos`) or
`__GetCurrentActionTargetObject()`. These do not establish the selected mission objective;
match target identity against the mission guide before using them to confirm objective arrival.

### Kind words
`HudDef.ContactUIType`: `None = -1`, `NPC = 0`, `Legendary = 1` (a Master), `OM = 2` (object/gimmick),
`OtherPlayer = 3`. Note Luke reads as `Legendary` in the opening tutorial, so he is announced as
"master", not "person".

### Notable vs. crowd (`Services/WorldTour/AvatarNameCache.cs`, 2026-09-07)
**A name is not a filter in World Tour.** Session log 2026-09-07 11:55–12:00: the street crowd is
NAMED and interactable too ("Kenneth, persona", "Susana, persona", every passer-by), so a hands-free
reader that speaks "anyone with a name" narrates the crowd — the tracker followed a new pedestrian
every few steps and read out a census. What actually separates the people worth a sentence is the
game's own contact KIND: `HudDef.ContactUIType != CONTACT_NPC` (a master, another player, or an
NPC-context-only name) is **notable**; plain `NPC` is the street. The homing pulse (audio only) still
follows the literal nearest person, crowd included — a sound toward a passer-by is fine, a sentence
about one is not.

`AvatarNameCache` caches `(name, kind)` per avatar ADDRESS (`AvatarFieldReader.Classify`, `Classify`'s
`(Name, Kind)` tuple), forgetting an address not seen for `FORGET_MS` = 30 s, so the notable/crowd
split costs a dictionary lookup instead of a component walk per avatar per poll — neither a person's
name nor their kind changes for their lifetime. `AvatarNameCache.NameOf` gives the same "{name},
{kind}" string `AvatarFieldReader.DescribeAvatar` did; `IsNotable`/`Notable(list)` are the filter the
tracker's voice applies before speaking (the look-sweep deliberately does NOT filter — see § 10). `AvatarFieldReader.CONTACT_NONE` (name from the
`WTNpcContext` fallback, or no name at all) is never notable.

### Clock direction (camera-relative)
`Services/WorldTour/FieldDirectionService.cs`. The announced hour ("person at 2 o'clock, 14 meters",
`wt.at_clock_meters`) is **camera-relative** — the stick steers relative to the camera, so 12 must mean
"push up", not "where the avatar model faces".
- **Camera source: `app.CameraManager`** (global managed singleton, resolved with the same
  stale-rebind `Singleton()` pattern as the WT managers). Forward = `LookAtPosition − CameraPosition`
  projected to XZ — two positions, so no quaternion decomposition and no sign ambiguity; `CameraVec`
  is the fallback. It also exposes `CameraRotation` (Quaternion) but that's unneeded. **Confirmed
  unavailable on this build (2026-09-07):** `CameraPosition`/`LookAtPosition` logged "Member not found"
  on every read — 2563 lines in one session, once the tracker/sweep/radar readers were all polling at
  10 Hz. `FieldDirectionService.GetCameraForward()` now remembers the miss after the first attempt
  (`_positionPairMissing`) and goes straight to `CameraVec` for the rest of the session, logging the
  fallback exactly once ("Camera forward: CameraPosition/LookAtPosition unavailable, using CameraVec")
  instead of retrying the dead pair every poll. The WT-specific
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
`Services/WorldTour/AvatarFieldReader.cs`; sticky-target logic extracted to
`Services/WorldTour/StickyTarget.cs`, also used by `FieldAimHooks`). Toggle key (provisional: keyboard
M, no pad button — Start is taken by the radar) starts guidance toward the NEAREST NOTABLE person: full
sentence when the target changes ("Luke, maestro a las 12, a 5 metros"), terse `wt.clock_short`
updates while closing in ("a las 12, a 4 metros").

**Notable people only (2026-09-07) — `Target => StickyTarget.NearestNamed`.** The tracker used to
follow the literal nearest avatar (`StickyTarget.NearestPerson`); in World Tour's named, interactable
crowd (see § Notable vs. crowd above) that meant a sentence per passer-by, each one passing at arm's
length so its clock hour swept half the dial in a second. `AvatarNameCache.Notable(...)` filters
`ReadOthers` down to masters/other players/NPC-context names before the sticky-pick, and the tracker
returns silently (no reading, no "nobody nearby" spam) when that filtered list is empty. The homing
pulse keeps following the raw nearest person, crowd included, so there is still a sound to walk toward
even when nobody notable is close.

**Cadence: events plus a distance-paced repeat (reworked 2026-09-05, then 2026-09-06 "verbalise more
often, like the beacon", then 2026-09-07 for the slower notable-only pace) — `PENDING RUNTIME
VERIFICATION`.** A terse update while walking to the SAME target fires when the clock hour changes or
the rounded distance crosses a coarse band — drops below half, or rises above double, the last
ANNOUNCED distance (`FieldTrackingHooks.CrossedBand`, reference floored at `MissionBeaconHooks.ARRIVED_M`
= 4 m rather than a new literal) — and now ALSO on a distance-paced repeat when nothing changed, so a
long straight approach is never silent for too long. The poll itself runs at `POLL_TICKS` = 30
LateUpdate ticks (0.5 s at 60 fps) — cheap enough to check every half second, while whether an update
is actually SPOKEN is still decided by the hour/band/repeat logic below, never by the poll alone.
- **Repeat period (`RepeatPeriod`), slowed 2026-09-07:** linear between `REPEAT_NEAR_MS` = 5000 ms
  (was 3000) at `MissionBeaconHooks.ARRIVED_M` (4 m, the interaction radius) and `REPEAT_FAR_MS` =
  10000 ms (was 8000) at `FieldBeaconHooks.HOME_RANGE_M` (25 m, the edge of the homing range) and
  beyond — the same ramp shape the NPC homing pulse already plays, so the voice and the sound agree
  about urgency. Now that only notable people repeat at all, the faster crowd-era cadence read as
  nagging for a master or a player standing still nearby.
- **Hour changes silenced up close (`CLOSE_M` = 2 × `MissionBeaconHooks.ARRIVED_M` = 8 m, new
  2026-09-07):** `hourMoved` requires `nearest.Dist > CLOSE_M`. Session log 2026-09-07: hour churn
  ("a las 4, a las 5, a las 8") while pedestrians passed at 1–3 m — that close, a sideways step is a
  full clock hour, and the homing pulse's pan already says which side. The distance-band and repeat
  events are unaffected, so a notable target that close still updates, just not on every hour wobble.
- A REPEAT is allowed to say the identical phrase again (`periodic` bypasses the "same as last spoken"
  dedupe) — that is its job; an hour/band EVENT that lands on the same phrase as last time is still
  swallowed as not-news.
- Silence rules otherwise unchanged: standing still is silent (`FieldPresenceService.CanSpeakWhileMoving`);
  holds while `SpTalkNovelHooks.DialogueActive`, while the panel guide (`PadGuideHooks.Active`) is
  running, while `GetAccessInfoCount(mgr) > 0` (arrival is the target-change reader's moment), and for
  `READER_HOLD_MS` = 1200 ms after any interrupting announcement from the reader — so a repeat never
  lands on top of a tutorial line or an arrival announcement; auto-off when the field unloads.

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

## World Tour phone: Messages and Missions

Captured with F8 on 2026-08-14 (`sf6access_autodump_135446.txt`) and implemented as four adapters
under `Hooks/WorldTour/`. **None of this family has an `IsActive` field** — screen presence is read
off `_Handles`, newest-first, as everywhere else.

### Messages — contact/thread list
`app.worldtour.UIFlowWTDeviceIM.DeviceIMParam` → `DeviceIMHooks`

- Contacts: `HolderPartsArray` (`app.UIPartsFaceIconItem[]`), each with `ItemText` (a field-backed
  `via.gui.Text` — read `get_Message`), `ItemCtrl` (selection state), `ListIndex`.
  Selected contact: `SelectedHolderID` (**uint**, sentinel `uint.MaxValue` = none — the same
  not-set convention as the sound system's language ids). Tab: `SelectedHolderTab`
  (`eHolderCategory {All, Master, Other}`).
- Threads for that contact: `SubjectPartsArray` (`app.UIPartsIMSubjectItem[]`), same `ItemText`
  shape, plus `SubjectIDProp` and `eStatePattern {Default, New, Reply, NewReply}` (the
  unread/reply badge). Selected: `SelectedSubjectIndex` (int).
- **`HolderListParts` / `SubjectListParts` are `UIPartsScrollList` and have NO `_Children`** — walk
  the parts arrays instead. Same trap as the other scroll lists in this doc.

### Messages — reading a thread
`app.worldtour.UIFlowIMContentScreen.IMContentFlowParam` → `IMContentHooks`

- **The body text is not on the param.** `IMDataList` holds `WTIMData` records whose content is
  asset/script-backed, with no plain string to read. Take it from the GUI owner
  **`IMContentScreen`**: `e_text_name` (sender) + `e_text_message` (body) — the same route the mod
  already uses for `MessageWindow`.
- State: `IsProcessedFinish`, `IsFinishUIFlowInput`, `IsRequestedInputWait`; reply choices in
  `ChoiceNum` / `ChoiceMessageIDs`.
- A passcode gate (`app.worldtour.UIFlowIMPasscodeScreen.IMPasscodeFlowParam`, GUI
  `IMPasscodeScreen`) can precede it the first time. Not yet handled.

### Missions — list
`app.UIFlowUI50600.Param` → `MissionListHooks`. **Namespace is `app`, not `app.worldtour`.**

- Highlighted mission: `get_CurrentSelectMissionInfo` → `WTMissionDeviceInfo`, whose accessors are
  **methods, not properties**: `GetTitleMessage()`, `GetDetailMessage()`, `GetChapterNo()`,
  `GetProgressRate()`, `IsCleared()`, `IsAccepted()`. There is no `get_TitleMessage`.
- Tabs: `CurrentTabCategory` (`TabCategoryType {All, Main, Master, Collection}`); sort:
  `CurrentSortType`; focus side: `Wait.CurrentSelectType` (`{MissionEntryList, MissionList}`).
- Per-mission status also available as `GetMissionStatus(info)` → `{Lock, Progress, Clear}`.
- Both scroll lists (`PartsScrollListMissionEntry`, `PartsScrollListMissionInfo`) are again
  `_Children`-less; reading the data avoids them entirely — and avoids de-duplicating the GUI,
  which renders the chapter/progress/name triple **twice** (row + preview pane).

### Missions — detail popup
`app.UIFlowUI50613.Param` → `MissionDetailHooks`

- One field, `MissionDeviceInfo`, the same `WTMissionDeviceInfo`; rewards via `GetReward()` /
  `GetRewardId()`, and GUI owner `ui50613` carries the rendered reward names.
- **Short-lived**: it opened and closed inside a second in the capture, and the auto-dump caught the
  param already dead. Poll fast and announce on bind — waiting for a change may mean never speaking.

### Not the Messages app
`app.UIFlowMessageLog.Param` (GUI `WTMessageLog`) is the **conversation recap** overlay, not the
phone. It fires around the same moments; do not confuse the two.

## World Tour phone map (`app.UIFlowWTDeviceMap`)

Captured with F8 on 2026-09-07 (`sf6access_autodump_154237.txt`, lines 770-965) and implemented as
`Hooks/WorldTour/DeviceMapHooks.cs` + `Services/WorldTour/DeviceMapText.cs`. The param is
`app.UIFlowWTDeviceMap.MapParam` (namespace `app`, **not** `app.worldtour`, like the Missions app).

- **State**: `FlowState` / `PreviousFlowState` (`eFlowState {MapView, TravelSelect, FastTravel,
  FailedTravel, OpenWorldMap, FailedAddPin}`), `OperationMode` (`eOperationMode {Default,
  TravelSelectOnly}`), `IsBattleHub`, `EnableFreeCursor`, `EnableSectionMap`, `EnableWorldMap`,
  `EnableFastTravel`, `EnableOpenTravelList`, `IsCursorDecideEnable`, `PinColorIndex`,
  `DispCityId` (400 = Metro City in the capture) / `DispSectionId`.
  **Enum widths**: none of these enums declares an underlying type in the generated stubs, and that
  generator *does* emit `: byte` where it applies (1027 of 5620 enums carry an explicit base), so
  they are 4-byte ints → `ReadIntField`. The mod never compares them to literals: values go back
  through `FlowHelper.ResolveEnumName` and are matched by member NAME.
- **Texts**: `GuideMessage` is a plain, already-localized `System.String` field (no Guid);
  `mTextCityName` and `mTextAreaName` are field-backed `via.gui.Text` (read `get_Message`).
  There is **no `mTextTitle`** on the param — the "World Tour" header seen in the GUI dump belongs
  to the phone frame, so the mod speaks its own word for the screen instead.
- **Icons**: `mIconList` (`app.UIPartsMapIconGroup : UIPartsGroup`). The icon under the free cursor
  comes from `MapParam.GetCursorSelectedItem() : UIPartsMouseOperable`; when its type name contains
  `MapIconPanel`, read `Info : app.UIMapWindowBase.IconInfo` → `TitleText` (string),
  `Type : ICON_TYPE`, plus `IsFastTravelPoint`, `UniqueIndex`, `GlobalPos`, `GroupType`. The panel
  itself has `Focus`/`ForceFocus`/`CursolOn` (from `UIPartsMapIconPanelBase`) and its own
  `mTextTitle` — used as the fallback when `Info.TitleText` is empty. **The panel carries no
  distance and no section name**, so neither can be announced from here.
  `ICON_TYPE` has 39 members; the mod maps them to nine family words by NAME prefix
  (`SHOP_*`, `MISSION_*`, `MASTER*`, `FAST_TRAVEL`, `MERCHANT`, `CHALLENGER`, `ENEMY*`, `PIN_*`,
  everything else → "point of interest") — lang keys `wt.map.icon_*` in `lang/en.txt` + `es.txt`.
- **The icon names are already localized.** The capture's on-screen texts (lines 809-837) read
  "Beat Square", "Chun-Li", "Style Lab Beauty Salon", "Moratones a los Caracartones" — so
  `TitleText` is spoken verbatim and only the *kind* needs a mod-supplied word.
- **Fast-travel list**: `mTravelPointList : app.UIPartsScrollList` (`SelectedIndex`, `ItemMax`;
  no `_Children`, as always) over the data list `TravelPoint :
  IList<app.worldtour.FastTravelPointUserDataRecord>`. A record has `id`, `CityID`, `SceneCityID`,
  `IsMyRoom`, `TimeType` and `PointNameID.GUID` → `FlowHelper.ResolveGuidField`.
  `SelectedTravelPoint` is **null until something is picked** (null in the MapView capture);
  `GetSelectedFastTravelPoint(bool checkSelectedPanel)` is the method form.
- **Other widgets**: `mGroupTop : UIPartsGroup`, `mCtrlTravelPointList`, `mCtrlFreeeCursor` (sic),
  `mCtrlPinInfoPanel`, `mCtrlIconType`, `mCtrlAreaName`, `mFastTravelThumbnail`, `mSituation`,
  `InputGuideDataList`, `DeviceMap : app.UIDeviceMapWindow`, `CityList :
  IList<app.UIFlowWorldMap.ItemParam>`.
- **`eTopGroupFocus {TravelList, IconList}` has no field.** The enum exists on
  `app.UIFlowWTDeviceMap`, but no field of that type appears on `MapParam`, on
  `Flow_MapView`/`Flow_TravelSelect`, anywhere else in the stubs, or in the dump. The closest live
  signal is `mGroupTop._FocusIndex`; the mod reads it null-safely and logs it once per screen entry,
  and announces nothing from it (the travel list and the map cursor each announce their own
  changes, so moving between them is already audible).

**Two follow-up dumps still needed:**
1. **`TravelSelect` populated** — open the fast-travel list (the input guide's "Abrir viaje rápido")
   and F9 there, to confirm `mTravelPointList.SelectedIndex` tracks the highlighted row and that
   `TravelPoint` is in the same order as the rendered list.
2. **An icon under the cursor** — park the free cursor on a map icon and F9, to confirm
   `GetCursorSelectedItem()` returns the `UIPartsMapIconPanel` (and not the group or a hit-test
   proxy) and that `Info.TitleText` / `Info.Type` are populated there. The same dump would confirm
   whether `mGroupTop._FocusIndex` matches `eTopGroupFocus`.

## World Tour mission objective (for the audio beacon)

**The game's own HUD marker answers first (2026-09-07, `Services/WorldTour/MissionGuideReader.cs` +
`MissionTargetService.cs`).** `app.UICityHud_MissionGuide` is the on-screen arrow's own source of
truth — it builds its own candidate list (`GetTargetNpc/OM/Zone`), picks one
(`GetNeareastTarget`/`ChangeMissionTarget`), and parks it in its `missionTarget` field — so reading
that field is reading exactly what a sighted player sees the arrow pointing at, including which
mission the player has SELECTED when they have both a primary and a secondary mission active.
`WTMissionSystem.FindProgressMissionId()` (below) answers "the mission in progress", the story's
notion of the current objective, which is not necessarily the one in the phone the player picked — the
HUD marker is the more accurate source, so it is tried first and wins whenever it resolves to a
position; the mission-system route is the fallback for whatever the marker cannot answer (not built
yet this loading screen, hidden, or following nothing).

**All member names below are decompile-only, unverified at runtime** — this is new code, not yet
run in game.

- **`MissionGuideReader.Read()`** finds the live `app.UICityHud_MissionGuide` component the same way
  `FieldHeadingService` finds the minimap window: `via.SceneManager.get_CurrentScene()` →
  `Scene.findComponents(System.Type)` with the guide's runtime type — the INSTANCE is never cached
  (the HUD is rebuilt across loads), only the TDB lookups are. Reads `missionTarget`
  (`ProgressMisionInfo`) → `TargetObject` (a live `GameObject`, same shape as the mission-system route
  below) and `TargetType` (`app.UICityHud_MissionGuide.eTargetType`: NPC/OM/ZONE, same order as
  `MissionTargetService`'s three lists so the index doubles as the enum value when naming the kind),
  plus the guide's own `curProgressMissionId`. Absent (no component in the scene, or nothing selected)
  is normal, logged nowhere; a genuine BIND failure (the type or a member missing, e.g. after a game
  patch) is warned exactly once, naming the member: `Mission guide: cannot bind '<member>' on
  app.UICityHud_MissionGuide; using WTMissionSystem only`.
- **`MissionTargetService.Find()`** tries `FindFromGuide()` (the HUD marker) first, then falls back to
  `FindFromMissionSystem()` (below) only when the marker yields no target or the target has no
  readable position. One log line per CHANGE of objective (mission id, target kind, object, or a
  switch between the two sources) — never once a second for a walking objective:
  `Mission guide: id=<n> type=<NPC|OM|ZONE|?> source=<hud|system> pos=(x, y, z)`.
- **Fallback — `app.worldtour.WTMissionSystem`** (singleton) → `FindProgressMissionId()` → one of
  `GetListNpcMissionTargetInfo(id)` / `GetListOmMissionTargetInfo(id)` / `GetListZoneMissionTargetInfo(id)`
  → per record `HaveMissionTarget` and **`ListHolderObj`, a list of live scene `GameObject`s** →
  `get_Transform` → `get_Position`. So the objective is a real object, not a bare coordinate, which
  means a beacon can sound *on* it.
  - All three lists are asked in turn: an objective is sometimes a person, sometimes a thing,
    sometimes a place, and the game keeps them separate.
  - **An empty holder list is normal**, not an error: the target has not streamed into the loaded
    scene (another district, or not spawned yet).
  - `WTPlayerDataMission.mProgressMainMissionId` is the save-data authority on which mission is the
    MAIN one, if "whatever the HUD is tracking" ever proves too loose.
- `app.UICityHud_MissionGuide.missionTarget`'s sibling `UIPos` is a screen-space projection — **not**
  a world position; never feed that to a 3D sound. (`TargetObject`'s own transform is the world
  position used above.)

**Verification (2026-09-07, pending):** accept a main and a sub mission, select the SUB one in the
phone, and confirm the beacon and the logged `source=hud` id follow it; then switch back to the main
mission and confirm the beacon follows that instead.

## World Tour — spatial navigation APIs (physics, navmesh, collision)

Started as a decompiled-code pass (`sf6 code/`) done while porting the RE7 mod's navigation radar
concept to World Tour. A first in-game dump (Metro City, 2026-09-04) confirmed some of it and appeared
to disprove other parts; a **second** in-game run the same day (~20:59, fixed probe) overturned two of
those "disproven" conclusions — they turned out to be a field-read bug in the first probe, not real API
failures. See "Value-type reads: a known trap" below for the mechanism. Tags below: `CONFIRMED
(decompiled)` = seen in the decompiled dump only (declarations only, no method bodies — argument values
and internal filter choices cannot be read from it and must be established at runtime); `CONFIRMED IN
GAME` = exercised live and returned real data; `DISPROVEN IN GAME` = the decompiled-era claim turned out
wrong at runtime (still trustworthy where used below — the read-path bug specifically hit vec3/struct
reads, not the cases tagged this way); `PENDING RUNTIME VERIFICATION` = not yet exercised live, or
exercised but not yet trustworthy.

**Bottom line after the second run (2026-09-04 ~20:59): both candidate routes for the navigation radar
are confirmed reachable.** The avatar's own raycast API (§3, A/B-tested open-street vs. wall) and the
NavMesh handle (§6, `AIMap.findMapHandle()` now confirmed non-null) are both live. Which one the radar
should be built on is an open architectural question — see the Design note; an earlier version of this
doc asserted raycast was the only option and that assertion is retracted. The `CollisionSystem` filter
table (§2) is confirmed live and now includes the player's own filter; the `AvatarComponent` capsule
route (§4) works via the corrected single-object route, with real capsule values now recorded.

### Value-type reads: a known trap

In the second run (2026-09-04 ~20:59), **every struct value came back zeroed** while reference types,
bools, and plain floats all read fine. Diagnostic rule for future work: **a zeroed vec3 or a null struct
from a new read path is far more likely a marshalling bug than a real game value — verify against a
known-good reader before concluding the API is dead.** The NavMesh retraction in §6 below is the
cautionary example: the first run's "NavMesh is dead in World Tour" conclusion was entirely this bug,
not a real API failure.

Broken in the second run:
- `out`/`ref` parameter write-back: `GetCastRayPosition`'s two `out vec3` params, `CastRay`'s
  caller-allocated `HitResult` (stayed empty even when the method returned `true`),
  `GetGroundPos(out vec3)` (returned `true` with `(0,0,0)`),
  `GetCurrentCharacterControllerSizeRatio(ref float, ref float)`, and `CastRayAll`'s `CastRayResult`
  (reported `NumContactPoints=0` while the `CharacterController` simultaneously reported 5 wall contact
  points).
- Plain struct field/property reads: `CharacterController.Position`, `CityPointDataInfoBase.Position`/
  `Rotation`, `CollisionInfo.AdjustedGroundPos` — all read back `(0,0,0)`.

Contrast: `Transform.Position` reads **correctly** via this mod's existing
`Services/WorldTour/AvatarFieldReader.cs` path, so a correct idiom for vec3 reads already exists in this
codebase — the bug is in the new probe's read path, not in vec3 reads generally.

**Status: IN PROGRESS** — a fix for the new probe's read path is being worked on separately (in code,
not in this doc). Until it lands, treat any zeroed vec3 or null struct coming from a newly-added World
Tour read path as unverified rather than as proof the underlying API is dead.

### 1. Physics raycast — `CONFIRMED (decompiled)`
- `via.physics.System`: `castRay(CastRayQuery, CastRayResult)`, `castRay(via.Scene, CastRayQuery,
  CastRayResult)`, `castRayAsync`, `castShape`; `getLayerName(uint)`, `getMaskName(uint layer, uint bit)`.
- `via.physics.CastRayQuery`: `FilterInfo`, `Options:uint`, `Ray`, `RayDistance`, `setRay(vec3,vec3)`,
  `setRay(vec3,vec3,float)`, `enableAllHits`, `enableNearSort`, `disableInsideHits`,
  `enableOneHitBreak`, `clearOptions`.
- `via.physics.CastRayResult`: `NumContactPoints:uint`, `clear()`, `getContactCollidable(uint)`,
  `getContactPoint(uint)`.
- `via.physics.ContactPoint` (ValueType): `Position:vec3`, `Normal:vec3`, `TimeOfImpact`,
  **`Distance:float`** — SF6 gives the hit distance directly (the RE7 mod had to bisect for it).
- `via.physics.Collidable`: `GameObject`, `FilterInfo`, `Shape`, `TransformedShape`, `UserData`.
- `via.physics.FilterInfo`: `Layer`, `Group`, `SubGroup`, `IgnoreSubGroup`, `MaskBits`.
- `via.physics.CastRayOption` enum: `AllHits=0, DisableBackFacingTriangleHits=1,
  DisableFrontFacingTriangleHits=2, BackFacing=3, FrontFacing=4, NearSort=5, InsideHits=6,
  OneHitBreak=7`.
- **GOTCHA**: `ContactPoint` is a ValueType → `InvokeBoxed` needs an explicit `typeof(...)`; passing
  null fails silently (cost the RE7 mod weeks — see that mod's history).

### 2. SF6 collision filters — `CONFIRMED (decompiled)`
There is **no** `app.Collision.CollisionSystem.Filter` (that's RE7). SF6's equivalent is
`app.CollisionSystem`.

- `app.CollisionSystem.eFilterInfo` (`sf6 code/REFramework.NET.application/app/CollisionSystem.cs:82`):
  `TerrainRayFilter=0, EffectRay=1, Terrain=2, Camera=3, Character=4, PhysicsDynamic=5,
  TerrainStopCamera=6, BattleLine=7, OnlyAttribute=8`.
- Static wrappers (no instance needed): `castRay(ref vec3 start, ref vec3 end, out HitResult,
  eFilterInfo, bool disableBackFacingTriangleHit)` (:781), multi-hit overload taking `IList<HitResult>`
  (:793), overloads taking a `via.physics.FilterInfo` directly (:787 / :799),
  `castRay(ref vec3 start, ref vec3 dir, float distance, out HitResult, eFilterInfo)` (:775),
  **`castRayAll(vec3 start, vec3 end, via.physics.CastRayResult, eFilterInfo, bool)`** (:805),
  `castSphere` (:691-745).
- `app.CollisionSystem.HitResult` (:144): `HitObject:GameObject`, `HitPoint:vec3`, `Normal:vec3`,
  `Distance:float`, `TimeOfImpact`, `Material:MaterialInfo`.
- Instance methods: `GetFilterInfo(eFilterInfo)` → `via.physics.FilterInfo` (:682),
  `getLayerIndex(eLayerID)` (:685), `getFilterResource` (:688).
- `app.gCollision.LayerId : byte` (`app/gCollision.cs:58-82`): `Terrain, Character, TerrainRay, Attack,
  Damage, Sign, OMPress, GayaPress, NpcPress, PlayerPress, Marker, Sensor, EffectChecker, EffectCheckRay,
  SoundSpace, SoundPosition, SoundRay, SoundWall, EnvMarker, EnvSensor, ChainSelf, ChainEffector,
  Dynamic, Static, _Num`. Official byte→index conversion: `app.gCollision.GetLayerIndex(LayerId)` +
  `LayerIndexArray` — this is the source to read layer indices from; do not hand-guess them (no-magic-
  numbers rule).
- `app.CollisionSystem.getWTEColMaterialID(vec3 pos, bool, float)` (:831) — surface material under a
  World Tour point (asphalt/grass/…).

`CONFIRMED IN GAME` (Metro City, 2026-09-04): `app.CollisionSystem` is reachable as a managed
singleton, and `GetFilterInfo(eFilterInfo)` is an **instance** method (not static) that returns valid
data for all nine `eFilterInfo` members. Recorded verbatim as confirmed runtime data:

| eFilterInfo | value | layer | mask |
|---|---|---|---|
| TerrainRayFilter | 0 | 3:TerrainRay | 0x8 [TCStopCamera] |
| EffectRay | 1 | 14:EffectCheckRay | 0xFFFFFFFF (all) |
| Terrain | 2 | 1:Terrain | 0x0 (empty) |
| Camera | 3 | 2:Character | 0x6 [TCAttribute\|TCThroughCamera] |
| Character | 4 | 7:OMPress | 0xFFFFFFFF (all) |
| PhysicsDynamic | 5 | 23:Dynamic | 0xFFFFFFFF (all) |
| TerrainStopCamera | 6 | 3:TerrainRay | 0x7 [TbDefault\|TCAttribute\|TCThroughCamera] |
| BattleLine | 7 | 3:TerrainRay | 0x18 [TCStopCamera\|TCNoBattleLine] |
| OnlyAttribute | 8 | 3:TerrainRay | 0xD [TbDefault\|TCThroughCamera\|TCStopCamera] |

Also `CONFIRMED IN GAME`: `app.gCollision.LayerId` → `GetLayerIndex()` works; 24 layers total; LayerId
ordinal N maps to index N+1 (`Terrain`=0→1 … `Static`=23→24, `_Num`=24→0).

`CONFIRMED IN GAME` (second run, 2026-09-04 ~20:59): the player's own filter is now known directly,
answering the earlier open question of which `eFilterInfo` matches it — `PLAYER
CharacterController.FilterInfo` → `layer=2:Character group=1374 subgroup=0
mask=0xA[TCAttribute|TCStopCamera]`. This is close to but not identical to the `Camera` row above (also
`layer=2:Character`, different mask); treat the player's own live `FilterInfo` as authoritative rather
than trying to force a match onto one `eFilterInfo` table row.

### 3. Authoritative ray heights (avatar's own echolocation) — THE IMPORTANT ONE — `CONFIRMED (decompiled)`
The avatar already casts rays at itself every frame for movement/animation purposes; reusing those
heights avoids inventing our own offsets. This is the game's own avatar raycast API, on
`app.worldtour.avatar.AvatarState_FieldBase`, reached via `AvatarBase.GetFieldState()` (`AvatarBase.cs:
1293`). Note `AvatarCollisionManager` (see §4 alternate route) hangs off this **state** object, not off
`AvatarPlayer` directly.

- `void GetCastRayPosition(CastRayTypes type, out vec3 start, out vec3 end)` (:2438) — returns the
  game's own world-space ray endpoints for a given `CastRayTypes`. Ray heights/lengths must **not** be
  hardcoded — always read them from this method.
- `bool CastRay(CastRayTypes type, out HitResult hit)` (:2441) — filter chosen internally by the game.
- `bool CastRay(ref vec3 start, ref vec3 end, out HitResult hit)` (:2444).
- `void CastRayAll(CastRayTypes type, via.physics.CastRayResult result, eFilterInfo filterId)` (:2450).
- `void CastRayAll(ref vec3 start, ref vec3 end, CastRayResult result, eFilterInfo filterId)` (:2453).
- `bool CastFloorSphere(IList<HitResult> hittedList, float checkDistance)` (:2447).
- `bool checkGroundHitResult(HitResult h)` (:3053).
- Enum `CastRayTypes` (:1083-1116), members in declaration order: `FRONT, FRONT_R, FRONT_L,
  HANGING_FRONT, HANGING_FACE_FRONT, HANGING_VERTICAL, FOOT_FRONT, FOOT_FRONT_L, FOOT_FRONT_R,
  WAIST_FRONT, SIDE_R, SIDE_L, CROUCH_UP, STEP_R, STEP_R2, STEP_L, STEP_L2, STEP_FOOT, BESIDE_R,
  BESIDE_L, RIGHT_FOWARD, LEFT_FOWARD, FRONT_LONG, FOOT_FRONT_LONG, WAIST_FRONT_LONG, GROUND,
  BUST_FRONT, HIWALL_FRONT, FALL_GUARD_CENTER, FALL_GUARD_RIGHT, FALL_GUARD_LEFT, _CAST_RAY_MAX`. Note
  the game misspells FORWARD as FOWARD in `RIGHT_FOWARD`/`LEFT_FOWARD`.
- Why this matters: it gives foot/waist/bust/high-wall ray heights **from the game's own data**,
  satisfying the no-magic-numbers rule instead of guessing offsets like the RE7 mod had to.
- `app.worldtour.ERayCastMode`: `UseCharacterController, Root, Foot, UseIkLeg`.
- There is **no** `CastRayTypes`-indexed value table (searched, not found). Raw scalar offsets live in
  `app.worldtour.avatar.AvatarConstSystemParams` group `RayOffset` (`AvatarConstSystemParams.cs:147`,
  floats at :161-337: `RAY_FRONT_LL/L/M/S, RAY_UP_XL/LL/L/M/S, RAY_SIDE_SS/S/M, RAY_STEP_*, RAY_LINE_M,
  RAY_GROUND, RAY_FALLGUARD_*`), reachable via `AvatarBase.ConstSystemParams` (:1005) — but they are not
  per-`CastRayType`, so `GetCastRayPosition` remains the correct accessor.
- **Invocation trap**: `app.CollisionSystem.HitResult` and `via.physics.CastRayResult` are objects the
  **caller allocates** and the game fills — the TDB signatures have no `ref` even though ILSpy renders
  them as `out`.

`CONFIRMED IN GAME` (second run, 2026-09-04 ~20:59): `WTPlayerManager.GetAvatarPlayer()` →
`AvatarBase.GetFieldState()` returns `app.worldtour.avatar.AvatarState_FieldPlayer` (concrete type at
runtime; the ray methods resolve on the base `app.worldtour.avatar.AvatarState_FieldBase`).
`CastRayTypes` enumerates 32 members from the TDB, matching the decompiled list above.
`CastRay(CastRayTypes, out HitResult)` returns a **reliable, meaningful** bool — recorded here as the
useful A/B evidence for that (the `HitResult` itself is currently unreadable, see "Value-type reads: a
known trap" above):
- Standing in an open street (player at `(-2.510, 0.091, -72.181)`, `CharacterController.Wall`=False,
  `NumWallContactPoints`=0) → 11 ray types hit, and they are all ground-related: `FOOT_FRONT_L, STEP_R,
  STEP_R2, STEP_L, STEP_L2, STEP_FOOT, FOOT_FRONT_LONG, GROUND, FALL_GUARD_CENTER, FALL_GUARD_RIGHT,
  FALL_GUARD_LEFT`.
- Standing against a wall (player at `(-16.324, 0.200, -54.841)`, `CharacterController.Wall`=True,
  `NumWallContactPoints`=5) → 23 ray types hit; the entire forward height stack lights up: `FRONT,
  FRONT_R, FRONT_L, HANGING_FRONT, HANGING_FACE_FRONT, FOOT_FRONT, FOOT_FRONT_L, FOOT_FRONT_R,
  WAIST_FRONT, FRONT_LONG, FOOT_FRONT_LONG, WAIST_FRONT_LONG, BUST_FRONT, HIWALL_FRONT` (plus the ground
  set above).

Practical consequence: the forward stack FOOT/WAIST/BUST/HIWALL gives an obstacle **height profile**,
and the ray names already encode direction and height, so a usable radar can be built from the
`CastRay` booleans alone, without any geometry read-back.

### 4. Avatar capsule — `CONFIRMED IN GAME` for the player/Transform, capsule route corrected

`CONFIRMED IN GAME` (Metro City, 2026-09-04): `WTPlayerManager.GetAvatarPlayer()`
(`app.worldtour/WTPlayerManager.cs:560`) returns `app.worldtour.avatar.AvatarPlayer`. Its `Transform`
reads fine — `Position`, `Rotation`, `EulerAngle`, `AxisZ`, `AxisX` — and `Position` agrees exactly with
the mod's existing player position from `Services/WorldTour/AvatarFieldReader.cs`.

`DISPROVEN IN GAME`: reading the player's `CharacterController` by scanning `AvatarBase.Components` as
an **array** returns null — because `Components` is a **single object**, not a collection. Documented
here as a trap; do not iterate it like `_Handles`/`_Children` elsewhere in this codebase.

Corrected route (`CONFIRMED (decompiled)`):
- Capsule route: `AvatarBase.Components : app.worldtour.avatar.AvatarComponent` (singular object,
  `AvatarBase.cs:1101`) → `AvatarComponent.CharacterController : via.physics.CharacterController`
  (`AvatarComponent.cs:42`).
- Alternate route: `AvatarBase.GetFieldState()` → `AvatarState_FieldBase.CollisionManager :
  AvatarCollisionManager` (`AvatarState_FieldBase.cs:2367`) → `AvatarCollisionManager.CharaController`
  (`AvatarCollisionManager.cs:385`). `AvatarCollisionManager` hangs off the **state**, not off
  `AvatarPlayer` — see §3.
- `AvatarBase.GetCurrentCharacterControllerSizeRatio(ref float widthRatio, ref float heightRatio)`
  (`AvatarBase.cs:1407`).
- `via.physics.CharacterController`: `Height`(266), `Radius`(274), `SlopeLimit`(282), `Position`,
  `Ground:bool`(314), `Wall:bool`(320), `Ceiling:bool`(326), `Jump:bool`(332),
  `NumGroundContactPoints`(356), `getGroundContactPoint(int)` → ContactPoint (428),
  `getGroundGameObject(int)`(431), `getGroundMaterialInfo(int)`(434), `overwriteFilterInfo` /
  `restoreFilterInfo`(452/461). Also cached without a raycast: `NumWallContactPoints:int`,
  `getWallContactPoint(int)`, `getWallGameObject(int)`, `getWallMaterialInfo(int)` (Wall equivalents of
  the Ground getters above), `FilterInfo`.

`CONFIRMED IN GAME` (second run, 2026-09-04 ~20:59): the single-object `Components` →
`CharacterController` route above works and reads correctly. Real values recorded: `Radius`=0.5,
`Height`=1.8, `SlopeLimit`=46, plus the bools `Ground`/`Wall`/`Ceiling` and `NumGroundContactPoints` (3
standing on flat ground) / `NumWallContactPoints` (5 against a wall) all read correctly. (`Position`
itself is still unreadable via this struct path — see "Value-type reads: a known trap" above; use
`AvatarPlayer.Transform.Position` instead, which is confirmed correct.)
- **IMPORTANT**: the effective size is the above multiplied by
  `AvatarBase.GetCurrentCharacterControllerSizeRatio(ref float widthRatio, ref float heightRatio)`
  (`AvatarBase.cs:1407`). Authored values live in
  `app.worldtour.avatar.CharacterControllerCustomParam` (`Height, Radius, Offset, SecondHeight,
  SecondRadius, SecondOffset, Situation`).
- `AvatarBase.GetContactedWallInfos(IList<ContactedWallInfo>)` (`AvatarBase.cs:1602`);
  `ContactedWallInfo` (`AvatarBase.cs:816-844`) = `{ bool CanWallRide; vec3 ContactedPos; vec3
  ContactedNormal; }`.

### 5. Ground/wall info the avatar already publishes (free every frame) — `CONFIRMED (decompiled)`
- `AvatarBase.__GetVolatileParam()` (`AvatarBase.cs:1770`) →
  `AvatarFieldParam_Volatile.Collision : CollisionInfo` (`AvatarFieldParam_Volatile.cs:603`, type at
  :164).
- `CollisionInfo`: `IsGround:bool`(380), `IsGroundTouch()`(530), `IsAirAndNearGround()`,
  `AdjustedGroundPos:vec3`(412), `GetGroundPos(out vec3)`(539), `GroundAngleRate`, `IsOnFloorSlope()`,
  `IsSlope`, `GroundObjInfoList : IList<FloorObjInfo>`, `WallContactInfoList : IList<WallInfo>`,
  `IsWallContact()`, `GetWallContact(out IList<WallInfo>)`, `WallMovableRate`,
  `IsContactedDashStopWall`, **`CalcMovableRate(ref vec3 moveVec, ref vec3 selfPos, ref
  AvatarFieldParam_Volatile preFrame)`** — the game already computes how far movement in a direction is
  possible (0..1), which is a ready-made "can I walk this way" signal.
- `FloorObjInfo{Normal:vec3, ContactPos:vec3, FloorObject:GameObject, FloorAngleX, FloorAngleRate}`
  (:285-325); `WallInfo{Contact : via.physics.ContactPoint, Material : MaterialInfo}` (:215-234).
- `AvatarBase.GetVelocity() : vec3` (:1443), `GetMotionAddedVelocity()` (:1758), `IsGround()` (:1422),
  `GetLastTouchedGroundPos(ref vec3)` (:1689).
- `WTPlayerManager.GetPlayerToAngle(vec3 currentPos, vec3 forwardDir) : float` (:566).

`CONFIRMED IN GAME` (second run, 2026-09-04 ~20:59): `__GetVolatileParam()` → `Collision` now resolves
correctly to `AvatarFieldParam_Volatile.CollisionInfo`. The first run's null was the same field-read-path
bug covered in "Value-type reads: a known trap" above, not a real absence of data — `IsGround`,
`IsSlope`, `IsWallContact()` and `WallContactInfoList.Count` all read correctly in the second run.

`PENDING` (open problem, second run): `AvatarBase.GetContactedWallInfos(IList<ContactedWallInfo>)`
could **not** be called — allocating the caller-side `List<AvatarBase.ContactedWallInfo>` (the game's
own nested type) failed. Cause not yet diagnosed.

### 6. NavMesh — `CONFIRMED IN GAME` (handle obtainable)

**RETRACTED — history note, read before touching this section again.** The first in-game run (Metro
City, 2026-09-04) reported `WTCommon.CityResource` and `WTCityResources.CityAIMap` as null and concluded
the NavMesh route was dead in World Tour. That conclusion was **wrong**: it was a field-read-path bug in
the first probe (see "Value-type reads: a known trap" above), not an absent API. A null coming back from
a read in this area should be treated as suspect until the read path is proven, not taken as proof the
API doesn't exist.

`CONFIRMED IN GAME` (second run, 2026-09-04 ~20:59, fixed probe): `WTCommon=ok CityResource=ok
CityAIMap=ok findMapHandle=via.navigation.MapHandle` — `AIMap.findMapHandle()` returns a valid
`via.navigation.MapHandle` in World Tour.

`PENDING RUNTIME VERIFICATION`: whether node queries against that handle (`queryClosestNode`,
`queryNode(NodeQueryInfo)`, etc.) return useful data has **not** yet been exercised — only obtaining the
handle itself is confirmed so far. Standing warning unchanged: the no-arg `MapHandleBase.queryNode()`
must **never** be called — it's an unbounded city-wide query and stalls the game.

The API surface below is `CONFIRMED (decompiled)` as a reference (types/signatures are real); node-query
behavior against the now-confirmed handle is still `PENDING RUNTIME VERIFICATION`.
- `app.global.WTCommon.CityResource` (`app.global/WTCommon.cs:280`) →
  `app.worldtour.WTCityResources.CityAIMap : via.navigation.AIMap` (`WTCityResources.cs:335`);
  `AIMapResources`(:481), `InitAIMapResources()`(:512). Also `app/WTCityHourlyResources.cs:13`
  (`NpcAIMap`).
- `via.navigation.AIMap`: `findMapHandle()`(177), `findMapHandle(string)`(180),
  `findMapHandleBase(MapType)`(189), `getMaps(uint)` / `getMapsCount()`(207/210).
- `via.navigation.MapHandleBase`: `queryClosestNode(vec3)` → NodeInfo (379),
  `queryClosestNode(vec3, NodeQueryInfo)`(382), `queryNode(NodeQueryInfo)` → NodeInfoList (388),
  `checkOutOfMap(NodeQueryInfo)`(364), `Boundary:AABB`(177), `MapName`(77), `SectionID`(95),
  `queryAttributeName(int bitNo)`(376).
- `via.navigation.NodeQueryInfo`: `setRegion(Sphere/AABB/OBB/Capsule/Cylinder/LineSegment/Collidable)`
  (121-139), `setFilter`(115/118), `ExcludeShapes`(100).
- `via.navigation.map.NodeInfo`: `Pos`(169), `Normal`(175), **`Wall:bool`(217)**,
  **`WallHeight:float`(223)**, `ShapeType`(229), `LinkBoundary`/`MaxY`/`MinY`/`EdgeCount`(193-211),
  `getVertices()`(248), `getVertex(uint)` / `getVertexCount()`(260/263), `getGlobalVertex(uint)`(254),
  `findIntersection(ref vec3, LineSegment)`(251), `queryLinkToNodes()` → NodeInfoList (278),
  `queryLinks()`(281), `queryClosestLinkBoundaryEdge(vec3)`(272), `hasAttribute(string)`(266).
- `via.navigation.NavigationSurface` — **synchronous** pathfinding: `queryPathSync(vec3 start, vec3
  end, PathQueryReport)`(556) and variants (559/562/565-571); `AgentRad`(257), `AgentHeight`(265),
  `UnderSearchLength`(273). `PathQueryReport{Exist, FailReport, PathInfo}`;
  `via.navigation.map.PathInfo`: `PathPointCount`(261), `getPathPointInfo(uint)`(274),
  `getPortalPos(uint)`(319), `getPortalEdge(uint, ref bool)`(313), `calcDistance()`(289).
- Real WT usage for reference: `app.worldtour.npc.WTEventNpcNavigator.NaviSurface`
  (`WTEventNpcNavigator.cs:75`), `NaviSurfaceMapSet()`(142); `WTNpcAI : AvatarNaviControllBase`
  (`WTNpcAI.cs:13`), `IsDontUseAIMap`(534).
- Authored routes: `app.worldtour.WTCityRoute.GetRouteInfosBySituationId(uint)` /
  `GetRoute(int,uint)` (`WTCityRoute.cs:135/141`).
- Status: `CityAIMap.findMapHandle()` returns a **valid handle** in World Tour (see confirmation above,
  retracting the earlier "returns NULL" claim). The real navmesh attribute names (`queryAttributeName
  (0..31)`) and node-query results against the handle have not yet been pursued/verified — see `PENDING
  RUNTIME VERIFICATION` above.

**Now has a consumer (2026-09-08):** `Services/WorldTour/NavMeshOpenings.cs` is the first code to
actually call `queryClosestNode(vec3)` and `queryLinkToNodes()` against this handle — see § World Tour —
spatial navigation APIs, "2026-09-08: precision rebuild" below for the full recipe. This is still
**code, not a runtime result**: the node-query behaviour itself remains `PENDING RUNTIME VERIFICATION`
exactly as this section already said: nothing in the new file has run in game. `NavMeshOpenings` logs its
own one-line answer (`[SF6Access] NavMesh openings: ...`) precisely so that question gets settled the
next time World Tour is played, including the `node.Pos == (0,0,0)` trap called out in "Value-type reads:
a known trap" above, which the new consumer treats as a marshalling bug rather than a real node at the
origin.

**`CRASHED THE GAME` on its first press (2026-09-08) — and what the post-mortem could and could not
prove.** B in World Tour killed SF6 with a clean log: the last mod line was the radar's own
`NavRadar verdict routes:` (10:35:21.422), written by the sample that runs immediately before the
navmesh call, and no `NavMesh openings` line ever appeared. Every failure path before the node query
writes a line, so the game died in the first unproven engine call and left no exception — the signature
of a NATIVE fault (or a hang), which no C# `try/catch` can see.

Cleared by the post-mortem, with evidence, so nobody re-suspects them:
- *Argument marshalling of `queryClosestNode(vec3)`.* The parameter is BY VALUE (the decompiler marks
  by-ref explicitly — `findIntersection(ref vec3, LineSegment)` in the same type), and the engine's own
  generated binding passes a by-value `via.vec3` as an OBJECT whose `Ptr()` is the argument
  (`app/CollisionSystem.cs:812` → `Invoke(null, new object[]{ start, end, ... })`). A `FieldOutBuffer`
  view is exactly that. The same buffer + view shape ran nine times in the crashing session
  (`Nav radar sideways probe: GetCastRayPosition`, 10:35:21.417).
- *The route to the handle.* `WTCommon.CityResource` → `CityAIMap` → `findMapHandle()` was confirmed
  2026-09-04 and every failure branch logs.
- *`FindByShape` binding the wrong overload.* It matches name + 1 parameter whose type ends in `vec3`,
  so it cannot reach `queryClosestNode(vec3, NodeQueryInfo)` nor the forbidden no-arg `queryNode()`.
- *`GetCurrentCharacterControllerSizeRatio(ref float, ref float)`.* Not reached: the same session logged
  `charaCtrl=missing`, so the capsule read returns 0 before the ratio call.
- *Value-type return trap.* `via.navigation.map.NodeInfo` and `NodeInfoList` are REFERENCE types
  (neither is marked `: ValueType` in the generated bindings, unlike `via.vec3`/`via.LineSegment`), so
  no explicit value-type return handling is needed. `InvokeBoxed` is now given `typeof(object)` anyway —
  `null` as a return type is only ever proven here for `void` methods.

Still suspect, in order: the `queryClosestNode(vec3)` call itself (an unbounded search over a streamed
city navmesh would hang exactly like this — the standing warning about no-arg `queryNode()` is the same
family); then the wrappers REFramework builds around `queryLinkToNodes()` / `getNode(i)` results, which
is the documented "bogus managed wrapper, AV in `ManagedObject.Finalize`, clean log" failure.

**Rewritten for the next attempt (still `false` in `FieldNavRadarHooks.MESH_WAYS_ENABLED` — only the
user re-enables it).** The engine calls moved to `Services/WorldTour/NavMeshNodes.cs`, one per method,
each writing `[SF6Access] NavMesh step N: ...` BEFORE it runs, so the last step in the log names the
call that did not return: **1** map handle, **2** `queryClosestNode(vec3)`, **3** the player node's
`getGlobalVertexCount`/`getGlobalVertex`, **4** the avatar capsule, **5** `queryLinkToNodes`/
`getNodeCount`, **6** `getNode(i)` and that neighbour's vertices, then the existing
`[SF6Access] NavMesh openings: ...` summary (which also switches the trace off, so a working navmesh
logs one line per city, not six per press). `NavMeshOpenings.cs` keeps the cache and the edge geometry,
its duplicate capsule reader was replaced by `FieldRayCaster.CapsuleRadius()`, and a navmesh that fails
is not re-queried until `Reset()` (it used to re-run a full query every sample whenever the capsule was
unreadable, which is exactly the current in-game state).

**2026-09-08, later session — crash root cause identified; pathfinder surface further mapped (nothing
implemented yet).**

**Root cause of the 2026-09-08 navmesh crash, now identified.**
`Services/WorldTour/NavMeshNodes.cs:121` resolves the map handle with `FlowHelper.Call(aiMap,
"findMapHandle")` — a **by-name** bind with **zero arguments** — while `via.navigation.AIMap` declares
three overloads named `findMapHandle`: `()`, `(string)`, `(MapType)`. A by-name call is arity-blind — it
returns the first TDB match regardless of parameter count — so the read could bind any of the three and
hand back a handle for a map that was never the intended one; the very next call (`queryClosestNode`)
then dereferenced that handle. This is the one by-name bind in a file whose whole premise (the
"Rewritten for the next attempt" note above) is binding every other call by shape. **Secondary
hypothesis, not yet tested:** Metro City is section-streamed (§ 7 below, `CitySectionManager`), so the
live navmesh may belong to a `via.navigation.SectionManagerHandle` reached through
`findMapHandleBase()`/`getManagers`, not to the plain `MapHandle` that `findMapHandle()` returns.

**A synchronous pathfinder exists and looks safely bindable, by the same shape rule already proven for
`castRayAll`.** `via.navigation.NavigationSurface.queryPathSync(via.vec3 start, via.vec3 end,
via.navigation.PathQueryReport report) : bool`. Its parameter shape — two by-value `vec3` plus one
caller-allocated reference-type result the engine mutates in place — is the same class already proven
safe in this codebase for `app.CollisionSystem.castRayAll` (§ 2 above): a value-type `out`/`ref` gets a
`FieldOutBuffer`, a reference-type result gets an ordinary `CreateInstance` object, never an `out`/`ref`
**reference** type (the standing rule in `docs/sf6-architecture.md` § Critical IL2CPP gotchas). Read
back `PathQueryReport.Exist`, `.PathInfo`; `PathInfo.calcDistance()` and `calcDistance(DistanceType.Raw
vs .Path)` (a detour ratio — how much longer the walked path is than the straight line); `PathInfo.
PathPointCount`; `getPathPointInfo(uint).PortalPos`.

**The player's own avatar carries a `NavigationSurface`, bypassing the map handle entirely.**
`AvatarBase.Components` → `AvatarComponent.Controll` (concrete type
`app.worldtour.avatar.AvatarPlayerControll : AvatarNaviControllBase`) → field `NaviSurface`. Pre-flight
checks before trusting it: `AvatarNaviControllBase.IsDontUseAIMap` and
`via.navigation.Navigation.hasValidMap()` (0 parameters, bool).

**Never bind `queryPath` (the async overload, no `Sync` suffix)** — one of its parameters is a
`MulticastDelegate`, the documented crash family for a by-ref **reference**-type argument
(`docs/sf6-architecture.md` § Critical IL2CPP gotchas, "NEVER call a method whose out/ref parameter is a
REFERENCE type"). Bind `queryPathSync` by shape and match its name exactly — do not let a by-name lookup
pick between the two.

**Also confirmed unusable, recorded so they are not retried:** `Navigation.setAvoidNodes(IList<uint>)`
(constructing a generic `IList<T>` is the interface/generic `CreateInstance` crash, § Critical IL2CPP
gotchas above); `MapHandleBase.queryNode()` (unbounded, city-wide, stalls the game — standing warning,
unchanged); `Navigation.adjust()`/`start()`/`stop()` (these drive the LIVE avatar's own navigation, not a
read-only query).

**Negative finding — the World Tour minimap is a dead end for walkability, not just for a compass.**
`app.MapSettingBase.MapTexture` is a texture resource, and there is no `via.render.Texture` readback
anywhere in this codebase or the decompiled sources; the only spatial data the map settings expose is one
world-space AABB (`MinPos`/`MaxPos`). Recorded so a future pass does not try to read walkability off the
map picture. Sub-finding worth keeping, since it IS usable: the world↔map transform exists and is
already in production use — `app.UIMapWindowBase.ConvertTo_UIPos(vec3)`, bound by
`Services/WorldTour/FieldHeadingService.cs:43` for the hands-free compass (§ 10 below) — and map icons/
pins carry real world positions (`IconInfo.GlobalPos`, `PinInfo.Position`, § World Tour phone map above),
which is a usable LANDMARK source, just not a walkability source.

**Correction to a rule stated elsewhere in this codebase's docs: `get_X` is not unusable across the
board.** The gotcha in `docs/sf6-architecture.md` § Critical IL2CPP gotchas concerns the REFramework-
**generated C# interface property getter** (a typed proxy's `.Position`), which returns null/empty on a
concrete IL2CPP instance. Calling `get_X` **by name through reflection** (`FlowHelper.Call(obj,
"get_X")`) works, and this codebase already relies on it: `Services/WorldTour/MissionGuideReader.cs:108`
and `Services/WorldTour/MissionTargetService.cs:121` both fall back to a by-name `get_` call when the
backing field is absent. Field-first, `get_` by name as fallback, is the pattern to reach for on the
pathfinder surface above too.

**Status: still nothing implemented from this section.** Everything above is reverse-engineering
groundwork for a future pathfinder-based radar/guidance layer; no shipped code calls `queryPathSync`,
`NaviSurface`, or the map AABB.

### 7. Transit points (doors, fast travel, sections) — `CONFIRMED (decompiled)`
- There are **no** `*Door*`/`*Gate*`/`*Entrance*`/`*Teleport*` types under `app.worldtour.*`. Building
  entrances are numbered "OM" objects (`app.worldtour.om.Om00xxxx`, e.g. `Om008000` = body shop) that
  fire contact events — not modeled doors.
- Fast travel: `WTCityManager.GetFastTravelPointList(uint cityId, uint situationId, bool releasedOnly,
  bool needSort) : IList<PointDataFastTravelInfo>` (`WTCityManager.cs:798`), `GetFastTravelPoint(uint)`
  (:795), `GetFastTravelAttachedData(uint)` (:792), `GetWarpPoint()`, `ReqFastTravel(...)`. Elements are
  `app.worldtour.PointDataFastTravelInfo` (`PointDataFastTravelInfo.cs:7`); id/position live on the
  **base class** `app.worldtour.CityPointDataInfoBase`: `int mPointId` (:12 — **INT**, reading it as
  uint/long gives garbage), `via.vec3 Position` (:20), `via.Quaternion Rotation` (:28).
  `PointDataFastTravelInfo.mAttachedData` → `app.worldtour.FastTravelPointUserDataRecord { uint id
  :24; FastTravelPointMessage PointNameID :32 (nested, `Guid GUID` :13 — feed the existing Guid→message
  resolver); uint CityID :40; uint SceneCityID :48; bool IsMyRoom :56; uint TimeType :64 }`. Catalog:
  `app.worldtour.udCityPointList{CityPoints, FastTravelPoints}`, served by
  `app.worldtour.CityPointHolder.PointList`. State: `WTFastTravelCtrl{PointID, UnlockPointIDArray,
  CurrentState}`.
- `CONFIRMED IN GAME` (Metro City, 2026-09-04): `GetFastTravelPointList` returned **8 points** for
  `cityId=400, situationId=1`. Context for reproducibility: `city=400, situation=1, section=0,
  CitySectionManager.CurrentSectionId=0, SectionInfoList count=0`.
- `CONFIRMED IN GAME` (second run, 2026-09-04 ~20:59): `mPointId` reads correctly as **int** (values
  seen for the 8 points above: 1, 3, 5, 6, 7, 8, 10, 68), and the localized names resolve through the
  existing Guid→message resolver path (e.g. 'Beat Square', 'Westbay Promenade', 'Urban Park', 'Beat
  Street - Pórtico de Chinatown').
- Fast-travel semantics (2026-10-01): the native point list and phone map `TRAVEL` group now feed
  `DestinationCategory.Travel`, retaining the native name plus the translated fast-travel kind.
  This identifies a point; the catalog includes unreleased points and asserts no unlock or current
  interaction state. Selecting one requests walking guidance, never travel execution.
- `CONFIRMED (decompiled), contact runtime check pending`: `om.WTOmAccessTarget.IsFastTravelOpenOM()`
  identifies the semantic object; `OmComp` may be the exact type `om.Om022000`, whose `FastTravelCtrl`
  points to `WTFastTravelCtrl.PointID:uint`. `FastTravelContact` accepts a positive semantic result
  or that exact OM type, preserves `GetDispName`, and uses the exact point ID only when the contact
  lacks a name. The `Interact` announcement additionally requires membership in the existing
  `AvatarManager.CurrentAccessInfoList`; nearby landmark coordinates never confirm interaction.
  The first successful live classification writes `Fast-travel contact identified` to the log.
  Read-only smoke test: approach a fast-travel object until its interaction is available, press Z,
  and check for `Interact: <native name>, fast travel` (or the generic kind if no name/ID resolves).
  Step back while still near the landmark: Z may identify the nearby travel point but must drop
  `Interact` once it leaves `CurrentAccessInfoList`. Repeat beside an ordinary nearby NPC to check
  that a travel landmark does not rename that contact; open the picker/dialogue and confirm the
  field interaction voice pauses. These checks do not activate, unlock or execute travel.
- Sections/districts: `app.worldtour.CitySectionManager`: `CurrentSectionInfo`(13),
  `CurrentSectionId:uint`(37), `SectionInfoList`(29), `UpdateCurrentSectionId()`(65),
  `CheckUsingSectionMap(uint)`(59), `RequestSectionNotice(...)`(68). Volumes:
  `CitySectionZone` / `CitySectionZoneGroup`.
- Streaming: `app.worldtour.WTAreaManager`: `OnAreaTransition`(1366), `InTransition`(1554),
  `getActiveAreaList()`(1573), `isAreaActive(string)`(1576), `isNearActiveArea(vec3, ref
  IList<uint>)`(1630).
- Interaction: `app.worldtour.WTContactSystem.GetActiveCallObjects() : IList<GameObject>`(8035),
  `ZoneInfoList`(7842), `EnterZoneContact`(8044).

### 7b. Naming what a ray HITS, and sub-zones finer than a district (2026-09-07 research) — `UNVERIFIED`

User request: "farola, poste, mostrador, saliendo de mostrador, puerta" on contact, and zones finer
than the district ("calle X"). What the game has, from the decompiled sources (`sf6 code/`) plus
every F10 probe dump in `reframework/data/`:

- **No object labels on geometry.** There is no `app.*Prop*`/`*Door*`/`*Interact*` component on
  World Tour static geometry; the `app.worldtour.om/*` classes are behaviour scripting for OMs, not a
  name registry. A hit's `Collidable.GameObject` (`sf6 code/via.physics/Collidable.cs:19` →
  `via.GameObject.Name`) is CONFIRMED reachable (`FieldRayProbe`, `getContactCollidable(uint)`), but
  across all probe dumps the only values are `(no GameObject)` — baked level geometry — and chunk ids
  `wtc0400_01` / `wtc0400_10`. "Farola" cannot come from a name.
- **Three things a hit DOES expose** (now written per contact by the F10 probe, see below):
  1. **collision layer** — `Collidable.FilterInfo.Layer` against `app.gCollision.LayerId`
     (`Terrain, Character, TerrainRay, Attack, Damage, Sign, OMPress, GayaPress, NpcPress,
     PlayerPress, Marker, Sensor, EffectChecker, EffectCheckRay, SoundSpace, SoundPosition, SoundRay,
     SoundWall, EnvMarker, EnvSensor, ChainSelf, ChainEffector, Dynamic, Static`), named live through
     `via.physics.System.getLayerName(uint)`. `Sign`/`OMPress`/`EnvMarker`/`EnvSensor` vs `Terrain`
     is the coarse "interactable vs wall" split;
  2. **surface material** — `via.physics.MaterialInfo{ Id, Attribute1..3 }` (`sf6 code/via.physics/
     MaterialInfo.cs:15-45`), reachable from the game's own wall contacts
     (`AvatarFieldParam_Volatile.CollisionInfo.WallContactInfoList → WallInfo{ Contact, Material }`,
     § 5) and from `app.CollisionSystem.getWTEColMaterialID(vec3, bool, float)` (`CollisionSystem.cs:831`).
     The only id→string table is the footstep system, `soundlib.SoundBodyMaterialParam`
     (`MaterialNames : String_Array1D`, `MaterialValues : UInt32_Array1D`, `.cs:69-107`): that yields a
     SURFACE word (concrete/wood/metal/glass/grass — internal English strings, to be mapped to
     localized words once the real set is seen), not an object word. "Metal" for a lamp post,
     "wood/glass" for a counter or a door is the most this route can give;
  3. **the GameObject's Folder name and component types**, when a GameObject exists — an OM hit
     carries an `app.worldtour.om.*` component, which is the real "this is a door / counter / vending
     machine" signal, and it already has a NAME through `WTOmAccessTarget` (§ Notable vs. crowd).
- **Sub-zones.** The district (`CitySectionManager` → `CitySectionDataUserDataRecord.SectionNameID`)
  is the finest LOCALIZED level the game has; there are no street names. Candidates below it, none
  wired: `app.UICityHud_SectionNotice.mTextSection : Text` (`UICityHud_SectionNotice.cs:147`, the
  district banner's own resolved text — a cross-check, not new information);
  `app.UICityHud_CityMiniMap.DispCityId/DispSectionId` (`.cs:11`); `app.worldtour.WTAreaManager`
  `AreaInfo.AreaName : string` / `AreaNameHash` (`WTAreaManager.cs:341,349,1119`) plus
  `NeighborhoodAreaList/AdditionalAreaList` (`:1197-1221`) and `WTCityManager.IsAreaStateActive(string)`
  (`WTCityManager.cs:855`) — streaming CHUNK names (likely the same `wtc0400_xx` codes), useful at
  most as an indoor/outdoor or "entered a shop interior" signal; `PointDataCityInfo` /
  `udCityPointList.CityPoints` (city points beyond the 8 fast-travel points ZoneHooks already uses —
  unexplored, may hold named shop/landmark POIs for a finer "near X"). `CityMessageUserDataRecord.
  CityFlavorMessage` is a city description blurb, not a name.
- **What shipped from this (both UNVERIFIED):** (a) the F10 probe's per-contact line now appends
  `layer=N(Name) grp=g/s mat=Id attr=a/b/c folder='..' tag='..' comps=[..]` (`Services/WorldTour/
  FieldProbeService.Collidable/MaterialText/GameObjectFolderAndTag/GameObjectComponents`); the
  `Material` member is documented on `app.CollisionSystem.HitResult`/`WallInfo`, NOT confirmed on
  `via.physics.ContactPoint`, so `mat=?` in a dump means "read the material from § 5 instead";
  (b) "Leaving X" (`wt.leaving`) from the arrival reader when the announced interactable's range is
  left with nothing else in range (`Hooks/WorldTour/FieldAwarenessHooks._announcedTarget`).
- **First dump (15:41 2026-09-07, a plain wall), `CONFIRMED IN GAME`:** `obj='wtc0400_24'
  layer=1(Terrain) grp=383/0 mat=? attr=?/?/? folder='wtc0400_24' tag='' comps=[via.Transform,
  via.render.Mesh, via.physics.Colliders, via.dynamics.RigidBodyMeshSet,
  app.sound.SoundObsOclTargetApp, app.WTEnvController]`. So: level chunks DO carry a GameObject
  (chunk id, `app.WTEnvController`), `via.physics.ContactPoint` has NO `Material` member (`mat=?`),
  and the layer names resolve live (`getLayerName` works: Terrain, TerrainRay, Character, OMPress,
  EffectCheckRay, Dynamic seen on the avatar's own rays). Material must come from § 5's
  `WallInfo.Material`, not the cast.
- **The diagnostic is now AUTOMATIC (2026-09-07), not F10.** The player is blind and cannot aim a
  one-shot probe key at a specific object, so `Services/WorldTour/ContactCatalog.cs` fires itself:
  every navigation-radar sample where `FieldNavVerdictService`'s own verdict is the one
  `FieldNavRadarHooks.BlockPhrase()` speaks as "wall"/"blocked" (never the ray-height ladder), it casts
  once along the camera-forward direction (`FieldNavRadarService.CastFront`, same
  `SWEEP_REACH_M` reach as the compass sweep), takes the NEAREST contact, and logs
  `[SF6Access] Contact: dist=X.Xm layer=N(Name) grp=g/s obj='..' folder='..' tag='..' comps=[..]` —
  built from the same `FieldProbeService.Collidable/GameObjectName/GameObjectFolderAndTag/
  GameObjectComponents` helpers the F10 probe uses, so no reflection is duplicated. It logs only when
  everything but `dist=` changed since the last logged line, at most once a second, so a session's log
  becomes the "what did I just bump into" answer without needing sight to aim anything: walk around
  normally with the radar on, bump into a lamp post, a counter, a door, a fence, a plain wall, then read
  the distinct `Contact:` lines back. Wired only into the continuous-radar (Shift+B) tick, since that is
  the only place the verdict is sampled repeatedly; a single **B** press also computes the verdict but
  is a one-off readout, not a sampling loop, so it does not call the catalog.

### Design note
The RE7 mod's radar (`D:\code\re engine\Re7Access`) is **reactive echolocation of geometry by raycast**
— a different job from the entity radar SF6 already has (`FieldAwarenessHooks`, § World Tour — field
awareness above). **Open question, revised 2026-09-04 (second run):** whether the navigation radar
should be built on the avatar's own raycast API (§3 — `GetCastRayPosition`/`CastRay`/`CastRayAll`, now
A/B-confirmed open-street vs. wall) or on the NavMesh handle (§6 — `AIMap.findMapHandle()`, now
confirmed to return a valid handle) is **UNDECIDED**. An earlier version of this note asserted NavMesh
could not be used and the design had to be raycast-based; that assertion is **retracted** — it rested on
§6's since-retracted "NavMesh is dead" finding, which was a field-read bug, not a real API failure (see
"Value-type reads: a known trap" above). Do not assume a winner until node queries against the NavMesh
handle have been exercised (still `PENDING RUNTIME VERIFICATION`, §6). In favor of the raycast option:
it gives real world-space ray endpoints from the game's own per-frame movement/animation casts and
avoids inventing offsets — the furniture false-positive concern a naive raycast approach would hit is
mitigated by reusing those authored `CastRayTypes` rays rather than casting arbitrary directions. Also
unlike RE7:
since World Tour is played in third person, navigation rays must originate from the **avatar's**
transform, not the camera (RE7 is first-person, so camera-origin rays were correct there) — while the
*directional frame* for reporting hits should still be the **camera's**, because World Tour movement is
camera-relative (already calibrated and confirmed in game, see § Clock direction (camera-relative)
above, `Services/WorldTour/FieldDirectionService.cs`).

### 7. Shipped implementation — the navigation radar (2026-09-04)
The Design note's open question is **settled for the first cut in favour of the raycast route**: it was
already A/B-confirmed live, it needs no node queries, and it satisfies the no-magic-numbers rule for
free (the obstacle class comes from WHICH named ray hit, so no height or reach is ever written down).
The NavMesh route stays open for a future "which way is walkable" layer; nothing here forecloses it.

- `Services/WorldTour/NavReading.cs` — the state model: `FrontProfile { Open, Step, WaistHigh, Wall,
  TallWall }` plus a `NavReading` struct (front class, nearest forward contact distance, per-side
  blocked flags, ground solid). `SameStateAs` deliberately **excludes distance** — including it would
  make every sample a "state change".
- `Services/WorldTour/FieldNavRadarService.cs` — the sensor. Nine casts per sample through
  `CastRayAll` with `TerrainRayFilter`; ray names and the filter id resolved by NAME from the TDB once
  per process; the `CastRayAll` handle cached per concrete field-state type; one engine-allocated
  `CastRayResult` per sample (never globalized, never held across frames); distance from
  `ContactPoint.Distance` via a getter bound on first use.
- `Hooks/WorldTour/FieldNavRadarHooks.cs` — **B** one-shot readout, **Shift+B** continuous reactive
  mode (spoken obstacle class on confirmed state change, a descending three-note motif for a drop; the
  `impassable.mp3`/`exit.mp3` open/closed cues moved to `NavBeams` 2026-09-07, see §9 below).

Ladder rule for the front class: the HIGHEST rung of `FOOT_FRONT < FRONT < WAIST_FRONT < BUST_FRONT <
HIWALL_FRONT` that reports a contact decides the class (`FOOT_FRONT`/`FRONT` share the low tier). Read
that way rather than as exact hit combinations, the class stays right when a lower ray misses under an
overhang. `FRONT_LONG` feeds **distance only** — classifying by it would report a wall two metres off
as something the player is standing against.

### 8. Sideways reach — why the radar casts two segments of its own (2026-09-04)
**Symptom:** in game the front cues fired correctly but the sides *never* did.

**Measured cause**, from the game's own `GetCastRayPosition` output (dump
`sf6access_fieldprobe_212037.txt`, player at `(-2.510, 0.091, -72.181)`, all rays at chest height
`y = 0.691` except the foot/waist/bust/hiwall rungs):

| ray | reach | direction |
| --- | --- | --- |
| `SIDE_R` / `SIDE_L` | **0.50 m** | ∓AxisX (pure sideways) |
| `BESIDE_R` / `BESIDE_L` | 0.40 m | ∓AxisX |
| `RIGHT_FOWARD` / `LEFT_FOWARD` (sic) | 0.60 m | mostly sideways, slight forward tilt |
| `FRONT_R` / `FRONT_L` | 1.30 m | **parallel to `FRONT`**, origin offset ±0.40 m sideways |
| `FRONT` | 1.30 m | +AxisZ |
| `FRONT_LONG` | **2.00 m** | +AxisZ |

The game publishes **no long sideways ray** — the avatar only needs short ones to hug a wall. With a
0.50 m feeler you must be all but touching a wall for a side to report, which is why the sides were
silent. `FRONT_R`/`FRONT_L` are *not* sideways probes: they are shoulder-offset forward feelers, so
they light up together with the whole front stack and were deliberately **not** added to the radar
(two more casts, no new information, and a wall approached head-on would fire a left *and* a right
cue).

**Fix — `Services/WorldTour/FieldNavSideRays.cs`:** cast our own segment through the free-form
overload `CastRayAll(ref vec3 start, ref vec3 end, CastRayResult, eFilterInfo)` (`:2453`). Everything
in that segment is still read from the game at run time, nothing is written down:
- **origin + direction** = the published `SIDE_R`/`SIDE_L` segment itself, normalized;
- **length** = the length of `FRONT_LONG`, the state's own longest forward probe. The radar's
  sideways reach is *defined as* the game's own longest forward reach, so the sensor is symmetric.

The long rays **replace** the published short ones (any hit inside 0.50 m is inside the longer
segment), so the sweep is still **nine casts per sample**, plus three `GetCastRayPosition` segment
reads, which cast nothing. If the free-form overload or its buffers cannot be bound, the radar falls
back to casting the short published pair and says so once in the log — a short truth beats a long guess.

**Handedness (important):** `SIDE_R` runs along **−AxisX**, not +AxisX (`AxisX = (0.956, 0, 0.293)`,
`SIDE_R` direction `(-0.956, 0, -0.294)`). That agrees with the right-basis `(-fz, fx)` that
`FieldDirectionService` had already confirmed in game for the clock readout. Taking direction from the
game's own ray instead of assuming an axis sign is what keeps the sides from being mirrored.

**`ref` vs `out` buffers:** those two `vec3` parameters are **inputs the engine reads**, the opposite
direction of travel from every other buffer in the probe, but the same memory rule applies — unmanaged,
over-reserved, aligned `FieldOutBuffer` (never `CreateValueType`). `FieldOutBuffer.SetComponent` writes
through the game's own field metadata and the value is **read back** before the cast; a mismatch skips
the call rather than handing the engine a half-filled struct. The buffers are allocated once for the
process (unmanaged memory never moves) instead of per sample.

**Also fixed:** a front class change *while still blocked* (kerb `Step` → `Wall`) used to be silent,
because cues only fired on open↔blocked. It now speaks the new class — **no** sound, since neither
"closed" nor "opened" happened and re-firing a cue would lie; the 2-sample confirmation and the reader's
duplicate filter keep it from chattering.

### 9. Front verdict — the game's own obstacle answer, not the ray ladder (2026-09-05) — `PENDING RUNTIME VERIFICATION`
**Symptom (user, in game):** "exit"/"open" announced while a waist-high wall was still stopping the
avatar, and "wall" announced for things the avatar walks straight over.

**Root cause.** The front class was derived from the height ladder alone, and a ladder of rays cannot
answer "am I stopped". Three separate reasons: (a) the rungs have **different reaches** (`FRONT` 1.30 m,
`FRONT_LONG` 2.00 m, the foot/waist/bust/hiwall rungs shorter), so mixing them makes "a rung hit" mean
different distances per rung; (b) `TerrainRayFilter` does **not** see the fences and props the capsule
nevertheless collides with, so a real obstruction can produce zero ray hits; (c) wall-ride walls read as
plain walls. Meanwhile kerbs and low fences the game **auto-steps** lit up the low rungs and were spoken
as obstacles.

**Fix.** The rays now feed DISTANCE and DESCRIPTION only. Whether the avatar is blocked comes from the
collision the game already resolves every frame — `Services/WorldTour/FieldNavVerdictService.cs`.

Sources, in the order they are trusted (each resolved once and cached by the owning type's name):

| # | route | members read |
|---|---|---|
| 1 | `AvatarBase.__GetVolatileParam().Collision` (`AvatarFieldParam_Volatile.CollisionInfo`, `:164`) — fall back to `AvatarState_FieldBase.VolatileParam` (`:2303`) | `ValidWallContactInfo`(`:332`), `IsWallContact()`(`:485`), `IsContactedDashStopWall`(`:428`), `WallMovableRate`(`:436`, 0..1) |
| 2 | `AvatarBase.Components.CharacterController`, else `AvatarState_FieldBase.CollisionManager`(`:2367`)`.CharaController`(`AvatarCollisionManager.cs:385`) | `Wall:bool` |
| 3 | `AvatarState_FieldBase.CollisionCache`(`:2375`) → `AvatarCollisionCache.GetGoupStepInfo(GoupStepTypes)`(`:248`) | `DoesGoupStepCheck`(`:220`), `GoupStepInfo.GetFlag(CheckFlagType.Seted)`(`:150`) |
| 4 | `AvatarBase.GetContactedWallInfos(IList<ContactedWallInfo>)`(`:1602`) — **DISABLED 2026-09-06** | `ContactedWallInfo.CanWallRide`(`:820`) |

Route 4 crashed the game: `FieldProbeService.NewInstance` built an instance of the generic interface
`IList<ContactedWallInfo>`, REFramework wrapped the non-object it got back and the wrapper's finalizer
raised `AccessViolationException` (`ManagedObject.Finalize`, GC thread) about 90 s into the field with
a clean log. `CanWallRide` now returns false unconditionally (never claimed) and `NewInstance` refuses
interfaces/abstract/generic types (see `docs/sf6-architecture.md`). Re-enabling needs a constructible
concrete list type the engine accepts, e.g. `System.Collections.Generic.List<ContactedWallInfo>` if
the TDB exposes that instantiation — not attempted.

Confirmed signatures (decompiled, this run): `GoupStepTypes` is a **top-level** enum
`app.worldtour.avatar.GoupStepTypes` = `{XS, S, M, FenceF, _NUM_}` (`_NUM_` is a count sentinel, never
queried); `CheckFlagType` is nested as
`app.worldtour.avatar.AvatarCollisionCache.GoupStepInfo.CheckFlagType` = `{Seted, RayRight, RayLeft,
RayCenter}`; `GoupStepInfo` also publishes the raw `CheckedFlags:int` bitmask, but `GetFlag()` is used so
no bit position is written down. `AvatarConstMoveParams.DashMove_StopWallRate` (`:120`) is an
**instance** property (unlike the `static` members around it) reached from
`AvatarState_FieldBase.ConstMoveParams` (`:2231`) or `__GetConstMoveParam()` (`:2810`). Note the game
counts **fences** among the things it auto-steps (`GoupStepTypes.FenceF`).

**Priority for the front verdict** (`NavReading.Block`, new enum `FrontBlock {None, WallRide, Blocked}`):
1. **Wall contact this frame** → `Blocked`, in the game's own order of authority: invalid wall info →
   not blocked; no `IsWallContact()` → not blocked; the contact is wall-rideable → `WallRide`;
   `IsContactedDashStopWall` → `Blocked`; otherwise `WallMovableRate < DashMove_StopWallRate` →
   `Blocked`. A rate outside its own 0..1 range counts as unread, and an unread rate never downgrades a
   wall the player is touching to "open".
2. **else** the auto-step cache has any size class `Seted` → the front is re-described as
   `FrontProfile.Step` and nothing is blocking (this is the kerb/fence false positive).
3. **else** the ray ladder's class stands as a **description** of what lies ahead within those rays' own
   reach, with `Block = None`.
4. **else** open.

`FrontProfile` keeps its five members but is now documentation of *what is ahead*, never *am I stopped*.

**Announcement rules** (`FieldNavRadarHooks`; cue vocabulary moved to beams 2026-09-07, see below):
- The game's verdict (`NavReading.Block`) drives SPEECH only: `wt.nav_front_wallride` on entering a
  `WallRide`, and the named class (or `wt.nav_front_blocked`) on `None → Blocked` **and** again on a
  blocked-to-blocked class change (a kerb stepped up to the wall right behind it) — the word alone
  carries a change of obstacle, no sound plays for either case.
- `WallRide` never fires the impassable cue.
- `impassable.mp3` / `exit.mp3` no longer belong to this verdict — they moved to `NavBeams` (below),
  panned per beam instead of tied to the front verdict's open/blocked transition.
- `CONFIRM_SAMPLES` debounce unchanged.

**Fallback and diagnostics.** If **no** wall route binds, the radar reverts to the previous behaviour
for blocking (any hit in the height stack = blocked) and logs a warning. The step route is independent:
"the game will climb this by itself" holds whether or not a wall route bound, so it is applied first
and ends the question -- nothing the avatar surmounts on its own is ever reported as a block. One info line is written
once per session naming which routes answered:

```
[SF6Access] NavRadar verdict routes: volatile=ok, charaCtrl=missing, goupStep=ok, wallRide=untried, stopWallRate=0.300 (4 step classes, Seted=0)
```

`charaCtrl=missing` is EXPECTED when `volatile=ok` — route 2 is only tried when route 1 does not bind.
`wallRide=untried` is expected until the avatar actually touches a wall; on a contact frame it reads
`wallRide=disabled` (route 4 is deliberately not read, see above) — wall-ride is never claimed. `stopWallRate=fallback 0.50` means
`DashMove_StopWallRate` could not be read and the midpoint of `WallMovableRate`'s own 0..1 range is
being used instead.

**Still unverified in game:** every one of the above. In particular: whether `WallMovableRate` stays low
while merely *standing* against a wall (if it reads high when the avatar is not pushing, a stationary
player at a wall would be reported not-blocked); whether `GoupStepInfo.Seted` means "found something
climbable this frame" rather than "this slot has ever been initialised"; and whether route 4 binds at
all. Read the route log line first — it says which of these the run actually exercised.

**Continuous-mode openings — three camera beams, not an armed exit chime (2026-09-07) — `RETIRED
2026-09-08, see § 9b's retraction note below: this second-pass beam design and the third-pass rewrite
that followed it are both gone, replaced by the continuous tone bed.`** The
2026-09-05/06 armed-exit design (`FieldNavRadarHooks._exitArmed`/`ExitCheck`: armed on
`None → Blocked`, released once `not-Blocked` **and** `LongRangeClear` held for `CONFIRM_SAMPLES`) and
the 2 m side feelers it used are GONE from the continuous mode. Symptom: "exit" only ever meant
"nothing inside the 2 m forward feeler", with no direction and no persistence, so turning inside an
enclosed yard to look for the way out produced "wall, exit, wall, exit" from every gap two metres deep
without ever pointing at the street (user report 2026-09-07).

Replacement, in `Services/WorldTour/NavBeams.cs` + `FieldNavRadarService.Beams`/`FreeCastContext`:
three CAMERA-relative beams — front, left, right — each `FieldNavRadarService.BEAM_REACH_M` (30 ×
`SWEEP_REACH_M` = 360 m, effectively unbounded) cast from the origin of the game's own `FRONT_LONG` ray
via `FieldNavSideRays.TryCastDirection` (the shared free-form-cast plumbing, §8). A Hero's Call's
porting guide is explicit that a capped reach turns "the far wall came into range" into a phantom cue
and kills open-field silence, so there is no open/closed STATE any more — each beam just tracks its hit
distance (a miss counts as the full reach), and a cue fires only on a confirmed JUMP in that distance:
- **Jump gate:** `NavBeams.IsJump` — the reach must move by at least `NavBeams.JUMP_M` (0.25 ×
  `SWEEP_REACH_M` = 3 m) AND by at least 25% of the shorter of the two readings. A facade edge a hundred
  metres off that shifts the beam a few metres is scenery; five metres becoming nine is a doorway. Smooth
  drift — walking along an angled wall, approaching a wall head-on — never crosses the gate and stays
  silent; the spoken contact verdict (above) covers the head-on case.
- **Silent first seed**, per beam: the first confirmed reading describes where the player already
  stands and is not an event.
- **Confirmation:** `FieldNavRadarHooks.CONFIRM_SAMPLES` (2 samples of the 10-tick sample cadence) — a
  lamp post or a railing flickering a beam for one sample cues nothing. This is the STANDING-STILL
  threshold; while walking a longer one applies (below).
- **Moving vs. standing role, and a longer hold while walking (`NavBeams.Sample(hits, moving)`, new
  2026-09-07).** `moving` comes from `FieldNavRadarHooks.Moved()`: the avatar's XZ position sampled each
  tick against the previous sample, displaced ≥ `MOVED_EPS_M` = 0.05 m (a twentieth of a metre in a
  sixth of a second — a fraction of the slowest walk, well above position-read jitter; an unreadable
  position counts as standing, the quieter role). Two effects while `moving` is true:
  - **The FRONT beam never cues.** It keeps tracking silently (`s.Announced` follows every sample) so
    that the instant the player stops and turns, the scan starts from where it already is; walking
    toward things changes the front beam constantly, and the spoken contact verdict is already the
    front's warning while moving. The side beams keep cueing while walking — that is how a cross street
    is heard on the side it lies on.
  - **A confirmed change must hold for `MOVING_HOLD_SAMPLES` = 6 samples (one second at the 10-tick
    cadence) instead of `CONFIRM_SAMPLES` = 2, in EITHER direction (open or close).** Session log
    2026-09-07: open/close pairs half a second apart on the same beam ("Right opened to 30.8m" then
    "Right closed to 3.5m" ~0.5 s later), 20 cues inside one 14 s burst — every one of them a false
    positive reported by the player. At walking pace, one second of persistence is roughly a four-metre
    feature: long enough to be a wall or a real cross street, short enough that a lamp post, a tree, a
    parked car or the gap between two buildings — which cross a side beam in well under a second —
    never confirm. A change that never confirms simply leaves the announced level alone (`s.Pending`
    keeps following the drift), so the beam's return to the facade after a too-brief opening is not a
    second event either. Standing still, the ordinary `CONFIRM_SAMPLES` applies, so a turn-scan still
    hears both edges of a gap promptly.
- **Per-beam cooldown:** `NavBeams.BEAM_COOLDOWN_MS` = 500 ms, so a beam jumping every confirmation
  window still cannot chatter faster than twice a second.
- **One cue per sample, openings first:** if several beams change in the same sample, a farther reading
  (opening) beats a nearer one (closing — "a way out is worth more than "still a wall"), then front,
  left, right in that fixed order; the rest wait for the next sample if they still differ, and are
  swallowed if they drifted back.
- **Cue:** `exit.mp3` when the reach jumps farther ("opened"), `impassable.mp3` when it jumps nearer
  ("closed"), panned to the beam (`FieldNavRadarHooks.SIDE_PAN` = ±0.8 for left/right, centred for
  front); the closing cue's VOLUME scales 0.15-0.5 by nearness over one street width
  (`NavBeams.CLOSED_FAR_VOLUME`/`CLOSED_NEAR_VOLUME`) — a wall two metres off is news, one at the edge of
  a street width is scenery.
- **Turning is a deliberate scan:** the beams are re-aimed from the camera every sample, so sweeping the
  camera across a gap plays the "opened" cue as the front beam enters it and the "closed" cue as it
  leaves — both edges of the gap — and turning back plays them again. Standing still in a closed yard is
  silent, because nothing changed: the silence IS "no way out here", and the chime IS "there".
- Log line: `Nav beam Front opened to 42.0m` / `Nav beam Left closed to 4.1m`; `Nav beams failed` if the
  sensor could not bind that sample.
- Design source: `D:\code\modding projects\reference\audio-navigation\united-minecraft` (per-bearing
  diff gate + cooldown + open-first dequeue) and its `a-heros-call` sibling (three beams, silent seed,
  one cue at a time, turn-scan as an explicit design choice).

The spoken class ("wall", "blocked", "wall you can run along") still comes from the game's own verdict
(above), unchanged; only the open/closed SOUNDS moved from the verdict to the beams, so a contact is
spoken once and the way through/around it is chimed, never the same fact twice — `impassable.mp3` is no
longer played on the verdict block itself. **Note:** the one-shot `B` readout still reports left/right
open/blocked from the original 2 m side feelers (`NavReading.LeftBlocked`/`RightBlocked`), so it can
disagree with what the continuous mode's beams say about the same spot — different sensors answering
different questions ("can I hug this wall" vs. "is there a way through at street scale").

### 9b. Continuous-mode beams, THIRD pass (2026-09-07) — `RETIRED 2026-09-08, code deleted`

**RETRACTED — history note, read before reaching for a beam design again.** Everything below describes
`Services/WorldTour/NavBeams.cs` as it stood after the 2026-09-07 rewrite. That file, `FieldNavRadarService
.Beams()` and `BEAM_REACH_M` are **deleted** as of the 2026-09-08 precision rebuild (§ World Tour —
spatial navigation APIs, "2026-09-08: precision rebuild" below). `LocalizedText.NavExit` is now unused
code left over from this design.

The reason was not a bug in this particular pass — the three-beam and four-beam designs were both
internally correct — but the whole *shape* of the thing: every version here was **event-only**, silent
except at the instant a beam's reading crossed a threshold, and each of the three 2026-09-05/06/07
tuning rounds made it fire *less* in order to kill chatter, which left it with almost nothing to say most
of the time. It is replaced by an always-on continuous 4-tone bed (`NavToneBed.cs` +
`NavProximityBed.cs`) that never needs a threshold because it has no state to flicker between — see the
2026-09-08 section for the design and the reasoning (the shared audio-navigation reference's "3D
first-person explorer" row, which this project had been building the *reactive supplement* for without
ever building the *continuous backbone* it supplements). The spoken front-verdict system this sat next
to (§9 above) is untouched by the retirement.

Kept below for the record, since two real techniques live in it that may be worth reusing elsewhere: the
open/closed hysteresis band, and the moving-vs-standing hold-time split.

`Services/WorldTour/NavBeams.cs` rewritten after the 15:40 session (102 `Nav beam` lines in 7 min,
user: "only the side cues sound; I don't want them by distance; long reach like A Hero's Call, but
tell me what is nearest — an exit, an enclosed space"):

- **Four beams** (`Beam.Front/Left/Right/Back`), cast by `FieldNavRadarService.Beams` at
  `BEAM_REACH_M` (30 street widths) from the game's long-forward-ray origin; the back beam is
  `-forward`.
- **State, not jumps:** a beam is OPEN once it reads ≥ `OPEN_M` = `SWEEP_REACH_M` (12 m, "room to
  walk a street width") and CLOSED again only under `CLOSED_M` = 0.75 × that (A Hero's Call's plane
  epsilon as hysteresis). Silent first seed; a change must hold `CONFIRM_SAMPLES` (2) standing or
  `MOVING_HOLD_SAMPLES` (6 = 1 s) walking; per-beam cooldown 500 ms; ONE cue per sample — exits first,
  then the nearest closed surface (`State.Reach`). The front cues while walking too (state changes
  only at 9/12 m, so one "wall ahead" per wall).
- **Constant volume** (`AudioService.DEFAULT_VOLUME`); pan ±`SIDE_PAN` for the sides, centre for
  front/back, back pitched down with `HomingCue.BEHIND_RATE` (the beacons' own convention).
- **Enclosed / exit, spoken:** all four closed → `wt.nav_enclosed` once; the first beam to reopen
  → `wt.nav_exit_{front,left,right,back}` on top of the open cue. Outside an enclosure only the
  cues play.
- Log: `Nav beam <Beam> opened|closed at X.Xm`.

### 9c. Precision rebuild — player-filter sensor, continuous tone bed, NavMesh exits (2026-09-08) — `PENDING RUNTIME VERIFICATION`

**Symptom (tester, blind, the mod's actual user):** "no logro nunca saber con precisión dónde hay una
salida, o dónde realmente no puedo pasar" — cannot ever tell precisely where there is an exit, or where
passage is genuinely blocked. This is the failure every beam iteration in §9/§9b tried to fix by tuning
chatter down; this rebuild instead re-examined what was being measured and how it was being reported, and
found five separate causes.

**Diagnosis.**
1. **Wrong filter.** The sweep casts with `eFilterInfo.TerrainRayFilter`, measured in game (§2 above) as
   `layer=3:TerrainRay mask=0x8 [TCStopCamera]` — it measures what stops the CAMERA, not the avatar.
2. **No continuous backbone.** The whole radar was 100% event-driven: it only ever spoke or sounded on a
   CHANGE. The three beam redesigns across 2026-09-07 all consisted of making it sound *less*, which left
   it with almost nothing to say. The shared cross-game reference
   (`D:\code\modding projects\reference\audio-navigation\README.md`, "Which to use" table, "3D
   first-person explorer" row) says the backbone for this genre must be CONTINUOUS sonification, with
   discrete events as the supplement — this mod had only ever built the supplement.
3. **Two contradictory notions of "open".** `NavBeams.OPEN_M` = 12 m (a street width, a UX choice) vs.
   the game's own rays at ~2 m reach — a beam and the ray ladder could describe the same spot as open and
   blocked simultaneously.
4. **Synthetic side rays.** `SIDE_R`/`SIDE_L` (real published reach 0.40-0.60 m, § 8 above) were stretched
   up to 6× their real length, amplifying grazing-angle noise rather than removing it.
5. **Unclassified contacts.** Nothing discarded a walkable slope/ramp against the avatar's own
   `SlopeLimit`, or a kerb (auto-stepped by the game) against a real wall — every blocking-shaped contact
   was treated the same.

**Fix 1 — a new sensor cast with the avatar's OWN filter, not the camera's.**
`Services/WorldTour/FieldRayCaster.cs` (+ `FieldRayContacts.cs`, `FieldRayMetrics.cs`, `RayHit.cs`) is a
new, independent sensor — it does not replace or call into `FieldNavRadarService` (§7-§9's nine-cast
sweep, still `TerrainRayFilter`-based and still what drives B/Shift+B's SPOKEN obstacle class, unchanged
by this rebuild).

- **Overload, bound by SHAPE:** `app.CollisionSystem.castRayAll(vec3, vec3, via.physics.CastRayResult,
  via.physics.FilterInfo, bool)` — five parameters, parameter 0 a value type, parameter 3 a REFERENCE
  type. That last property is what tells it apart from its twin at `CollisionSystem.cs:805` taking the
  `eFilterInfo` enum (a value type) — see § 2 above. No name string is matched; a build where no overload
  fits this shape casts nothing and says so in the route log, rather than falling back to the camera
  filter silently.
- **Filter:** the avatar's own live `CharacterController.FilterInfo`
  (`WTPlayerManager.GetAvatarPlayer()` → `AvatarBase.Components` — **one object, never a collection**,
  the same trap documented in § 4 — → `CharacterController.FilterInfo`), not a table entry from § 2's
  `eFilterInfo`.
- **Origin:** the capsule's own mid-height above the avatar's feet — `CharacterController.Height` ×
  `AvatarBase.GetCurrentCharacterControllerSizeRatio`'s height ratio, halved, then clamped inside one
  `Radius` (scaled by the width ratio) of each cap so a degenerate ratio can never place the origin
  outside the body. Third-person, so the origin is the avatar's transform, not the camera's — the same
  choice the Design note (above) already made for §7's sweep.
- **Classification** (`FieldRayContacts.Classify`): a contact's normal Y against
  `cos(CharacterController.SlopeLimit)` — at or above, it is `Ground`; at or below the negated cut,
  `Ceiling`; neither is ever a wall (ported directly from the RE7 mod's `RayCaster.cs` rule). Below a
  step height it is `Step`. The step height itself comes from the published `FOOT_FRONT` ray's own start
  height above the feet (`GetCastRayPosition`, read through the same unmanaged out-buffer plumbing § 8
  already uses for `ref` vec3 parameters), falling back to
  `AvatarConstSystemParams.RayOffset.RAY_STEP_UP` when that read fails; neither is ever written down as a
  constant. Anything left over is `Wall`, or `Unknown` when the slope cut itself could not be read — an
  unreadable normal is kept as a wall rather than silently dropped, since losing a real wall is judged the
  worse mistake for a navigation aid.
- **Architectural vs. clutter:** SF6 publishes no `isFixedObject` (the field the RE7 mod used for this on
  RE7's engine build), so the cut is the collision LAYER NAME
  (`via.physics.System.getLayerName(uint)`, § 2) against `{Terrain, Static}`, resolved once per layer id
  and cached. Static level geometry carries no `Collidable`/`GameObject` at all, and that absence is
  itself treated as architectural rather than demoted to clutter.
- **Safety, carried over from the F10 probe's lessons (§ 1, § 3, § 8):** the `CastRayResult` is allocated
  fresh per call and never held across frames; the vec3 endpoints are unmanaged `FieldOutBuffer`s, written
  and read back through the same field metadata the engine uses, never a managed `CreateValueType`;
  `getContactPoint`'s boxed `ContactPoint` return is read as the plain boxed value, never through the
  generated interface (which reads back as zeros — "Value-type reads: a known trap" above); the
  `castRay(..., out HitResult, ...)` overloads — a reference type behind an `out` — are never called,
  the same call that is on record as having crashed `FieldRayProbe` earlier in this file.
- **Diagnostics:** one line, logged once, `[SF6Access] FieldRayCaster route: ...` — the bound overload,
  the filter's layer/group/mask, and every derived threshold (waist height, slope cut, step height,
  whether layer names bound), each naming itself as unavailable rather than being silently skipped.
- **Batching:** `TryCastMany(dirs, maxDistance, hits)` resolves the avatar, its filter and every threshold
  ONCE per sample and fires every requested direction from that same origin at that same instant — the
  audit's point that two directions of one reading must never be able to disagree because the avatar
  moved between them.

**Fix 2 — an always-on continuous tone bed, replacing event cues as the backbone.**
`Services/WorldTour/NavToneBed.cs` is a direct NAudio port of this team's own proven DRG/Megabonk
wall-sonification synth (design doc `D:\code\modding projects\reference\audio-navigation\
wall-sonification\README.md`, source `D:\code\unity and such\drg access\drgAccess\Components\
WallNavigationAudio.cs`): 4 directional channels — front 500 Hz, back 180 Hz, sides 300 Hz (pitch tells
front/back apart; § README invariant 2), sides hard-panned ±1 and front/back centred (pan tells
left/right apart; invariant 3), each a per-sample-smoothed 70% triangle + 30% sine wave (frequency
smoothing 0.05, volume smoothing 0.02, both tuned by ear in the source mods) so a changing distance is
heard as a glide, never a click. Channels are created once by `Ensure()` and mixed into
`AudioService`'s shared NAudio mixer via `AudioService.AddPersistentInput`; they are **never stopped**
for the life of the process — only their volume moves, which is the mechanism that makes "silence" mean
"open" rather than "not currently checking".

`Services/WorldTour/NavProximityBed.cs` is what drives it every sample: four rays — front/back/left/right
of the CAMERA (the frame World Tour direction is already reported in, per the Design note above) — cast
in one `FieldRayCaster.TryCastMany` call, reach `FieldNavRadarService.SWEEP_REACH_M` (12 m, the existing
"street width" constant — reused rather than inventing a second reach the bed and the rest of the radar
could disagree about), quadratic falloff `1 − norm²` (same shape as the DRG/Megabonk source), `minRange`
= the avatar's own live capsule radius (`FieldRayCaster.CapsuleRadius()`) — the closest the waist origin
can physically get to a wall is exactly where the tone must already be at full volume. A `Step` contact
is deliberately NOT sonified (it is a kerb the avatar climbs on its own; humming at it would teach the
player to avoid ground they can freely walk over); an `Unknown` contact IS sonified, on the same
worse-mistake reasoning as the classifier above. `Ground`/`Ceiling` never reach this layer at all — they
are discarded inside `FieldRayContacts` before a `RayHit` is even returned.

**Fix 3 — the actual answer to "where is the exit": the NavMesh, not a ray fan.**
`Services/WorldTour/NavMeshOpenings.cs` is the first code in this codebase to exercise § 6's confirmed
`AIMap.findMapHandle()` handle with a real node query — see § 6's own update above. Route:
`app.global.WTCommon.CityResource` → `WTCityResources.CityAIMap` → `AIMap.findMapHandle()` →
`queryClosestNode(vec3)` — picked apart from the sibling `(vec3, NodeQueryInfo)` overload by argument
count, never by name string, since a by-name call could bind either; the zero-argument
`MapHandleBase.queryNode()` is **never** called anywhere in this file (§ 6's standing warning: unbounded,
city-wide, stalls the game). Every linked neighbour of the node under the player
(`NodeInfo.queryLinkToNodes()`) is walked, and the edge shared with each is found geometrically — the
vertices the two polygons hold in common, via `getGlobalVertexCount`/`getGlobalVertex` (world space, not
`getVertex`'s node-local space, which would mix frames) — so that shared edge's length IS the doorway's
real width, at any distance, and a neighbour node flagged `Wall` is excluded regardless of that width.
`Passable` compares the width against the avatar's live capsule diameter (`Radius` × the width ratio from
`GetCurrentCharacterControllerSizeRatio`, the same live scaling § 4 documents). Nothing engine-owned is
cached between calls — the handle and every `NodeInfo` are re-resolved per query, since city streaming can
retire them — but the RESULT is cached as plain floats and only recomputed once the player has moved half
their own capsule width, which is what makes this affordable to poll a few times a second. Logs one line,
`[SF6Access] NavMesh openings: queryClosestNode(vec3) ok via WTCommon.CityResource.CityAIMap.
findMapHandle(); node (x, y, z) vs player (x, y, z); N vertices, M links, K shared edges, capsule D m` —
printing the node's own position NEXT TO the player's own already-trusted position is deliberate: an
exact `(0, 0, 0)` there is called out explicitly as "Value-type reads: a known trap" (above) rather than
reported as a real node at the world origin, since the edge widths come from `getGlobalVertex` and remain
meaningful either way.

**Negative finding — a ring of rays cannot answer "where is the exit"; recorded so it is not
retried.** `Services/WorldTour/NavOpenings.cs`, a ring-of-rays gap detector, was written during this
session and then DELETED before shipping. A standalone 2D simulation (independent of the game) was run
first and found two failures with no tuning fix:
- **Angular resolution.** With 24 rays at 15° spacing, a 1.6 m gap at 6 m subtends about 5° — no ray
  passes through it. The simulated profiles for "a straight street", "a street with a doorway" and "a
  street with a narrow slot" came out IDENTICAL. Seeing a gap that size reliably at 20 m would need on the
  order of 78 rays, which is not an affordable per-sample cast count.
- **Grazing-angle false positives.** A single flat wall viewed at a shallow angle produces a distance jump
  between neighbouring rays as large as a real gap does, so the same 24-ray fan reported a plain straight
  street as having 8 "exits".

Conclusion kept for future reference: **a ray fan can answer "what is near me on each side", never "where
is the exit" — that question belongs to the NavMesh** (Fix 3 above), which has no angular resolution limit
because it reasons about polygon edges rather than sampled directions.

**Wiring.** `Hooks/WorldTour/FieldNavRadarHooks.cs`: **Shift+B** (continuous mode) now drives
`NavProximityBed.Update` every sample instead of the retired beams (§9b); **B** (one-shot readout) is
unchanged for the spoken obstacle class but now additionally appends the NavMesh's passable openings —
`wt.nav_opening` ("opening at {hour} o'clock, {width} meters wide, {distance} meters away") for each
passable gap nearest-first, or `wt.nav_gap_narrow` for the nearest too-narrow gaps when nothing passable
is in range, or `wt.nav_no_openings` when the query itself returned nothing. The tone bed is silenced
during World Tour dialogue and on leaving the field (`NavProximityBed.Reset()` /
`NavMeshOpenings.Reset()`, called from the same `ResetContinuous()` the mode already used); the channels
themselves are torn down in `Plugin.Unload` (`NavToneBed.Shutdown()`) ahead of `AudioService.Shutdown()`.
New lang keys `wt.nav_opening`, `wt.nav_gap_narrow`, `wt.nav_no_openings` added to `lang/en.txt` and
`lang/es.txt`. **Nothing else about the radar changed:** the nine-cast sweep, the `TerrainRayFilter`
filter, and the `FieldNavVerdictService`-driven spoken class from §9 are exactly as documented there.

**Status: compiles with 0 errors/warnings; NOTHING in this section has run in game.** See `STATUS.md` §
"Built but not yet verified in game" (2026-09-08 entry) for the in-game verification checklist.

### 9d. Line-memory radar retired; body-clearance passability model shipped (2026-09-08, later session) — `PENDING RUNTIME VERIFICATION`

**2026-10-01 follow-up:** the listening log proves queued PASSABLE cues could outlive their
measurement (0.24/0.91 m at emission despite 4.56 m open and 1.52 m blocked thresholds). Playback now
requires fresh matching clearance through `RadarCueAdmission`; a debounce-held state alone cannot
authorize sound. Native rays, filter, body dimensions and thresholds are unchanged. Regression tests
exercise the actual clearance state machine and audio-lane policy. See [soundscape validation](wt-soundscape.md)
for evidence and the next user-run test; this does not establish that every geometry complaint is solved.

**Symptom that started this pass.** § 9c's "second pass" (`FieldRadarService.cs`, an event radar that
remembered the LINE each beam looked at and spoke when that line broke — a faithful port of A Hero's
Call's `ReactiveRadar` by way of the RE7 mod's `RadarService`) was measured in Metro City and fired **109
cues in 90 seconds at 109 DIFFERENT contact points** — 36 cues per 30 s, almost none coalescing into
repeats, and (unlike an earlier session that alternated between two static surfaces) no alternation
between fixed points this time. So the detector was not malfunctioning: it was correctly reporting that
a city facade changes slope roughly once a second — shopfront recesses, columns, awnings, kerbs, benches.

**Retired and DELETED — `Services/WorldTour/FieldRadarLines.cs`.** Recorded here as a negative finding,
the same way the ring-of-rays gap detector is recorded in § 9c, so nobody re-implements it. Two
independent reasons, both structural (no tuning fixes either):
1. AHC's world is a tile grid, where a slope change in the geometry IS a corner or a door. A city street
   is not, so an event model whose triggering event (a slope change) is real and near-continuous cannot
   be tuned quiet.
2. A line break means "this beam now sees PAST where the boundary was" — a depth discontinuity, not a
   passage. A 40 cm shopfront recess produces one exactly as readily as a real doorway does. This is what
   made the mod announce "exit to your right", after which walking right produced the spoken verdict
   "blocked" — the cue channel and the spoken verdict were answering two different questions and could
   openly contradict each other.

An earlier round in the same lineage had already capped the line model's reach (`ReachM`) from AHC's
1000 m down to 30 m — AHC's own `GameConfig.ScanDistance` — after measuring a beam alternating between
two static surfaces 21.2 m apart in depth, recurring to within 9-27 cm of each other: camera drift across
a depth edge. That fix worked (100 cues/30 s → 36) and its lesson (cap the reach to something the world
actually has) is not undone by this retirement; it just was not the whole problem, as the 109-cues
session above shows.

**Replacement — a body-clearance PASSABILITY model, `Services/WorldTour/FieldRadarClearance.cs`.** The
radar now asks "can the player go that way", not "did the geometry change".
- **Input:** the waist row of each beam, already cast as three parallel rays at the capsule centre and at
  ±its radius (`FieldRayCaster.TryCastBodyStack`, § 9c), taking the MOST obstructed of the three — so the
  measurement is how far the avatar's BODY can advance, not how far a single ray can. A beam grazing a
  corner cannot report the street behind it, because the outer ray hits the corner first.
- **Output** is a STATE per beam (`PassState`: `Unknown` / `Passable` / `Blocked`), not an event. Nothing
  sounds while a verdict holds; only a beam that CHANGES verdict speaks.
- **Two thresholds with a dead band between them**; inside the band the previous verdict stands. The
  avatar must physically travel metres to flip a verdict, where the line model flipped on a fraction of a
  degree of camera settle.
- **Debounce:** a changed verdict must persist for `FieldRadarTuning.StateConfirmMs` (4 sensing
  intervals) before it is believed — rejects a pedestrian or a car crossing a beam for a frame or two.
- **Silent seed:** the first verdict a beam ever forms is seeded SILENTLY, so arming the mode, a
  teleport, or entering a new area does not open with three cues at once.
- **Turning is still the scan, selectively.** While the camera turns, the LATERAL beams keep measuring
  but stay silent — every verdict they form belongs to a different slice of the world, so speaking
  mid-sweep would be reporting a place the player has already turned away from. The FRONT beam keeps
  talking regardless, so sweeping the camera still aims the player at exits (AHC's own
  `ResetAllButFrontRadar` precedent, already noted in `FieldRadarService.cs`'s own header). The beam
  pointing away from the direction of travel is likewise held silent.
- **Duplicate cues are no longer filtered — they are UNREPRESENTABLE.** A `PassState` cannot settle twice
  into the same value without passing through the other value first, so the per-beam coalescer in
  `Services/WorldTour/FieldRadarCues.cs` was deleted outright, together with the now-orphaned tuning
  constants `PosNoiseFloorM`, `MinStepM`, `LineSameCos`, `DepthDiscontinuityM`, `SameCueCoalesceMs` (all
  were line-memory-only knobs).

**One shared definition of "blocked" — the fix for the exit/blocked contradiction.** Root cause found:
the spoken readout calls a side blocked whenever anything sits inside `FieldNavSideRays`' own segment,
whose length is the game's own longest published forward feeler (`FRONT_LONG`, **2.00 m** at runtime,
§ 8 above) — while the radar's cue channel used to cut its blocked boundary somewhere else entirely.
`FieldNavSideRays` now exposes `PublishedReachM` (the last measured length of that segment) and
`FieldRadarClearance` uses it as its own blocked boundary, falling back to
`FieldRadarTuning.BlockedEnterRadii * bodyRadius` (4 body radii = 2.0 m at the reported 0.5 m capsule)
only before the probe has measured once. One boundary, measured from the game, instead of two that
happen to agree — or, as measured, do not.

**Passable threshold, and why the dead band costs nothing.** Passable requires
`FieldRadarTuning.PassableEnterRadii` (12 body radii = 6.0 m at the reported capsule). Clearance is
measured ALONG the beam, so a side alley reports its own DEPTH (tens of metres) while the open side of an
ordinary street reports half its WIDTH (one to three metres) — the quantity is bimodal with nothing in
between, which is why a dead band this wide buys total stability for free. **Honest caveat, recorded so
it is not mistaken for a game-published number:** the 12-radii multiplier is a DESIGN choice, calibrated
against RE7's own measured door-leaf span of 3 body radii (`FieldRadarTuning.DoorLeafSpanRadii`, cited to
RE7's `RadarService.cs:193`), not something read from SF6 itself. It is printed in the arming log line so
a session in play can correct it.

**Status: compiles; nothing in this entry has run in game.** See `STATUS.md` § "Built but not yet
verified in game" for the in-game checklist and the baseline to compare against (36 cues/30 s, measured
above).

### 10. World Tour compass, aim and sweep (2026-09-05) — `PENDING RUNTIME VERIFICATION`
Three new hands-free/on-demand readers and one shared support service, all built on top of the
navigation-radar sensors above and the clock-direction maths in `FieldDirectionService`. None of this
has run in game yet.

**Camera look-input hold — `Services/WorldTour/CameraHoldService.cs`.** Every direction announcement
(compass sector change, aim-at-nearest alignment) needs the player to actually stop turning for the
word to still be true when it lands. `app.worldtour.WTPlayerCameraController` consumes the look input
in two per-frame methods found in the decompiled controller: `camera_input_proc(dt)` for the pad stick
and `camera_input_proc_with_mouse(dt, axis)` for the mouse. Dynamic pre-hooks (`AddHook(false)`, pre
only, method-level so a controller recreated between cities costs nothing) return
`PreHookResult.Skip` on both while `CameraHoldService.Holding` is true, so those frames apply no look
input at all — movement input is a different code path and is untouched. `CameraHoldService.HOLD_MS`
= 50 ms is a user preference: long enough to release the stick, short enough not to read as a stutter.
`CameraHoldService.Hold()` extends an in-progress hold but never shortens one. `Available` reports
whether either method was actually found and hooked — false means announcements still speak but
nothing freezes.

**Hands-free compass — `Services/WorldTour/FieldHeadingService.cs` + `Hooks/WorldTour/FieldHeadingHooks.cs`.**
Speaks one of 8 compass points (clockwise from north, `FieldHeadingService.SECTORS`) whenever the
camera's facing crosses into a new sector, gated the same way as the other hands-free WT readers
(field presence, not during dialogue or the panel guide) and quiet at rest. **2026-09-06:** reads sit
behind a `POLL_TICKS` = 6 gate (10 Hz at 60 fps, same as `FieldAimHooks` below) instead of running every
frame — no turn crosses a 45° sector in a tenth of a second that the hysteresis would not have
swallowed anyway, and the gate also removed log spam from the underlying camera reads ("Member not
found: CameraPosition/LookAtPosition", "get_Count").

- **Where north comes from.** The game defines no compass constant anywhere (decompiled types
  searched: no "north", no map rotation offset). What it does have is the minimap:
  `app.UIMiniMapWindow` (found by walking the scene the same way `GuiTextReader` finds GUI
  components) exposes `ConvertTo_UIPos(vec3)`, which projects a world position onto the map picture —
  and in "Fixing" (north-up) mode, resolved via `get_MapRotateFlag()`, that picture's "up" is what a
  sighted player calls north. **North is derived, not guessed:** probe the map at the player's
  position and at two offsets, `+X` and `+Z` (`PROBE_M` = 1 m, any non-zero length works since the
  projection is affine), giving the two columns of the world→UI linear map:
  `world +X -> (ax, ay)`, `world +Z -> (bx, by)` (each column = probed point − origin point). GUI
  coordinates grow **downward** (top-left origin), so "up the picture" is the vector `(0, −1)`; solving
  `M · n = (0, −1)` for that 2×2 matrix gives the world-space direction that projects straight up,
  which is normalized and cached as north. The solve is refused below `MIN_DETERMINANT` (a degenerate
  projection, not a real map) and retried every `RETRY_MS` = 2 s while it fails. A rotating minimap
  (camera-relative, `MapRotateFlag` true) is not a compass at all and is skipped outright.
- **Fallback:** `NORTH_FALLBACK` = world `+Z`, used until the minimap derivation binds (or if it never
  does). Chosen pending in-game confirmation — 2026-09-05. Which source is in effect is always logged
  (`"Heading: north from minimap = (...)"` vs `"...from fallback axis = (...)"`), so a wrong guess is
  visible rather than silently trusted. `Reset()` on leaving the field forgets north so the next city
  derives its own.
- **Heading maths.** Headings are camera-relative, computed with the SAME
  `FieldDirectionService.GetBearing` the clock readout uses, with north as the "forward" argument:
  `heading = atan2(bearing.Right, bearing.Ahead)`, wrapped to `[0, 360)`, so the compass and the clock
  can never disagree about left and right (confirmed handedness: rightward = `forward × up = (−fz,
  fx)`, § Clock direction above). `Sector(heading) = round(heading / 45°) mod 8`.
- **Hysteresis.** A sector switch is accepted only once the heading is `HYSTERESIS_DEG` = 7.5° (a
  sixth of the 45° sector) past the CENTRE-relative half-width, via
  `OffsetFromSectorCentre`: `|offset| > 22.5° − 7.5°` must hold before the new sector is spoken. Small
  enough that a deliberate turn is answered promptly, large enough that a hand resting near a stick
  edge does not flap between two names. The very first reading after entering the field is remembered
  silently (no announcement on arrival).
- Each announcement calls `CameraHoldService.Hold()` before speaking, interrupting whatever was being
  said (`interrupt: true`) — a turn in progress is exactly when the player wants the newest word.
- `FieldHeadingHooks.CurrentFacing()` exposes the current point name (or null) for other readers to
  append (see Z below).

**Look-sweep — `Hooks/WorldTour/FieldAimHooks.cs`, reworked 2026-09-06, then again 2026-09-07 (user
request: find a specific NPC, e.g. "talk to Chun-Li", just by looking — no keys, no menus, no spam) —
`PENDING RUNTIME VERIFICATION`.** As the camera turns, every NAMED person — crowd included
(`AvatarNameCache.NameOf(o)`; only the nameless are skipped) — it sweeps across is announced with their
distance ("Chun-Li, 12 meters away", "Kenneth, person, 4 meters away",
`LocalizedText.AtMeters`), so a player hunting for someone specific can stand still, turn, and hear who
is where. The shared nearest person (`StickyTarget.NearestPerson`, the same one the continuous tracker
and the homing pulse follow — the RAW nearest, crowd included, not filtered to notable) additionally
gets a rising two-note tone (`AudioService.NoteMi` → `NoteLaHigh`) and "{name}, straight ahead"
(`LocalizedText.AimAhead`) — the one instant answer the tracker's periodic clock readout cannot give,
and it still works standing still.
- **Only while the camera is actually turning (new 2026-09-07, `IsTurning`/`SCAN_TURN_DEG_PER_S`).**
  Walking down a street, people walk INTO the aim cone by themselves — before this, that produced a
  running commentary the player never asked for. `SCAN_TURN_DEG_PER_S` = half a clock hour per second
  (`FieldDirectionService.DEGREES_PER_HOUR / 2`) is the threshold, measured between two 10 Hz polls
  (`_lastForward`/`_lastForwardTick`); slower than that is the follow camera settling behind a walking
  avatar, not a deliberate look-around. A Hero's Call gates its own automatic scan the same way (silent
  while walking straight, spoken on a turn). **Cone bookkeeping (`InCone`) still runs every poll
  regardless of `turning`** — only the speech/tone OUTPUT is gated — so a look-back after walking
  straight past someone finds exactly the people it should, not a stale "already seen" state.
- **Cone:** entering is `ENTER_DEG` = half of `FieldDirectionService.DEGREES_PER_HOUR` (±15°, half a
  clock hour, the same precision the clock readout already promises); leaving is `LEAVE_DEG` = a full
  hour (30°) — wider, so a person sitting near the edge cannot retrigger by jitter.
- **Range:** only people within `SCAN_RANGE_M` = `FieldBeaconHooks.HOME_RANGE_M` (25 m, now `internal`
  so the sweep and the homing pulse share one literal) are swept — beyond it the homing pulse is silent
  too, and a name a street away is not something the player can walk to by ear.
- **Crowd is spoken by the sweep (user rule 2026-09-07):** unlike the tracker's voice, the sweep does
  NOT filter to notable people — a passer-by can be fought or talked to for an item, and a look is the
  player asking. A notable-only sweep was tried the same day and rejected for that reason; the turn gate
  is what keeps it from being a census. Only the nameless are skipped (`AvatarNameCache.NameOf` null —
  the tone still marks them when they are the shared nearest). Earlier filter (`DescribeAvatar(...) !=
  null`), which spoke every crowd passer-by since World Tour names them too.
- **No per-person cooldown (removed 2026-09-07):** `SCAN_REPEAT_MS` is gone. The only anti-spam left is
  the angular hysteresis above (now compounded by the turning gate) — entering the cone while turning
  speaks/tones every time, so looking away past `LEAVE_DEG` and back re-announces the same person, on
  purpose (user rule 2026-09-07: "found them, overshot, coming back" must answer). A 10 s cooldown was
  tried first and swallowed exactly that case, so it was removed rather than tuned. The shared nearest
  person is likewise re-spoken — tone, "straight ahead" and the camera hold all fire again — on every
  re-entry; the old once-per-person `Seen.NamedAsTarget` flag that suppressed the repeat is gone.
- **One sentence per poll, nearest first:** `AvatarFieldReader.ReadOthers` is already nearest-sorted, so
  only the nearest person whose speech is due is spoken this poll; everyone else due waits for the next
  one. Non-interrupting (`interrupt: false`) for the sweep's name-and-distance line — it is a background
  listing and the reader already speaking is free to finish; the shared-nearest-person's "straight
  ahead" line still interrupts (`interrupt: true`), since that is the one instant answer worth cutting
  in for.
- Polled at `POLL_TICKS` = 6 LateUpdate ticks (10 Hz at 60 fps) — reading the avatar list is the
  expensive part; faster than the tracker's beat since the camera turns quickly. The turning check is
  measured between these same polls, so it needs no extra reads.
- Silence rules mirror the tracker's: nothing during dialogue, the panel guide, or while an
  interaction prompt is already up (`AvatarFieldReader.GetAccessInfoCount(mgr) > 0` — the arrival
  reader owns that moment).
- **State:** a `Dictionary<ulong, bool>` (`InCone`) keyed by avatar ADDRESS, holding only whether that
  avatar is currently inside the cone — no last-spoken tick and no per-target "named once" flag any
  more, since there is nothing left to cool down. Pruned every poll against the field's current avatar
  list (`Alive`/`Gone`), and cleared (`Reset()`) on leaving the field.

**Shared sticky target — `Services/WorldTour/StickyTarget.cs`.** Extracted 2026-09-05 so the
continuous tracker and the aim reader can never name different people; 2026-09-06 gained a third
follower, the NPC homing pulse (`Hooks/WorldTour/FieldBeaconHooks.cs`, see below), so all three agree
on who "the nearest person" is. `Pick(others)` (others pre-sorted nearest-first by
`AvatarFieldReader.ReadOthers`) keeps the currently tracked avatar, matched by ADDRESS (never a cached
`ManagedObject` — an address is a plain number, safe to hold across frames, and simply fails to match
once stale), until somebody else beats it by more than `DEFAULT_SWITCH_MARGIN_M` = 2 m. `Reset()`
forgets the target (used when presence/target list drops out) so the next `Pick` starts unbiased.
`StickyTarget.NearestPerson` is the one shared static instance every follower calls.

**Shift+Z compass sweep — `Hooks/WorldTour/FieldSweepHooks.cs`, `FieldNavRadarService.Sweep`,
`FieldNavSideRays.TryCastDirection`.** On demand only (by design — a picture of the street is worth
asking for and worthless as running commentary): casts one ray per compass point (8, index 0 = north,
clockwise) and answers in one sentence, e.g. "Open: north, east. Walls: south 3 meters, west 6
meters" (`LocalizedText.SweepOpen`/`SweepWalls`/`SweepAllOpen`/`SweepEntry`/`SweepUnavailable`).
- **Origin:** the start point of the game's own `FRONT_LONG` ray for the current field state (read via
  `GetCastRayPosition`, the same origin the forward-stack sensor already trusts) — not an invented
  chest-height offset.
- **Directions:** built from north (`FieldHeadingService.GetNorth()`) rotated by `k × 45°`; east is
  derived the same right-hand way the clock hours are, `(ex, ez) = (−north.Z, north.X)`. Each direction
  vector is cast via `FieldNavSideRays.TryCastDirection`, which reuses the free-form
  `CastRayAll(ref vec3 start, ref vec3 end, CastRayResult, eFilterInfo)` overload and the same
  process-lifetime unmanaged `FieldOutBuffer`s the sideways probe already allocated (§8) — no new
  buffers, no per-call allocation.
- **Reach:** `FieldNavRadarService.SWEEP_REACH_M` = 12 m — a documented UX literal, not a game value.
  The game's longest published ray (`FRONT_LONG`, 2 m) is a feeler for walking, not a look-around
  distance; 12 m was picked as "a street in Metro City is of the order of ten metres across", so the
  sweep reads as a picture of the immediate street rather than the whole district. If this needs
  tuning after the in-game pass, it is the one number to change.
- Each entry is the hit distance along that ray, or 0 (open) when nothing was hit within the reach;
  `Sweep()` returns null (spoken as `SweepUnavailable`) when the sensor or north cannot be reached at
  all — never a fabricated "all open".
- **Key:** Shift+Z, via `Services/ReadoutShortcut.cs` (`new ReadoutShortcut(VK_Z, PAD_NONE, shift:
  true)`) — the same shortcut class used by every other letter-key reader, gated on
  `ReadoutShortcut.IsGameForeground()` so it never fires while the user types elsewhere, and its
  `shift: true` constructor argument means the CHORD only, leaving plain Z to `ZoneHooks` untouched
  (the two never fire together off one key edge).

**Z now appends facing — `Hooks/WorldTour/ZoneHooks.WithFacing`.** The on-demand "where am I" key (Z)
now answers both "where am I" and "which way am I looking" in one sentence: "In Beat Street, facing
north" (`LocalizedText.Facing`, appended via `FieldHeadingHooks.CurrentFacing()`). Manual reads only —
the automatic (hands-free) zone announcement on a district change stays bare, since the hands-free
compass already covers turning on its own.

**Key bindings recap (World Tour field, all provisional pending a check against the game's own
bindings — see STATUS.md):** `B` one-shot nav radar readout, `Shift+B` toggle continuous nav radar,
`N`/pad Start nearby-avatar radar, `Z` speak current area (+ facing), **`Shift+Z` new: compass sweep**.
The hands-free compass, aim-at-nearest and camera hold have no keys at all — they run continuously
while in the field, gated the same way as every other always-on WT reader.

## World Tour minimap tracker + route guidance (2026-09-09) — `NOT YET RUN IN GAME`

Pick any icon the HUD minimap is showing, and have the mission beacon take you to it. Code:
`Hooks/WorldTour/FieldTrackerHooks.cs` on `Services/WorldTour/{MapIcon, MapIconCatalog,
TrackerSelection, TrackerButtons, FieldInputMask, BeaconDestination}.cs`, plus the previously inert
`FieldNavPath.cs`. **Everything below is decompile-derived and compiles; none of it has run in game.**

### 1. The icon source — the HUD minimap publishes world positions

`app.UIMiniMapWindow : app.UIMapWindowBase` (`UIMiniMapWindow.cs:12`) is the live component the mod
already finds for the compass (`FieldHeadingService.MiniMapWindow()`, one search for it mod-wide). It
carries the icon set itself:

- `mIconInfoList : IDictionary<eUpdateTargetType, IconInfo_Array1D>` (`:260`)
- `mInfoCount : IDictionary<eUpdateTargetType, int>` (`:268`) — **the live length of each bucket.
  The arrays are reused buffers; entries past this count are last frame's leftovers.**
- `eUpdateTargetType {NPC=0, LEGEND=1, OM=2, ZONE=3, PIN=4, MAX=5, START=0}` (`:63-72`). `START`
  aliases `NPC`, so buckets are de-duplicated **by value** and bounded by `MAX`.
- Rebuild methods (not called by the mod, listed so they are not mistaken for accessors):
  `UpdateIcons()` `:304`, `UpdateIconInfoList()` `:316`, `CheckUseIcon(IconInfo)` `:352`.

`app.UIMapWindowBase` itself holds **no** icon collection — only factories (`MakeIconInfo` `:1008`,
`MakeMissionIconInfo` `:1032`, `MakeFastTravelIconInfo` `:1044`, `GetIconSetting` `:1026`). The phone
map keeps its own separate store (`app.UIDeviceMapWindow.mIconInfoList`, keyed by `ICON_GROUP_TYPE`).

`app.UIMapWindowBase.IconInfo` (`UIMapWindowBase.cs:317-477`), all read via `FieldProbeService.Member`
(it is a **struct**, so `FlowHelper.GetListItem` is useless — it casts to `ManagedObject` and would
yield null for every entry; call `FlowHelper.Call(array, "Get", i)` and keep the raw object):

| Field | Line | Type | Used for |
|---|---|---|---|
| `TitleText` | `:334` | string | already localized — spoken verbatim |
| `UniqueIndex` | `:342` | **int** (not uint) | part of the pin key — see the warning below |
| `Type` | `:406` | `ICON_TYPE` (int) | family word via `DeviceMapText.IconType` |
| `GroupType` | `:422` | `ICON_GROUP_TYPE` (int) | the tracker's categories |
| `GlobalPos` | `:430` | `via.vec3` | **the world position — what makes this trackable** |
| `UiPos` | `:350` | `via.vec3` | projected picture position; deliberately unused |

`ICON_GROUP_TYPE {All=0, TRAVEL=1, FACILITY=2, JOB=3, MISSION=4, MASTER=5, PIN=6, MAX=7, START=1}`
(`:687-698`) is the game's own grouping and is used as the category cycle verbatim. Neither `IconInfo`
nor `CityPointDataInfoBase` carries a completed/consumed flag — and none is needed: **the game removes
an icon from the minimap when it is consumed**, so auto-retracking is "the pinned icon stopped
appearing".

**Do NOT pin a selection on `UniqueIndex` alone.** Only `MakePinIconInfo(PinInfo, int)` (`:1038`) takes
a unique index; `MakeIconInfo` (`:1008`), `MakeMissionIconInfo` (`:1032`) and
`MakeFastTravelIconInfo` (`:1044`) do not — so the field may well be 0 for every shop and mission. If
it is, an index-based pin makes a whole category read as one target: the cursor refuses to move and
auto-retracking never fires. `MapIcon.Key` is therefore the composite
`GroupType/Type/UniqueIndex/TitleText`, which also stays stable for an icon that MOVES (a walking NPC),
unlike a position-based key. **Still worth confirming in game** what `UniqueIndex` actually holds for a
non-pin icon.

**Dictionary access:** `ContainsKey(key)` then `get_Item(key)`, never `TryGetValue` — its second
parameter is an **out reference type** (the array), the documented crash family. There was no prior
IL2CPP-dictionary read anywhere in this codebase; the keys are taken from the enum (via
`FieldProbeService.ReadEnum`) rather than by enumerating the dictionary.

### 2. Taking the field's input — the game's own lock, no hook

`app.InputManager.RequestMaskLowerAndEqualLayers(app.InputLayerPriority)` (`InputManager.cs:1524`) —
one static call, one by-value enum argument, no overloads, nothing by reference. **The request lasts
one frame**, so holding it means asking every frame — which makes it fail safe: if the mod stops
ticking the player's controls return next frame.

**"AndEqual" is load-bearing, and `RequestMaskLowerLayers` is the trap.** The enum is declared
`UI=0, Flow=1, Battle=2, Action=3, Other=4` (`InputLayerPriority.cs:3-10`), so "lower" means lower
PRECEDENCE — a HIGHER number. That is how the phone, on `UI`, silences the field on `Action`. It also
means `RequestMaskLowerLayers(Action)` masks only `Other` and leaves the avatar walking and the d-pad
live, while binding and logging exactly as if it had worked. The WT field player is on `Action`, so
`Action` itself has to be in the mask; `LowerAndEqual` leaves `UI`/`Flow`/`Battle` alive, which is what
keeps the pause menu and the phone reachable.

Rejected alternatives, recorded: pre-hooking `app.InputLayer.CheckTrigger`/`CheckDown`
(`InputLayer.cs:64,67`) works and is safe but is the hottest method in the input system and must fake
a return value; `InputLayer.GetValue` (`:82,85`) has a `ref float` overload and must only ever be
bound by shape; `AvatarPlayerControll.UpdateLocalPlayerInput` (`:299`) is blunt (kills movement only).
`app.PadManager` is device assignment, not button state.

**What R3 / L1 / R1 / d-pad do in the field is NOT in the stubs** — the physical→action mapping is
runtime data (`InputAssign.Digital.ConfigParam.GamePadButton`, `InputAssign.cs:962/970`, applied by
`InputManager.ApplyFieldGamePadPreset`, `:1371`). The field slots that exist include `ACT_RSB` (R3)
and `SC_LB`/`SC_RB` (L1/R1 on the shortcut wheel), so those presses do have game meaning; the mask is
what stops them reaching it. There is no `ACT_PLUS_U/D`: the field d-pad is not a configurable slot.

`via.hid.GamePadButton` values (decompiled, and they agree with `Services/InputNameResolver.cs`):
`LUp=0x1`, `LDown=0x2`, `LTrigTop`(L1)`=0x100`, `RTrigTop`(R1)`=0x400`, `RStickPush`(R3)`=0x2000`.
`TrackerButtons` resolves all five **by member name** out of the TDB, so no hex appears in mod code.

### 2b. The minimap window is NOT a scene component — `CONFIRMED IN GAME 2026-09-09`

`via.Scene.findComponents(System.Type)` for `app.UIMiniMapWindow` returns **nothing**, for a whole
session. The window is owned by a HUD component: **`app.UICityHud_CityMiniMap : UIHudBase`
(`UICityHud_CityMiniMap.cs:7`) → `MiniMapWindow : UIMiniMapWindow` (`:27`)**. `UICityHud_*` components
ARE findable in the scene — `MissionGuideReader` finds `app.UICityHud_MissionGuide` exactly that way and
has always worked — so the obvious fix is to find the HUD component and read its member.

**That fix did NOT work either, and this is still OPEN** (`CONFIRMED IN GAME 2026-09-09`, second pass):
searching the scene for `app.UICityHud_CityMiniMap` also finds nothing (no `minimap window via ...` line
was ever logged, and the catalogue kept reporting `the live app.UIMiniMapWindow was not found`). So one
`UICityHud_*` component is findable and another is not, and we do not know why — it may be built only
while the minimap is actually displayed, live in a different scene, or under a different concrete type.
**Do not assume the minimap is reachable until this is solved.** The consequence is recorded here
because it decided a design: the minimap-icon radar was deleted and the PHONE MAP's cursor selection,
which demonstrably works, became the selection source instead.

**This had been silently breaking the compass since 2026-09-05.** `FieldHeadingService` derives north
from the minimap projection and falls back to the world +Z axis when it cannot; the log line
`Heading: north from fallback axis = (0.00, 1.00) until the minimap binds` was the only symptom, and
every compass reading in the mod had been running on a *guessed* axis. The scene-component idiom now
lives in one place, `FieldProbeService.SceneComponents` / `FirstSceneComponent`, instead of the four
copies it had grown.

### 3. Route guidance — the pathfinder finally has a caller

`FieldNavPath` (mapped 2026-09-08, inert until now) is switched on by **Shift+V** and off again the
same way; it starts off every session. `BeaconDestination.Resolve` is the only caller: it picks the
tracked icon over the story objective, then — with route guidance on — replaces the **aim point** with
the route's first corner while keeping the **destination** for distance, ping cadence and arrival.
Aiming at a corner but measuring to it would announce arrival at every turn.

**`NavigationSurface` is an AGENT, not the navmesh, and the player has none** —
`CONFIRMED IN GAME 2026-09-09`. `via.navigation.NavigationSurface` (`NavigationSurface.cs:9`) carries
`AgentRad` (`:257`), `AgentHeight` (`:265`), `Speed`, `TargetPos`: it is a thing that walks a navmesh.
The World Tour player is stick-driven and never navigated, so nothing attaches one — in game,
`AvatarNaviControllBase.NaviSurface` was null AND `getComponent(NavigationSurface)` on the player's own
GameObject was null, once a second for two minutes, and the beacon fell back to the straight line in
silence. The whole game declares only two members of that type
(`AvatarNaviControllBase.cs:430`, `WTEventNpcNavigator.cs:75`), both on NPC controllers, and **no
accessor anywhere returns one**.

So the surface is **borrowed from a navigating NPC**: ask the scene for
`via.navigation.NavigationSurface` components and take the first whose `hasValidMap()` is true. Its map
is the city the player is standing in, which is all the query needs; that it belongs to a passer-by does
not change whether a route from A to B exists, and crowd NPCs are avatar-sized. The query is READ-ONLY —
`start()`/`adjust()`/`stop()`, the calls that would actually steer that NPC, are never made. It is never
cached: the NPC can walk away or despawn, and a wrapper outliving its object is the finalizer crash.

`queryPathSync` is declared only on the three `Navigation` subclasses (`NavigationSurface.cs:556-571`,
`NavigationVolumeSpace.cs:190-196`, `NavigationWaypoint.cs:364-370`), never on the base and **never on
`MapHandle`/`MapHandleBase`** — so the whole `AIMap`/`findMapHandle` family that crashed the game in
2026-09-08 was a dead end on its own terms, not just a dangerous one. `hasValidMap()` is declared once,
on `via.navigation.Navigation` (`Navigation.cs:1375`).

**A path point is a PORTAL CROSSING, not a waypoint** — `CONFIRMED (decompiled + in game 2026-09-09)`.
`via.navigation.map.PathInfo.PathPointInfo` (`PathInfo.cs:224-251`) declares exactly three members:
`NodeInfo PortalNode` (`:228`), `vec3 PortalPos` (`:236`), `bool IsSkiped` (`:244`). There is no
`Pos`/`Point`/`WayPoint`. A "point" is therefore one boundary between two navmesh polygons — the sibling
`PortalEdgeInfo.PortalEdge` (`:152`) exposes the same thing as a `LineSegment`, and
`NodeInfo.LinkBoundaryMinY/MaxY` (`NodeInfo.cs:199/205`) show it is a vertical span. So `PortalPos` sits
somewhere along an EDGE, not on any line a walker would take, and the count is one per polygon crossed —
NOT a fixed sample spacing (65 points for 19 m and 170 for 228 m, which is what disproved the
"30 cm samples" reading recorded here earlier).

**Reading them raw is useless, and this is what "it leads me into a wall" actually was.** In game, two
completely different destinations produced nearly the same bearing, because both readings were the same
few edges around the polygon the player was standing in. **The missing step is
`PathInfo.createTraceLine()`** (`PathInfo.cs:307`) — the string-pulling pass that turns the corridor of
portals into an optimised line. Call it on the report's `PathInfo` BEFORE reading `PathPointCount` or any
point. It takes no arguments, returns `void`, and mutates our own `PathQueryReport`, not the borrowed
NPC's navigation. Also skip any point whose `IsSkiped` is true. Do **not** use the parallel
`getPathObjectAttribute(uint)` bitmask (`:310`, members at `:117-125`): its bit convention is not
declared anywhere, and guessing one would be a magic number.

**Index domain and order:** `getPathPointInfo(uint)` (`:274`) is bounded by `PathPointCount` (`:261`) and
runs START → END (proved in game: walking from i=0 on a 228 m route yielded a point 4 m away, not 228 m).
Same-domain siblings: `getPortalPos(uint)` `:319`, `getPortalNode(uint)` `:316`,
`getPortalEdgeInfo(uint)` `:277`. DIFFERENT domain, do not mix: `getLink(int)`/`getLinkCount()`
`:280/:268`, `getNode(int)`/`getNodeCount()` (`NodeInfoList.cs:18/21`).

**There is no steering API to borrow.** Nothing in `via.navigation` offers `getNextPoint`/`calcNextPos`/
`getSteerTarget`. `via.navigation.NavigationInfo` has exactly what one would want —
`CurrentDestination` (`NavigationInfo.cs:48`), `FinalDestination` (`:54`), `DirectionVectorXZ` (`:36`),
all safe parameterless `vec3` getters — but **nothing returns a `NavigationInfo`**: it only arrives as a
delegate argument (`AvatarNaviControllBase.cs:633 OnNavigateUpdate(NavigationInfo)`) to an agent actually
running `start()`. Unreachable from a passive query, and constructing the delegate is the forbidden
family. `Navigation.queryCurrentNode()`/`queryNextLink()`/`queryNextNode(...)` (`:1378-1390`) are safe
shapes but describe the BORROWED NPC's own route, not ours.

**`calcDistance`:** `enum DistanceType { Raw, Path }` (`PathInfo.cs:218-222`), `calcDistance()` `:289`,
`calcDistance(DistanceType)` `:292`. `Raw` = straight start→end, `Path` = summed along the corridor.

**Measured reference values** (Metro City, borrowed NPC agent): `agent radius=0.250 m height=2.000 m`,
`map=''` (the name reads empty), 14-21 `NavigationSurface` components live in the scene at any time. An
objective 12 m up a building correctly returned `Exist=False`.

**Path point 0 may be the player, not a turn.** The step-9 trace was written to settle that and it no
longer has to be settled: `FirstCorner` now walks the points in order and takes the first one farther
than the navmesh agent's own radius (`NavigationSurface.AgentRad`) from the avatar, on the ground
plane. Every negative answer — switch off, no navmesh, rate-limited, no route, corner unreadable —
falls back to the straight line, i.e. to the beacon's previous behaviour, never to silence.


## World Tour — multi-level routes through ladders (2026-09-22) — `PENDING RUNTIME VERIFICATION`

**Problem, from the 2026-09-09 log.** Mission 11011's NPC stood at y=12.2 on a roof; the player's
navmesh node could not reach it (`PathNotFound`, `destNode=10421`: a real node on a DISCONNECTED
island). `ApproachFinder` then guided to the ground directly beneath it, and the player circled
there for minutes ("Hacia el pie de objetivo, a 4 metros"). Ground-level route guidance itself
worked in that session: repeated V presses read 96, 90, 84 … 5, 4 m along real bends.

**Evidence the fix relies on.** The navmesh covers roofs as separate islands, so
`queryPathSync` can answer "are A and B connected on foot" between ANY two points, not only from
the player. No ledge-climb mechanic exists in the avatar stubs (`Climb`/`GetOver` are only
action-guide labels); the explicit vertical connector is the ladder gimmick
`app.worldtour.om.Om011000` with `GroundPoint`, `EndPoint`, `StartPoint`, `UpVec`,
`IsInitializeParam` (`Om011000.cs`). Other connector leads, not yet used: `Om026000` warps expose
`GetPlayerStartPos()`/`PlayerStartPos`; `Om013000.RequestType.CabinetWarp`;
`Om006000.State {Up, OnDescent, Stop, Under}` (a lift-like gimmick).

**Design (code: `LevelRoutePlanner` pure search, `LevelRoute` runtime driver, `LadderCatalog`).**
1. The direct query from `BeaconDestination` fails with the player on the mesh → search starts
   (`Searching`); the beacon is silent meanwhile and V says "searching".
2. `LadderCatalog` reads every live, initialised ladder (32 per frame): low/high standing points
   = `GroundPoint`/`EndPoint` ordered by height.
3. `LevelRoutePlanner` asks one question per free query slot (`FieldNavPath.TryConnect`, shared
   250 ms limiter), with union-find so answers are reused. One ladder first — its end nearest the
   target must connect to the target, its other end to the player — then two chained ladders.
   Candidates are ordered by straight-line detour. Budget: 48 answers, 96 raw queries.
4. An endpoint the engine reports off-mesh (`StartNodeNotFound`/`DestNodeNotFound`) is retried
   moved one stride (1 m) along ±X/±Z; a moved spot that answers replaces the ladder end. A target
   that is itself off-mesh first re-asks the direct walk with the moved target; if no move puts it
   on the mesh the search aborts.
5. `Following`: legs are "walk to ladder end" then "walk to target". `Progress` advances only when
   the player stands within one avatar height (2 m) of the far end's height and 4 m of it
   horizontally, and goes back the same way at the near end. Failed answers are ignored while
   the avatar is off-mesh or climbing (within 2 m of a planned ladder and more than 1 m inside
   both ends). A leg unreachable for 3 s replans (at most 2) and the replan asks the direct walk
   first, in case the player reached the target's island another way. The last leg follows a
   drifting target (walking NPC, contact point that moves with the player).
6. `Failed`: the old `ApproachFinder` point below/above the target, spoken as such. Every 30 s a
   SILENT retry re-sweeps the ladders and re-asks the direct walk; the full ladder search only
   reruns if the ladder set changed or the player moved 8 m. It speaks only if it finds a route.
7. The route belongs to the selection key (map pick or mission object), not to the field
   session: menus, the phone and dialogue pause it without discarding the plan. The search
   starts from the player's position when the direct walk failed, so walking meanwhile does not
   mix two islands. A query that cannot run (no navmesh agent yet) is retried, never read as
   "not connected"; the 96-query budget bounds it.

**Speech (`RouteNotice`, `wt.route.*` in en/es/pt):** "The destination is N meters above you.
Searching for a ladder" → "Route found through a ladder, N meters away" → clock/turn lines name
"ladder" → at 4 m "Ladder here. Climb up" (the game's own action prompt follows) → after climbing
"Ladder done. Continue to objective, N meters". Failure: "No ladder found that reaches the
destination", then the approach texts.

**Log lines to read after a test:** `Level route: N of M ladders readable` (N=0 → ladder reads are
wrong), `Level route: no walk from … (+12.1 m vertical); searching ladders`, `Level route: plan
after Q questions: ladder up (…) to (…) -> objective`, `Level route: leg 0 -> 1`, or `Level route:
no plan after … over N ladders; #i low (…) [target yes/no/?, player yes/no/?], high (…) […]` —
the eight ladders nearest the target, which is what the next iteration needs.

**First run (2026-09-26, mission 11011, same roof NPC as 2026-09-09).** The search ran as designed:
`7 of 9 ladders readable`, 14 questions, and EVERY ladder end answered `target no` — no loaded ladder
reaches the objective's island (nearest: #1 at (-245.2, 4.8→10.1, -111.1), #3 at (-244.0, 0.1→5.1,
-121.6), 16-17 m from it; the target stands at y 12.2, and was also logged at y 14.0 at the same XZ).
Fallback guided to the ground under it and the periodic line said "objetivo a las 12, a 5 metros": the
distance was to the approach point, the objective 12 m above. Fixed: approach legs are named "pie de
<objetivo>" and arrival says the height (`wt.aim.approach_here`). Unknowns for the next run, now logged:
the two unread ladders (`unread: hidden|uninitialised (x, y, z)`), why the HUD marker was not used
(`HUD unused (...)`), and every holder object of the mission target by name and position — the
first holder may be a placeholder, not the NPC the player must reach.

**Second run (2026-09-26): partial plans.** The mission is Chapter 2-2 "Moratones a los Caracartones":
the target is the boss Boluo (`pNPCAvatarBase`, the `IconNpc_450033` holder floats 1.8 m above him);
the map icon sits at the same point, so the target is right. Walkthroughs: climb a ladder beside the
building, a "Chinese Food Restaurant" sign blocks the roof path (break it with any master action,
hold R2/RT + a face button), then a second ladder leads to Boluo. That matches #3 (0.1→5.1) then #1
(4.8→10.1); yet Boluo stands at 12.2 and no ladder end connects to his node, so something the navmesh
does not model (a ledge climb, a jump, or the sign itself) closes the last gap. The planner therefore
no longer stops at "no plan": `LevelRoutePlanner.Closest` walks ladders outward from the player
(breadth first, one question per slot, nearest foot with the top nearest the target's height first)
and returns a PARTIAL plan to the reached spot nearest the target's height (at least one level, 2 m,
better than the player's). Its last leg stops at that ladder top; reaching it switches to the
approach finder (`LevelRoute.AtClosest`), which now restarts when the player changes level and
ignores probes made while off the navmesh. Speech: `wt.route.partial_one/partial_many` when found,
`wt.route.closest_up/down/level` at the top ("look for a ledge, a jump or something to break with a
master action"). The failure line now also carries `player yes/no` for every end the partial search
asked, which says whether the sign cuts the navmesh (#3 high to #1 low `no`) or not.

**Third run (2026-09-26): the partial plan worked, the ladder did not.** 48 questions produced
`partial plan: ladder up #3 (-244.0, 0.1→5.1, -121.6)`; #1's foot (4.8) answered `player no`, i.e. not
reachable from the street. The walk to #3 was 126 m for a 28 m straight line (the ladder is round
the far side of the building, as the guides say). The player stood within 1-2 m of the foot for two
minutes, "Ladder here" was spoken, and nothing happened: no `leg 0 -> 1`, and the action-prompt reader
never logged once (`Exploration action HUD` absent — unknown whether the HUD hook fires at all).
Leading hypothesis: a fire-escape ladder whose lowest rung hangs above the ground (`GuideDef`
has `Ladder_Catch`/`Ladder_Up_Air`). Added: `LadderCatalog` reads each ladder's `StartPoint` height
above its floor and its `WTCityOM.AccessTarget`; the sweep line now ends with `lowest rung above the
floor: +x.x m` per ladder; at a planned ladder `Ladder contact:` logs every 3 s what
`CurrentAccessInfoList` holds (`(this ladder)` marks a match), "Ya puedes agarrar la escalera" is
spoken when the game starts offering it, and a rung ≥ 1 m up says "salta hacia ella para agarrarla";
`Exploration action HUD hook fired; IsShow=` logs once. The mission beacon's far gap went from 2.5 s to
0.5 s (near 0.1 s), and it keeps pinging under other readers' speech (tester: impossible to follow).

**Fourth run (2026-09-26): climbing works; the partial plan picked the wrong building.** All 9 ladders
read, every `StartPoint` equals its `GroundPoint` (lowest rung +0.0 m: no fire-escape ladders here).
`Ladder contact:` shows the game offering the ladder as `app.worldtour.om.WTOmAccessTarget (this
ladder) at 0.50 m` only when the avatar stands at it, facing it; "Ya puedes agarrar la escalera"
fired at that moment and the tester climbed with the action button. `Exploration action HUD hook
fired; IsShow=False` — the prompt HUD is reached, but its `IsShow` never read true all session.
Two faults: (1) gap-only ranking chose a ladder 85 m from the boss (top 7.9, gap 4.3) over #5 23 m
away (top 5.1), and the 30 s retry then sent the player back down toward a 133 m-away tower (top
18.7). Partial ends are now limited to `LevelRoutePlanner.NeighbourhoodM` (32 m flat from the
target) and questions go to ladders whose top is nearest the target first. (2) The mission
target's single holder alternates between `IconNpc_450033` (y 14.0, the NPC's map icon while the
NPC is streamed out) and `pNPCAvatarBase` (y 12.2), and the route was keyed on the object address,
so every swap restarted the search. The key is now the objective's slot (mission id, kind, index)
plus a generation bumped when the target jumps more than 8 m. Also: the partial end is reached only
standing within 1 m of the ladder top's height (it fired at y 6.0 of 7.9, mid-climb).

**Fifth run (2026-09-26): right ladder, wrong hand-over.** The plan chose #5 (-244.0, 0.1→5.1, -121.6)
as intended. "Climbed" fired at (-244.0, 4.5, -121.5) — still ON the ladder, 0.6 m below its top — and
the approach finder probed from there: the avatar's navmesh node was the street below, so the point
under the boss "routed" 131 m along the streets and the beacon led the player back down into walls.
Now the partial end is declared only when a route from the player to the last ladder's top EXISTS
(`LevelRoute.LegResult`), i.e. the player stands on the roof the ladder serves; until then the leg
stays the ladder's (no new speech), holding within 2 m of the top.

**Sixth run (2026-09-26): on the 5 m roof, nowhere to go.** Climbed #4 (the (-244.0, 0.1→5.1) ladder;
indices change per sweep). The hand-over still fired at the ladder's XZ (4.7 of 5.1): a route to the top
existed from there, so the navmesh check alone does not prove stepping off. The approach again chose a
133 m route (the point under the boss, reached through the streets), and the tester tried breaking
everything without result. The second ladder's foot (-245.2, 4.8, -111.1) is 10.5 m from #4's top,
level with it, and answered NOT connected: the signature of the sign. Added **blocked legs**:
`LevelRoutePlanner.BlockedHop` takes one ladder whose foot is level with (< 2 m) and within
`BlockedHopM` (16 m) of a reached spot the answers say does not connect, when its top is at least a
level nearer the target. The leg is guided straight at the foot (no route, never "broken"), spoken as
`wt.route.blocked_ladder` ("…no hay paso libre…acción de maestro, o un salto…"), and
`wt.route.path_open` when a route appears. On entering it, `ObstacleProbe` logs every
`WTCityOM` within 3 m of the straight walk (`Level route: blocked walk … city objects within 3 m`),
which should name the sign's type. Up a partial route, approach stand-ins whose walk exceeds the
straight distance plus `NeighbourhoodM` are rejected (the beacon points at the destination).

**Seventh run (2026-09-26): both ladders climbed, stopped 16 m from the boss.** Plan: #4 up → blocked #2
up → stop at (-245.2, 10.1, -111.1). The corridor probe found only the ladder itself among 456
`WTCityOM` objects (the sign is not a city object, or stands outside 3 m); 14 s later a route to #2's
foot existed ("Ya hay paso") and the tester climbed it. On the 10 m roof the approach finder's
under-target and 8 m ring probes all failed; ring-B probe 15 (-240.6, 9.4, -115.6), 16 m from Boluo,
routed. So Boluo's spot (y 12.2) is its own island ~3 m above the roof, joined by something the NPC
agent's navmesh does not model (probably a ledge the avatar climbs: `GuideDef.Climb`). The tester
then circled the stand-in against a "pared alta". Changes: once the player stands at the stand-in
(`ApproachFinder.Arrived`), the beacon aims at the destination itself and says `wt.route.at_foot`
("…El sonido apunta ahora a él: acércate buscando por dónde subir"). The prompt HUD's `IsShow` never
read true, so `FieldExplorationHooks` now also hooks `UIHud_PlayerActionGuide.RequestShowFromPlayer` /
`RequestShowFromOM` (args[2] = `GuideDef`): each request is announced as "Disponible: <acción>"
(Trepar, Saltar por encima…) and each distinct action logged once as `the game requested action
prompt <GuideDef>`.

**Eighth run (2026-09-26): mission completed.** The route led up both ladders; from the 10 m roof the
tester got up to Boluo. How is not in the log: the `RequestShowFromPlayer`/`RequestShowFromOM` pre-hooks
were installed (`request hooks=2`) but never fired, not even at ladders, so those methods are not how
this build raises its prompts (or the hook does not reach them). Next lead for context actions: the
widget side (`UIWidgetPlayerActionGuide`) or `AvatarPlayerControll.CancelableActionInfo`
(`UIHud_PlayerActionGuide.ActionInfo` / `SetActionInfo`).

**Open questions for the first run:** are `GroundPoint`/`EndPoint` on the navmesh (count of
nudges), does the ladder top connect to the roof island, does the avatar read off-mesh while
climbing, and are there roofs whose only access is not a ladder (then the failure line shows
every nearby ladder answering `target no`).


## World Tour — turning the camera to face a world point (2026-09-09) — `NOT YET RUN IN GAME`

`Services/WorldTour/CameraAimService.cs`, driven by `Hooks/WorldTour/MapAimHooks.cs` (**R3 / V**).

**Turn the CAMERA, not the avatar.** World Tour movement is camera-relative, so rotating the avatar
leaves "forward" pointing the old way — useless. The avatar's rotation is also rewritten every frame by
`AvatarFieldParam_Common.ApplyDefaultRot(Transform, float, AvatarFieldParam_Volatile)`
(`AvatarFieldParam_Common.cs:565`), so a `via.Transform.set_Rotation` write does not survive. (`AxisZ`
is getter-only, `Transform.cs:112`.)

**What to write:** `app.worldtour.WTFollowCameraController.CurrentRotation` — a `via.vec3` of euler
angles, get+set (`WTFollowCameraController.cs:20`), consumed by `updateRotation(float)` (`:68`). It is a
value-type field write: no engine call, nothing by reference, nothing constructed. Hold it for a few
frames (the follow layer interpolates) and freeze look input meanwhile via the existing
`CameraHoldService`. Controller chain: `WTPlayerCameraController → WTFollowCameraController →
WTFixedCameraController → WTCameraControllerBase → WTBaseBehavior`
(`WTPlayerCameraController.cs:12`), and it IS findable with `via.Scene.findComponents`.

**Two things the stubs cannot answer, and must be derived at runtime rather than guessed:** which euler
component is the yaw (the base class has BOTH `deg2_to_rad3_xy` and `_xz` converters,
`WTCameraControllerBase.cs:452/458`) and whether the stored value is radians or degrees. Ground truth is
already available — the camera's true bearing from `FieldDirectionService.GetCameraForward()` — so every
component is compared against it under both units and both signs, and the match wins. The write is then
read back: `FieldProbeService.Member` may hand back a COPY rather than a view into the controller, and
the read-back is what distinguishes "it worked" from "it silently did nothing". On any failure the
feature speaks the clock direction instead of turning. A wrong guess would spin a blind player's camera
to an arbitrary heading, which is worse than not turning at all.

**Other routes, ranked and recorded so they are not re-derived:** `followPlayer(vec3, bool)`
(`WTPlayerCameraController.cs:665`) is shape-safe but its semantics are unverified; there is **no**
named camera-reset method (the reset is state — `_IsReset`, `_ResetStartRotation`, `_ResetTargetRotation`,
`_ResetCameraTimer`, `WTPlayerCameraController.cs:329-367` — almost certainly begun inside
`camera_input_proc`); `AvatarInputController.SetMove(vec3)` (`AvatarInputController.cs:1126`) is
shape-safe but moves without turning, so the compass stays wrong; `AvatarFieldParam_Volatile.RotationInfo
.AngleControllTargetVec` (`AvatarFieldParam_Common.cs:580`) turns the avatar only. **UNSAFE, excluded:**
`SetAngleControllParam(ref vec3, ref float)` (`:589`) and `AvatarBase.RequestSetRot(ref Quaternion)`
(`AvatarBase.cs:1539`) need a `FieldOutBuffer`, never `CreateValueType`;
`_GetCurrentInput(ref AvatarInputData_Array1D)` (`AvatarInputController.cs:1120`) is a by-ref REFERENCE
type — the documented crash family.


## World Tour — naming WHERE THE PLAYER IS (2026-09-10)

What the game models between "Metro City" and "that shop over there", and what it does not.

### The good: the game announces sub-areas itself

**`app.UICityHud_SectionNotice`** — the on-screen banner a sighted player sees on crossing into a named
part of the city. `mTextSection` (`UICityHud_SectionNotice.cs:147`) is the already-localized, already-
worded string ("Hong Hu Lu Chinatown - Calle principal"); `mLastReuestedSectionId` (`:139`) is the id it
last announced; `RequestSectionNotice(CitySectionInfo, bool)` (`:184`) is the entry point, mirrored by
`CitySectionManager.RequestSectionNotice` (`:68`). **This is the best source by a distance**: the game
decides WHEN a crossing counts and has already resolved WHAT to call it, so nothing has to be inferred.
It is a `UICityHud_*` component and so IS reachable with `via.Scene.findComponents` — the same route
`MissionGuideReader` has always used. Implemented as `Hooks/WorldTour/SectionNoticeHooks.cs`.

**Sections, in the field:** `CitySectionManager.SectionInfoList` (`CitySectionManager.cs:29`) is the set
the player is currently INSIDE, maintained by `AddSectionInfo`/`RemoveSectionInfo` (`:44/:47`);
`CurrentSectionId` (`:37`), `UpdateCurrentSectionId()` (`:65`). Fed by `CitySectionZoneGroup`
(`CitySectionZoneGroup.cs`) — a world Component with `onContact`/`onOverlapping`/`onSeparate`
(`:112/:114/:117`). `CitySectionZone.cs:8` and `via.physics/Zone.cs:6` are **empty stubs**: there is no
point-in-zone test to call. Per-section world AABBs do exist:
`SectionMapSettingCatalogUserDataHolder.GetSectionMapSettingData(uint)` (`:16`, static) →
`SectionMapSetting.SectionId` + `MapSettingBase.MinPos/MaxPos` (`:53/:61`).

**`CitySectionDataUserDataDict` is ENUMERABLE** (`TableDataManager.cs:960`, an `IDictionary<uint,object>`
on a resident table, `ResidentTableData.cs:134`). `ZoneNameService` currently does a keyed `get_Item`
plus a `ManageId` cross-check (`ZoneNameService.cs:130`) that **can reject a good record** — enumerating
the dict once per city and building `id → name` is both safer and cheaper. Avoid
`TryGetCitySectionDataUserData(uint, out ...)` (`:3079`): an `out` parameter.

### The good: interiors are nameable

`GUIAccessDataManager.GetWTCityOMSetterData_OM(OMType.Shop)` (`:126`, dict at `:90`) returns the
registered facility setters. Each `WTCityOMSetter` gives `GetDispName() : Guid` (`WTCityOMSetter.cs:929`
— feed the existing Guid resolver, yields e.g. "salón de belleza"), `GetShopCategory()` (`:908`),
`GetFacilityIconType()` (`:926`) and `SpawnedObject` (`:792`) for a transform. Skip
`GetShopId(out uint)` (`:911`). **The game's own "you went in":**
`Om008000.CallShopInSoundTrigger()` / `CallShopOutSoundTrigger()` (`Om008000.cs:241/:244`), with
`ShopId` (`:17`) and `IsBodyShop` (`:25`). There is **no** `IsInside`/`CurrentShop` member anywhere.

### Correction (2026-09-27): why the section id always read 0

`app.worldtour.CitySectionManager` is **not a managed singleton**: it is owned by
`app.GuiManager.AccessData` (`GUIAccessDataManager.SectionManager`, `GuiManager.cs:4653`,
`GUIAccessDataManager.cs:74`), so `API.GetManagedSingleton("app.worldtour.CitySectionManager")` found
nothing and `CurrentSectionId` was 0 in every session. Also available: static
`app.Kpi.WT.GetCurrentSectionId()` (`Kpi.cs:397`). The banner component is unreachable through the scene
(never found in game), so its requests are hooked instead. Full design and checklist:
[`wt-orientation.md`](wt-orientation.md). `UNVERIFIED` until the next test.

### The bad: street names and intersections DO NOT EXIST

Searched the whole tree. The only `Street` is `WTDefine.BattleType.Street` (`WTDefine.cs:5540`) and
`HudDef.BATTLE_TYPE.STREET` (`HudDef.cs:661`) — a fight type, not a road. There is no `Road`, `Cross`,
`Intersection`, `Corner`, `Block`, `Plaza` or `Avenue` type or member. **"Cruce entre X e Y" cannot be
read from the game**; it could only be synthesised from two named points, or derived geometrically from
navmesh nodes with three or more exits — our invention either way, and it must be labelled as such.

Named-place-with-position sources are exactly three: fast-travel points
(`FastTravelPointUserDataRecord.PointNameID.GUID`, confirmed in game), OM setters (above), and section
AABBs. `PointDataCityInfo` has an id and a `Position` but **no name** (`CityPointDataInfoBase.cs:12,20`).

### Also dead ends

`IconInfo.ZoneInfo` is `ZoneInfoData { float Radius }` and nothing else (`UIMapWindowBase.cs:319-323`);
there is no ZONE member in `ICON_TYPE` or `ICON_GROUP_TYPE`, and the radius decorates icons like
`BH_RAID_COOPACTION_AREA`. `WTAreaManager.getActiveAreaList()` (`:1573`) → `AreaControl.AreaName`
(`:341`) resolves to streaming FOLDER names (`wtc0400_*`), never localized — usable as a change trigger,
never as something to speak.

## World Tour — native navigation infrastructure leads (from stubs, 2026-09-12) — `NOT YET RUN IN GAME`

### Follow-up: independent exploration radar (2026-09-14)

**Implemented prototype, later 2026-09-14 — pending runtime verification.**
`FieldExplorationHooks` installs a dynamic pre-hook on
`UIHud_PlayerActionGuide.FlowEvent_LateUpdate`: read already active widget data, copy
strings/IDs, and announce from `LateUpdateBehavior.Post`. `FieldActionReader` requires
HUD/widget `IsShow`, excludes manual hides, reads `InputGuideParts.Control` visible text
before localized action-name fallbacks, and expires snapshots after 500 ms. It never
retains native HUD wrappers. New `MemberAccess.ReadFieldsFirst` checks concrete and
backing fields before getters, caching only metadata; existing consumers are unchanged.

`FieldAccessCatalog` samples `Om011000`, `Om016000`, `Om017000`, one family per 500 ms
and at most 32 objects per pass, rotating large lists. It retains only ID/text/position
snapshots for 6 s and reports within 12 m. Ladder `GroundPoint`/`EndPoint` require
`IsInitializeParam`; the nearest endpoint determines up/down description. Doors use
their int-width open-state enum; byte-width `Lock=AlwaysLock` describes a blocked door.
Other lock configurations do not claim the player lacks a key. Discovery does not claim
walkability and does not select a destination. Repeated announcements use identity,
state, approach-distance bands and alignment, with a 4 s speech gap. Area/field changes
clear the memory. Active action prompts take priority over access discovery; the existing
field, battle, dialogue and tutorial gates apply. An aligned discovery adds a two-note
cue using existing tone synthesis, without new audio files.

`AccessPointReader` now calls `GetNearPos` on enabled, non-dynamically-disabled shapes
from the target's `CheckSetting.ShapeData`, using the access component's own transform.
It honors the bool return and checks finite output before choosing the nearest point.
Global/default access must be enabled. This is still geometric proximity, not a guarantee
of reachability. Mission contact confirmation matches the HUD objective access-target
identity against the existing `CurrentAccessInfoList`.

`FieldNavPath` is split into query, binding, reading and diagnostics partial files.
`RouteLookAhead` preserves bends outside half the borrowed agent's radius instead of
skipping everything within 4 m. Four metres now bounds straight look-ahead only; point
reads are capped at 64. It reports left/right bends using the calibrated XZ basis.
`MissionBeaconHooks` refreshes at 500 ms, announces approaching/near turns, changes in
route availability, and nearby versus interaction-range states. The mission sound keeps
guiding inside 4 m and uses route distance when known. `BeaconDestination` preserves
approach metadata on failure, resets on goal identity change, and reuses same-destination
answers for throttled queries for at most 500 ms; actual failures invalidate that answer.
No automatic movement, interaction, camera turn or goal switching was added.

Radar continuous mode now defaults on; tutorial panels suppress it along with dialogue.
The front debounce ignores lateral-state changes. Its one-shot formatting was moved to
`FieldNavRadarReadout.cs`. The existing sensor/filter split and disabled navmesh-exit
finder remain; this prototype does not claim to find every alley or connect all floors.
All new captions have English, Spanish and Portuguese entries. Twenty-seven pure-logic
checks pass (`dotnet run --project tests/Navigation.Tests`); native semantics/audio still
need the runtime sequence below. The review findings that follow describe the pre-fix
implementation where stated.

Read-only review of the local generated game interfaces and current mod. These files
expose signatures and metadata, not native method bodies. Findings below are declaration
evidence and design implications, not runtime confirmation. No new native calls were made.

**Interaction-position binding mismatch.** In
`app.worldtour.avatar/AvatarAccessTargetBase.cs:467`, `bool GetNearPos(ref vec3,
Transform, ref vec3)` belongs to the NESTED `AvatarAccessTargetBase.AccessCheckShape`,
not to `AvatarAccessTargetBase` itself. Current `AccessPointReader.Bind` searches the
target component and parents, so it cannot find this declared method there unless a
runtime subtype adds another method. `MissionTargetService` calls that reader, but its
successful binding has not been verified. Correct investigation chain:
`target.CheckSetting.ShapeData` (or `target.GetShapeData(index)`) -> an enabled shape
-> `GetNearPos` on THAT shape, respecting the bool return. Existing reader ignores the
return as well. A nearest geometric access point is not necessarily reachable, nor an
entrance to a disconnected floor; the reader's existing comments overstate that guarantee.

`AccessCheckShape` includes `DistanceXZ`, `DistanceY`, the open-detail variants,
`ToAvatarAngle_Degree`, `CustomAvatarAngle_Degree`, and option names `Enable`,
`CheckCapsule`, `CheckToAvatarAngle`, `CheckAvatarAngle`, `CheckRayCast`,
`CalcWhenAreaOut`, `LandAccess`, `AirAccess`. Target has `DynamicDisableShapeIndexList`
and `Enable_AllAccess`/`Enable_DefaultAccess`. Do not treat every configured shape as
currently usable. Prefer already computed contact data over replaying `AccessCheck`.

**Final approach uses an existing successful runtime lead.** The earlier field-awareness
capture above confirms `AvatarManager.CurrentAccessInfoList` becomes populated at
interaction range (example 1.54 m); entries expose `AccessInfo.TargetInfo` with `Target`,
`ShapeIndex`, `NearPos`, `Distance`, `Angle`. This is a stronger starting point than a
fixed 4 m arrival sphere. Match the intended target and action availability; list presence
alone must not be assumed to prove mission completion. Additional declaration leads:
`AvatarBase.GetCurrentAccessTarget()` and
`PlayerAccessTargetSearcher.CurrentTargetInfo` / `CalclatedList`. Do not confuse the
currently selected contact target with the mission target.

**Action vocabulary already exists.** `app/UIWidgetPlayerActionGuide.cs:118` declares
`GuideDef`: `Ladder_Up`, `Ladder_Down`, `Ladder_UpDown`, `Ladder_Slide`, `Ladder_Catch`,
`Ladder_Up_Air`, `Open_TreasureChest`, `Open`, `Close`, `EnterShop`, `GetOutCity`,
`Climb`, `GetOver`, among others. Widget has `GuideType`, `AccessTarget`, `DispPosition`,
`ViewType`, `InputGuideParts`. `UIHud_PlayerActionGuide` has `_widgetList`, `ActionInfo`,
`RequestShowFromOM(GuideDef, WTOmAccessTarget, vec3)` and `RequestShowFromPlayer`.
Use active widgets, their localized GUI/input-guide text and target identity to announce
available actions. Confirm world versus screen coordinates before interpreting position.
No current mod code references this guide family. `PadGuideHooks` covers tutorial panels,
not this native context-action surface. Seeing a widget does not prove it is active.

**Ladder geometry is explicit.** `app.worldtour.om/Om011000.cs` declares `StartPoint`,
`EndPoint`, `GroundPoint`, `UpVec`, `AllLength`, `AllStepNum`, `IsInitializeParam`,
`GetBothEndPoints(out vec3, out vec3, out vec3)` and `GetNearStepPos`. The avatar exposes
`CalcNearLadderSelfPos(Om011000, out vec3, out vec3, out int)` and `CalcLadderSelfPos`.
Together these identify a ladder-specific component and possible approach geometry;
verify live instances and which endpoint is accessible from the player's floor. Merely
knowing the upper endpoint does not prove it connects to the objective's walkable region.
Read existing fields first; do not invoke contact actions or ladder-control actions.

**Door-like objects carry access restrictions.** `app.worldtour.om/Om016000.cs` exposes
`State` (`Close`, `UnlockKey`, `LockRequesting`, `LockRequestingReverse`, `Opened`,
`OpenedReverse`), `Side` (`BothSides`, `DefaultSide`, `ReverseSide`), `Lock`
(`NoLock`, `NeedKey`, `SwitchOm`, `AlwaysLock`), `Move` (`OpenOnly`, `CityChange`),
`CityId`, `FastTravelPointId`, `IsNeedKey`, `IsEnableExecute`, and `IsLightDoor`.
`Om017000` additionally has `PointID`, `CurrentState` (`Open`, `Close`), and
`ActionType` (`None`, `DoorTouch`). These are concrete door/transition leads, not a
confirmed catalogue of every entrance. Configuration `NeedKey` alone does not tell us
whether the player currently has the key. Do not call `RequestOpen`, `UnlockKey`,
`RequestAction` or script executors to discover state.

**Route corridor information can refine the radar.** `via.navigation.map/PathInfo.cs`
already offers `getPortalEdgeInfo(uint)` -> `{ PortalEdge, IsValid }`, `getPortalNode`,
`getLink`, `checkWallOverFloor` and `queryWallOverFloor`. `LinkInfo` has `Enabled`,
`Extra`, `Attribute`, `queryFromNode`, `queryToNode`; wall-over enum is
`NoWall/Up/Down/Same/Invalid`. Inspect OUR query report, not the borrowed NPC's
`NavigationInfo`, whose movement vector and next link describe that NPC. This may let
us inspect relevant corridor boundaries without the older `AIMap.findMapHandle` path,
but each additional native binding still needs a bounded runtime probe. A portal is a
polygon boundary, not automatically a physical doorway; wall-over is not proof of a
ladder, staircase or action the player can perform. Do not guess attribute bit encodings.

**Borrowed-agent caveat.** `NavigationSurface` exposes `AgentRad`, `AgentHeight`,
`PortalOffset`, `CornerCloseRad`, `UseLowerOnly`, `DisableUseWallSameLevel`, and
`AvoidSideSmallNode`; `PathQueryInfo` exposes `FilterTarget`/`FilterGroup`. A valid
NPC surface proves a loaded map, not equivalence to the player's body and traversal
permissions. Compare dimensions/constraints in diagnostics; do not modify the NPC.

**Runtime sequence:** automatically log only changing relevant records in exploration:
(1) contact-list target + active action widget for final approach; (2) one ladder's
endpoints + prompt from both ends; (3) a closed/open/restricted door and access side;
(4) a short route's portal edges and observed clearance. No diagnostic hotkey should
be required. Preserve current battle/menu/dialogue gates and native lifetime rules.
The available game log is dated 2026-09-09; it confirms old ground-level approach probes,
not the new ladder/door/guide bindings. No current runtime verification is claimed.

Result of a systematic sweep of `sf6 code/` for what the game itself has for navigation. Everything
below is **declaration evidence only** (signatures, no bodies) unless marked otherwise; each item
lists the type, the members that matter, and the caveat. Feeds `docs/wt-radar-improvements.md`.

**Navmesh node API (for the exit finder).** `via.navigation.MapHandleBase`: `queryClosestNode(vec3)`,
`queryClosestNode(vec3, NodeQueryInfo)`, `queryNode()` / `queryNode(NodeQueryInfo)` (city-wide —
banned), `checkOutOfMap(NodeQueryInfo)`, `queryAttributeBitNo(string)`. **`via.navigation.
NodeQueryInfo`** (concrete class): `setRegion(Sphere | AABB | OBB | Capsule)`, `clearRegion()`,
`EnabledOnly`, `ExcludeShapes`, `setFilter(string, FilterType[, bool])` — the bounded search.
**`via.navigation.map.NodeInfo`**: `Pos`, `Normal`, `Wall`, `WallHeight`, `ShapeType`,
`LinkBoundaryEdgeCount`, `LinkBoundary`, `LinkBoundaryMinY/MaxY`, `getGlobalVertexCount()` /
`getGlobalVertex(uint)`, **`queryLinkBoundaryEdge(uint) : via.LineSegment`**,
**`queryClosestLinkBoundaryEdge(vec3) : LineSegment`**, `queryLinkToNodes()`, `queryLinks()`,
`findIntersection(ref vec3, LineSegment)`, `hasAttribute(string)`. `via.LineSegment` is a value
type with `start`/`end` (`p0`/`p1`). No navmesh raycast exists anywhere in `via.navigation`
(exhaustive grep). Physics shape casts: `via.physics.System.castCapsule(Capsule, vec3, vec3,
FilterInfo, ShapeCastResult)` and `castSphere(...)` (+ `uint options` overloads).

> **Do not build auto-walk / teleport, and do not replace the radar (user decisions 2026-09-12).**
> The player-movement leads in the next three paragraphs are recorded as facts only; nothing may
> move the avatar. The navigation direction is "keep the radar, improve it" —
> `docs/wt-radar-improvements.md`.

**Player is driven by a navigation controller.** `app.worldtour.avatar.AvatarPlayerControll :
AvatarNaviControllBase : AvatarControllBase` (the only other subclasses are `WTNpcAI` and
`npc.WTNpcAIController`). Reached through `AvatarBase.Components` (`AvatarComponent.Controll`).
`AvatarPlayerControll.UpdateLocalPlayerInput(AvatarInputController, bool isNavigationWorking)` —
the game threads a "navigation is working" flag into the *player's* input update. CONFIRMED IN GAME
(2026-09-09, above): the player's `NaviSurface` is null, so `queryPathSync` must still borrow an
NPC's surface. `AvatarBase.StartNavi(PriorityType, NaviRequestData)` / `ForceEndNavi()` exist, but
`NaviRequestData` has no factory (only `WTNpcAI.Get*NaviData_ForBehaviorTree(out ...)`), so using it
means `CreateInstance` of a concrete class — legal but the crash-prone family; last resort.
Escape hatch: `AvatarNaviControllBase.IsDontUseAIMap` + `NaviControllInfo.OnUpdateWithoutAIMap`
(straight-line mode) and `SetSlaveMoveVec(vec3)` / `SetSlaveMoveRate` / `SetSlaveAngleRate`.

**Input synthesis (preferred auto-walk route).** `AvatarBase.CreateInputController(PriorityType)`
(+ `(PriorityType, bool destroyOnRequest, bool destroyOnRequestSuccess)`),
`CreateSingleRequestInputController`, `DestroyInputController(PriorityType)`,
`GetCurrentInputControllerPriority()`, `InputManager` (`AvatarInputControllerManager`:
`DefaultInputController`, `AddedInputControllerList`, `CurrentTargetController`, `MoveVec`,
`IsEnableInput()`). `AvatarInputController.PriorityType : byte { _Invalid, Default,
__DebugDefault, FieldEvent, MenuUI, Flow, BattleIn, __DebugForce }`. Setters: `SetMove(vec3)`,
`SetMoveRate(float)`, `SetAngleRate(float)`, `SetDoContact()`, `SetDashTurbo()`. **Space of the
`SetMove` vec3 is UNKNOWN** (world vs stick); discriminator: compare
`AvatarBase.GetCurrentInputMoveVec(out vec3)` with `GetMoveVec()` while pushing the stick under a
rotated camera. Nothing in the game ever auto-moves the free-roam player (cutscenes use a separate
`WTEventPlayer` actor and then warp: `ApplyEndPosition(out vec3, out Quaternion)`).

**Teleport.** `AvatarBase.WarpImmediately(ref vec3)`, `(ref vec3, ref Quaternion)`,
`(ref vec3, vec3 dir)`, `RequestWarp(ref vec3, ref Quaternion)`, `IsRequestingWarp()`.

**Camera.** `AvatarPlayer.RequestResetCamera(vec3 cameraDir)` / `(bool isforce, bool useDir,
ref vec3 dir)` / `()` — the game's own interpolated reset (`_ResetTargetRotation` on
`WTPlayerCameraController`), cleaner than writing `WTFollowCameraController.CurrentRotation`.
`app.helper.hWT.ResetPlayerCamera` — **native signature is `(System.Nullable<via.vec3>)`**, not a
bare `vec3`; `hWT.CtrlPlayerInput(bool)` gates player input. `WTPlayerCameraController` has no
target-yaw setter (only `followPlayer(vec3 rotation, bool)`, `followReset()`,
`calcTargetRotation(vec3, vec3)`, `getCurrentRotation()`). `WTCameraManager.CurrentActualPosition`
/ `CurrentActualRotation`.

**Authoritative interactable registry.** `app.GUIAccessDataManager` (GuiManagerLayerBase):
`IDictionary<HudDef.ContactUIType, IList<AvatarAccessTargetBase>> AccessTargetList`,
`GetAccessTargetData(ContactUIType)`, `IDictionary<OMType, IList<WTCityOMSetter>> OMSetterInfoList`,
`GetScenarioZoneData()`, `GetNpcDummyAccessTargetData(ContactUIType)`, `SectionManager`.
`HudDef.ContactUIType { None=-1, NPC=0, Legendary=1, OM=2, OtherPlayer=3 }`.
`AvatarAccessTargetBase`: `GetDispName()`, `GetContactUIType()`, `GetShopCategory()`,
`TryGetShopIdFromMyShopComp(out uint)`, `GetUIPanelBasePos(ref vec3)`, `GetMissionTargetInfo()`,
`IsMissionTarget`, `IsRelationToMaster()`, `AccessCheckSetting.ShapeData[i]` (`AccessCheckShape`:
`DistanceXZ`, `DistanceY`, `ToAvatarAngle_Degree`, `GetNearPos(ref vec3, Transform, ref vec3)`).
Concrete: `WTNpcAccessTarget` (`ObjectPos`, `IsLegendaryNPC`, `ShopComponent`),
`om.WTOmAccessTarget` (`DispName`, `IsFastTravelOpenOM()`, `IsHome()`, `IsBodyShop()`,
`TryGetContactIconDistance(out float)`). Gimmicks: `om.WTOMManager.AllOmList`, `OmWithId`,
`OmSetterWithId`; `WTCityOM` (`InstanceId`, `AreaId`, `AccessTarget`); `WTCityOMSetter`
(`SetAreaName`, `MissionTargetInfo`). Which `OmNNNNNN` is which gimmick is unverified beyond the
five breakable families already in `BreakableCatalog`.

**"Can I interact now".** `avatar.IAccessTargetSearcher.GetCurrentTarget()` →
`AccessTargetInfo { Target, Type, ShapeIndex, NearPos, Distance, Angle, BasePriority }`;
`AvatarManager.GetAccessableTarget(AvatarBase, ref IList<AccessTargetInfo>, ref IList<...>)`
(by-ref reference type: do NOT call). Prompt UI: `app.UICityHud_ContactPanel.mActivePanelList`,
`TargetPanel`, `static CheckForward(vec3, float)`; `UIWidgetContactPanel { TargetCtrl, Distance,
Priority, DispState, AssetType }`.

**Mission targets.** `app.UICityHud_MissionGuide`: `targetList`, `missionTarget`,
`GetNeareastTarget(vec3 playerPos)` (selection is nearest-only, no priority field),
`dispDistance` (straight-line — nothing in the UI layer touches `PathInfo`), `showArrow`,
`IsOutSideScreen`, `CheckHitTerrian(vec3)`, `NEAR_DISTANCE` / `FAR_DISTANCE`.
`ProgressMisionInfo { MissionTargetInfo, TargetObject, AccessTarget, NpcDummyTargetData,
TargetType (NPC|OM|ZONE), UIPos }`. Zone objectives: `worldtour.ScenarioZoneGroup`
(`ZoneTargetInfo { mTargetInfo, mListZoneTarget }`, `OnContactPos`, `ContactZoneID`),
`MissionZoneTarget.mRadius`, `WTMissionZoneSystem` (`mDicScenarioZone`,
`GetScenarioZoneGroup(uint)`, `CheckContactScenarioZone(vec3 start, vec3 end)`).

**Map icon registries.** `app.UIMapWindowBase`: `ICON_TYPE` (SHOP_APPAREL … MASTER_TALK_SP),
`ICON_GROUP_TYPE { All, TRAVEL, FACILITY, JOB, MISSION, MASTER, PIN, MAX }`, `IconInfo { GlobalPos,
UiPos, TitleText, Type, GroupType, Pattern, IsFastTravelPoint, Priority, ZoneInfo }`, `PinInfo {
CityId, PinTypeId, Position }`, `ConvertTo_UIPos(vec3)` / `ConvertTo_GlobalPos(vec3)`.
`app.UIDeviceMapWindow`: `IDictionary<ICON_GROUP_TYPE, IList<IconInfo>> mIconInfoList`,
`PinInfoList`, `TravelPoint`, `AddPinIcon(vec3 cursorPos, int colorTypeId)`, `RemovePinIcon(...)`,
`SetupIconInfo_Travel/_FacilityNPC/_FacilityOM/_Mission/_Master/_Job/_Pin()` (probably only
populated while the map is open — verify). Persistent pins: `worldtour.WTPlayerDataUI.
GetCityPinInfoList(uint cityId)` / `AddCityPinInfo` / `RemoveCityPinInfo`. **No compass type and no
route-guide line/arrow exist in the game** (exhaustive grep); the only directional HUD element is
the mission-guide arrow.

**World structure.** Fast travel: `worldtour.udCityPointList { CityPoints, FastTravelPoints }`
(`CityPointDataInfoBase { mPointId, Position, Rotation }`, `PointDataFastTravelInfo.
mAttachedData`), `CityPointHolder.PointList`, `WTFastTravelCtrl` (`PointID`, `UnlockPointIDArray`,
`CurrentState`). Streaming graph: `WTAreaManager._AreaCtrlDict`, `_ActiveAreaHashList`,
`OnAreaTransition`, `AreaData.NeighborhoodAreaList`. Physics: `via.physics.System.castRay /
castSphere / castCapsule / castShape / closestSphere / queryAabb` with `CastRayQuery
{ setRay(start,end) | (origin,dir,dist), enableNearSort(), enableOneHitBreak() }`.

**Audio.** `soundlib.SoundManager.postRequestInfo(RequestInfo)` is the single play entry;
`RequestInfo { SrcGameObj, TargetGameObj, Container, Positioned, ... }` — positional playback is a
GameObject + `Positioned`; still only existing soundbank events (see § Game audio in
`sf6-architecture.md`). `via.simplewwise.Driver.PlayerPosition` / `setListenerPosition`.
`app.sound.Accessibility` holds only battle gauge/side SEs; `app.Option.ValueType` has no
navigation-assist option (relevant: `MiniMapDisp=150`, `MiniMapRotate=151`, `CityHudAreaName=141`,
`CameraAssist=313`, `CameraResetDirection=316`, `SoundSpatial=516`).
