# Practical Code Examples

## Lua Examples

### Complete Hook Example with State Tracking
```lua
local last_hp = nil
local player_hp_method = sdk.find_type_definition("app.PlayerStatus"):get_method("get_CurrentHP")

sdk.hook(
    player_hp_method,
    function(args)
        return sdk.PreHookResult.CALL_ORIGINAL
    end,
    function(retval)
        local hp = sdk.to_int64(retval) & 0xFFFFFFFF
        if hp ~= last_hp then
            log.info("HP changed: " .. tostring(hp))
            last_hp = hp
        end
        return retval
    end
)
```

### 3D Gizmo Test (Multi-Game Player Finder)
```lua
local gn = reframework:get_game_name()

local function get_localplayer()
    if gn == "re2" or gn == "re3" then
        local pm = sdk.get_managed_singleton(sdk.game_namespace("PlayerManager"))
        if not pm then return nil end
        return pm:call("get_CurrentPlayer")
    elseif gn == "dmc5" then
        local pm = sdk.get_managed_singleton(sdk.game_namespace("PlayerManager"))
        if not pm then return nil end
        local comp = pm:call("get_manualPlayer")
        if not comp then return nil end
        return comp:call("get_GameObject")
    elseif gn == "mhrise" then
        local pm = sdk.get_managed_singleton(sdk.game_namespace("player.PlayerManager"))
        if not pm then return nil end
        local comp = pm:call("findMasterPlayer")
        if not comp then return nil end
        return comp:call("get_GameObject")
    end
    return nil
end

re.on_frame(function()
    local player = get_localplayer()
    if not player then return end
    local transform = player:call("get_Transform")
    if not transform then return end

    local mat = transform:call("get_WorldMatrix")
    local changed, mat = draw.gizmo(transform:get_address(), mat)
    if changed then
        transform:set_rotation(mat:to_quat())
        transform:set_position(mat[3])
    end
end)
```

### GUI Element Debugger
```lua
local known_elements = {}

re.on_pre_gui_draw_element(function(element, context)
    known_elements[element:call("get_GameObject")] = os.clock()
    return true
end)

re.on_draw_ui(function()
    for go, time in pairs(known_elements) do
        local ok, name = pcall(go.call, go, "get_Name")
        if not ok or not name or go:get_reference_count() == 1 or (os.clock() - time > 1) then
            known_elements[go] = nil
        else
            if imgui.tree_node(name .. " " .. string.format("%x", go:get_address())) then
                object_explorer:handle_address(go)
                imgui.tree_pop()
            end
        end
    end
end)
```

### Material Toggler (RE2/RE3)
```lua
local function display_mesh(transform)
    local gameobj = transform:get_GameObject()
    if not gameobj then return end
    local mesh = gameobj:call("getComponent(System.Type)", sdk.typeof("via.render.Mesh"))
    if mesh then
        for i = 0, mesh:get_MaterialNum() - 1 do
            local name = mesh:getMaterialName(i)
            local enabled = mesh:getMaterialsEnable(i)
            if imgui.checkbox(name, enabled) then
                mesh:setMaterialsEnable(i, not enabled)
            end
        end
    end
end
```

### Config System Pattern
```lua
local config = { enabled = true, verbosity = 2 }
local config_path = "my_mod/config.json"

local saved = json.load_file(config_path)
if saved then config = saved end

re.on_config_save(function()
    json.dump_file(config_path, config)
end)

re.on_draw_ui(function()
    local changed
    changed, config.enabled = imgui.checkbox("Enabled", config.enabled)
    changed, config.verbosity = imgui.slider_int("Verbosity", config.verbosity, 0, 3)
end)
```

### Enum Generation Utility
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

-- Usage
local GamePadButton = generate_enum("via.hid.GamePadButton")
local HIDInputMode = generate_enum("app.HIDInputMode")
```

---

## C# Examples

### Complete Plugin with Hooks (RE9 Save Slots Pattern)
```csharp
using System;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;

public class AccessibilityPlugin
{
    static bool initialized;

