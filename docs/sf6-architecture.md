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
- `ColorNamer.cs` — RGB/HSL → localized spoken color name ("dark red"/"rojo oscuro"); the game
  stores avatar colors as raw values with no name table (used by `Hooks/AvatarCreate/`).
- `FlowHelperStructs.cs` (partial FlowHelper) — struct-field reads: `ReadColorField`
  (via.Color → packed rgba uint) and `ReadVec2Field`, via ValueType address + Marshal.
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

**Migration status (2026-07-07): COMPLETE.** Every hook whose core is a poll lifecycle now extends
`ScreenAdapter`/`SingleParamScreenAdapter` and is registered in `ScreenRegistry` (~50 adapters). The
recipe used: drop the hook's `[Callback]`/`[PluginEntryPoint]` poll scaffold, implement
`Locate`/`OnBind`/`Poll` (or `OnActivate`/`OnPoll` for multi-Param), keep any `method.AddHook(false)`
registrations in a static `[PluginEntryPoint]`, and preserve externally-read statics via the `_self`
pattern (`private static X _self;` set in the ctor; `public static bool IsInX => _self != null &&
_self.Active`). Reference examples: `MatchingSettingHooks` (single-Param), `OptionSubScreenHooks`
(multi-Param), `FighterSettingHooks`/`AvatarCreateHooks` (custom `_Handles` walk in `Locate`),
`StatusMenuHooks`/`NewsHooks`/`ComboTrialHooks` (per-frame work: `ReadInterval = 1` + an instance
tick gating heavier reads), `TutorialHooks`/`BootMessageHooks` (Locate keeps the adapter alive while
state ages out / during flow-param-less phases), `KeyConfigHooks` (a popup Param outliving the menu
keeps the adapter active).

**Hooks that deliberately stay OFF the adapter (do not migrate them):**
- *Event-driven* — their `[Callback]` is a pending-work flusher or a combine timer, not a poll
  scaffold; activity comes from `method.AddHook` events: `FGMenuHooks`, `OptionMenuHooks`
  (`IsInOptionMenu` is get+SET by MainMenuHooks), `TutorialControlTypeHooks`, `CharacterSelectHooks`,
  `AvatarEmoteHooks`, `ItemNoticeHooks`, `SocialChatHooks`, `SpTalkHooks`, `TrainingAttackDataHooks`.
- *Always-on monitors (no screen to own)*: `BattleInfoHooks` (VS/rounds/matchmaking + per-frame
  watchdog), `GuideTextHooks`, `DialogHooks`, `ExtremeBattleHooks`.
