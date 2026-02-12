# Critical .NET AOT Incompatibilities

Patterns that **will** crash, throw, or produce corrupt data in a Native AOT application.
Every pattern here is a hard rule: violating it causes failure after AOT publish.

## Dynamic Code Generation

### 1. System.Reflection.Emit is Unavailable
🔴 **AVOID** | .NET 7+

`System.Reflection.Emit` requires a JIT compiler. Native AOT has no JIT — all `Emit` calls throw `PlatformNotSupportedException`.

❌
```csharp
var method = new DynamicMethod("Add", typeof(int), new[] { typeof(int), typeof(int) });
var il = method.GetILGenerator();
il.Emit(OpCodes.Ldarg_0);
// PlatformNotSupportedException in AOT
```
✅
```csharp
// Use a static method or source generator instead
static int Add(int a, int b) => a + b;

// If dynamic dispatch is truly needed, use RuntimeFeature check:
if (RuntimeFeature.IsDynamicCodeSupported)
    UseDynamicMethod();
else
    UseFallback();
```
**Impact: PlatformNotSupportedException at runtime. Warning IL3050.**

### 2. Dynamic Assembly Loading is Unsupported
🔴 **AVOID** | .NET 7+

`Assembly.LoadFrom`, `Assembly.LoadFile`, and `Assembly.Load(byte[])` cannot load new assemblies at runtime in AOT — all code must be present at compile time.

❌
```csharp
var asm = Assembly.LoadFrom("/plugins/MyPlugin.dll"); // fails in AOT
var type = asm.GetType("MyPlugin.Handler");
```
✅
```csharp
// Register plugins at compile time via direct references
// or use a compiled plugin manifest
[RequiresUnreferencedCode("Plugin loading is not compatible with AOT")]
void LoadPlugin(string path) { /* ... */ }
```
**Impact: FileNotFoundException or PlatformNotSupportedException. Warning IL2026.**

### 3. The `dynamic` Keyword Uses DLR (Runtime Code Gen)
🔴 **AVOID** | .NET 7+

The `dynamic` keyword relies on the Dynamic Language Runtime which emits IL at runtime. This fails in AOT.

❌
```csharp
dynamic obj = GetResponse();
string name = obj.Name; // DLR tries to emit call site — fails in AOT
```
✅
```csharp
var obj = GetResponse();
string name = ((JsonElement)obj).GetProperty("Name").GetString();
// or deserialize to a strongly-typed class
```
**Impact: RuntimeBinderException or PlatformNotSupportedException. Warning IL3050.**

## Reflection

### 4. Type.GetType with Runtime-Determined Strings
🔴 **AVOID** | .NET 7+

When the type name comes from config, user input, or external data, the trimmer cannot know which types to preserve.

❌
```csharp
string typeName = config["HandlerType"]; // runtime value
Type t = Type.GetType(typeName); // trimmer can't see this
var handler = Activator.CreateInstance(t); // may be trimmed away
```
✅
```csharp
// Use a factory with statically known types
Type t = handlerName switch
{
    "OrderHandler" => typeof(OrderHandler),
    "PaymentHandler" => typeof(PaymentHandler),
    _ => throw new NotSupportedException($"Unknown handler: {handlerName}")
};
var handler = Activator.CreateInstance(t);
```
**Impact: TypeLoadException or MissingMethodException. Warning IL2026.**

### 5. Unbounded Reflection in Static Constructors
🔴 **AVOID** | .NET 7+

Reflection in static constructors propagates `[RequiresUnreferencedCode]` warnings to **every member** of the class — making the entire class unusable in trimmed code.

❌
```csharp
class MyService
{
    static readonly PropertyInfo[] Props;
    static MyService()
    {
        Props = typeof(MyService).GetProperties(); // warning on ALL members
    }
}
```
✅
```csharp
class MyService
{
    // Move reflection to a dedicated method with proper annotation
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    private static Type SelfType => typeof(MyService);

    static PropertyInfo[] GetProps() => SelfType.GetProperties();
}
```
**Impact: Cascading warnings across all class members. Warning IL2026/IL2070.**

## Serialization

### 6. Reflection-Based JSON Serialization
🔴 **DO** use source generation | .NET 6+

`JsonSerializer.Serialize(obj)` without a `JsonTypeInfo` or `JsonSerializerContext` uses reflection to discover properties. This is incompatible with trimming.

