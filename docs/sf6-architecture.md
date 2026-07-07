# SF6Access — Architecture & Core Patterns

Project-specific knowledge for the Street Fighter 6 accessibility plugin. This is a
REFramework.NET **C#** plugin (not Lua). For generic RE Engine / REFramework API docs see the
other files in `docs/`. For per-screen type/field reference see [`sf6-screens.md`](sf6-screens.md).

> This document (and `sf6-screens.md`) is the durable, version-controlled record of everything we
> learned reverse-engineering SF6's UI. Prefer adding findings here over leaving them only in notes.

## Plugin layout

- `Plugin.cs` — entry point; initializes Tolk. Hooks auto-register via attributes (there is **no**
  central hook list; each `Hooks/*.cs` registers itself).
- `Services/` — shared infrastructure (see below).
- `Hooks/` — one file per screen/feature (~65 files). Naming mirrors the screen
  (`StatusMenuHooks`, `TrainingReversalHooks`, `BattleInfoHooks`, …).
- `sf6 code/` — decompiled game code (interfaces only, no concrete classes). **Gitignored.**
  Useful for type/field names, but runtime uses CONCRETE types — always verify names via a dump.

## Core services

### `Services/FlowHelper.cs` — the workhorse
- `FindFlowParam(typeName)` / `FindActiveParam` — iterate `UIFlowManager._Handles` to find a screen's
  flow param by type FullName.
- `TrackFlowParam(type, cached, out changed)` — the **stale-param re-entry** helper (see below).
- Field reads: `GetObjectField` (plain + `k__BackingField`), `ReadIntField`, `ReadBoolField`,
  `ReadShortField` (typeof(short)), `ReadByteField`. **Use the width-correct reader** — reading a
  `short`/`byte` field as int pulls adjacent bytes and yields garbage.
- `Call` / `CallInt` — `IObject.Call` wrappers; dispatch fine on concrete instances even when
  interface *property getters* don't.
- Guid resolution: `ResolveGuid` (200 ms timeout — `via.gui.message.get()` crashes on some Guids),
  `ResolveGuidField`, `CleanTags`, `SpeakableIcons` (keeps input-tag content as speech),
  `ResolvePlatformTags` (`<PLATMSG>` via `app.MessageManager.ExchangePlatformMessage`).
- Lists: `GetListCount` / `GetListItem` (detects arrays by type-name `"[]"`), `ReadSelectedItemText`
  (Call `get_SelectedItem` then walk subtree — never index `_Children`), `ResolveItemName`
  (`app.InventoryManager.GetName(ItemCategory, itemId)`).
- Misc: `AddressOf()`, `DiffSegments(old,new)` (announce only changed segments on L/R),
  `GetDisplayLang()` / `GetTrainingDisplaySetting()` / `AreSubtitlesEnabled()`.

### `Services/GuiTextReader.cs` — on-screen text scraping (fallback)
- `scene.findComponents(via.gui.GUI runtimeType)` → `GUI.get_View()` → recursive
  `Control.getChildren(System.Type)`. Get the `System.Type` via `TypeDefinition.GetRuntimeType()`.
- `via.gui.Text` is an Element/PlayObject, **not** a Component — walk down from the GUI, don't
  `findComponents` it directly.
- Methods: `ReadControlTextJoined`, `ReadControlTexts(resolveMessageIds)`, `ReadSceneTexts`
  (Message-only), `ReadTextsByOwner(owner)`, `FindGuiViews(name)`, `ReadPlayStates`,
  `FindSelectedItemIndex(view, playStateName)`.
- **Expensive** — call on-demand, cache the view, refresh only every N frames.

### `Services/ScreenReaderService.cs` — speech (Tolk, `DavyKager`)
- `Speak(text, interrupt)` — `interrupt:false` queues, `interrupt:true` cancels queued speech.
- **Central duplicate filter:** drops text identical to the previous within `DUPLICATE_WINDOW_MS`
  (currently 250 ms; a 600 ms window that also dropped *contained* substrings existed earlier).
  Consequence: for runs of identical rows ("Empty"/"Slot"), make each utterance DISTINCT (append the
  slot/preset number or position) or the filter collapses them.
- Every `Speak` is logged (`Speak(interrupt|queue): text`) — ground truth for diagnosing double reads.

