# Hot-Reload Workflow — iterate without restarting the game

> See also: `lua-hooks-and-patterns.md`, `csharp-hooks.md`, `tools.md`, `discovery-and-gotchas.md`.

The pain this doc kills: the `compile → close game → reopen game` loop. **Both C# and Lua can
hot-reload without restarting the game** — the trick is using the right pipeline. Pick your language
by capability (below); the iteration loop is a solved problem in both.

## What survives a reload (applies to both languages)

- **Survives:** the game/engine state itself — player position, current scene, health. The game
  process is **not** restarted, only your plugin/script sandbox. This is the entire point vs. a native
  recompile+relaunch: you never lose the play session while iterating.
- **Does not survive:** your mod's own state — static fields / Lua globals, registered
  callbacks/hooks from the old instance, and any cached managed-object handles (they become invalid).
  Re-acquire singletons/objects on reload.

---

## C# (REFramework.NET) — source-plugin hot-reload

**This is the path for this repo's RE mods** (Re7Access, sf6Access), which are C#/REFramework.NET.

- **Source plugins**: drop a loose `.cs` file in **`reframework/plugins/source/`**. REFramework.NET
  compiles it at runtime and, per the docs, *"Edit, save, and the plugin hot-reloads without
  restarting the game."* Source plugins also always compile against the latest generated reference
  assemblies (highest forward-compat).
- **Lifecycle**: `[PluginEntryPoint]` runs on load; `[PluginExitPoint]` is *"called when the plugin
  unloads — either during a hot-reload cycle or when the game exits."* On save: exit point runs → plugin
  torn down → recompiled → entry point fires again, all in the live process. Use the exit point to
  release hooks/state (the C# analogue of Lua's `re.on_script_reset`).
- **Compiled `.dll` plugins** (`reframework/plugins/managed/`): the docs only ever advertise hot-reload
  for the *source* pipeline. Live hot-swap of a compiled managed DLL is **undocumented — assume it
  needs a game restart** (a loaded managed assembly is file-locked, and clean runtime unload would need
  a collectible `AssemblyLoadContext`, which the docs never mention for this path).

### Confirmed mechanics of the source pipeline (read before planning a dev loop)

Verified 2026-08-03 against REFramework's own source (`csharp-api/REFrameworkNET/PluginManager.cpp`,
`csharp-api/REFCoreDeps/Compiler.cs`). These constraints are the reason **this repo does NOT run the
whole mod from `plugins/source/`**:

- **One file = one assembly.** `LoadPlugins_FromSourceCode` enumerates `*.cs` and calls
  `Compiler::Compile` once **per file**; `GenerateCode` builds a `CSharpCompilation` with exactly
  **one syntax tree**, into its own collectible `AssemblyLoadContext`. Loose `.cs` files therefore
  **cannot reference each other's types** — a multi-file mod does not work here. **Escape hatch, not
  yet built:** a single file may hold many `namespace X { … }` blocks, so a build step could merge the
  whole mod into one generated `.cs`. All 113 source files use file-scoped namespaces and none uses
  `unsafe`/`global using`, so the rewrite is mechanical — per file, emit
  `namespace X { <that file's usings> <body> }`, keeping the `using`s *inside* their block so merging
  can't create ambiguities (e.g. `ValueType` between `System` and `REFrameworkNET`). The compiled DLL
  must be moved out of `plugins/managed/` while doing this, or every hook fires twice.
- **Top level only.** The scan is `Directory::GetFiles(dir, "*.cs")` (`TopDirectoryOnly`). The
  `FileSystemWatcher` does set `IncludeSubdirectories`, so edits in subfolders trigger a reload — but
  the reload still only re-scans the top level, so those files never load. Don't use subfolders.
- **References = `plugins/managed/dependencies/`.** The compilation references REFramework.NET itself,
  the matching `Microsoft.NETCore.App.Ref` pack, and **every DLL in that dependencies folder** — which
  is how a source plugin can use `TolkDotNet.dll`. There is no `#r` directive and no config file:
  dropping a DLL in that folder is the only way to add a reference.
