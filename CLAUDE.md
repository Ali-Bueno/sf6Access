# RE Engine Accessibility Framework - Project Skeleton

## Purpose
Skeleton project for building accessibility mods for Capcom RE Engine games using REFramework.
This project contains all documentation and patterns needed to mod any RE Engine game.

## Reference Documentation (in `docs/`)
- `docs/setup.md` - REFramework installation, file structure, script loading
- `docs/lua-api-core.md` - Core Lua APIs: sdk, re, reframework, log, fs, draw
- `docs/lua-api-imgui.md` - ImGui, ImNodes, ImGuizmo APIs
- `docs/lua-api-types.md` - Type system: REManagedObject, RETypeDefinition, RETransform, vectors, etc.
- `docs/lua-hooks-and-patterns.md` - Hooking system, best practices, performance, code patterns
- `docs/csharp-api.md` - C# scripting: API, TDB, VM, attributes, typed proxies
- `docs/csharp-hooks.md` - C# hooks: pre/post hooks, threading, ByRef params
- `docs/csharp-objects-and-arrays.md` - ManagedObject, NativeObject, arrays, collections, lifetime
- `docs/examples.md` - Practical code snippets per game (RE2, RE3, RE7, RE8, DMC5, MHRise)
- `docs/tools.md` - Object Explorer, Chain Viewer, BHVT Editor usage
- `docs/accessibility-patterns.md` - Patterns for accessibility modding with REFramework

## Key Decisions
- **Primary scripting language:** Lua for rapid prototyping, C# for performance-critical code
- **C# is 3-7x faster** than Lua single-threaded, up to 80x in multi-threaded scenarios
- **REFramework uses RE Engine's IL2CPP** (NOT Unity IL2CPP - no Unity tooling works here)
- Scripts go in `reframework/autorun/` for auto-loading
- Filesystem access is sandboxed to `reframework/data/`

## Supported Games (RE Engine)
RE2, RE3, RE4, RE7, RE8 (Village), RE9, DMC5, Monster Hunter Rise, Monster Hunter Wilds, and other Capcom RE Engine titles.

## Quick Start Pattern
```lua
-- 1. Find singletons via Object Explorer
-- 2. Get singleton reference
local manager = sdk.get_managed_singleton("app.SomeManager")

-- 3. Hook methods to intercept game logic
sdk.hook(
    sdk.find_type_definition("app.SomeType"):get_method("someMethod"),
    function(args) return sdk.PreHookResult.CALL_ORIGINAL end,
    function(retval) return retval end
)

-- 4. Use callbacks for per-frame logic
re.on_frame(function()
    -- read game state, announce via screen reader
end)

-- 5. Use re.on_draw_ui for config menus
re.on_draw_ui(function()
    imgui.text("Accessibility Options")
end)
```

## Accessibility Integration Notes
- Use `re.on_frame` to poll game state changes and announce via TTS/screen reader
- Use `sdk.hook` to intercept UI updates, combat events, menu navigation
- Use `re.on_pre_gui_draw_element` to intercept GUI rendering and extract text
- Object Explorer is essential for finding what to hook in each specific game
- Cache method/field lookups for performance (don't lookup every frame)
- Detect state *changes* before announcing (avoid spam)

## Code Conventions
- All code in English
- All comments in English
- Commits in English
- Use `local` for all Lua variables and functions (shared state across scripts)
- Modular architecture: separate files for each concern