### Other services
- `GameStateTracker.cs` — change detection (avoid spam); ~2.5 s state expiry.
- `GroupFocusPoller.cs` / `Hooks/GroupFocusHooks.cs` — generic focused-row reader (see below).
- `LeagueRankResolver.cs` — resolves `LeagueRankWithLevel` → localized rank (shared, see screens doc).
- `ControlTypeNames.cs` — Classic/Modern/Dynamic name from `EConfigInputType` (shared).
- `InputNameResolver.cs` — pad/keyboard button names.
- `ComboTracker.cs` — authoritative combo detection via `cTeam.mComboCount`.
- `AvatarStatsReader.cs` — World Tour avatar stats.
- `ObjectDumper.cs` / `ScreenshotService.cs` — research tools (see Dump tools).

## Screen adapter architecture (menu hooks) — `Services/Ui/`

Most menu/screen hooks share one shape: search `_Handles` for a flow Param, activate, then each frame
read the focused row / changed value and announce it (diff-gated). Historically every hook re-wrote
that scaffold (poll counter, `_isActive` lifecycle, its own `[Callback]`, and a hand-rolled
first/changed/diff gate). The `Services/Ui/` layer removes that duplication with a reusable
bottom layer + a central dispatcher, following `reference/ui-accessibility/generic-strategy.md`.

- **`UiDispatcher`** — the single `[Callback(LateUpdateBehavior.Post)]` that ticks every registered
  adapter. This central tick is *required*: REFramework.NET discovers `[Callback]` methods by attribute
  scan, so a base class cannot supply an inherited callback — one dispatcher driving instances is what
  enables a base class at all. Exposes `AnyAdapterActive` (for suppressing the generic reader).
- **`ScreenRegistry`** — the `[PluginEntryPoint]` that instantiates and registers adapters. Adding a
  screen is one line here.
- **`ScreenAdapter`** (abstract) — owns the poll lifecycle: `Locate()` searches every `SearchInterval`
  frames while inactive; once active, `OnPoll()` runs every `ReadInterval`; on close, `OnDeactivate()`.
  **`SingleParamScreenAdapter`** is the 80 % case — bound to one Param type, it does `FindFlowParam` +
  `TrackFlowParam` stale-instance re-bind for you; the subclass writes only `OnBind` (cache child
  widgets + announce entry, called on open *and* on Param recreate), `OnExit`, and `Poll`.
- **Archetype readers** ("how each control sounds", reused across screens):
  - `GroupFocusPoller` — focused row of a `UIPartsGroup`/list/grid (list-item archetype).
  - `ValueTextWatcher` — a set of `via.gui.Text` fields → announce only the changed value
    (slider/checkbox/dropdown archetype); `Compose(...)` joins fields for an entry announcement.
  - `TabWatcher` — tab index → label on change (tab-bar archetype).
  - `ChangeGate` — the first/changed/diff-gate-before-speak decision for one focused `(index, text)`
    source (moving rows speaks the whole row; editing a value speaks only the `DiffSegments` result).

**Migrating a hook:** drop its `[Callback]`/`[PluginEntryPoint]`, make it extend
`ScreenAdapter`/`SingleParamScreenAdapter`, move its per-widget reads onto the archetype readers, and
register it in `ScreenRegistry`. Legacy hooks that still own a `[Callback]` run untouched alongside the
dispatcher, so migration is incremental. Before converting, grep for external references to the hook's
public statics (`IsInX` suppression flags etc.) and preserve them. Reference examples:
`Hooks/MatchingSettingHooks.cs` (single-Param) and `Hooks/OptionSubScreenHooks.cs` (multi-Param).

**Not every hook fits.** Method-hook–based hooks (their core is `method.AddHook(false)` on game
methods — combat/combo readouts, subtitle advance, social chat, side-select Left/Right) are a different
pattern and stay as-is. The adapter base is for the poll-a-flow-Param screens, which are the bulk of
the duplication.

## Critical IL2CPP gotchas (SF6 / RE Engine)

- **Attribute hooks (`[MethodHook]`) do NOT fire for interface dispatch.** Use dynamic hooks:
  `method.AddHook(false)`. Don't add both `AddPre` and `AddPost` to the same dynamic hook (breaks pre).
- **Interface property getters (`get_X`) return null/empty on concrete IL2CPP types** — read the
  FIELD directly (`GetField` + `GetDataBoxed`). `FlowHelper.Call` / `GetSelected*` still *dispatch*
  fine on concrete instances; it's only typed-proxy property getters that bite.
- `UIFlowManager._Handles` is a **field**, not a property; iterate it, **newest first** (pick first match).
- `IObject.Call` with a full signature string (`"getChildren(System.Type)"`) does **not** resolve —
  use `TypeDefinition.GetMethod(sig).InvokeBoxed`.
- `Field.GetDataBoxed()` returns `REFrameworkNET.ValueType` for structs (not System types). Pass that
  ValueType DIRECTLY to `InvokeBoxed` — converting to `System.Guid` causes an access violation.