- **C# 12, .NET 10** (`LanguageVersion.CSharp12`, hardcoded). File-scoped namespaces, records and
  primary constructors are all fine.
- **Source and managed both load, always.** `LoadPlugins` calls `LoadPlugins_FromSourceCode` and then
  *unconditionally* `LoadPlugins_FromDLLs`. If the same hook exists in `plugins/source/` **and** in
  `plugins/managed/SF6Access.dll`, it registers **twice** and every announcement double-fires. Either
  give the source file its own distinct types/keys, or move the compiled DLL aside while iterating.
- **Reload trigger**: the "Reload Scripts" button, or the `FileSystemWatcher`, honored when the
  **"Auto Reload" checkbox** (REFramework.NET header) is on. **Auto Reload was observed already active
  on this install** (2026-08-03): saving over `plugins/source/SoundProbe.cs` unloaded, recompiled and
  re-ran its entry point in ~1.7 s with no game restart and no UI interaction. `re2_fw_config.txt`
  carries only `ScriptRunner_*` (Lua) keys, so whether that checkbox survives a game restart is
  unconfirmed — if a save ever stops triggering a reload, tick it again in the overlay.
  The reload cycle logs `Attempting to initiate first phase unload of …` → `Successfully unloaded …`
  → `Compiled …` → `Found PluginEntryPoint in …`.
- **Compile errors go to the log only** (no in-game overlay), formatted
  `{path}({line},{col}): {DiagnosticId}: {message}` via `API::LogError` → `re2_framework_log.txt`.

- **Recommended dev loop for THIS repo**: ship and iterate the mod as the compiled
  `plugins/managed/SF6Access.dll` (`dotnet build`, restart the game). Reach for a source `.cs` plugin
  only for a **small, self-contained, throwaway probe** — one file, no dependency on the mod's own
  types, its own hotkeys — where the answer needs many in-game attempts. Live example:
  `dev-source/SoundProbe.cs` (see below).
- **Driving PRISM stays trivial**: `[DllImport("prism.dll")]` P/Invoke works directly — P/Invoke is a
  base CLR feature, independent of how the assembly was produced, so it keeps working under the
  source-plugin pipeline. No native shim needed (unlike Lua, below). *Worth a one-time smoke test:*
  call one PRISM function from a `[PluginEntryPoint]` in a source `.cs` file before trusting it for the
  whole mod.