❌
```csharp
string json = JsonSerializer.Serialize(myObj); // reflection-based
var obj = JsonSerializer.Deserialize<MyType>(json); // reflection-based
```
✅
```csharp
[JsonSerializable(typeof(MyType))]
internal partial class AppJsonContext : JsonSerializerContext { }

string json = JsonSerializer.Serialize(myObj, AppJsonContext.Default.MyType);
var obj = JsonSerializer.Deserialize(json, AppJsonContext.Default.MyType);
```
**Impact: MissingMetadataException or incorrect serialization. Warning IL2026 + IL3050.**

### 7. Newtonsoft.Json is Fundamentally Incompatible
🔴 **AVOID** | .NET 7+

Newtonsoft.Json uses deep reflection and is not designed for AOT. It will not be updated for AOT compatibility. Migrate to source-generated `System.Text.Json`.

❌
```csharp
var obj = JsonConvert.DeserializeObject<MyType>(json); // Newtonsoft — not AOT safe
```
✅
```csharp
[JsonSerializable(typeof(MyType))]
internal partial class AppJsonContext : JsonSerializerContext { }
var obj = JsonSerializer.Deserialize(json, AppJsonContext.Default.MyType);
```
**Impact: Missing type metadata, incorrect deserialization, or crashes. Multiple IL2026 warnings.**

### 8. BinaryFormatter is Removed
🔴 **AVOID** | .NET 7+ (throws by default), .NET 9+ (removed)

`BinaryFormatter` was disabled by default in .NET 7 and fully removed in .NET 9 due to security and compatibility flaws.

❌
```csharp
var formatter = new BinaryFormatter();
formatter.Serialize(stream, obj); // throws PlatformNotSupportedException
```
✅
```csharp
// Use System.Text.Json, protobuf-net, or MessagePack with source generation
```
**Impact: PlatformNotSupportedException. SYSLIB0011 obsolete warning.**

## Expressions

### 9. Expression.Compile() Uses Interpreter in AOT
🔴 **AVOID** on hot paths | .NET 7+

`Expression.Compile()` falls back to an interpreter in AOT instead of JIT-compiled delegates. This is 10-100x slower and may cause unexpected behavior for complex expressions.

❌
```csharp
Expression<Func<int, int>> expr = x => x * 2;
var compiled = expr.Compile(); // uses slow interpreter in AOT
int result = compiled(5);
```
✅
```csharp
// Use a regular method or static lambda
static int Double(int x) => x * 2;

// If Expression trees are required (e.g., EF Core queries), they work
// for query translation but avoid Compile() on hot paths
```
**Impact: 10-100x performance degradation. No warning emitted — silent slowdown.**

## Configuration

### 10. Reflection-Based Configuration Binding
🔴 **DO** use source generator | .NET 8+

`ConfigurationBinder.Bind()` and `services.Configure<T>()` without the source generator use reflection to set properties. Enable the configuration binding source generator.

❌
```csharp
services.Configure<MyOptions>(config.GetSection("MySection")); // reflection-based
```
✅
```xml
<!-- In .csproj -->
<PropertyGroup>
  <EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>
</PropertyGroup>
```
```csharp
// Code stays the same — the source generator intercepts the call
services.Configure<MyOptions>(config.GetSection("MySection"));
```
**Impact: MissingMetadataException at runtime. Warning IL2026.**

## Regex

### 11. Compiled Regex is Unavailable in AOT
🔴 **DO** use source generation | .NET 7+

`RegexOptions.Compiled` requires JIT. In AOT, it silently falls back to interpretation. Use `[GeneratedRegex]` for ahead-of-time compiled patterns.

❌
```csharp
var re = new Regex(@"\d+", RegexOptions.Compiled); // falls back to interpreter in AOT
```
✅
```csharp
[GeneratedRegex(@"\d+")]
private static partial Regex DigitsRegex();
```
**Impact: Silent fallback to interpreted regex — slower but functional. No crash.**

## COM and Platform

### 12. Built-in COM Marshalling is Not Supported
🔴 **AVOID** | .NET 7+

Automatic COM interop uses runtime code generation. Use `ComWrappers` API instead.

❌
```csharp
[ComImport, Guid("...")]
interface IMyComInterface { void DoWork(); }
```
✅
```csharp
// Use ComWrappers for AOT-compatible COM interop
// See: https://learn.microsoft.com/dotnet/standard/native-interop/com-wrappers
```
**Impact: TypeLoadException or MarshalDirectiveException. Warning IL2050.**

### 13. C++/CLI is Not Supported
🔴 **AVOID** | .NET 7+

C++/CLI assemblies cannot be compiled with Native AOT. Use P/Invoke or COM Wrappers for native interop.

**Impact: Build failure. No AOT compilation possible.**