- `via.gui` `get_Position` returns nothing on Text/Control — don't trust it for ordering.
- C# discard `out _` does **not** compile here (namespace `_` exists) — use a named dummy.
- **Callback timing:** use `LateUpdateBehavior.Post` (data is fresh); `UpdateBehavior.Pre` sees stale data.
- `_Children` order can be REVERSED vs `SelectedIndex` — never index `_Children`; use
  `Call("get_SelectedItem")` (or `UIPartsGroup.GetFocusChild()`), then read the subtree.

## Stale-param re-entry pattern (MANDATORY for flow-param hooks)

Never trust a cached `mIsActive` or a type-name-only match. Every tick: re-scan `_Handles` via
`FindActiveParam`, and when the found param's `GetAddress()` differs from the cached one, re-bind
(`ActivateWith`). Return "not active" when none is found. Params are frequently destroyed and
recreated (e.g. Status menu on equip, guide flows on loop/step). `FlowHelper.TrackFlowParam` packages
this. Applied across BattleSettingsHooks, StageSelectHooks, SideSelectHooks, NewsHooks,
CommandListHooks, CustomRoomHooks, ShortcutSettingHooks, KeyConfigHooks, MatchingSettingHooks,
StatusMenuHooks, EmulatorPauseHooks, TickerHooks, AvatarCreateHooks, FGMenuHooks, and ~14 others.

## Generic focused-row reader (`GroupFocusHooks` + `GroupFocusPoller`)

- Auto-discovers `UIPartsGroup` / `UIPartsSimpleList` / `UIPartsScrollList` / `UIPartsScrollGrid`
  fields on a watched param and announces only the FOCUSED row. Enum list items read via `get_Item`
  `InvokeBoxed(typeof(int))`.
- Screens opt in via `WatchPrefixes` (type-FullName prefixes). Screens with a dedicated hook opt OUT
  via `ExcludedTypes` (and MainMenuHooks suppresses their `FocusChanged`).
- Focused child: prefer `UIPartsGroup.GetFocusChild()` (authoritative) over `_Children[_FocusIndex]`
  (order can be reversed). `GetFocusedChild` falls back to the index when the method is absent.
- Polls faster while idle (every 20 frames, no active type) than while active (60) so a freshly
  opened menu activates within ~0.3 s instead of ~1 s.

### Generic first, dedicated readers only when justified (user preference, 2026-07-06)

New screens should try the generic reader first (add a `WatchPrefixes` entry — one line). Write a
dedicated reader ONLY when the screen needs per-screen knowledge the generic reader cannot infer:

1. **Detail/tooltip panels outside the focused row** — every screen puts the description in a
   different widget with different element names (sometimes hidden texts, sometimes unresolved WLTAG
   raws); no generic rule can associate row → panel.
2. **Junk vs. meaningful elements that CONFLICT across screens** — e.g. `e_txt_num` is a junk "0" on
   WTM perk rows but the booth number on Battle Hub tables; it cannot be filtered globally.
3. **Non-navigable panels** — the generic reader is focus-driven; a static info panel (WTM Battle
   Info) produces no events, so announce-once-on-entry logic must be screen-specific.
4. **Labeling bare values** — saying "Damage 700" instead of "700" requires knowing what
   `e_text_value` means on that screen.

Never encode per-screen if/else inside `GroupFocusHooks` — that is a dedicated reader in disguise
and every tweak risks regressions on the ~30 screens it already serves. Dedicated readers must stay
THIN (~100 lines of screen mapping) and reuse the shared services (`GuiTextReader`,
`GroupFocusPoller`, `FlowHelper`, `ScreenAdapter`).

TODO (generic improvement, agreed 2026-07-06): skip texts containing a `{` placeholder (e.g.
"SA {0}") in generic row reading (`FlowHelper.FormatRowTexts` / GroupFocusHooks row paths) —
template junk is never speakable.

## Localization (ALWAYS prefer game text)

