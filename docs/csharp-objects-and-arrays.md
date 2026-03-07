# C# Managed Objects, Arrays & Collections

## ManagedObject

Handles garbage-collected engine objects.

### Creating from Address
```csharp
var mo = ManagedObject.ToManagedObject(address);
// In hooks:
var self = ManagedObject.ToManagedObject(args[1])?.As<app.SomeType>();
```

### Typed Proxy Casting (.As<T>)
```csharp
var typed = mo.As<app.SaveServiceManager>();
bool ready = typed.IsInitialized;        // Direct property access
typed._MaxUseSaveSlotCount = 117;        // Direct field access
typed.reloadSaveSlotInfo();              // Direct method call
```

### Getting Raw Address
```csharp
ulong addr = mo.GetAddress();
// Useful in post-hooks to replace return values:
retval = replacement.GetAddress();
```

### Reflection-Style Access (IObject)
```csharp
// Read fields
string name = (string)mo.GetField("_Name");
int count = (int)mo.GetField("_Count");
var child = (ManagedObject)mo.GetField("_ChildRef");

// Call methods
object result = (mo as IObject).Call("reloadSaveSlotInfo");
object result = (mo as IObject).Call("setValue", 42, true);

// Disambiguate overloads
object result = (mo as IObject).Call("getValue(app.SaveSlotSegmentType)", segmentValue);

// Get type
TypeDefinition td = mo.GetTypeDefinition();
```

---

## NativeObject

Non-GC native engine objects (C++).

```csharp
var app = API.GetNativeSingletonT<via.Application>();
var sceneMgr = API.GetNativeSingletonT<via.SceneManager>();
object result = (sceneMgr as IObject).Call("get_CurrentScene");
```

---

## Typed Proxies

Auto-generated from TDB. Located in `reframework/plugins/managed/generated/`.

### REFType Static Field
```csharp
// Instead of: TDB.Get().FindType("app.GuiSaveDataInfo")
var td = app.GuiSaveDataInfo.REFType;

// Create instances
var instance = app.SomeType.REFType.CreateInstance();
var array = app.SomeType.REFType.CreateManagedArray(100);
```

### Namespace Mapping
- `app.PlayerManager` -> `app.PlayerManager` interface
- `System.*` -> `_System.*` (avoid .NET conflicts)

### Enum Types
```csharp
// Instead of magic numbers:
if (part._Usage == app.SaveSlotCategory.Game) { ... }

switch (part._Usage) {
    case app.SaveSlotCategory.Game: break;
    case app.SaveSlotCategory.System: break;
}
```

### Generic Type Fallback
```csharp
// Generics don't have proxies - use reflection
var mo = managedObj as IObject;
var count = (int)mo.Call("get_Count");
var value = mo.Call("get_Item", key);
```

### Proxy Factory
```csharp
var saveMgr = ManagedProxy<app.SaveServiceManager>.Create(mo);
var saveMgr = ManagedProxy<app.SaveServiceManager>.CreateFromSingleton("app.SaveServiceManager");
```

---

## Globalize() - Object Lifetime

**MUST call `.Globalize()` on any ManagedObject that persists across frames.**

### Required
```csharp
// Static field storage
static ManagedObject _cached;
_cached = API.GetManagedSingleton("app.SomeManager");
_cached.Globalize();

// Collection storage
obj.Globalize();
_list.Add(obj);

// Created arrays
var arr = app.SomeType.REFType.CreateManagedArray(100);
arr.Globalize();
```

### NOT Required
```csharp
// Temporary within a single hook - GC won't run mid-callback
[MethodHook(...)]
static PreHookResult OnPre(Span<ulong> args) {
    var self = ManagedObject.ToManagedObject(args[1]); // No Globalize needed
    return PreHookResult.Continue;
}
```

---

## Arrays

Cast any `T[]` to `_System.Array`.

