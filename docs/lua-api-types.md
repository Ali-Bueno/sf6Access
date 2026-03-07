# Lua Type System Reference

## VectorXf (Vector2f, Vector3f, Vector4f)

### Constructors
```lua
Vector2f.new(x, y)
Vector3f.new(x, y, z)
Vector4f.new(x, y, z, w)
```

### Fields
- `x`, `y` (all), `z` (3f/4f), `w` (4f only)

### Methods
- `self:dot(other)`, `self:cross(other)`, `self:length()`
- `self:normalize()` (in-place), `self:normalized()` (returns new)
- `self:reflect(normal)`, `self:refract(normal, eta)`
- `self:lerp(other, t)`
- `self:to_vec2()`, `self:to_vec3()`, `self:to_vec4()`
- `self:to_mat()` -> Matrix4x4f, `self:to_quat()` -> Quaternion

### Operators
`+`, `-`, `*` (scalar)

---

## Matrix4x4f

### Constructors
```lua
Matrix4x4f.new()                    -- default
Matrix4x4f.new(x1,y1,z1,w1, ...)   -- 16 floats (4 rows)
Matrix4x4f.identity()               -- identity matrix
```

### Methods
- `self:to_quat()` -> Quaternion
- `self:inverse()` -> new Matrix4x4f, `self:invert()` -> in-place
- `self:interpolate(other, t)` -> lerp
- `self:matrix_rotation()` -> extract rotation matrix

### Operators
- `Matrix4x4f * Matrix4x4f`, `Matrix4x4f * Vector4f`
- `Matrix4x4f[i]` (i in [0,3)) -> Vector4f row

---

## Quaternion

### Constructor
```lua
Quaternion.new(w, x, y, z)  -- NOTE: w comes first!
Quaternion.identity()
```

### Fields
- `x`, `y`, `z`, `w`

### Methods
- `self:to_mat4()` -> Matrix4x4f, `self:to_euler()` -> Vector3f
- `self:inverse()` -> new, `self:invert()` -> in-place
- `self:normalize()` -> in-place, `self:normalized()` -> new
- `self:slerp(other, t)`, `self:dot(other)`, `self:length()`
- `self:conjugate()` -> new

### Operators
- `Quaternion * Quaternion`, `Quaternion * Vector3f`, `Quaternion * Vector4f`
- `Quaternion[i]` (i in [0,4))

---

## REManagedObject

Basic building block for most engine types. Returned from `sdk.call_native_func`, `sdk.call_object_func`, `sdk.get_managed_singleton`.

### Custom Indexers (Syntactic Sugar)
```lua
local value = obj.fieldName           -- get field
obj.fieldName = newValue              -- set field (auto ref counting)
local result = obj:methodName(args)   -- call method
local item = obj[i]                   -- calls get_Item
obj[i] = foo                          -- calls set_Item
```

### Methods
- `self:call(method_name, args...)` - Supports full prototypes: `"Bar(System.Int32, System.Single)"`
- `self:get_type_definition()` -> RETypeDefinition
- `self:get_field(name)` / `self:set_field(name, value)`
- `self:get_address()` -> memory address
- `self:get_reference_count()`

### Reference Management (Dangerous)
- `self:add_ref()` / `self:add_ref_permanent()` / `self:release()` / `self:force_release()`
- REFramework auto-increments ref count for Lua references. Manual management rarely needed except newly created objects.

### Memory Read/Write
- `self:read_byte/short/dword/qword/float/double(offset)`
- `self:write_byte/short/dword/qword/float/double(offset, value)`

---

## RETypeDefinition

### Getting One
```lua
local td = sdk.find_type_definition("app.SomeType")
local td = obj:get_type_definition()
```

### Methods
- `self:get_full_name()`, `self:get_name()`, `self:get_namespace()`
- `self:get_method(name)` -> REMethodDefinition (supports prototypes for overloads)
- `self:get_methods()` -> list of REMethodDefinition
- `self:get_field(name)` -> REField
- `self:get_fields()` -> list of REField
- `self:get_parent_type()` -> RETypeDefinition
- `self:get_runtime_type()` -> System.Type
- `self:get_size()`, `self:get_valuetype_size()`
- `self:is_a(typename_or_td)` - Checks inheritance chain
- `self:is_value_type()`, `self:is_by_ref()`, `self:is_pointer()`, `self:is_primitive()`
- `self:is_generic_type()`, `self:is_generic_type_definition()`
- `self:get_generic_argument_types()`, `self:get_generic_type_definition()`
- `self:create_instance()` -> REManagedObject

---

## REMethodDefinition

- `self:get_name()`, `self:get_return_type()` -> RETypeDefinition
- `self:get_function()` -> void* (native function pointer)
- `self:get_declaring_type()` -> RETypeDefinition
- `self:get_num_params()`, `self:get_param_types()`, `self:get_param_names()`
- `self:is_static()`
- `self:call(obj, args...)` - Same as `obj:call(args...)`

---

## REField

- `self:get_name()`, `self:get_type()` -> RETypeDefinition
- `self:get_offset_from_base()`, `self:get_offset_from_fieldptr()`
- `self:get_declaring_type()` -> RETypeDefinition
- `self:get_flags()`, `self:is_static()`, `self:is_literal()`
- `self:get_data(obj)` - Returns field data. `obj` = nil (static), REManagedObject*, or void*

---

## RETransform (inherits REComponent -> REManagedObject)

- `self:get_position()` -> Vector4f (world)
- `self:get_rotation()` -> Quaternion (world)
- `self:set_position(pos, no_dirty)` - pos is Vector4f. no_dirty=true when scene locked
- `self:set_rotation(rotation)` - Quaternion
- `self:calculate_base_transform(joint)` -> Matrix4x4f (T-pose reference)

---

## REComponent (inherits REManagedObject)

Fundamental building block for all GameObjects. Can be added/removed at runtime.

---

## REResource

Requires manual reference counting (unlike REManagedObject).

- `self:add_ref()` / `self:release()`
- `self:get_address()`
- `self:create_holder(typename)` - Creates `via.ResourceHolder` variant

```lua
local res = sdk.create_resource("via.motion.MotionFsm2Resource", "path.motfsm2"):add_ref()
local holder = res:create_holder("via.motion.MotionFsm2ResourceHolder"):add_ref()
```

---

## SystemArray (inherits REManagedObject)

Wrapper over `System.Array`.

**WARNING:** Do NOT use `ipairs` on SystemArray (skips first, goes past end). Use `pairs` or `get_elements()`.

- `self:get_elements()` -> Lua table (elements as REManagedObject, not raw ValueTypes)
- `self:get_element(index)`, `self:get_size()`
- `self[index]` - Bracket notation

---

## ValueType

Container for unknown ValueTypes.

```lua
local vt = ValueType.new(type_definition)
```

- `self.type`, `self.data`
- `self:call(name, args...)`, `self:get_field(name)`, `self:set_field(name, value)`
- `self:address()`, `self:get_type_definition()`

**Important:** `set_field` only modifies local copy. Pass to game functions to apply changes.

Has same `read_*/write_*` memory operations as REManagedObject.