    [PluginEntryPoint]
    public static void Main()
    {
        API.LogInfo("[Accessibility] Plugin loaded");
    }

    [PluginExitPoint]
    public static void OnUnload()
    {
        initialized = false;
        API.LogInfo("[Accessibility] Plugin unloaded");
    }

    [Callback(typeof(UpdateBehavior), CallbackType.Pre)]
    public static void OnUpdate()
    {
        if (initialized) return;

        var manager = API.GetManagedSingletonT<app.SomeManager>();
        if (manager == null) return;

        // Deferred init - wait for game to be ready
        initialized = true;
        API.LogInfo("[Accessibility] Initialized");
    }

    [MethodHook(typeof(app.SomeManager),
                nameof(app.SomeManager.someMethod),
                MethodHookType.Pre)]
    static PreHookResult OnPre(Span<ulong> args)
    {
        var self = ManagedObject.ToManagedObject(args[1])?.As<app.SomeManager>();
        if (self == null) return PreHookResult.Continue;
        // Read game state for accessibility
        return PreHookResult.Continue;
    }

    [MethodHook(typeof(app.SomeManager),
                nameof(app.SomeManager.getMaxCount),
                MethodHookType.Post)]
    static void OnPostGetMax(ref ulong retval)
    {
        retval = 117; // Override return value
    }
}
```

### Array Expansion in Post-Hook
```csharp
[MethodHook(typeof(app.SomeModel), nameof(app.SomeModel.makeDataList), MethodHookType.Post)]
public static void OnMakeListPost(ref ulong retval)
{
    var arr = ManagedObject.ToManagedObject(retval)?.As<_System.Array>();
    if (arr == null || arr.Length >= TARGET_SIZE) return;

    var newMo = app.DataInfo.REFType.CreateManagedArray((uint)TARGET_SIZE);
    newMo.Globalize();
    var newArr = newMo.As<_System.Array>();

    for (int i = 0; i < arr.Length; i++)
    {
        var elem = arr.GetValue(i);
        if (elem != null) newArr.SetValue(elem, i);
    }

    retval = newMo.GetAddress();
}
```

### Navigating Object Graph with Generic Fallback
```csharp
static ManagedObject GetItemFromGenericDict(app.SomeManager mgr)
{
    // Generic types have no proxy - use reflection
    var dict = (mgr as IObject).GetField("_ItemDict") as ManagedObject;
    if (dict == null) return null;

    try {
        return (dict as IObject)?.Call(
            "getValue(app.ItemType)",
            (int)app.ItemType.Weapon) as ManagedObject;
    } catch {
        // Fallback: access internal dictionary
        var internalDict = dict.GetField("_Dict") as ManagedObject;
        return (internalDict as IObject)?.Call(
            "FindValue(app.ItemType)",
            (int)app.ItemType.Weapon) as ManagedObject;
    }
}
```

### Thread-Safe Data Collection
```csharp
static readonly object _lock = new object();
static List<string> announcements = new List<string>();

[MethodHook(typeof(app.UIManager), nameof(app.UIManager.showMessage), MethodHookType.Pre)]
static PreHookResult OnShowMessage(Span<ulong> args)
{
    var msg = ManagedObject.ToManagedObject(args[2])?.ToString();
    if (msg != null)
    {
        lock (_lock) { announcements.Add(msg); }
    }
    return PreHookResult.Continue;
}

[Callback(typeof(UpdateBehavior), CallbackType.Pre)]
public static void OnUpdate()
{
    List<string> toAnnounce;
    lock (_lock)
    {
        toAnnounce = new List<string>(announcements);
        announcements.Clear();
    }
    foreach (var msg in toAnnounce)
    {
        // Send to screen reader
    }
}
```

---

## Script Repository Links
- [REFramework Scripts](http://praydog.com/projects/reframework-scripts/)
- [GitHub Scripts](https://github.com/praydog/REFramework/tree/master/scripts)
- [MHRise Scripts](https://github.com/Sarayalth/mhr_scripts)
- [alphazolam Scripts](https://github.com/alphazolam/REFramework-Scripts)
- [RELit](https://github.com/originalnicodr/RELit)