- *Infra*: `GroupFocusHooks`, `MainMenuHooks`, `FlowTrackerHooks`, `FocusValueHooks`,
  `StatusStatsDiagnostic` (F6 dev tool), and the research tools (`ObjectDumper`, `ScreenshotService`,
  AvatarCreate's global F11 dump callback).

## Critical IL2CPP gotchas (SF6 / RE Engine)

- **Attribute hooks (`[MethodHook]`) do NOT fire for interface dispatch.** Use dynamic hooks:
  `method.AddHook(false)`. Don't add both `AddPre` and `AddPost` to the same dynamic hook (breaks pre).
- **Interface property getters (`get_X`) return null/empty on concrete IL2CPP types** — read the
  FIELD directly (`GetField` + `GetDataBoxed`). `FlowHelper.Call` / `GetSelected*` still *dispatch*
  fine on concrete instances; it's only typed-proxy property getters that bite.
- `UIFlowManager._Handles` is a **field**, not a property; iterate it, **newest first** (pick first match).
- `IObject.Call` with a full signature string (`"getChildren(System.Type)"`) does **not** resolve —
  use `TypeDefinition.GetMethod(sig).InvokeBoxed`.
- **`FlowHelper.Call(obj, name, params object[] args)` — the 3rd+ params are METHOD ARGUMENTS, not a
  flag.** For a NO-ARG getter call `Call(obj, "get_X")` with nothing extra; passing `Call(obj, "get_X", false)`
  invokes `get_X(false)`, finds no such overload and returns null (silently). This bit the whole World
  Tour field reader — `GetDispName`/`get_Transform`/`get_Position`/`GetContactUIType`/`IsActivated` all
  returned null, so the radar never spoke a name and positions read (0,0,0). Match `GuiTextReader`, which
  calls `Call(transformObj, "get_Position")` with no trailing arg.
- `Field.GetDataBoxed()` returns `REFrameworkNET.ValueType` for structs (not System types). Pass that
  ValueType DIRECTLY to `InvokeBoxed` — converting to `System.Guid` causes an access violation.
- **`GetDataBoxed(addr, isContainerValueType)`: the flag describes the CONTAINER, not the field.** When
  reading a component out of a `via.vec2/vec3/vec4` (or any struct) the container IS a value type, so it
  must be `true`; `false` makes the read skip a managed-object header that isn't there and the
  components land at the wrong offsets — x/y come back 0 and z reads adjacent garbage (this silently
  broke every World Tour avatar world position). Use `FlowHelper.ReadVecComponent`, the single shared
  reader (getter first, then the value-type field path); don't hand-roll a second copy.
- **NEVER pass a generated engine type (`typeof(via.vec3)`, `via.Quaternion`, `via.physics.ContactPoint`)
  as the target return type of `InvokeBoxed` / `GetDataBoxed`.** Those generated C# types are
  **interfaces**, not structs. REFramework already boxes the result correctly from the member's own TDB
  type; naming an interface on top makes it wrap that result in a `DispatchProxy`, and a proxy is not a
  `REFrameworkNET.ValueType`, so `ReadVecComponent` finds neither the value-type fields nor a `get_x` it
  can dispatch — **every component reads back 0** and it looks exactly like an object sitting at the world
  origin. Pass nothing (or `typeof(object)`, which `TranslateBoxedData` leaves untouched) for struct
  members and struct returns; keep a target type only for primitives, where it just picks the final
  conversion. The width worry is unfounded: boxing always uses the member's real TDB type.
- **`out` / `ref` parameters: REFramework copies NOTHING back into the `object[]` after the call.** The
  only thing that works is pointer aliasing — every argument implementing `IObject` is passed as
  `Ptr()`, i.e. its own address, so the engine writes straight into it and you read the buffer
  afterwards. A `ref float` needs the same treatment (a boxed `float` is passed BY VALUE, and is even
  up-converted to `double` first). Shape the buffer from the method's own `GetParameters()[i].Type`,
  never by naming a type by hand. `ValueType.New<T>()` is NOT the way — with a generated interface `T`
  it hands back a proxy, which marshals as nothing useful.
- **DO NOT use `TypeDefinition.CreateValueType()` for a buffer native code writes into** (this replaces
  earlier advice in this file — it is what crashed SF6 from the F10 field probe, 2026-09-04). Decompiling
  `REFramework.NET.dll` shows `ValueType` is a managed `new byte[ValueTypeSize]` on the GC heap, and
  `ValueType.Ptr()` takes its address inside a `fixed` block that is **released before the pointer is
  returned**. The address handed to the engine is therefore unpinned — a GC during the native call moves
  the array and the engine keeps writing into memory that is no longer ours. It is also exactly
  `ValueTypeSize` bytes with no slack, on an 8-byte-aligned array, so a whole-register store of a 12-byte
  `via.vec3` can run off the end and an *aligned* SIMD store can fault. Symptom: correct data comes back,
  then the game dies silently seconds later with nothing in `re2_framework_log.txt`. Use
  `SF6Access/Services/WorldTour/FieldOutBuffer.cs` instead: `Marshal.AllocHGlobal` (never moves),
  rounded up to and aligned on the engine's own vector-register width read from the TDB (`via.vec4`),
  zeroed per call, wrapped in `NativeObject.FromAddress` — whose `Ptr()` returns that stable address
  verbatim.
- **NEVER call a method whose `out` / `ref` parameter is a REFERENCE type.** For a value type the by-ref
  address is a struct buffer we can shape correctly. For a reference type there is no buffer that can be
  shown correct: the engine either stores an object pointer *through* the address (trampling the header
  of whatever was passed) or writes the whole record into REFramework's own argument slot. Both are
  native writes outside anything we allocated. Check `GetParameters()[i].Type.IsValueType()` and
  **skip the call**, printing why. Concrete case:
  `AvatarState_FieldBase.CastRay(CastRayTypes, out app.CollisionSystem.HitResult)` — `HitResult` is a
  class, so `CastRay` returned a truthful `bool` over a permanently blank `HitResult` and corrupted the
  heap. Its sibling `CastRayAll(CastRayTypes, via.physics.CastRayResult, eFilterInfo)` is safe because
  `result` carries **no** by-ref marker (the same signature does mark its `vec3` params, so the absence
  is real information): it is an ordinary by-value object the engine mutates in place, created with
  `CreateInstance(0)` and **not** globalized. It also returns strictly more —
  `NumContactPoints` + `getContactPoint(i)` → `via.physics.ContactPoint` (Position/Normal/Distance/
  TimeOfImpact), returned BY VALUE and boxed by REFramework, so no caller buffer is involved at all.
- **Don't `Globalize()` a buffer the engine writes into.** `Globalize` is an `AddRef` with no inverse
  besides `Release`, so it roots the object forever (a leak per probe run) and keeps a possibly-damaged
  object alive for later traversal. Out buffers live for one synchronous call inside a single frame and
  need no rooting at all.
- **`AvatarBase` has no `DrawObj`** — in the decompiled source `DrawObj` only exists inside the nested
  per-body-part `WTBodyDisp` struct. Reach an avatar's transform through its own
  `Component.get_GameObject()` → `get_Transform()` → `get_Position()`.
- **World coordinates are right-handed, Y-up** (confirmed in game 2026-07-20 via the WT clock-direction
  calibration): facing along `forward` on the XZ plane, the RIGHT side is `forward × up = (−fz, fx)`.
  `via.Transform.get_AxisZ` is the engine's forward-axis idiom (there is no `get_Forward`); a camera
  forward is best derived from two positions (`app.CameraManager` `LookAtPosition − CameraPosition`) —
  no quaternion math, no sign ambiguity. Details in `docs/sf6-screens.md` § Clock direction.
- `via.gui` `get_Position` returns nothing on Text/Control — don't trust it for ordering.
- **The screen reader stops speaking at an embedded `\n`** — multi-row game texts (dialogue lines,
  descriptions) carry real newlines between visual rows, so a raw `Speak()` reads only the first row.
  Flatten whitespace before speaking (`Regex.Replace(text, @"\s+", " ").Trim()`, as
  SpTalkNovelHooks/GuideTextHooks do) whenever a hook speaks raw game text.
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
- Platform/variant + reference tags via `FlowHelper.ResolvePlatformTags` (call it BEFORE `CleanTags`,
  which would silently erase them). Used by Tips/help windows and tutorial operation prompts, where the
  body renders as `<PAD ref="Name"><KBM ref="Name">` device variants (e.g. the World Tour "how to walk"
  tip). The pipeline (runtime-confirmed 2026-07-07 via probe): `app.MessageManager.ExchangeGamePadTag` /
  `ExchangeKeyboardMouseTag` take the tag's INNER arg (`ref="Name"`) and, on the ACTIVE input device,
  emit `<REF Name>` (empty on the other device); then `<REF Name>` is a message-BY-NAME reference,
  resolved by `FlowHelper.ResolveMessageName` = `via.gui.message.getGuidByName(Name)` → `ResolveGuid`.
  `<PLATMSG>` (Steam-store dialog whole-message) uses `ExchangePlatformMessage`. NOTE: if a Tips body
  reads only its heading, the `UIFlowDialog.TipsParam.ArrPage[i].Message` Guid was empty — fall back to
  the `Tips_Media` GUI raw text and run it through this resolver.
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

## Game audio (Wwise) — for spatialized beacons

Mapped and **confirmed in game 2026-08-03**: triggering a World Tour NPC's own sound container plays
the sound **in true 3D at that NPC's position** (verified by rotating the camera — the sound stays
glued to the NPC). Spatialized beacons are therefore built on the game's own audio.

