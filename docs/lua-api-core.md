# Lua API Core Reference

## sdk API (Primary API)

### Type & Instance
- `sdk.get_tdb_version()` - Type database version (~RE Engine version)
- `sdk.game_namespace(name)` - Returns game-specific namespace prefix (`app.name` in DMC5, `offline.name` in RE3)
- `sdk.get_thread_context()` - Current thread context
- `sdk.find_type_definition(name)` - Returns `RETypeDefinition*`
- `sdk.typeof(name)` - Returns `System.Type` (equivalent to `find_type_definition(name):get_runtime_type()`)

### Singleton Access
- `sdk.get_native_singleton(name)` - Returns `void*` (C++ singletons)
- `sdk.get_managed_singleton(name)` - Returns `REManagedObject*` (C# singletons)

### Object Creation
- `sdk.create_instance(typename, simplify)` - Returns `REManagedObject`. Set `simplify=true` if returns nil
- `sdk.create_managed_string(str)` - Creates `System.String`
- `sdk.create_managed_array(type, length)` - Creates `SystemArray`
- `sdk.create_resource(typename, resource_path)` - Returns `REResource` or nil
- `sdk.create_userdata(typename, userdata_path)` - Returns `REManagedObject`

### Primitive Creation
All return `REManagedObject`:
`sdk.create_sbyte`, `sdk.create_byte`, `sdk.create_int16`, `sdk.create_uint16`,
`sdk.create_int32`, `sdk.create_uint32`, `sdk.create_int64`, `sdk.create_uint64`,
`sdk.create_single`, `sdk.create_double`

### Function Calling
- `sdk.call_native_func(object, type_definition, method_name, args...)` - Call native functions
- `sdk.call_object_func(managed_object, method_name, args...)` - Call managed methods
  - Alternative: `managed_object:call(method_name, args...)`

### Field Access
- `sdk.get_native_field(object, type_definition, field_name)`
- `sdk.set_native_field(object, type_definition, field_name, value)`

### Camera
- `sdk.get_primary_camera()` - Returns active camera `REManagedObject*`

### Hooking
```lua
sdk.hook(method_definition, pre_function, post_function, ignore_jmp)
```
- `ignore_jmp` - Skip first jmp instruction (default false)
- Pre-function args: `args[1]` = thread_context, `args[2]` = object (instance) or first param (static)
- Pre-function returns: `sdk.PreHookResult.CALL_ORIGINAL` or `sdk.PreHookResult.SKIP_ORIGINAL`
- Post-function receives return value, can return modified value

```lua
sdk.hook_vtable(obj, method, pre, post) -- Per-object virtual method hook
```

### Type Conversion
- `sdk.is_managed_object(value)` - Validates (expensive)
- `sdk.to_managed_object(value)` - Convert to `REManagedObject*`
- `sdk.to_double(value)`, `sdk.to_float(value)`, `sdk.to_int64(value)` - Convert `void*`
- `sdk.to_ptr(value)` - Convert to `void*`
- `sdk.to_valuetype(obj, typename)` - Convert to ValueType
- `sdk.float_to_ptr(number)` - Convert float to `void*`

### Int64 Bitmasking for Smaller Types
```lua
local bool_val = sdk.to_int64(value) & 1
local byte_val = sdk.to_int64(value) & 0xFF
local short_val = sdk.to_int64(value) & 0xFFFF
local int_val = sdk.to_int64(value) & 0xFFFFFFFF
```

### Serialization
- `sdk.deserialize(data)` - Table of bytes (raw RSZ data) -> list of `REManagedObject`

### Utility
- `sdk.copy_to_clipboard(text)`

---

## re API (Callbacks)

### Core Callbacks
- `re.msg(text)` - MessageBox (pauses game)
- `re.on_script_reset(fn)` - Called when scripts reset
- `re.on_config_save(fn)` - Called when REFramework saves config
- `re.on_draw_ui(fn)` - Called every frame when Script Generated UI is open (use imgui here)
- `re.on_frame(fn)` - Called every frame (use `draw` functions here, not imgui unless `begin_window`)

### Application Entry Callbacks
- `re.on_pre_application_entry(name, fn)` - Before entry executes
- `re.on_application_entry(name, fn)` - When entry executes

**Key entry points for accessibility:**
- `UpdateBehavior` - Main game logic update
- `UpdateGUI` - GUI update
- `LockScene` - Scene is locked (safe to modify transforms)
- `BeginRendering` - Before rendering begins
- `UpdateMotion` - Motion/animation update

### GUI Callbacks
- `re.on_pre_gui_draw_element(fn)` - Before GUI element draws. Return `false` to hide element.
  - `fn(element, context)` where element is `REComponent*`
- `re.on_gui_draw_element(fn)` - When GUI element draws

### Full Application Entry List
Initialize, Setup, Start, Update, BeginRendering, EndRendering, Terminate, Finalize series.
See `docs/lua-api-core.md` source for the complete list of 300+ entry names.

---

## reframework API

- `reframework:is_drawing_ui()` - Is REFramework menu open?
- `reframework:get_game_name()` - Game identifier (e.g. `"re2"`, `"dmc5"`, `"mhrise"`)
- `reframework:is_key_down(key)` - Check Windows virtual key code
- `reframework:get_commit_count()`, `get_branch()`, `get_commit_hash()`, `get_tag()`

---

## log API

- `log.info(text)` - Info level
- `log.warn(text)` - Warning level
- `log.debug(text)` - Debug level (needs DebugView or debug console)
- `log.error(text)` - Error level

---

## draw API

**Must be used in `re.on_frame` or `re.on_draw_ui`.**

### Coordinate Conversion
- `draw.world_to_screen(world_pos)` - Returns `Vector2f` or `nil` if not visible

### Text
- `draw.world_text(text, 3d_pos, color)` - Text at 3D position
- `draw.text(text, x, y, color)` - Text at 2D screen position

### Shapes
- `draw.filled_rect(x, y, w, h, color)` / `draw.outline_rect(...)`
- `draw.line(x1, y1, x2, y2, color)`
- `draw.outline_circle(x, y, radius, color, segments)` / `draw.filled_circle(...)`
- `draw.outline_quad(...)` / `draw.filled_quad(...)`
- `draw.sphere(world_pos, radius, color, outline)`
- `draw.capsule(start, end, radius, color, outline)`
- `draw.cube(matrix)` / `draw.grid(matrix, size)`

### Gizmo
- `draw.gizmo(unique_id, matrix, operation, mode)` - Interactive 3D transform gizmo. Returns `(changed, modified_matrix)`

---

## fs API

**Sandboxed to `reframework/data/` subdirectory.** `$natives` token for natives dir.

- `fs.glob(filter)` - Returns table of file paths matching regex
- `fs.read(filename)` - Returns file content as string
- `fs.write(filename, data)` - Write string to file

---

## json API

- `json.load_string(json_str)` - JSON string -> Lua table
- `json.dump_string(value, indent)` - Lua table -> JSON string (default indent: -1)
- `json.load_file(filepath)` - Read JSON from `reframework/data/`
- `json.dump_file(filepath, value, indent)` - Write JSON to `reframework/data/` (default indent: 4)

---

## Return Type Auto-Conversions
Functions `sdk.call_native_func`, `sdk.call_object_func`, `REManagedObject:call`, `REManagedObject:get_field`:
- `System.String` -> Lua string
- `System.Int/UInt/Boolean/Single` -> Lua number/boolean
- `via.vec2/vec3/vec4` -> Vector2f/Vector3f/Vector4f
- `via.mat4` -> Matrix4x4f

## ByRef Parameters in Hooks
ByRef params are `T**` not `T*`. Use deref helper:
```lua
local function deref_ptr(ptr)
    local fake_int64 = sdk.to_valuetype(ptr, "System.UInt64")
    return fake_int64:get_field("mValue")
end
```
