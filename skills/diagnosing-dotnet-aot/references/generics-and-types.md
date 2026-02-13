# Generic Types and Expressions in AOT

How generic instantiation, `MakeGenericType`, and `System.Linq.Expressions` behave under Native AOT.

## Contents
- [Generic Value Type Specialization](#generic-value-type-specialization) — The fundamental rule, MakeGenericType safety, rooting instantiations
- [MakeGenericMethod](#makegenericmethod) — Same rules as MakeGenericType
- [Generic Virtual Methods](#generic-virtual-methods) — Binary size impact
- [System.Linq.Expressions](#systemlinqexpressions) — Expression.Compile limitations, safe vs unsafe usage
- [Nullable Value Types and Generics](#nullable-value-types-and-generics)

## Generic Value Type Specialization

### The Fundamental Rule
🔴 **UNDERSTAND** | .NET 7+

In Native AOT, **every generic instantiation with a value type (struct) gets its own unique machine code**. Unlike reference types (which share code via canonical forms), value types like `int`, `float`, `double`, and custom structs each require dedicated compiled code.

This means:
- `List<int>` and `List<double>` produce separate compiled methods
- The AOT compiler must see all value-type instantiations at build time
- Dynamically constructing `GenericType<SomeStruct>` at runtime will fail if the compiler didn't generate code for it

### MakeGenericType — When It's Safe

| Scenario | Safe in AOT? | Why |
|----------|:---:|-----|
| `typeof(List<>).MakeGenericType(typeof(string))` | ✅ | Reference types share code — only one canonical form needed |
| `typeof(List<>).MakeGenericType(typeof(int))` | ⚠️ | Works only if `List<int>` is statically referenced somewhere |
| `typeof(List<>).MakeGenericType(runtimeType)` where `runtimeType` is a class | ✅ | Reference types share code |
| `typeof(List<>).MakeGenericType(runtimeType)` where `runtimeType` could be a struct | 🔴 | May fail — specific struct instantiation not generated |

### Rooting Generic Value Type Instantiations
🟡 **DO** when using MakeGenericType with value types | .NET 7+

If you must use `MakeGenericType` with value types, ensure all needed instantiations are statically referenced:

❌
```csharp
// runtimeType might be int, float, or double — compiler may not generate code for all
Type closedType = typeof(Converter<>).MakeGenericType(runtimeType);
var converter = Activator.CreateInstance(closedType);
```
✅
```csharp
// Root all needed instantiations so the compiler generates code for them
static void PreserveGenericInstantiations()
{
    _ = typeof(Converter<int>);
    _ = typeof(Converter<float>);
    _ = typeof(Converter<double>);
}

// Better: use a static dispatch pattern
IConverter GetConverter(Type t) => t switch
{
    _ when t == typeof(int) => new Converter<int>(),
    _ when t == typeof(float) => new Converter<float>(),
    _ when t == typeof(double) => new Converter<double>(),
    _ => throw new NotSupportedException($"No converter for {t}")
};
```

### Reference Types Are Safe — Use This Knowledge
🟡 **DO** | .NET 7+

When you must use `MakeGenericType` at runtime and the type argument is always a reference type (class), the call is safe. The runtime shares one code path for all reference-type instantiations.

```csharp
// ✅ Safe — only reference types are passed
Type closedType = typeof(Repository<>).MakeGenericType(entityType);
// entityType is always a class (Customer, Order, etc.)

// ✅ Verify with a runtime check
if (runtimeType.IsValueType)
    throw new NotSupportedException("Value types require static registration");
Type closedType = typeof(Handler<>).MakeGenericType(runtimeType);
```

### Bridging Generic Constraints

Sometimes `MakeGenericType` is used to bridge constraints (e.g., calling `Method<T>` where `T : unmanaged` from code with an unconstrained `T`). This is a known limitation of C# generics.

**Preferred solution**: Remove the constraint if possible, or use an interface-based approach:

```csharp
// Instead of: MakeGenericType to bridge constraint
// Use: Interface dispatch
interface IProcessor { void Process(ReadOnlySpan<byte> data); }

class IntProcessor : IProcessor { /* ... */ }
class FloatProcessor : IProcessor { /* ... */ }

// Register all processors statically
Dictionary<Type, IProcessor> processors = new()
{
    [typeof(int)] = new IntProcessor(),
    [typeof(float)] = new FloatProcessor(),
};
```

## MakeGenericMethod

### Same Rules as MakeGenericType
🔴 **UNDERSTAND** | .NET 7+

`MethodInfo.MakeGenericMethod(Type[])` has identical constraints — value type arguments need pre-generated code.

❌
```csharp
var method = typeof(Utils).GetMethod("Parse")!.MakeGenericMethod(runtimeType);
// If runtimeType is a struct, may fail in AOT
```
✅
```csharp
// Use a static dispatch pattern
object Parse(Type t, string input) => t switch
{
    _ when t == typeof(int) => Utils.Parse<int>(input),
    _ when t == typeof(DateTime) => Utils.Parse<DateTime>(input),
    _ => throw new NotSupportedException()
};
```

## Generic Virtual Methods

### Binary Size Impact
ℹ️ **UNDERSTAND** | .NET 7+

Generic virtual methods (and generic interface methods) in AOT generate specialized code for **every combination** of implementing type × type argument. This can significantly increase binary size.

```csharp
interface ISerializer
{
    T Deserialize<T>(string json); // each implementation × each T = separate code
}
```

Consider non-generic alternatives for AOT-sensitive applications:

```csharp
interface ISerializer
{
    object Deserialize(string json, Type type); // single implementation, smaller binary
}
```

## System.Linq.Expressions

### Expression.Compile() Limitations
🟡 **UNDERSTAND** | .NET 7+

In AOT, `Expression.Compile()` uses an **interpreter** instead of JIT compilation. This means:
- It works functionally (no crash)
- It is 10-100x slower than JIT-compiled delegates
- Complex expressions may have subtle behavioral differences

### Where Expressions Are Fine

**EF Core queries**: Expression trees used for LINQ-to-SQL translation do **not** call `Compile()` — they are translated to SQL by the query provider. These work in AOT.

```csharp
// ✅ Fine — EF Core translates the expression to SQL, never compiles it
var orders = await db.Orders
    .Where(o => o.Total > 100)
    .OrderBy(o => o.Date)
    .ToListAsync();
```

### Where Expressions Cause Problems

**Compiled delegates on hot paths**: Any code that calls `.Compile()` and invokes the result frequently:

❌
```csharp
// Builds an expression tree and compiles to a delegate — uses interpreter in AOT
var param = Expression.Parameter(typeof(MyObj));
var prop = Expression.Property(param, "Name");
var lambda = Expression.Lambda<Func<MyObj, string>>(prop, param);
var getter = lambda.Compile(); // 10-100x slower in AOT
```
✅
```csharp
// Use a direct delegate or generated code
Func<MyObj, string> getter = obj => obj.Name;

// Or use a source generator to produce the accessor
```

### Expression.Property Overloads

The overload `Expression.Property(Expression, string propertyName)` is not trim-safe because the trimmer can't determine which property is referenced by the string. Use the `PropertyInfo` overload instead:

❌
```csharp
var prop = Expression.Property(instance, "Name"); // IL2026 warning
```
✅
```csharp
var propInfo = typeof(MyObj).GetProperty("Name")!;
var prop = Expression.Property(instance, propInfo); // ✅ trim-safe
```

## Nullable Value Types and Generics

### Nullable<T> in Generic Contexts

`Nullable<T>` is a value type, so `typeof(Nullable<>).MakeGenericType(runtimeType)` follows value-type rules. However, `Nullable<T>` instantiations for common types (`int?`, `bool?`, etc.) are typically already rooted by the runtime libraries.

If you're constructing `Nullable<CustomStruct>` dynamically, ensure it's statically referenced.