Why it matters: a beacon that marks an NPC's position is far more useful in 3D (real panning,
distance attenuation, occlusion, and it obeys the player's own volume sliders) than a stereo pan we
mix ourselves. SF6 can do that — with one hard limit.

- **The engine is Wwise, under `via.simplewwise`** (there is NO `via.wwise` namespace), plus
  `via.wwiselib`. Managed layers on top: `soundlib.SoundManager` (all-static), `soundlib.SoundContainer`
  → `app.sound.SoundContainerApp` (the per-GameObject component), `app.sound.SoundManager` (a
  `via.Behavior`, not a singleton).
- **HARD LIMIT: we cannot play our own audio file this way.** Wwise only fires events that already
  exist in SF6's soundbanks. Using the game's audio means picking an existing SE, not shipping a sample.
  Shipping our own sample means mixing it ourselves (no HRTF, stereo pan only) — the fallback.
- **Playing at a position.** Two mechanisms, both first-class:
  - by GameObject — `SoundContainer.trigger(uint trgId, via.GameObject positionGameObj, …)`; the
    engine tracks the object, which is what we want for a moving NPC;
  - by raw position — `SoundContainer.trigger(uint trgId, via.vec3 pos, …)`,
    `app.sound.SoundContainerApp.trigger(uint, via.vec3, …)`, and the static shortcut
    `app.UISound.Trigger(uint triggerId, via.vec3 pos, app.sound.SoundManager.ContainerType)`.
  - **Simplest path: NPCs already carry their own emitter.** `app.sound.SoundNPCBehavior` +
    a sibling `SoundContainerApp` sit on the NPC's GameObject (same component array
    `AvatarFieldReader.DescribeAvatar` already walks), so calling the plain
    `soundlib.SoundContainer.trigger(System.UInt32)` on *that* container needs no vec3 at all — the
    position comes from the GameObject for free.
