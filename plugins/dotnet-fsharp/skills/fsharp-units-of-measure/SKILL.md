---
name: fsharp-units-of-measure
description: "Add compile-time unit-of-measure safety to numeric F# code so quantities with different units cannot be mixed by mistake. Use when modeling physical quantities, currency, or any dimensioned numbers (meters, seconds, kg, USD), or to catch unit-mismatch bugs at compile time. Covers declaring measures with [<Measure>], annotating literals and types, derived units, conversions, and the fact that measures are erased at runtime and invisible to other .NET languages. Do not use when values are plain dimensionless numbers."
license: MIT
---

# F# Units of Measure

## Purpose

Units of measure attach a dimension (meters, seconds, USD) to numeric types so the compiler
rejects nonsensical operations - adding meters to seconds, or passing a distance where a time is
expected - with zero runtime cost.

## When to Use

- Modeling physical quantities (length, time, mass, speed) or money
- Preventing unit-mismatch bugs in numeric-heavy code
- Making conversion functions explicit and type-safe

## When Not to Use

- Plain dimensionless numbers
- Public API surface intended for C# consumers (units are erased and invisible there - see
  `design-fsharp-for-dotnet-interop`)

## Declaring and using measures

```fsharp
[<Measure>] type m       // meters
[<Measure>] type s       // seconds
[<Measure>] type kg      // kilograms

let distance = 100.0<m>
let time = 9.58<s>
```

### Operations carry units automatically

```fsharp
let speed = distance / time      // float<m/s>
```

Mixing units is a compile error:

```fsharp
// let bad = distance + time      // error: the unit 'm' does not match 's'
```

### Derived units

```fsharp
[<Measure>] type N = kg m / s^2   // newton, defined from base units
```

### Annotating function signatures

```fsharp
let kineticEnergy (mass: float<kg>) (v: float<m/s>) : float<kg m^2/s^2> =
    0.5 * mass * v * v
```

### Conversions are explicit

Define conversion constants with the right compound unit:

```fsharp
[<Measure>] type ft
let feetPerMeter = 3.28084<ft/m>
let toFeet (d: float<m>) : float<ft> = d * feetPerMeter
```

### Adding and removing units

```fsharp
let raw : float = 5.0
let typed = raw * 1.0<m>           // add a unit
let back : float = float typed     // strip units back to plain float
```

`LanguagePrimitives.FloatWithMeasure` is the explicit way to attach a measure to a computed
value.

## Workflow

1. Declare a `[<Measure>]` type per base unit the domain uses.
2. Annotate literals (`100.0<m>`) and function parameters/returns.
3. Define derived units from base ones where helpful.
4. Make every conversion an explicit function with the compound unit in its type.
5. Strip units only at boundaries that need plain numbers.
6. Verify mismatches are caught (`dotnet fsi` - the bad line should fail to compile).

## Validation

- [ ] Base units declared with `[<Measure>]`
- [ ] Quantities annotated at literals and signatures
- [ ] Mixing incompatible units fails to compile
- [ ] Conversions are explicit, typed functions
- [ ] Units stripped only where plain numbers are required

## Common Pitfalls

| Pitfall | Correction |
|---------|------------|
| Expecting units to appear in a C# consumer | Measures are erased at runtime; C# sees `float` |
| Reusing `float` raw then re-adding units ad hoc | Keep values typed end-to-end; convert explicitly |
| Hard-coding conversions inline | Define typed conversion functions/constants |
| Using measures on dimensionless values | Only annotate genuinely dimensioned quantities |

## More info

- Units of measure: https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/units-of-measure
