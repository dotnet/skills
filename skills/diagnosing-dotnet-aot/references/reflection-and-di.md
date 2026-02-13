# Reflection and Dependency Injection Patterns for AOT

How to annotate reflection usage and design DI registrations for trim and AOT compatibility.

## Contents
- [The Annotation Workflow](#the-annotation-workflow) — Decision tree for handling reflection warnings
- [DynamicallyAccessedMembers — Step by Step](#dynamicallyaccessedmembers--step-by-step) — API-to-annotation mapping, propagation, common mistakes
- [RequiresUnreferencedCode and RequiresDynamicCode](#requiresunreferencedcode-and-requiresdynamiccode) — When and how to mark APIs
- [Suppressing Warnings Safely](#suppressing-warnings-safely) — Legitimate suppression, DynamicDependency
- [Dependency Injection Patterns](#dependency-injection-patterns) — Static registration, keyed services, open generics

## The Annotation Workflow

When you encounter an AOT or trim warning on reflection code, follow these steps in order:

1. **Can you eliminate reflection entirely?** → Best option. Use generics, source generators, or static dispatch.
2. **Are the types known at compile time?** → Annotate with `[DynamicallyAccessedMembers]`.
3. **Is the code fundamentally dynamic?** → Mark with `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`.
4. **Are you certain the code is safe despite warnings?** → Suppress with `[UnconditionalSuppressMessage]` (last resort).

## DynamicallyAccessedMembers — Step by Step

### Identifying What to Annotate

Look at the reflection API being called and match it to the correct member type:

| Reflection API | Required `DynamicallyAccessedMemberTypes` |
|---------------|------------------------------------------|
| `Activator.CreateInstance(type)` | `PublicParameterlessConstructor` |
| `Activator.CreateInstance(type, args)` | `PublicConstructors` |
| `type.GetMethod(name)` / `type.GetMethods()` | `PublicMethods` |
| `type.GetProperty(name)` / `type.GetProperties()` | `PublicProperties` |
| `type.GetField(name)` / `type.GetFields()` | `PublicFields` |
| `type.GetEvent(name)` / `type.GetEvents()` | `PublicEvents` |
| `type.GetConstructor(...)` | `PublicConstructors` |
| `type.GetMembers()` | `All` (avoid — use narrowest type) |

### Propagating Annotations Up the Call Chain

Annotations must flow from the reflection call site back to the source of the `Type`:

**Step 1: Annotate where reflection is used**
```csharp
void CreateWidget(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    Type widgetType)
{
    var widget = Activator.CreateInstance(widgetType); // ✅ no warning
}
```

**Step 2: Propagate to callers**
```csharp
void BuildWidget<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    TWidget>()
{
    CreateWidget(typeof(TWidget)); // ✅ no warning — TWidget is annotated
}
```

**Step 3: Verify at the call site**
```csharp
BuildWidget<MyWidget>(); // ✅ no warning — MyWidget is a concrete type
```

### Annotating Fields and Properties

When a `Type` is stored in a field before being passed to reflection, the field must also be annotated:

```csharp
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
private Type _serializationType;

void Serialize(object obj)
{
    foreach (var prop in _serializationType.GetProperties()) // ✅ no warning
    {
        // ...
    }
}
```

### Common Mistakes with Annotations

| Mistake | Problem | Fix |
|---------|---------|-----|
| Annotating only the leaf method | Intermediate methods still produce warnings | Annotate the entire chain from call site to reflection |
| Using `DynamicallyAccessedMemberTypes.All` | Preserves everything — defeats trimming purpose | Use the narrowest type (e.g., `PublicParameterlessConstructor`) |
| Annotating virtual/interface methods | All overrides must have matching annotations | Avoid annotating virtual methods — redesign instead |
| Missing annotation on generic type parameter | `typeof(T).GetProperties()` warns without annotation on `T` | Add `[DynamicallyAccessedMembers]` to the generic parameter |

## RequiresUnreferencedCode and RequiresDynamicCode

### When to Use

- `[RequiresUnreferencedCode]` — the code uses reflection that can't be annotated (truly dynamic types)
- `[RequiresDynamicCode]` — the code uses APIs that require runtime code generation (Reflection.Emit, MakeGenericType with unknown value types)

### Writing Effective Messages

```csharp
// ❌ Not helpful
[RequiresUnreferencedCode("Uses reflection")]

// ✅ Helpful — explains what's incompatible and suggests alternative
[RequiresUnreferencedCode(
    "Plugin discovery uses Assembly.LoadFrom which is not compatible with trimming. "
    + "Register plugins at compile time using AddPlugin<T>() instead.")]

// ✅ With URL for more guidance
[RequiresUnreferencedCode("Dynamic handler resolution is not compatible with trimming.",
    Url = "https://learn.microsoft.com/dotnet/core/deploying/native-aot/fixing-warnings")]
```

### Propagating Up Public APIs

```csharp
class HandlerRegistry
{
    const string TrimMessage = "Handler registration by name is not compatible with AOT.";

    [RequiresUnreferencedCode(TrimMessage)]
    private Type ResolveByName(string name) => Type.GetType(name);

    [RequiresUnreferencedCode(TrimMessage)]
    public IHandler GetHandler(string name)
    {
        var type = ResolveByName(name); // no warning — method is also marked
        return (IHandler)Activator.CreateInstance(type);
    }
}
```

## Suppressing Warnings Safely

### When Suppression Is Legitimate

1. **Runtime feature check**: Code is guarded by `RuntimeFeature.IsDynamicCodeSupported`
2. **Known types preserved elsewhere**: Types are kept via `[DynamicDependency]` or always referenced
3. **EventSource false positives**: `WriteEvent` with >3 params triggers IL2026 but is safe for primitive types

### How to Suppress

```csharp
[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
    Justification = "Only primitive types are passed to WriteEvent")]
void LogEvent(string name, int id, long timestamp, string category)
{
    WriteEvent(1, name, id, timestamp, category);
}
```

**Never use `#pragma warning disable` or `[SuppressMessage]` for trim/AOT warnings** — they are not preserved in the compiled assembly and the trimmer will not see them.

### DynamicDependency as a Preservation Tool

Use `[DynamicDependency]` to keep specific members when you know they'll be needed but can't express it via annotations:

```csharp
[DynamicDependency("Process", typeof(MyHandler))]
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "MyHandler.Process is preserved via DynamicDependency")]
void InvokeHandler()
{
    var method = typeof(MyHandler).GetMethod("Process");
    method!.Invoke(null, null);
}
```

## Dependency Injection Patterns

### Static Registration (AOT-Safe)

Always register services with statically known types:

```csharp
// ✅ AOT-safe — types are known at compile time
builder.Services.AddSingleton<IOrderService, OrderService>();
builder.Services.AddScoped<IPaymentGateway, StripeGateway>();
builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
```

### Avoid Assembly Scanning

❌
```csharp
// Scans assemblies at runtime — not AOT compatible
builder.Services.Scan(scan => scan
    .FromAssemblyOf<IHandler>()
    .AddClasses(c => c.AssignableTo<IHandler>())
    .AsImplementedInterfaces());
```
✅
```csharp
// Explicit registration — AOT compatible
builder.Services.AddTransient<IHandler, OrderHandler>();
builder.Services.AddTransient<IHandler, PaymentHandler>();
builder.Services.AddTransient<IHandler, ShippingHandler>();
```

### Keyed Services (.NET 8+)

Use keyed services for named registrations instead of string-based resolution:

```csharp
builder.Services.AddKeyedSingleton<INotifier, EmailNotifier>("email");
builder.Services.AddKeyedSingleton<INotifier, SmsNotifier>("sms");

// Inject by key
public class OrderProcessor([FromKeyedServices("email")] INotifier notifier) { }
```

### Factory Methods with DynamicallyAccessedMembers

When a factory must create instances by type, annotate the type parameter:

```csharp
public static T CreateService<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    T>() where T : class
{
    return (T)Activator.CreateInstance(typeof(T))!;
}
```

### Open Generic Registration

Open generic registrations (`services.AddSingleton(typeof(IRepository<>), typeof(Repository<>))`) work with the built-in DI container in AOT **if** the container can determine all closed types at compile time. This works when closed types are requested through constructor injection of known services.

If you dynamically construct `IRepository<T>` for runtime-determined `T`, it may fail. Ensure all generic closures are statically reachable.
