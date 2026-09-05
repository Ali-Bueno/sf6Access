# REFramework Discovery Tricks & Data-Reading Gotchas

See also: `lua-hooks-and-patterns.md`, `tools.md`, `lua-api-types.md`, `hot-reload-workflow.md`.

## Object Explorer — discovery without restarts

Menu: **REFramework menu -> DeveloperTools -> Object Explorer**.

- **Singletons panel** lists managed + native singletons **live**. This is how you *discover* the
  name strings for `sdk.get_managed_singleton(name)` / `sdk.get_native_singleton(name)` — there is
  **no Lua API to enumerate singletons**, you have to already know the name string.
- **Right-click a method -> Hook** opens a tracking window with **live call counts** for that method
  and a "skip" toggle. Use this to **validate a candidate hook target actually fires** (and how often)
  **before** writing any Lua hook code — the key trick for iterating without restarting the game.
- **"Dump SDK"** generates `il2cpp_dump.json` (all types/fields/methods/inheritance/offsets/addresses)
  plus generated C++ headers. Use this for bulk **offline** reference instead of browsing hundreds of
  types in the UI.
- From Lua: `object_explorer:handle_address(addr)` jumps the UI straight to a given address
  (accepts uintptr_t / `REManagedObject*` / `void*` / a raw number). Pattern: print an address from a
  hook, then open it live in the inspector to see its type and fields.
- **Caveat:** only TDB members show in the explorer. Pure memory-layout ("Reflection Methods/Properties")
  fields still need manual offset work.

---

## Enumeration primitive

`RETypeDefinition:get_types_inheriting_from_this()` is the **one true enumeration primitive** — it
returns every `RETypeDefinition` inheriting from a base type. Use it to get, e.g., every class
inheriting `via.Component` without guessing names. Pair with `:is_a(typename)` to classify results.

```lua
local base = sdk.find_type_definition("via.Component")
for _, td in ipairs(base:get_types_inheriting_from_this()) do
    log.info(td:get_full_name())
end
```

- Members of a type at runtime: `t:get_fields()` / `t:get_methods()`. Each `REField` has
  `get_offset_from_base()` (byte offset, for raw `read_*`); each `REMethodDefinition` has
  `get_num_params()` / `get_param_types()` / `get_param_names()`.
- Components of a GameObject: get its `Transform`, call `get_Components` to get a Lua table, then
  filter with `:is_a("via....")`.

---

## Live debug surface (imgui + draw)

- `re.on_draw_ui(fn)` + `imgui.*` -> a collapsible panel appears automatically in the REFramework menu
  ("Script Generated UI"). Use it for a runtime tuning/toggle panel (verbosity, cue volume, per-feature
  on/off — every accessibility feature must be toggleable). **imgui calls belong in `on_draw_ui`, not
  `on_frame`.**
- `draw.*` world/2D overlay: `draw.world_text`, `draw.text`, `draw.line`, `draw.sphere`,
  `draw.world_to_screen(pos)` -> `Vector2f|nil`. Use it to draw raycast rays and hit points in-world to
  eyeball-verify direction conventions — this is what catches the mirrored-direction bugs documented in
  `reference/audio-navigation`. **`draw` calls work in `on_frame`.**

---

## Input / hotkeys

No dedicated input table. Bind accessibility hotkeys by polling in `re.on_frame`:

```lua
re.on_frame(function()
    if reframework:is_key_down(0x70) then -- VK_F1
        -- trigger action
    end
end)
```

- `reframework:is_key_down(vk)` — Windows VK codes.
- `reframework:get_keyboard_state()` — 256-entry bool array.
- `reframework:get_first_key_down()`.
- **No gamepad polling API.** For pad buttons, build the `via.hid.GamePadButton` enum via the
  `generate_enum` pattern below and compare against the game's pad-state field, or hook the game's
  input read.

---

## Reading data — gotchas

### `List<T>` / `Dictionary<K,V>` — no dedicated wrapper

Read via the real C# API, not a Lua collection wrapper:

```lua
local count = list:call("get_Count")
for i = 0, count - 1 do
    local item = list:call("get_Item", i) -- 0-indexed
end
```

Dictionaries: enumerate via `GetEnumerator`/`MoveNext`/`get_Current`, or `get_Keys`/`get_Values`.
Critical for menus/inventories. **Cache the `REMethodDefinition`**
(`find_type_definition(...):get_method(...)`) — every `:call` is a hashmap lookup.

### `SystemArray` trap

**Never `ipairs` a raw `SystemArray`** — it skips the first element and overruns the end. Use `pairs`,
or call `get_elements()` first and `ipairs` the resulting plain Lua table. Also has `get_element(i)`,
`arr[i]`, `get_size()`.

### Enums — no wrapper, build a name<->value table once

Enum values come back as raw underlying integers. Build the table once with `generate_enum`, gate with
`RETypeDefinition:is_enum()`, and cache it. This turns a raw state/tab int into speakable text
**without hardcoding numbers** (the no-magic-numbers rule).

```lua
local function generate_enum(typename)
    local t = sdk.find_type_definition(typename)
    if not t or not t:is_enum() then return nil end

    local enum = {}
    for _, field in ipairs(t:get_fields()) do
        if field:is_static() then
            local name = field:get_name()
            local value = field:get_data(nil)
            enum[value] = name
            enum[name] = value
        end
    end
    return enum
end
```

### Return auto-conversion — does NOT apply inside hooks

From `sdk.call_*` / method calls, these auto-convert: `System.String` -> Lua string;
int/uint/bool/float/double -> Lua number; `via.vec2/3/4` -> `VectorXf`; `via.mat4` -> `Matrix4x4f`;
`via.quat` -> `Quaternion`. **Everything else** (custom managed objects, enums-as-objects, arrays)
comes back as a raw pointer/wrapper needing `sdk.to_managed_object` / `sdk.to_valuetype` /
`sdk.get_native_field`.

**Important:** inside a hook, args are **always raw `void*`** — this auto-conversion does not apply to
hook arguments; convert them explicitly.

### ValueType `set_field` is local only

`ValueType:set_field` only modifies the local Lua-side copy — it does **not** write back into game
memory automatically. Pass the modified value type into a game function to actually apply it.

---

## Performance gotchas

- Every `obj:call` / `obj:get_field` / `obj:set_field` is a hashmap lookup. Cache `get_method` /
  `get_field` results **outside** per-frame hooks.
- Hooking `get_`/`set_` accessors is risky: compilers dedup/inline them, so your hook may fire for
  unrelated properties. Verify in Object Explorer's disassembly, and check the object's type inside
  the hook before acting.
- Heavy per-entity-per-frame work: stagger across frames, cache aggressively, or push it to a native
  plugin (REFramework's own best-practices advice).

---

## Math helpers for spatial audio

- `Vector3f`: `:dot`, `:cross`, `:length`, `:normalize()` / `:normalized()`, `:reflect`, `:lerp`,
  `+ - *`.
- `Quaternion`: `:to_euler()`, `:inverse()`, `:slerp`, `Quaternion.identity()`, and
  `Quaternion * Vector3f` rotates a vector — turn a facing quaternion into a forward vector for
  radar/sonification math.
- `Matrix4x4f`: `:to_quat()`, `:inverse()`, `:interpolate(other, t)`, row access `mat[i]`.