- **GC / threading caveats** (C#-specific, confirmed in the C# docs):
  - `.Globalize()` any `ManagedObject` that must persist across frames (static fields, objects in
    collections, arrays you create) — otherwise the engine GC collects it. Not needed for temporaries
    used within a single callback (the GC won't run mid-callback).
  - On your **own** (non-engine) threads, call `REFrameworkNET.API.LocalFrameGC()` periodically or the
    thread heap grows unbounded and crashes.
  - Hooks run **concurrently** (true multithreading) — `lock` any shared state (statics/collections)
    written from a hook.

### Live probe: `dev-source/SoundProbe.cs`

The one source plugin this repo maintains. It answers a question that needs many in-game attempts:
**can SF6's own Wwise audio emit a beacon at an NPC's position, and which trigger id sounds right?**
(Background: `sf6-architecture.md` § Game audio.)

- Install: copy `dev-source/SoundProbe.cs` → `<game>\reframework\plugins\source\`. It lives OUTSIDE the
  `SF6Access/` project folder on purpose, so the csproj glob never compiles it into the shipped DLL.
- It is self-contained (its own condensed copies of the `FlowHelper` helpers, `[DllImport("Tolk.dll")]`
  for speech instead of `DavyKager`) and uses distinct types (`SF6AccessDev.SoundProbe`) and unused
  keys, so it **coexists with the compiled `SF6Access.dll`** without double-firing anything.
- Keys, in World Tour: **F2** scan the nearest NPC (logs every component on its GameObject, then
  enumerates its sound container's trigger ids) · **F3** fire the next id · **Shift+F3** previous ·
  **Ctrl+F3** jump 10 · **F4** re-fire · **F5** next filter (**Shift+F5** previous): All → Voices →
  Effects → then each soundbank on its own. It speaks the index — plus the soundbank name whenever the
  group changes — then plays.
- **Stopping, three levels** (some triggers are loops and will otherwise play until the game is
  restarted): **Shift+F4** the last one · **Ctrl+F4** everything the probe fired this session (the one
  to reach for — it leaves the game's own audio alone) · **Ctrl+Shift+F4** `stopAll()`, which also
  kills the music and it does not come back.
- **F1 — ambient beacon prototype**: every 3–8 s a random one of the 5 nearest NPCs plays one of its
  own idle noises at its own position, selected as `foot_steps_es` bank + position 0–4 (never by
  hashed id, so it carries across NPCs). This is the feature under design; the keys above are the
  research tools that found it.
- ~~F1 — spy mode~~ **(removed, kept here as a warning).** It logged sound requests the GAME posts
  (`trigger id`, the GameObject it played on, and the bank when known), by polling
  `soundlib.SoundManager._RequestInfoList` rather than hooking anything. Stand near an idle NPC with
  spy on and the log names exactly which triggers Capcom uses for its little ambient noises — a set
  already curated, timed and positioned, which is a far better shortlist than auditioning raw ids.
  Press F1 again to end the capture; it then **speaks** the tally, split into your own avatar's sounds
  and other objects'. It has never once caught an NPC, including while an NPC was audibly walking
  past — the request lists are drained within the frame, so a per-frame poll misses ~99% of them (see
  `sf6-architecture.md` § Game audio). Making this reliable needs a hook on
  `SoundManager.postRequestInfo`, **from the compiled DLL rather than here**, since there is no
  documented unhook and this file hot-reloads. Useful facts it did establish: the player's own
  footstep is trigger `3631883013` on `AvatarPlayer`, and the game posts ~110 sound requests/second.
  (F2–F5 because F6 is the mod's stats dump, F7/F8/F9 its other dumps, F10 opens the Windows window
  menu and F12 is Steam's screenshot key.)
- **Confirmed in game 2026-08-03:** an NPC's emitter is `app.sound.SoundDynamicContainerApp`, it
  exposes **543 trigger ids**, and firing one plays it **in true 3D at that NPC** (the sound stays
  glued to the NPC as the camera rotates). Match the emitter by inheritance
  (`IsDerivedFrom("soundlib.SoundContainer")`) — a `Contains("SoundContainer")` name test MISSES it,
  since "SoundContainer" is not a substring of "SoundDynamicContainerApp".
- What to listen for: **turn the camera between F12 presses.** If the sound stays glued to the NPC's
  direction, that event is authored 3D and the beacon can be the game's own audio. If it stays
  centered, that container is 2D and we fall back to mixing our own sample.
- Nothing managed is cached across frames (stale-param rule): only the discovered ids — plain `uint`s —
  survive between presses; the NPC and its container are re-resolved on every fire, so the probe always
  targets whoever is nearest right now.

---

## Lua — autorun hot-reload

- Scripts auto-load from `reframework/autorun/*.lua`. Manual load: REFramework menu → **ScriptRunner** →
  "Run Script".
- The **"Reset Scripts"** button reloads scripts **without restarting the game**. It fully **recreates
  the entire Lua VM** (not a per-script hot-swap); the log line `[ScriptRunner] Lua state initialized`
  confirms the teardown+rebuild. All autorun scripts then re-execute top-to-bottom into the fresh state.
- `re.on_script_reset(fn)` is the teardown hook: it fires around the reset and also triggers
  `on_config_save()` automatically. Use it to release/reset held state (cached singletons, hook storage,
  timers).
- Lua uses **one shared global state across all scripts** — always use `local` to avoid cross-script
  collisions (see `lua-hooks-and-patterns.md` § Best Practices).

> **Known rough edge** ([REFramework #608](https://github.com/praydog/REFramework/issues/608)): on large
> multi-file mods, after "Reset Scripts" sometimes only some files reload correctly. Test after every
> reset — don't assume it's equivalent to a fresh launch.

### Persisting settings across reloads (Lua)

Config APIs resolve under `reframework/data/`: `json.load_file(path)` (returns `nil` if missing),
`json.dump_file(path, value, indent=4)`, plus `json.load_string`/`json.dump_string`.

```lua
local config = { enabled = true, verbosity = 2 }

local config_path = "my_mod/config.json"
local saved = json.load_file(config_path)
if saved then config = saved end

re.on_config_save(function()
    json.dump_file(config_path, config)
end)
```
Since `re.on_script_reset` triggers `on_config_save`, a "Reset Scripts" click loses nothing
user-visible. `reframework:get_game_name()` (e.g. `"re2"`, `"dmc5"`) is a handy subfolder key so shared
code across games doesn't clobber one settings file. `fs` namespace (path-sandboxed): relative paths
only, no `..`, cannot write `.dll`/`.exe`, base dir is `reframework/data/`.

### Driving PRISM from Lua (the one catch)

Lua **cannot** open `prism.dll` directly. `require()`-ing an arbitrary native DLL is **not reliably
supported** ([REFramework #623](https://github.com/praydog/REFramework/issues/623)), and Lua's C-module
convention expects a `luaopen_<name>` export registering `lua_CFunction`s, whereas `prism.dll` exports a
plain C ABI (`prism_init`, `prism_speak`, …). There is no P/Invoke equivalent from Lua.

**Confirmed-working solution** (already documented for this repo): write one thin **native REFramework
plugin** (`reframework/plugins/*.dll`) that links `prism.dll` internally and, in its
`on_lua_state_created` callback, uses sol2 to register a small `prism` table into the Lua global state —
`prism.init()`, `prism.speak(text, interrupt)`, `prism.shutdown()`. No `require()` involved. See
`accessibility-patterns.md` § Strategy Overview. This shim is small, stable, compiled **once** — not
part of the iteration loop; only it needs a restart to update, and it rarely changes.

---

## Choosing: C# vs Lua vs native C++ plugin

For accessibility mods the real choice is **C# vs Lua** — both hot-reload; C# is the stronger surface.
A full native C++ plugin buys raw power (D3D device, non-TDB hooks, own threads) but **cannot
hot-reload** and is only worth it for what C#/Lua genuinely can't reach.

| | C# (REFramework.NET) | Lua | Native C++ plugin |
|---|---|---|---|
| Hot-reload without restart | ✅ source plugins (`plugins/source/*.cs`); ❌ compiled `.dll` | ✅ autorun + "Reset Scripts" | ❌ restart to reload |
| Drive `prism.dll` | ✅ `[DllImport]` direct, no shim | ⚠️ needs a native shim (`on_lua_state_created` inject) | ✅ direct native call |
| Multithreading | ✅ true concurrent hooks/threads | ❌ single VM lock (serializes) | ✅ |
| Speed vs Lua | ✅ ~3–7× single-thread, ~10–80× multi-thread | baseline | fastest |
| Typed proxies / IDE autocomplete | ✅ generated reference assemblies (`.As<T>()`, `[MethodHook]`) | ❌ dynamic/untyped `sdk.*` | n/a (raw C++) |
| Free convenience wrappers | ⚠️ fewer documented (`ImGuiRender` callback exists; no first-class `draw.*`/`object_explorer` helpers) | ✅ `imgui.*`, `draw.*`, `json.*`, `fs.*`, `object_explorer` | ❌ build your own |
| Per-frame / UI callbacks | `[Callback]` `UpdateBehavior`/`LateUpdateBehavior`/`ImGuiRender` (Pre/Post) | `re.on_frame` / `re.on_draw_ui` | own `on_frame`/imgui |
| GC caveats | `.Globalize()` cross-frame objs; `API.LocalFrameGC()` on own threads | handled by VM | manual |

**Bottom line for this repo:** stay in **C#**. It already gives you hot-reload (via source plugins),
plus direct `[DllImport]` to PRISM, true threading, and typed proxies — Lua unlocks **no new capability**
and would *remove* the direct PRISM call. Switch only your **dev loop** to a source `.cs` plugin; keep
shipping a compiled `.dll` release. The one thing Lua still does more conveniently is `draw.*`/`imgui.*`
overlays and `object_explorer` helpers — reach for a Lua script only if you want a quick throwaway debug
overlay, not for the mod itself.
