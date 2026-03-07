# C# Hooks Reference

## Key Difference from Lua
C# hooks are **non-blocking** and may run on **multiple threads simultaneously**. Lua hooks are serialized.

---

## Attribute-Based Hooks

### Pre-Hook (Instance Method)
```csharp
[MethodHook(typeof(app.SomeType), nameof(app.SomeType.someMethod), MethodHookType.Pre)]
static PreHookResult OnPre(Span<ulong> args)
{
    // args[0] = thread context
    // args[1] = this pointer (instance methods)
    // args[2+] = parameters
    var self = ManagedObject.ToManagedObject(args[1])?.As<app.SomeType>();
    return PreHookResult.Continue; // or PreHookResult.Skip
}
```

### Pre-Hook (Static Method)
```csharp
[MethodHook(typeof(app.SomeType), nameof(app.SomeType.StaticMethod), MethodHookType.Pre)]
static PreHookResult OnPre(Span<ulong> args)
{
    // args[0] = thread context
    // args[1] = first parameter (no this pointer)
    int param1 = (int)args[1];
    return PreHookResult.Continue;
}
```

### Post-Hook (Value Type Return)
```csharp
[MethodHook(typeof(app.SomeType), nameof(app.SomeType.getCount), MethodHookType.Post)]
static void OnPost(ref ulong retval)
{
    retval = 42; // Override return value directly
}
```

### Post-Hook (Reference Type Return)
```csharp
[MethodHook(typeof(app.SomeType), nameof(app.SomeType.getName), MethodHookType.Post)]
static void OnPost(ref ulong retval)
{
    var original = ManagedObject.ToManagedObject(retval);
    // Build replacement...
    retval = replacement.GetAddress();
}
```

### Post-Hook (No Return)
```csharp
[MethodHook(typeof(app.SomeType), nameof(app.SomeType.update), MethodHookType.Post)]
static void OnPost() { /* simple notification */ }
```

### skipJmp Parameter
```csharp
// For trampolined functions - skip first jmp instruction
[MethodHook(typeof(app.SomeType), nameof(app.SomeType.method), MethodHookType.Pre, true)]
static PreHookResult OnPre(Span<ulong> args) => PreHookResult.Continue;
```

---

## Modifying Arguments
```csharp
[MethodHook(typeof(app.DamageParam), nameof(app.DamageParam.calcDamage), MethodHookType.Pre)]
static PreHookResult OnCalcDamage(Span<ulong> args)
{
    args[3] = args[3] * 2; // Double damage value
    return PreHookResult.Continue;
}
```

---

## Pre+Post Pattern (Stashing Context)
```csharp
static app.SomeType pendingObj;

[MethodHook(typeof(app.SomeType), nameof(app.SomeType.onSetup), MethodHookType.Pre)]
static PreHookResult OnPre(Span<ulong> args)
{
    pendingObj = ManagedObject.ToManagedObject(args[1])?.As<app.SomeType>();
    return PreHookResult.Continue;
}

[MethodHook(typeof(app.SomeType), nameof(app.SomeType.onSetup), MethodHookType.Post)]
static void OnPost(ref ulong retval)
{
    if (pendingObj != null) {
        pendingObj._SomeField = 99;
        pendingObj = null;
    }
}
```

---

## ByRef / Out Parameters
```csharp
// void Foo(ref int count) -> args[2] is pointer to int
unsafe {
    int* countPtr = (int*)args[2];
    int count = *countPtr;
    *countPtr = 99; // Modify ref parameter
}
```

### Capturing Out Params Across Hooks
```csharp
[ThreadStatic] static ulong pendingOutPtr;

[MethodHook(typeof(app.SomeType), nameof(app.SomeType.TryGetValue), MethodHookType.Pre)]
static PreHookResult OnPre(Span<ulong> args)
{
    pendingOutPtr = args[3]; // save pointer to out param
    return PreHookResult.Continue;
}

[MethodHook(typeof(app.SomeType), nameof(app.SomeType.TryGetValue), MethodHookType.Post)]
static void OnPost(ref ulong retval)
{
    if (pendingOutPtr != 0) {
        unsafe {
            var result = ManagedObject.ToManagedObject(*(ulong*)pendingOutPtr);
        }
        pendingOutPtr = 0;
    }
}
```

---

## Thread Safety

### Lock Pattern
```csharp
static readonly object _lock = new object();
static int totalDamage;

[MethodHook(typeof(app.DamageParam), nameof(app.DamageParam.applyDamage), MethodHookType.Pre)]
static PreHookResult OnDamage(Span<ulong> args)
{
    int damage = (int)args[2];
    lock (_lock) { totalDamage += damage; }
    return PreHookResult.Continue;
}
```

### ThreadStatic Pattern
```csharp
[ThreadStatic] static app.SomeType pendingObj; // Each thread gets its own copy
```

### Type Verification (for Inlined Getters)
```csharp
[MethodHook(typeof(app.SomeType), nameof(app.SomeType.get_Value), MethodHookType.Pre)]
static PreHookResult OnPre(Span<ulong> args)
{
    var self = ManagedObject.ToManagedObject(args[1]);
    if (self == null) return PreHookResult.Continue;

    var td = self.GetTypeDefinition();
    if (td == null || td.GetFullName() != "app.SomeType")
        return PreHookResult.Continue; // Not our type, skip

    // Safe to proceed
    return PreHookResult.Continue;
}
```

---

## Callbacks (Frame Updates)
```csharp
using REFrameworkNET.Callbacks;

[Callback(typeof(UpdateBehavior), CallbackType.Pre)]
public static void OnUpdate()
{
    // Runs every frame before engine update
}

[Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
public static void OnLateUpdate()
{
    // Runs after late update
}

[Callback(typeof(ImGuiRender), CallbackType.Pre)]
public static void OnImGui()
{
    // Draw ImGui UI
}
```

---

## Dynamic Hooks (Runtime)
```csharp
var method = typeDef.GetMethod("update");
var hook = method.AddHook(false);
hook.AddPre(args => {
    API.LogInfo("update called!");
    return PreHookResult.Continue;
});
hook.AddPost(args => { });
```

---

## Common Gotchas
1. **Inlined accessors** may fire on unrelated call sites - always verify object type
2. **Constructor hooks** apply to all subclass constructors too
3. **Heavy operations in hooks** stall the game - use background tasks
4. **Post-hooks run even with SKIP_ORIGINAL** - retval may be uninitialized
5. **Locking degrades performance** significantly (500/s -> 40-60/s in benchmarks)
6. **Some methods can't be hooked** if aggressively inlined by compiler