Read text from the game's localization/GUI system; hardcode strings only as a **last resort** after
verifying the text is image/texture-based and truly unreadable, and document WHY. Resolution order:
1. `via.gui.Text` — `get_Message`, or `MessageId` → `via.gui.message.get(Guid)`.
2. Guids from data fields (`SpinText_MessageList`, `TableDataManager`, record `messageId.GUID`).
3. GUI tree walk (`GuiTextReader`).
4. Poll across frames for text set programmatically (typewriter/late-load).
5. Mod-specific fallback (documented) via **`Services/LocalizedText.cs`** — never add an inline
   language switch in a hook. The code holds ONLY the English defaults; the translations live in
   **`SF6Access/lang/*.txt`** (one `key=text` UTF-8 file per game language, copied on build to
   `<game>\reframework\plugins\managed\SF6Access.lang\`), loaded by `Services/LangFile.cs` with the
   chain: current-language file → `en.txt` → in-code English. Translators can fix any wording
   without recompiling; non-English files only need the keys that DIFFER from `en.txt`.

- Display language: `OptionManager.GetOptionValue(611)` (DispLanguage `TypeId`); the value is the
  game's language-LIST index, in options-menu order: 0 Ja, 1 En, 2 Fr, 3 It, 4 De, 5 Es, 6 Ru, 7 Pl,
  8 Pt-BR, 9 Ko, 10 Zh-Hant, 11 Zh-Hans, 12 Ar, 13 Es-LATAM (1/5/8/13 runtime-confirmed anchors).
  `FlowHelper.UiLang` covers all of them; lang file names = the enum member lowercased ("zhhant.txt").
  Currency/brand proper nouns (Zenny, Fighter Coins, Drive Tickets, World Tour, Battle Hub…) stay in
  English, matching the game's own localizations.
- Input tags kept as speech by `SpeakableIcons`: `<INPT id="BTL_X" type="dc">` (type="g"=stick),
  `<CMD _236>` (numpad motion), `<ICON …>`, `<TYPEICON c>`. The English vocabulary (directions,
  motions, attack icons, input glyphs) lives in **`Services/FlowHelperVocab.cs`** (partial
  FlowHelper); other languages override per key ("dir.2", "motion.236", "icon.lp", "input.BTL_X")
  in their lang file. RELEASE PACKAGING: the `SF6Access.lang` folder must ship with the DLL.

## Research / dump tools (`Services/ObjectDumper.cs`, `ScreenshotService.cs`)

Output lands in `<game>\reframework\data\` (path derived from `Environment.ProcessPath`).

- **F8 — auto-dump toggle (PRIMARY tool).** While enabled, every NEW flow-param type that appears
  (FlowTrackerHooks → `QueueAutoDump` on transitions) gets its fields + on-screen GUI texts appended
  to ONE session file (`sf6access_autodump_HHmmss.txt`) after a ~90-frame delay so the screen inits.
  Dedupes by type per session; skips non-`app.` types and `BaseParam_Create`. **OFF by default**
  (speaks "Auto dump enabled/disabled" on toggle).
- **F9 — focused state dump.** Active flow handles (with field values) + on-screen GUI texts →
  `sf6access_state_HHmmss.txt`. Small/fast; what menu research usually needs.
- **Shift+F9 — heavy full dump** (`DumpEverything`: handles + managed/native singletons + TDB scan)
  → `sf6access_dump_HHmmss.txt`, for hunting something not on screen.
- **F7 — PNG screenshot** → `sf6access_shot_HHmmss_N.png` (GDI BitBlt of the foreground window).
- Dump tools use `GetAsyncKeyState`, so they work without window focus. **Letter-key shortcuts**
  (e.g. "G" to re-read stats) are instead gated on the game being foreground
  (`GetForegroundWindow`/`GetWindowThreadProcessId == Environment.ProcessId`).
- Intra-flow popups/submenus that create no new flow param aren't captured by F8/F9 — use F7.
- Research workflow: toggle F8, navigate the flow once, send the session file + `re2_framework_log.txt`.

## Release packaging

Players extract `SF6Access.zip` into the SF6 game folder (merge). The asset is named **without a
version** so the permanent link never changes:
`https://github.com/Ali-Bueno/sf6Access/releases/latest/download/SF6Access.zip` — keep this name on
every release. Zip layout mirrors the game root:

```
dinput8.dll                    (REFramework loader)
Tolk.dll, nvdaControllerClient64.dll   (native, game root)
re2_fw_config.txt              (overlay hidden, menu key = Pause)
README.txt                     (EN+ES install + "send me logs/dumps")
reframework\plugins\
  Ijwhost.dll, REFramework.NET.dll, REFramework.NET.runtimeconfig.json
  managed\SF6Access.dll + managed\dependencies\*.dll
```

Build steps: `dotnet build` (fresh DLL copies into the game folder) → copy the files above into the
kept `release\SF6Access\` staging folder → `Compress-Archive` the CONTENTS into `release\SF6Access.zip`
→ `gh release create vX.Y.Z release\SF6Access.zip …`. EXCLUDE `managed\generated\` (per-PC),
`reframework\data\`, and `.xml` doc files. Players need **.NET Desktop Runtime 10 x64** (linked in README).
Do not create GitHub releases automatically — only when explicitly asked.