- **Trigger ids are hashed `uint`s with NO enum anywhere in the TDB.** They can only be discovered at
  runtime by walking `SoundContainer.AllTriggerInfoListData → SoundTriggerInfoListData.TriggerInfoList
  → SoundTriggerInfo.TriggerId / .EventId`. `SoundContainerApp.exists(uint)` validates one.
  Raw Wwise names hash via `via.simplewwise.Driver.getIdFromString("Play_…")`.
- **Design principle this unlocks:** reusing the game's own sounds gives audio cues that are
  *diegetic* — they read as part of the world instead of a mod layered on top, so they locate things
  without breaking immersion the way synthetic beeps do. Prefer an existing SE/voice over a shipped
  sample wherever one fits.
- **HAZARD: some triggers LOOP.** Firing a trigger blind can leave a sound playing forever (confirmed
  2026-08-03 — it forced a game restart). Anything that fires triggers must be able to stop them:
  - `soundlib.SoundContainer.stopTriggered(uint trgId, via.GameObject gameObj, uint duration)` —
    targeted, `duration = 0` stops immediately instead of fading;
  - `soundlib.SoundManager.stopAll()` — static panic button, silences everything;
  - also available: `stopEvent(GameObject, uint evId, uint duration)`, `stopEventByRequestId`.
  `stopAll()` works but **also kills the music, which does not restart on its own** — treat it as a
  last resort, not the default panic button. For the shipped beacon this means **never fire an
  unvetted id**: use a curated set, and/or watch `RequestInfo.Playing` and stop anything still
  sounding past its expected length.
- **Watching what the game itself plays — polling does NOT work; use a hook.** `soundlib.SoundManager`
  has the static fields `_RequestInfoList` (`IList<RequestInfo>`), `_RequestInfoDict`
  (`IDictionary<uint, RequestInfo>`) and `_GameObjectInfoDict` (`IDictionary<ulong, GameObjectInfo>`,
  each with its own `RequestInfoList`), read with `GetDataBoxed(..., address 0, false)`. Each
  `RequestInfo` carries `RequestId`, the inherited `TriggerId`, `SrcGameObj`, `Playing`/`PlayingId`
  and `Position`. **Measured 2026-08-03: polling them once per frame catches almost nothing.**
  `_RequestInfoList` is a transient queue filled and drained *within a frame* — every heartbeat read
  it as empty (`global 0, npc 0`) while `RequestId` advanced ~110/second, so a per-frame sample caught
  ~1/s and only ever the player's own footsteps. The per-NPC lookup works
  (`via.simplewwise.Driver.getGameObjectId(via.GameObject, uint)` returned a valid id) but its list
  reads empty for the same reason.
  To watch what the game plays, **hook `soundlib.SoundManager.postRequestInfo(RequestInfo)`** — the
  single choke point every request passes through — and do it from the **compiled DLL**, not a source
  plugin: there is no documented unhook, so a hot-reloading plugin would leave a stale delegate
  pointing into an unloaded assembly.
