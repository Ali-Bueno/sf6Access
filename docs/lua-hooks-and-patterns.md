# Lua Hooks, Best Practices & Code Patterns

## Hooking System

### Basic Hook Pattern
```lua
sdk.hook(
    sdk.find_type_definition("app.SomeType"):get_method("someMethod"),
    function(args)
        -- Pre-hook
        -- args[1] = thread_context
        -- args[2] = this (instance methods) or first param (static)
        -- args[3+] = remaining params
        -- All args are void*, need conversion
        return sdk.PreHookResult.CALL_ORIGINAL
    end,
    function(retval)
        -- Post-hook
        -- Can modify and return retval
        -- Runs even with SKIP_ORIGINAL (retval may be invalid)
        return retval
    end
)
```

### Converting Hook Arguments
```lua
-- To managed object
local obj = sdk.to_managed_object(args[2])

-- To integer
local int_val = sdk.to_int64(args[3]) & 0xFFFFFFFF

-- To float
local float_val = sdk.to_float(args[3])

-- To boolean
local bool_val = (sdk.to_int64(args[3]) & 1) == 1
```

### Returning Modified Values from Post-Hook
```lua
function(retval)
    -- For managed objects: return the pointer
    return sdk.to_ptr(new_managed_object)

    -- For integers
    return sdk.to_ptr(42)

    -- For floats
    return sdk.float_to_ptr(3.14)
end
```

### Per-Object Hook (vtable)
```lua
sdk.hook_vtable(specific_object, method_definition, pre_fn, post_fn)
-- Only hooks this specific object instance, not all objects of the type
```

### ByRef Parameter Handling
```lua
local function deref_ptr(ptr)
    local fake_int64 = sdk.to_valuetype(ptr, "System.UInt64")
    return fake_int64:get_field("mValue")
end

-- In pre-hook:
local deref = deref_ptr(args[6])
local arg = sdk.to_managed_object(deref):add_ref()
```

### Using thread.get_hook_storage() for Pre->Post Data
```lua
sdk.hook(method,
    function(args)
        local storage = thread.get_hook_storage()
        storage["this"] = sdk.to_managed_object(args[2])
    end,
    function(retval)
        local this = thread.get_hook_storage()["this"]
        -- storage is destroyed after post-hook, safe for recursion
        return retval
    end
)
```

---

## Best Practices

### Use Local Variables
```lua
-- GOOD: local scope, no conflicts with other scripts
local my_var = "value"
local function my_func() end

-- BAD: global scope, can conflict
my_var = "value"
function my_func() end
```

### Cache Method and Field Lookups
```lua
-- GOOD: lookup once, use many times
local method1 = sdk.find_type_definition("Foo"):get_method("Bar")
local field1 = sdk.find_type_definition("Foo"):get_field("Baz")

re.on_frame(function()
    local obj = sdk.get_managed_singleton("Qux")
    local result = method1:call(obj, 1, 2, 3)
    local data = field1:get_data(obj)
end)

-- BAD: lookup every frame (hashmap lookup each time)
re.on_frame(function()
    local obj = sdk.get_managed_singleton("Qux")
    obj:call("Bar", 1, 2, 3)  -- hashmap lookup
    obj:get_field("Baz")       -- hashmap lookup
end)
```

### Hooking Gotchas
1. **Duplicate functions:** Some `get_`/`set_` methods are very simple and may get inlined by the compiler, making unrelated calls trigger your hook. Always verify object type in the hook.
2. **Performance:** Hooking frequently-called functions (per-entity-per-frame) degrades performance. Mitigate:
   - Stagger work across ticks
   - Cache lookups
   - Use native plugins for intensive code
3. **Verify disassembly:** Use Object Explorer to check if a function is trivial before hooking.

### Module Organization
```lua
-- autorun/subfolder/MyModule.lua
local M = {}
function M.do_something() ... end
return M

-- autorun/Main.lua
local my_module = require("subfolder/MyModule")
my_module.do_something()
```

---

## Common Code Patterns

### Get Local Player (Per Game)
```lua
-- RE2/RE3
local function get_localplayer()
    local playman = sdk.get_managed_singleton(sdk.game_namespace("PlayerManager"))
    if not playman then return nil end
    return playman:call("get_CurrentPlayer")
end

-- RE8
local function get_localplayer()
    local propsman = sdk.get_managed_singleton(sdk.game_namespace("PropsManager"))
    if not propsman then return nil end
    return propsman:call("get_Player")
end

-- DMC5
local function get_localplayer()
    local playman = sdk.get_managed_singleton(sdk.game_namespace("PlayerManager"))
    if not playman then return nil end
    local comp = playman:call("get_manualPlayer")
    if not comp then return nil end
    return comp:call("get_GameObject")
end

-- MHRise
local function get_localplayer()
    local playman = sdk.get_managed_singleton("snow.player.PlayerManager")
    if not playman then return nil end
    return playman:call("findMasterPlayer")
end
```

### Get Component from GameObject
```lua
local function get_component(game_object, type_name)
    local t = sdk.typeof(type_name)
    if t == nil then return nil end
    return game_object:call("getComponent(System.Type)", t)
end
```

### Get All Components
```lua
local function get_components(game_object)
    local transform = game_object:call("get_Transform")
    if not transform then return {} end
    return game_object:call("get_Components"):get_elements()
end
```

### Get Elapsed Time
```lua
local get_elapsed = sdk.find_type_definition("via.Application"):get_method("get_UpTimeSecond")
local function get_time()
    return get_elapsed:call(nil)
end
-- Or simply: os.clock() (supported in newer builds)
```

### Generate Enum from Static Fields
```lua
local function generate_enum(typename)
    local t = sdk.find_type_definition(typename)
    if not t then return {} end
    local fields = t:get_fields()
    local enum = {}
    for i, field in ipairs(fields) do
        if field:is_static() then
            enum[field:get_name()] = field:get_data(nil)
        end
    end
    return enum
end

-- Usage:
local GamePadButton = generate_enum("via.hid.GamePadButton")
```

### State Change Detection Pattern (for Accessibility)
```lua
local last_state = nil

re.on_frame(function()
    local current_state = get_current_state()  -- your state reading function
    if current_state ~= last_state then
        -- State changed! Announce via screen reader
        announce(current_state)
        last_state = current_state
    end
end)
```

### GUI Element Interception
```lua
re.on_pre_gui_draw_element(function(element, context)
    local go = element:call("get_GameObject")
    local name = go:call("get_Name")
    -- Extract text, position, visibility info
    -- Announce to screen reader if relevant
    return true  -- return false to hide element
end)
```

### Config Save/Load Pattern
```lua
local config = {
    enabled = true,
    verbosity = 2,
}

local config_path = "my_mod/config.json"

-- Load
local saved = json.load_file(config_path)
if saved then config = saved end

-- Save
re.on_config_save(function()
    json.dump_file(config_path, config)
end)
```

### Safe Singleton Access Pattern
```lua
local cached_manager = nil

local function get_manager()
    if cached_manager == nil then
        cached_manager = sdk.get_managed_singleton("app.SomeManager")
    end
    return cached_manager
end

re.on_script_reset(function()
    cached_manager = nil  -- Clear cache on reset
end)
```