### Basic Operations
```csharp
var arr = managedObj.As<_System.Array>();
int len = arr.Length;
var elem = arr.GetValue(i);     // read
arr.SetValue(newElem, i);       // write
```

### Creating Arrays
```csharp
var newArr = app.SomeType.REFType.CreateManagedArray(90);
newArr.Globalize();
var arr = newArr.As<_System.Array>();
```

### Copy Between Arrays
```csharp
for (int i = 0; i < oldArr.Length; i++) {
    var elem = oldArr.GetValue(i);
    if (elem != null) newArr.SetValue(elem, i);
}
```

### Array Replacement in Post-Hook
```csharp
[MethodHook(typeof(app.SomeType), nameof(app.SomeType.getItems), MethodHookType.Post)]
static void OnPost(ref ulong retval) {
    var arr = ManagedObject.ToManagedObject(retval)?.As<_System.Array>();
    if (arr == null || arr.Length >= TARGET_SIZE) return;

    var newMo = app.ItemType.REFType.CreateManagedArray((uint)TARGET_SIZE);
    newMo.Globalize();
    var newArr = newMo.As<_System.Array>();

    for (int i = 0; i < arr.Length; i++) {
        var elem = arr.GetValue(i);
        if (elem != null) newArr.SetValue(elem, i);
    }
    retval = newMo.GetAddress();
}
```

### Limitations
- `_System.Array` only works on actual `T[]` types
- NOT for `List<T>`, `RingBuffer<T>`, etc. - navigate to backing array field
- Value-type elements returned as boxed representations

---

## Collections (Typed Proxies)

Proxies implement `IDictionary<K,V>`, `IList<T>`, `ISet<T>`, `ICollection<T>`.

```csharp
using Col = REFrameworkNET.Collections;

// Dictionary
var handlers = saveMgr._GameSlotSaveHandlers;
foreach (var key in handlers.Keys) { ... }
var handler = handlers["CharacterManager"];

// List
Col.IList<app.PlayerContext> players = charMgr.PlayerContextList;
foreach (var player in players) { ... }
var first = players[0];

// HashSet
Col.ISet<app.ItemID> items = itemMgr._AcquiredIDSet;
bool has = items.Contains(someItem);
```

### ManagedObject foreach (Untyped)
```csharp
// Array
foreach (var item in arrMo) {
    var typed = ((ManagedObject)item).As<app.SomeType>();
}

// Dictionary (elements are KeyValuePair ValueTypes)
foreach (var item in dictMo) {
    var kvp = item as IObject;
    var key = kvp.Call("get_Key");
    var val = kvp.Call("get_Value");
}
```

---

## ValueType (Stack-Allocated Structs)
```csharp
var vec3 = ValueType.New<via.vec3>();
(vec3 as IObject).Call("set_x", 1.0f);
(vec3 as IObject).Call("set_y", 2.0f);

// Or from TypeDefinition
var quat = TDB.Get().FindType("via.Quaternion").CreateValueType();
```

---

## Threading

### Custom Threads MUST call LocalFrameGC
```csharp
var thread = new Thread(() => {
    while (!cts.Token.IsCancellationRequested) {
        // ... logic ...
        API.LocalFrameGC(); // REQUIRED
        Thread.Yield();
    }
    API.LocalFrameGC();
});

[PluginExitPoint]
public static void Unload() {
    cts.Cancel();
    foreach (var t in threads) t.Join();
}
```

---

## Plugin Attributes Summary

| Attribute | Signature | Purpose |
|-----------|-----------|---------|
| `[PluginEntryPoint]` | `public static void Main()` | Plugin load |
| `[PluginExitPoint]` | `public static void OnUnload()` | Plugin unload/cleanup |
| `[MethodHook]` | Pre: `static PreHookResult(Span<ulong>)` | Intercept methods |
| `[MethodHook]` | Post: `static void(ref ulong)` | Modify returns |
| `[Callback]` | `public static void()` | Frame callbacks |
