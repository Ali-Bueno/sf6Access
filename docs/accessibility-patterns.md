# Accessibility Patterns for RE Engine Games

## Strategy Overview

REFramework gives us two powerful entry points for accessibility:
1. **Hooks** - Intercept game methods to capture state changes (combat, menus, dialogue)
2. **Callbacks** - Per-frame polling to track game state and announce changes

For screen reader output, we need a bridge between REFramework and the OS TTS. Options:
- **Lua + native DLL**: Write a Tolk wrapper as a native DLL, `require` it from Lua
- **C# plugin**: Directly P/Invoke to Tolk or SAPI from C# code
- **File-based bridge**: Write state to a file, external app reads and speaks

## Recommended Architecture (C# Plugin)

C# is preferred for accessibility because:
- Direct P/Invoke to Tolk.dll or SAPI
- Better performance for per-frame state tracking
- True multi-threading (TTS calls don't block game)
- Typed proxies for cleaner code

```
MyAccessibilityMod/
├── Plugin.cs              # Entry point, initialization
├── Services/
│   ├── ScreenReaderService.cs   # Tolk/SAPI wrapper
│   ├── GameStateTracker.cs      # Central state tracking
│   └── AudioCueService.cs       # Sound effect playback
├── Hooks/
│   ├── MenuHooks.cs       # Menu navigation hooks
│   ├── CombatHooks.cs     # Combat event hooks
│   ├── DialogueHooks.cs   # Dialogue/cutscene hooks
│   └── UIHooks.cs         # General UI hooks
└── Config/
    └── AccessibilityConfig.cs   # User preferences
```

## Recommended Architecture (Lua)

```
reframework/autorun/
├── accessibility_main.lua         # Entry point, loads modules
├── accessibility/
│   ├── screen_reader.lua          # Tolk wrapper (via native DLL)
│   ├── state_tracker.lua          # Central state tracking
│   ├── audio_cues.lua             # Sound cue management
│   ├── menu_reader.lua            # Menu navigation
│   ├── combat_reader.lua          # Combat events
│   ├── dialogue_reader.lua        # Dialogue/text
│   └── config.lua                 # Settings (saved via json API)
```

## Core Patterns

### 1. State Change Detection (Don't Spam)
```lua
local StateTracker = {}
local tracked_states = {}

function StateTracker.track(key, current_value, announce_fn)
    if tracked_states[key] ~= current_value then
        tracked_states[key] = current_value
        announce_fn(current_value)
    end
end

-- Usage:
re.on_frame(function()
    local hp = get_player_hp()
    StateTracker.track("player_hp", hp, function(val)
        screen_reader.speak("Health: " .. val)
    end)
end)
```

### 2. Menu Navigation Tracking
```lua
-- Hook menu cursor movement
sdk.hook(
    sdk.find_type_definition("app.UIMenuCursor"):get_method("setCursorIndex"),
    function(args)
        local index = sdk.to_int64(args[3]) & 0xFFFFFFFF
        -- Store for post-hook
        thread.get_hook_storage()["index"] = index
        return sdk.PreHookResult.CALL_ORIGINAL
    end,
    function(retval)
        local index = thread.get_hook_storage()["index"]
        -- Read menu item text at this index and announce
        return retval
    end
)
```

### 3. GUI Element Text Extraction
```lua
re.on_pre_gui_draw_element(function(element, context)
    local go = element:call("get_GameObject")
    if not go then return true end

    -- Try to get text component
    local gui_text = go:call("getComponent(System.Type)", sdk.typeof("via.gui.Text"))
    if gui_text then
        local text = gui_text:call("get_Message")
        if text and text ~= "" then
            -- Process text for accessibility
            on_gui_text_visible(go:call("get_Name"), text)
        end
    end
    return true -- don't hide element
end)
```

### 4. Combat Event Hooks
```lua
-- Hook damage application
sdk.hook(
    sdk.find_type_definition("app.DamageManager"):get_method("applyDamage"),
    function(args)
        local target = sdk.to_managed_object(args[2])
        local damage = sdk.to_int64(args[3]) & 0xFFFFFFFF
        local target_name = get_entity_name(target)
        screen_reader.speak(target_name .. " takes " .. damage .. " damage")
        return sdk.PreHookResult.CALL_ORIGINAL
    end,
    function(retval) return retval end
)
```

### 5. Spatial Audio Cues
```lua
-- Convert enemy position to stereo pan + volume for audio cue
local function get_spatial_info(player_transform, enemy_transform)
    local player_pos = player_transform:get_position()
    local enemy_pos = enemy_transform:get_position()

    local diff = enemy_pos - player_pos
    local distance = diff:length()

    -- Calculate angle relative to player facing direction
    local player_forward = player_transform:get_rotation() * Vector3f.new(0, 0, 1)
    local to_enemy = diff:normalized()
    local dot = player_forward:dot(to_enemy)
    local cross = player_forward:cross(to_enemy)

    local pan = cross.y > 0 and 1 or -1  -- left/right
    local volume = math.max(0, 1 - distance / MAX_DISTANCE)

    return pan, volume, distance
end
```

### 6. Config UI for Accessibility Options
```lua
local config = {
    announce_hp = true,
    announce_items = true,
    announce_enemies = true,
    combat_verbosity = 2,  -- 0=off, 1=minimal, 2=normal, 3=verbose
    spatial_audio = true,
}

re.on_draw_ui(function()
    if imgui.collapsing_header("Accessibility Options") then
        local changed
        changed, config.announce_hp = imgui.checkbox("Announce HP Changes", config.announce_hp)
        changed, config.announce_items = imgui.checkbox("Announce Items", config.announce_items)
        changed, config.announce_enemies = imgui.checkbox("Announce Enemies", config.announce_enemies)
        changed, config.combat_verbosity = imgui.slider_int("Combat Verbosity", config.combat_verbosity, 0, 3)
        changed, config.spatial_audio = imgui.checkbox("Spatial Audio Cues", config.spatial_audio)
    end
end)
```

## C# Screen Reader Integration via P/Invoke
```csharp
using System.Runtime.InteropServices;

public static class ScreenReader
{
    [DllImport("TolkDotNet.dll")]
    private static extern void Tolk_Load();

    [DllImport("TolkDotNet.dll")]
    private static extern void Tolk_Unload();

    [DllImport("TolkDotNet.dll")]
    private static extern bool Tolk_Output(
        [MarshalAs(UnmanagedType.LPWStr)] string str,
        bool interrupt);

    public static void Initialize() => Tolk_Load();
    public static void Shutdown() => Tolk_Unload();
    public static void Speak(string text, bool interrupt = true)
        => Tolk_Output(text, interrupt);
}
```

## Key Singletons to Investigate Per Game

Every RE Engine game will have variations, but common patterns:

| Purpose | Common Singleton Names |
|---------|----------------------|
| Player | `app.PlayerManager`, `app.PropsManager` |
| Enemies | `app.EnemyManager`, `app.CharacterManager` |
| UI/Menus | `app.UIManager`, `app.GUIManager`, `app.GuiManager` |
| Items | `app.ItemManager`, `app.InventoryManager` |
| Combat | `app.DamageManager`, `app.HitManager` |
| Save | `app.SaveManager`, `app.SaveServiceManager` |
| Sound | `app.SoundManager` |
| Game Flow | `app.GameFlowManager`, `app.SceneManager` |
| Input | `app.InputManager`, `app.PadManager` |
| Camera | `app.CameraManager` |

**Always use Object Explorer to discover the actual singleton names for the specific game.**

## Development Workflow

1. **Install REFramework** on target game
2. **Open Object Explorer** and browse singletons
3. **Identify key managers** for the game
4. **Use SDK dump** (`il2cpp_dump.json`) for offline reference
5. **Start with menu navigation** - usually most impactful
6. **Add combat/gameplay** announcements
7. **Add spatial audio** cues where helpful
8. **Test with screen reader** active throughout
9. **Tune verbosity** - avoid information overload
10. **Add config options** for user control

## Important Notes

- Each RE Engine game uses different class names - always explore first
- `sdk.game_namespace(name)` handles the namespace prefix differences between games
- GUI systems vary significantly between games
- Test frequently with actual screen reader
- Performance matters: cache lookups, detect changes, don't announce every frame
- Use `thread.get_hook_storage()` in hooks for thread safety
- In C#, use `[ThreadStatic]` or locks for shared hook state