- **Making one sound carry further, without touching the mix.** `RequestInfo.AttenuationScalingFactor`
  stretches the Wwise attenuation curve for a SINGLE playback: at a given real distance the sound is
  treated as though it were nearer, so it carries. It still rides the player's SFX volume, which is
  what we want. Do **not** instead use a global RTPC / bus volume (moves all game audio) or mutate
  `SoundTriggerInfo.TriggerRange` (shared data the game itself uses).
  Applying it means building the request by hand instead of calling `trigger(id)`:
  1. `soundlib.SoundContainer.createRequestInfo(SoundTriggerInfo, GameObject src, GameObject target,
     uint jointHash, bool symmetry, bool positioned, uint seekTime, CallbackType, object, object)` —
     `positioned: true`, trailing callbacks `null`, `CallbackType` accepts a plain `0`. **There are two
     10-parameter overloads**; pick by first parameter type (`System.Int32` vs
     `soundlib.SoundTriggerInfo`), and wrap the per-parameter inspection in its own try — letting one
     bad overload throw aborts the whole search and silently yields null.
  2. **`AttenuationScalingFactor` is a PROPERTY with no backing field** — `GetField` returns null and
     the write is a silent no-op. Use `set_AttenuationScalingFactor` / `get_AttenuationScalingFactor`.
     (The usual "read fields, not getters" rule targets concrete types that have both; here there is
     no field at all.) Always read the value back: this whole path fails silently otherwise, and
     "it sounds a bit louder" is not evidence — measured, 54 pings believed to be boosted were not.
  3. `soundlib.SoundManager.postRequestInfo(RequestInfo)` to play it.
- **Overload trap:** `trigger` has three 1-argument overloads (`uint`, `SoundTriggerInfo`,
  `RequestInfo`). `IObject.Call("trigger", id)` may bind the wrong one — resolve explicitly with
  `TDB.Get().FindType("soundlib.SoundContainer").GetMethod("trigger(System.UInt32)")`.
- **The full `trigger` overload set** (dumped from the TDB 2026-08-14). Two of these take a
  **`positionGameObj`**, which is the important one: it lets a *known-good* sound be played at
  *another object's* position, so a cue is not limited to the sounds in the emitter's own banks, and
  still needs no `via.vec3` construction:

  ```
  uint trigger(SoundTriggerInfo trgInfo)
  uint trigger(SoundManager.RequestInfo reqInfo)
  void trigger(uint trgId)
  void trigger(uint trgId, via.GameObject positionGameObj, via.GameObject targetGameObj,
               uint jointHash, bool symmetry, uint seekTime,
               via.simplewwise.CallbackType callbackType,
               Action<RequestInfo> postRequestInfo, Action<RequestInfo> endOfEvent)
  void trigger(uint trgId, via.vec3 pos, via.GameObject targetGameObj, uint seekTime,
               via.simplewwise.CallbackType callbackType,
               Action<RequestInfo> postRequestInfo, Action<RequestInfo> endOfEvent)
  void trigger(via.GameObject targetGameObj, uint trgId, via.GameObject positionGameObj,
               uint jointHash, bool symmetry, uint seekTime,
               via.simplewwise.CallbackType callbackType, Action<RequestInfo> endOfEvent)
  void trigger(via.GameObject targetGameObj, uint trgId, via.vec3 position, uint seekTime,
               via.simplewwise.CallbackType callbackType, Action<RequestInfo> endOfEvent)
  ```

  Also on the type: `bool exists(uint)`, `SoundTriggerInfo getTriggerInfo(int idx)`,
  `bool getTriggerInfoIdx(uint trgId, int firstIdx, int trgCnt)`, `void sortTriggerInfoList()`,
  `void loadTriggerInfoList()`.
- **World-object emitters follow the same rule as NPCs.** Interactive field objects
  (`app.worldtour.om.GimmickVisualController`) carry their own `SoundContainerApp` with a bank named
  after the object — e.g. the World Tour tutorial's step-on pads (`vi_020000*`) expose bank
  `om020000_es` with 3 trigger ids. So a cue on a world object can be *its own* sound, played
  through *its own* emitter: positioned for free and immersive by construction.
