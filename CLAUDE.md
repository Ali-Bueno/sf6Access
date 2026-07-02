# SF6Access — Street Fighter 6 Accessibility Mod

REFramework.NET **C#** plugin that makes Street Fighter 6 usable with a screen reader (Tolk). Built on
Capcom's RE Engine via REFramework. This repository began as a generic RE Engine modding skeleton (the
`docs/lua-*` and `docs/csharp-*` reference files remain for that); it is now the SF6Access plugin.

- Repo: https://github.com/Ali-Bueno/sf6Access.git (HTTPS)
- Game path: `D:\games\steam\steamapps\common\Street Fighter 6`
- Code path: `D:\code\re engine\sf6Access\SF6Access`
- Build: `dotnet build` from the `SF6Access` folder (auto-copies the DLL into the game). Compile after
  every code change without asking.
- Log: `re2_framework_log.txt` in the game root; dumps/screenshots in `<game>\reframework\data\`.

## Documentation map

**SF6-specific (read these first when working on SF6):**
- [`docs/sf6-architecture.md`](docs/sf6-architecture.md) — plugin layout, core services (`FlowHelper`,
  `GuiTextReader`, `ScreenReaderService`, GroupFocus), IL2CPP gotchas, the mandatory stale-param
  re-entry pattern, localization rules, dump tools (F7/F8/F9), release packaging.
- [`docs/sf6-screens.md`](docs/sf6-screens.md) — per-screen technical reference: confirmed type
  FullNames, fields, enums, and read recipes for every menu/screen we've accessibilized.

> These two docs are the durable, version-controlled record of what we learned reverse-engineering
> SF6's UI. **When you discover something new about a screen, add it here** — don't leave it only in
> notes/memory. Prefer updating the relevant section over duplicating.

**Generic RE Engine / REFramework reference (kept from the skeleton):**
- `docs/setup.md`, `docs/lua-api-core.md`, `docs/lua-api-imgui.md`, `docs/lua-api-types.md`,
  `docs/lua-hooks-and-patterns.md` — Lua side (this plugin uses C#, but the type system is shared).
- `docs/csharp-api.md`, `docs/csharp-hooks.md`, `docs/csharp-objects-and-arrays.md` — REFramework.NET C# API.
- `docs/examples.md`, `docs/tools.md`, `docs/accessibility-patterns.md` — patterns & Object Explorer.

## Architecture (at a glance — details in `docs/sf6-architecture.md`)

- `Plugin.cs` — entry point, inits Tolk. Hooks **auto-register via attributes** (no central list).
- `Services/` — shared infra: `FlowHelper` (flow-param discovery, field reads, Guid resolution),
  `GuiTextReader` (on-screen text scraping), `ScreenReaderService` (Tolk + duplicate filter),
  `GameStateTracker`, `GroupFocusPoller`, `LeagueRankResolver`, `ControlTypeNames`, `InputNameResolver`,
  `ComboTracker`, `AvatarStatsReader`, `ObjectDumper`, `ScreenshotService`.
- `Hooks/` — one file per screen/feature (~65). `sf6 code/` = decompiled game code (interfaces only,
  gitignored) — useful for names but always verify against a runtime dump.

## Non-negotiable technical rules (the ones that bite)

- **Dynamic hooks, not attribute hooks:** `method.AddHook(false)` for IL2CPP interface dispatch;
  `[MethodHook]` never fires. Don't mix AddPre + AddPost on one dynamic hook.
- **Read fields, not interface property getters** on concrete IL2CPP types (`get_X` returns null/empty).
  `_Handles` is a field; iterate newest-first. `FlowHelper.Call`/`GetSelected*` still dispatch fine.
- **Width-correct field reads:** `short` → `ReadShortField`, `byte`/byte-enum → `ReadByteField`.
  Reading them as int grabs adjacent bytes = garbage.
- **Never index `_Children`** (order can be reversed) — use `get_SelectedItem` / `GetFocusChild`.
- **Stale-param re-entry:** re-scan `_Handles` every tick; re-bind when `GetAddress()` changes. Never
  trust cached `mIsActive` or a type-name-only match.
- **Detect state *changes* before announcing** (avoid spam); the `ScreenReaderService` filter drops text
  identical to the previous within ~250 ms, so make runs of identical rows DISTINCT (append slot/index).
- **Always prefer game text** (localization); hardcode strings only as a documented last resort for
  image/texture-rendered text, keyed on `FlowHelper.GetDisplayLang()` (En/Es/Pt).
- Use `LateUpdateBehavior.Post` (fresh data). Cache method/field lookups; don't look up every frame.

## Research workflow

Toggle **F8** (auto-dump), navigate the flow once, then read the session file
(`sf6access_autodump_*.txt`) + `re2_framework_log.txt`. Use **F9** for a one-off state dump of the
current screen, **Shift+F9** for a heavy full dump, **F7** for a screenshot (intra-flow popups that
create no new flow param are only caught by F7). Dump keys work unfocused; letter-key shortcuts (e.g.
"G" to re-read stats) require the game to be the foreground window.

## Code conventions

- All code, comments, and commits in English.
- Modular: one hook file per concern; refactor files over ~200-300 lines.
- Simple solutions, no code duplication, no magic numbers (derive values from the game's own data/APIs;
  if a literal is unavoidable, name and document where it comes from).
- Only touch code relevant to the task; don't introduce new tech to fix a bug.
- Do NOT create GitHub releases automatically — only when explicitly asked. Do NOT overwrite `.env`.
- Temporary files go to the session scratchpad, never the project root or `D:\code`.
