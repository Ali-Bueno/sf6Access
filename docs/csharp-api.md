# C# Scripting API Reference

## Overview
- REFramework.NET: C# scripting for RE Engine games
- 3-7x faster than Lua (single-threaded), up to 80x multi-threaded
- JIT compiled vs Lua interpreted
- True multi-threading support (Lua uses shared locked state)
- Requires .NET 10.0 runtime

## Minimal Plugin
```csharp
using REFrameworkNET;
using REFrameworkNET.Attributes;

public class MyPlugin {
    [PluginEntryPoint]
    public static void Main() {
        API.LogInfo("Hello from C#!");
    }
}
```

## Plugin Lifecycle
1. .NET runtime loads
2. Reference assemblies generated from game's TDB
3. Dependencies loaded from `managed/`
4. Source files in `source/` compiled
5. Pre-compiled assemblies loaded
6. `[PluginEntryPoint]` methods execute
7. `[PluginExitPoint]` methods called on reload/exit

---

## API Class (REFrameworkNET.API)

### Logging
```csharp
API.LogInfo("message");
API.LogWarning("message");
API.LogError("message");
API.LogLevel = LogLevel.Warning;  // Suppress info
API.LogToConsole = false;         // Disable console
```

### Singletons (Untyped)
```csharp
ManagedObject playerMgr = API.GetManagedSingleton("app.PlayerManager");
NativeObject sceneMgr = API.GetNativeSingleton("via.SceneManager");
```

### Singletons (Typed - Preferred)
```csharp
var playerMgr = API.GetManagedSingletonT<app.PlayerManager>();
var sceneMgr = API.GetNativeSingletonT<via.SceneManager>();
// Direct property/method access with autocomplete
var player = playerMgr.CurrentPlayer;
```

### Singleton Enumeration
```csharp
List<ManagedSingleton> managed = API.GetManagedSingletons();
List<NativeSingleton> native = API.GetNativeSingletons();
```

### Utility
```csharp
API.LocalFrameGC();               // Flush VM local reference frame (needed in custom threads)
bool drawing = API.IsDrawingUI(); // In ImGui overlay?
string dir = API.GetPluginDirectory(typeof(MyPlugin).Assembly);
TDB tdb = API.GetTDB();
ResourceManager mgr = API.GetResourceManager();
```

---

## TDB Class (REFrameworkNET.TDB)

Type Database - reflection over RE Engine metadata.

### Type Lookup
```csharp
var tdb = TDB.Get();
TypeDefinition td = tdb.FindType("app.PlayerManager");     // uncached
TypeDefinition td2 = tdb.GetTypeT<app.PlayerManager>();    // cached (preferred)
TypeDefinition td3 = tdb.GetType(index);                   // by index
```

### Method/Field Lookup
```csharp
Method m = tdb.FindMethod("app.PlayerManager", "get_CurrentPlayer");
Field f = tdb.FindField("app.EnemyContext", "_ConditionDamageList");
```

### Metadata
```csharp
uint types = tdb.GetNumTypes();
uint methods = tdb.GetNumMethods();
foreach (var td in TDB.Get().Types) { /* iterate all types */ }
```

---

## VM Class (REFrameworkNET.VM)

### String Creation
```csharp
var str = VM.CreateString("Hello");
str.Globalize();  // Prevent GC if storing persistently
```

---

## ResourceManager

```csharp
var mgr = API.GetResourceManager();
var tex = mgr.CreateResource("via.render.Texture", "path/to/texture.tex");
var userData = mgr.CreateUserData("app.ItemUserData", "data/item_data.user");
```

---

## TypeDefinition

### Identity
- `Name`, `Namespace`, `FullName`, `Index`, `Size`, `ValueTypeSize`

### Type Queries
```csharp
bool isVal = td.IsValueType();
bool isEnum = td.IsEnum();
bool isDerived = td.IsDerivedFrom("app.EnemyCharacter");
VMObjType vmType = td.GetVMObjType(); // Object, Array, String, Delegate, ValType
```

### Members
```csharp
Method m = td.GetMethod("doSomething");   // or FindMethod
Field f = td.GetField("_Health");          // or FindField
var methods = td.GetMethods();
var fields = td.GetFields();
```

### Instance Creation
```csharp
ManagedObject instance = td.CreateInstance(0);
ValueType vt = td.CreateValueType();
ManagedObject array = td.CreateManagedArray(10);
// Call .Globalize() if keeping reference!
```

### Hierarchy
- `ParentType`, `DeclaringType`, `UnderlyingType`, `ElementType`
- `GetGenericArguments()`

### Statics
```csharp
var statics = td.Statics;
var val = (statics as IObject).GetField("_SomeStaticField");
```

---

## Method

### Identity
- `Name`, `Index`, `VirtualIndex`, `DeclaringType`, `ReturnType`
- `IsStatic()`, `IsVirtual()`, `IsOverride()`
- `GetFunctionPtr()` -> IntPtr

### Parameters
```csharp
uint count = method.GetNumParams();
var parameters = method.GetParameters(); // List of MethodParameter { Name, Type }
```

### Invocation
```csharp
// Recommended - auto-boxing
int hp = (int)method.InvokeBoxed(typeof(int), instance, new object[] { });

// Low-level - returns InvokeRet struct
InvokeRet ret = method.Invoke(instance, new object[] { 42 });
if (!ret.ExceptionThrown) { float val = ret.Float; }

// Dynamic dispatch
object result = null;
method.HandleInvokeMember_Internal(instance, new object[] { arg }, ref result);
```

### Dynamic Hooking
```csharp
var hook = method.AddHook(false);
hook.AddPre(args => { return PreHookResult.Continue; });
hook.AddPost(args => { });
```

---

## Field

### Identity
- `Name`, `Index`, `Flags`, `DeclaringType`, `Type`
- `IsStatic()`, `IsLiteral()`, `OffsetFromBase`

### Data Access
```csharp
object value = field.GetDataBoxed(objAddress, isContainerValueType);
field.SetDataBoxed(objAddress, 999, isContainerValueType);
// CRITICAL: isValueType refers to the CONTAINING object, not the field itself!
```

---

## InvokeRet Struct
128-byte union struct from `Method.Invoke()`. Fields at offset 0:
- `Byte`, `Word`, `DWord`, `QWord`, `Float`, `Double`, `Ptr`
- `ExceptionThrown` at offset 128

---

## Performance Tips
Cache type/method/field lookups in static fields:
```csharp
static Method s_getHealth = app.SomeType.REFType.GetMethod("get_Health");
static Field s_maxHp = app.SomeType.REFType.GetField("_MaxHealth");
```
Typed proxies cache automatically.
