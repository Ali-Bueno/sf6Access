# REFramework Setup & File Structure

## What is REFramework
- Modding framework for Capcom's RE Engine games
- Injects into the game process and provides scripting access via Lua and C#
- Leverages RE Engine's IL2CPP implementation (NOT Unity's - incompatible with Unity tools)
- Provides Object Explorer for runtime inspection of game objects

## Installation
1. Download REFramework from GitHub releases or nightly builds
2. Place `dinput8.dll` (REFramework) in the game's root directory
3. REFramework auto-creates `reframework/` folder structure on first run

## File Structure
```
game_root/
├── dinput8.dll              # REFramework DLL
├── reframework/
│   ├── autorun/             # Lua scripts auto-loaded on startup
│   │   └── subfolder/       # Sub-modules loaded via require()
│   ├── data/                # Sandboxed filesystem for scripts (fs API, json API)
│   ├── plugins/             # Native DLL plugins
│   │   ├── source/          # C# source files (.cs) - auto-compiled
│   │   └── managed/         # Pre-compiled C# DLLs
│   │       └── generated/   # Auto-generated typed proxy assemblies
│   └── fonts/               # Custom fonts for ImGui (imgui.load_font)
├── re2_framework_log.txt    # Log file (name varies by game)
└── reframework_crash.dmp    # Crash dump (if crash occurs)
```

## Script Loading

### Lua - Automatic
Place `.lua` files in `reframework/autorun/`. They load on startup automatically.

### Lua - Manual
Open REFramework menu > ScriptRunner > "Run Script" > select `.lua` file.

### Lua - Modules
```lua
-- In autorun/subfolder/Module1.lua
local module = {}
function module.foo() print("foo") end
return module

-- In autorun/Main.lua
local module1 = require("subfolder/Module1")
module1.foo()
```

### C# - Source Plugins (recommended for dev)
Place `.cs` files in `reframework/plugins/source/` for auto-compilation and hot-reload.

### C# - Pre-compiled
Compile `.dll` targeting x64 with .NET 10.0, place in `reframework/plugins/managed/`.

## C# Prerequisites
- .NET 10.0 runtime installed
- REFramework nightly build with .NET support
- `csharp-api.zip` extracted into `reframework/` folder

## Error Handling

### Lua Startup Errors
MessageBox popup with error explanation.

### Lua Callback Errors
Written to debug log. Newer builds show errors in ScriptRunner window.

## Reporting Bugs
Upload `re2_framework_log.txt` (complete) and `reframework_crash.dmp` to GitHub Issues.

## Key REFramework Menu Sections
- **ScriptRunner** - Load/manage Lua scripts, view errors
- **DeveloperTools** - Object Explorer, Chain Viewer
- **Script Generated UI** - Custom UI from `re.on_draw_ui`

## Native Plugins
- Drop `.dll` in `reframework/plugins/`
- Include REFramework headers for C++ SDK access
- Can do everything Lua can plus more (direct memory access, custom rendering)
- Lua can `require` native DLLs for performance-critical operations

## Important Notes
- RE Engine's IL2CPP differs from Unity's - no existing Unity/IL2CPP tooling is compatible
- Lua uses shared state across all scripts - always use `local`
- RE_RSZ tool is useful for scene/object edits that don't need runtime state