- **Escape hatch** if the managed layers misbehave: `via.simplewwise.SendRequest.registerEmitter` +
  `set3dPosition(ulong, via.vec3, via.vec3, via.vec3)` + `postEvent(…)`, with the game-object id from
  `Driver.getGameObjectId(via.GameObject, uint)`.
- **Telling voices from noises without hardcoding ids.** `soundlib.SoundTriggerInfo` carries
  `LanguageEventId_Jpn` / `LanguageEventId_Eng` alongside `TriggerId`/`EventId`. A trigger with a
  per-language event id **is a voice line** — the game must swap the asset per spoken language, which
  footsteps, cloth and prop SE never need. This is how to pick an NPC's greetings out of its triggers
  from the game's own data, with no hardcoded id list, so it keeps working across NPCs loading
  different banks.
  - **Group-level metadata is richer than the per-trigger flags.** The triggers arrive grouped as
    `soundlib.SoundTriggerInfoListData`, and each group exposes **`IsLanguage`** (the game's own "this
    group is voice data" bool) and **`Bank`**, a `via.simplewwise.BankResourceHolder` whose inherited
    `via.ResourceHolder.ResourcePath` is a real string path. Since the trigger ids are hashed names
    with no readable form, that bank path is the only human-meaningful label available — group by it.
  - **Bank map of a World Tour NPC** (measured 2026-08-03: 464 triggers in 15 banks; a different NPC
    gave 543, so counts are per-actor but the bank *kinds* repeat). `esf002` is that actor's id, so
    voice bank names vary per NPC — select voices by the `IsLanguage` flag, never by name:

    | Bank | Contents | Triggers | Use for a beacon? |
    |---|---|---|---|
    | `esf002_v_es`, `esf002_v_tutorial_es`, `wnf500_v_pv_es` | the actor's spoken lines (`IsLanguage=True`) | 134 / 31 / 35 | **yes** — greetings; skip the tutorial bank |
    | `foot_steps_es` | footsteps | 53 | **yes** — the fallback for NPCs with no lines |
    | `wcs_mvmt_cotton_01_es` | cloth movement | 58 (×2 groups) | **yes** — subtle |
    | `wtnp_cmn_sfx_es`, `wtnp_city_action_sfx_es` | World Tour NPC SFX | 11 / 23 | maybe |
    | `act_cmn_es`, `wpl_city_action_sfx_es` | generic action SFX | 38 / 29 | maybe |
    | `dmg_cmn_es`, `dmg_human_es`, `down_human_es` | damage, pain, falls | 58 / 91 / 14 | **no** — an NPC groaning in pain on a timer is worse than a beep |
    | `ui_bh_raid_cmn_es` | UI | 3 | **no** — UI events are authored 2D |

  - **The "not set" value is `uint.MaxValue`, NOT zero.** Testing `!= 0` marks *every* trigger as a
    voice. Measured on a World Tour NPC: of **543** triggers, **306** carry the sentinel in both
    fields and **237** carry real ids. In all 237, `LanguageEventId_Eng == EventId` (the base asset)
    and `LanguageEventId_Jpn` differs from it (the localized variant) — a clean, consistent signature.
- **UI-container events are 2D** and would ignore a position argument; the NPC's own container is the
  one to use. Probe both with `dev-source/SoundProbe.cs` — see `docs/hot-reload-workflow.md`.

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
  managed\SF6Access.lang\*.txt     (localized strings — WITHOUT this every readout
                                    falls back to English; forgotten once, v0.5.x)
```

Build steps: `dotnet build` (fresh DLL copies into the game folder) → copy the files above into the
kept `release\SF6Access\` staging folder → `Compress-Archive` the CONTENTS into `release\SF6Access.zip`
→ `gh release create vX.Y.Z release\SF6Access.zip …`. EXCLUDE `managed\generated\` (per-PC),
`reframework\data\`, and `.xml` doc files. Players need **.NET Desktop Runtime 10 x64** (linked in README).
Do not create GitHub releases automatically — only when explicitly asked.
