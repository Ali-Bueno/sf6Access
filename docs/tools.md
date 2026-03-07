# REFramework Tools

## Object Explorer

**The most important tool for modding.** Access via DeveloperTools menu.

### What is TDB (Type Database)?
Stores metadata for classes, fields, methods, and events. Comparable to IL2CPP metadata in Unity. This is what REFramework reads to provide scripting access.

### Browsing
- **Managed Singletons**: Global managers written in C#. Most exposed data. Access via `sdk.get_managed_singleton("name")`
- **Native Singletons**: C++ global managers. Selectively exposed. Access via `sdk.get_native_singleton("name")`
- **TDB Fields**: All visible fields for types
- **TDB Methods**: All visible methods with context menu (copy address/name, hook)

### What's Fully Supported
Only **TDB Methods** and **TDB Fields** are fully supported by the scripting API. Reflection Methods and Properties require direct memory reading/writing.

### Dump SDK Feature
Generates:
1. `il2cpp_dump.json` in game folder (offline reference, can be huge ~1GB)
2. C++ headers/sources from TDB data

Use the JSON dump with Python or other tools as offline reference.

### Workflow for Finding What to Hook
1. Open Object Explorer
2. Browse Managed Singletons for managers relevant to your goal
3. Expand to see fields and methods
4. Identify methods to hook or fields to read
5. Use context menu to copy method names for use in scripts
6. Test with hooks

### Key Singletons to Look For (Common Patterns)
- `app.PlayerManager` - Player character management
- `app.EnemyManager` - Enemy management
- `app.UIManager` / `app.GUIManager` - UI system
- `app.SaveManager` / `app.SaveServiceManager` - Save system
- `app.ItemManager` - Inventory
- `app.SoundManager` - Audio
- `app.GameFlowManager` - Game state/flow
- `app.InputManager` - Input handling
- `app.CameraManager` - Camera control

---

## Chain Viewer

Views active `via.motion.Chain` objects and **visualizes their collisions**.

Useful for:
- Understanding physics chain behavior
- Making informed decisions when editing chain files
- Debugging collision data

---

## Behavior Tree / FSM Editor

**External Lua script** (not built into REFramework). May become native in the future.

Repository: https://github.com/praydog/RE-BHVT-Editor

### Features
- Visual UI for behavior tree editing
- Lua-driven condition evaluators
- Custom actions/effects on specific nodes
- Dynamic game speed adjustment
- Modification of transition states for custom combos

### Usage
- Uses the ImNodes API for the node editor interface
- Run as a Lua script through REFramework

---

## RE_RSZ Tool (External)

Mentioned as a powerful alternative for:
- Inserting/cloning game objects into scenes
- File edits not requiring runtime awareness
- Base edits that REFramework can layer scripts on top of
- Modifications impossible via REFramework alone

---

## Useful Discords for RE Engine Modding
- **Modding Haven** (General RE Engine): discord.gg/9Vr2SJ3
- **Infernal Warks** (DMC5): discord.com/invite/nX5EzVU
- **Monster Hunter Modding**: discord.gg/gJwMdhK
- **Flatscreen to VR Modding**: flat2vr.com
